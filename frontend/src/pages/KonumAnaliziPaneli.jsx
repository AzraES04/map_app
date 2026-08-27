// ============================================================================
//  Ödev 14 — KONUM ANALİZİ PANELİ
//
//  Ödevin iki maddesi de bu bileşende karşılanıyor:
//
//    Madde 1 — Alan seçimi: "İller listesinden seçim yapma VEYA haritada
//              Poligon çizme". İki yol da burada, sekmeli olarak.
//
//    Madde 2 — Kriter ve ağırlık: 2–5 kategori, her birine 100 üzerinden puan,
//              toplam TAM 100 değilse analiz başlamıyor.
//
//  NEDEN AYRI DOSYA? MapPage.jsx zaten dört bin satıra yakın. Bu panelin
//  MapPage ile paylaştığı tek şey harita: kriterler, il listesi ve puan
//  toplamı tamamen kendi içinde yaşayan bir durum. Ayrı bileşen olunca
//  MapPage yalnızca "analiz başlat" isteğini ve sonucun haritaya çizimini
//  biliyor.
//
//  SORUMLULUK SINIRI:
//    burası → alan seçimi (il listesi), kriterler, doğrulama, sonucun özeti
//    MapPage → haritaya çizim (poligon aracı, ısı katmanı, aday işaretçileri)
// ============================================================================

import { useEffect, useMemo, useState } from 'react'

import { UYGUNLUK_GRADYANI, uygunlukRengiCss } from '../isiIzgarasi'
import { AnalizIkonu, IsiIkonu, DunyaIkonu, SilIkonu } from '../icons'

/** Ödev metnindeki sınırlar. Backend'de de aynı sabitler var (KonumAnaliziService). */
export const EN_AZ_KRITER = 2
export const EN_COK_KRITER = 5
export const TOPLAM_AGIRLIK = 100

/** Yeni bir kriter satırı. `anahtar` yalnızca React listesi için. */
let sonrakiAnahtar = 1
const bosKriter = (agirlik = 0) => ({ anahtar: sonrakiAnahtar++, kategoriId: 0, agirlik })

/**
 * Ağırlıkları eşit dağıtır; artan puanı ilk satıra ekler.
 *
 * 3 kritere 100 puanı bölmek 33.33 eder ama ağırlıklar TAM SAYI: ondalık
 * kabul etseydik "toplam tam 100 olmalı" kuralı kayan nokta hatalarına
 * takılırdı (0.1 + 0.2 !== 0.3). Bu yüzden 34 + 33 + 33.
 */
function esitDagit(kriterler) {
  const pay = Math.floor(TOPLAM_AGIRLIK / kriterler.length)
  const artan = TOPLAM_AGIRLIK - pay * kriterler.length

  return kriterler.map((k, i) => ({ ...k, agirlik: pay + (i < artan ? 1 : 0) }))
}

/** Metre değerini okunur hâle getirir — aday konum satırlarında kullanılıyor. */
function mesafeMetni(metre) {
  if (metre === null || metre === undefined) return '—'
  return metre >= 1000 ? `${(metre / 1000).toFixed(1)} km` : `${Math.round(metre)} m`
}

export default function KonumAnaliziPaneli({
  kategoriler,
  stiller,
  iller,
  illerHatasi,
  cizilenAlanWkt,
  cizimAktif,
  onCizimBaslat,
  onCizimIptal,
  sonuc,
  yukleniyor,
  hata,
  onCalistir,
  onTemizle,
  onAdayaGit,
}) {
  // 'il' → listeden seçim · 'cizim' → haritaya poligon
  const [alanTuru, setAlanTuru] = useState('il')
  const [secilenPlakalar, setSecilenPlakalar] = useState([])
  const [ilArama, setIlArama] = useState('')

  // İki satırla başlıyor: ödevin alt sınırı zaten 2, kullanıcıyı "kriter ekle"
  // düğmesini iki kez aramaya zorlamanın anlamı yok.
  const [kriterler, setKriterler] = useState(() => esitDagit([bosKriter(), bosKriter()]))

  // Çizim sekmesine geçildiğinde il seçimi duruyor ama GÖNDERİLMİYOR
  // (backend ikisini birden reddediyor). Sekme değişince seçimi silmiyoruz:
  // kullanıcı çizimden vazgeçip geri dönerse illeri yeniden işaretlemek
  // zorunda kalmasın.
  useEffect(() => {
    if (alanTuru === 'il' && cizimAktif) onCizimIptal?.()
  }, [alanTuru, cizimAktif, onCizimIptal])

  // --- Kategori id → renk. Haritadaki POI simgeleriyle AYNI kaynak
  // (GET /api/poi/stiller), böylece paneldeki nokta ile haritadaki simge
  // aynı renkte oluyor. Stil kategori KÖKÜNE göre tanımlı olduğu için
  // yaprak kategorinin rengini kökünden buluyoruz.
  const kategoriRenkleri = useMemo(() => {
    const kokIle = new Map(stiller.map((s) => [s.kategoriId, s.renk]))
    const idIle = new Map(kategoriler.map((k) => [k.id, k]))

    const renkBul = (kategori) => {
      let gecerli = kategori
      let guvenlik = 0
      while (gecerli) {
        if (kokIle.has(gecerli.id)) return kokIle.get(gecerli.id)
        if (gecerli.parentId == null || ++guvenlik > 32) break
        gecerli = idIle.get(gecerli.parentId)
      }
      return null
    }

    return new Map(kategoriler.map((k) => [k.id, renkBul(k)]))
  }, [kategoriler, stiller])

  // --- İl listesi süzgeci
  const suzulmusIller = useMemo(() => {
    const q = ilArama.trim().toLocaleLowerCase('tr')
    if (!q) return iller
    return iller.filter(
      (il) => il.ad.toLocaleLowerCase('tr').includes(q)
        || il.bolge.toLocaleLowerCase('tr').includes(q)
        || String(il.plaka) === q,
    )
  }, [iller, ilArama])

  // --- Doğrulama: düğmenin neden kapalı olduğu TEK yerde hesaplanıyor
  const toplamAgirlik = kriterler.reduce((t, k) => t + (Number(k.agirlik) || 0), 0)
  const secilenKategoriler = kriterler.map((k) => k.kategoriId).filter(Boolean)
  const tekrarVar = new Set(secilenKategoriler).size !== secilenKategoriler.length
  const alanSecildi = alanTuru === 'il' ? secilenPlakalar.length > 0 : Boolean(cizilenAlanWkt)

  const engel = !alanSecildi
    ? (alanTuru === 'il' ? 'En az bir il seçin.' : 'Haritada bir alan çizin.')
    : secilenKategoriler.length !== kriterler.length
      ? 'Her kriter için bir kategori seçin.'
      : tekrarVar
        ? 'Aynı kategori birden fazla kritere verilemez.'
        : toplamAgirlik !== TOPLAM_AGIRLIK
          ? `Puan toplamı ${TOPLAM_AGIRLIK} olmalı (şu an ${toplamAgirlik}).`
          : null

  // --- Kriter satırı işlemleri
  const kriteriDegistir = (anahtar, alan, deger) =>
    setKriterler((onceki) => onceki.map(
      (k) => (k.anahtar === anahtar ? { ...k, [alan]: deger } : k),
    ))

  const kriterEkle = () => setKriterler((onceki) => (
    onceki.length >= EN_COK_KRITER ? onceki : esitDagit([...onceki, bosKriter()])
  ))

  const kriterSil = (anahtar) => setKriterler((onceki) => (
    onceki.length <= EN_AZ_KRITER ? onceki : esitDagit(onceki.filter((k) => k.anahtar !== anahtar))
  ))

  const ilDegistir = (plaka) => setSecilenPlakalar((onceki) => (
    onceki.includes(plaka) ? onceki.filter((p) => p !== plaka) : [...onceki, plaka]
  ))

  const calistir = () => {
    if (engel || yukleniyor) return

    onCalistir({
      // Yalnızca AKTİF sekmenin alanı gönderiliyor: ikisini birden yollamak
      // sunucudan 400 alırdı (bkz. KonumAnaliziRequestDto).
      ilPlakalari: alanTuru === 'il' ? [...secilenPlakalar].sort((a, b) => a - b) : null,
      wkt: alanTuru === 'cizim' ? cizilenAlanWkt : null,
      kriterler: kriterler.map((k) => ({
        kategoriId: k.kategoriId,
        agirlik: Number(k.agirlik),
      })),
    })
  }

  return (
    <section className="panel-section konum-analizi">
      <h2>
        <span className="tool-icon"><IsiIkonu /></span>
        Konum Analizi
      </h2>

      {/* ---------- ① HEDEF BÖLGE ---------- */}
      <h3 className="analiz-adim">1 · Hedef bölge</h3>

      <div className="analiz-sekme">
        <button
          type="button"
          className={alanTuru === 'il' ? 'aktif' : ''}
          onClick={() => setAlanTuru('il')}
          aria-pressed={alanTuru === 'il'}
        >
          İl listesi
        </button>
        <button
          type="button"
          className={alanTuru === 'cizim' ? 'aktif' : ''}
          onClick={() => setAlanTuru('cizim')}
          aria-pressed={alanTuru === 'cizim'}
        >
          Haritada çiz
        </button>
      </div>

      {alanTuru === 'il' ? (
        <div className="il-secici">
          <input
            type="search"
            value={ilArama}
            onChange={(e) => setIlArama(e.target.value)}
            placeholder="İl veya bölge ara…"
            aria-label="İl ara"
          />

          {illerHatasi && <p className="analiz-hata">{illerHatasi}</p>}

          {/* Seçilenler ÜSTTE ve rozet olarak: 81 satırlık listede neyin
              işaretli olduğunu aşağı kaydırarak aramak zorunda kalmasın. */}
          {secilenPlakalar.length > 0 && (
            <div className="il-rozetleri">
              {secilenPlakalar
                .map((plaka) => iller.find((il) => il.plaka === plaka))
                .filter(Boolean)
                .map((il) => (
                  <button
                    key={il.plaka}
                    type="button"
                    className="il-rozet"
                    onClick={() => ilDegistir(il.plaka)}
                    title="Seçimden çıkar"
                  >
                    {il.ad} <span aria-hidden="true">×</span>
                  </button>
                ))}
              <button
                type="button"
                className="il-rozet temizle"
                onClick={() => setSecilenPlakalar([])}
              >
                Tümünü kaldır
              </button>
            </div>
          )}

          <div className="il-listesi" role="group" aria-label="İl seçimi">
            {suzulmusIller.length === 0 ? (
              <p className="muted">Eşleşen il yok.</p>
            ) : suzulmusIller.map((il) => (
              <label key={il.plaka} className="il-satiri">
                <input
                  type="checkbox"
                  checked={secilenPlakalar.includes(il.plaka)}
                  onChange={() => ilDegistir(il.plaka)}
                />
                <span className="il-plaka">{String(il.plaka).padStart(2, '0')}</span>
                <span className="il-ad">{il.ad}</span>
                <span className="il-bolge muted">{il.bolge}</span>
              </label>
            ))}
          </div>

          <p className="tool-hint muted">
            Birden çok il seçilebilir; analiz alanı seçilen illerin
            <strong> birleşimi</strong> olur.
          </p>
        </div>
      ) : (
        <div className="cizim-secici">
          <button
            type="button"
            className={`tool-btn genis${cizimAktif ? ' active' : ''}`}
            onClick={cizimAktif ? onCizimIptal : onCizimBaslat}
            aria-pressed={cizimAktif}
          >
            <span className="tool-icon"><AnalizIkonu /></span>
            {cizimAktif ? 'Çizimi iptal et' : cizilenAlanWkt ? 'Yeniden çiz' : 'Haritada alan çiz'}
          </button>

          {cizimAktif ? (
            <p className="tool-hint">
              Köşeleri tıklayın, bitirmek için <strong>çift tıklayın</strong>.
              Çizilen alan <strong>veritabanına kaydedilmez</strong>; yalnızca
              analizin sınırıdır.
            </p>
          ) : cizilenAlanWkt ? (
            <p className="analiz-durum ok">Alan çizildi — haritada mor kesikli çerçeve.</p>
          ) : (
            <p className="tool-hint muted">Analizin sınırı için haritaya bir poligon çizin.</p>
          )}
        </div>
      )}

      {/* ---------- ② KRİTERLER ---------- */}
      <h3 className="analiz-adim">
        2 · Kriterler
        <span className="muted"> ({EN_AZ_KRITER}–{EN_COK_KRITER} adet)</span>
      </h3>

      <div className="kriter-listesi">
        {kriterler.map((kriter, sira) => {
          const renk = kategoriRenkleri.get(kriter.kategoriId)

          return (
            <div key={kriter.anahtar} className="kriter-satiri">
              <div className="kriter-ust">
                <span className="dot" style={{ background: renk || 'var(--metin-soluk)' }} />

                <select
                  value={kriter.kategoriId}
                  onChange={(e) => kriteriDegistir(kriter.anahtar, 'kategoriId', Number(e.target.value))}
                  aria-label={`${sira + 1}. kriterin kategorisi`}
                >
                  <option value={0}>Kategori seçin…</option>
                  {/* Başka bir satırda seçilmiş kategori burada KAPALI: aynı
                      kategorinin iki kez sayılması sessizce yanlış bir sonuç
                      üretirdi (sunucu da aynı isteği reddediyor).
                      Girinti bölünmez boşlukla: normal boşluk <option>
                      içinde tarayıcı tarafından kırpılıyor. */}
                  {kategoriler.map((k) => (
                    <option
                      key={k.id}
                      value={k.id}
                      disabled={k.id !== kriter.kategoriId && secilenKategoriler.includes(k.id)}
                    >
                      {' '.repeat(k.seviye * 4)}{k.seviye > 0 ? '└ ' : ''}{k.ad}
                      {k.poiSayisi > 0 ? ` (${k.poiSayisi})` : ''}
                    </option>
                  ))}
                </select>

                <input
                  type="number"
                  className="kriter-puan"
                  min={1}
                  max={100}
                  value={kriter.agirlik}
                  onChange={(e) => kriteriDegistir(kriter.anahtar, 'agirlik', Number(e.target.value))}
                  aria-label={`${sira + 1}. kriterin ağırlık puanı`}
                />

                <button
                  type="button"
                  className="kriter-sil"
                  onClick={() => kriterSil(kriter.anahtar)}
                  disabled={kriterler.length <= EN_AZ_KRITER}
                  title={kriterler.length <= EN_AZ_KRITER
                    ? `En az ${EN_AZ_KRITER} kriter gerekli`
                    : 'Kriteri kaldır'}
                >
                  <SilIkonu size={13} />
                </button>
              </div>

              {/* Kaydırıcı, sayı kutusuyla AYNI değeri sürüyor. İkisi birden
                  duruyor çünkü kaydırıcı hızlı deneme için, sayı kutusu
                  toplamı tam 100'e getirmek için gerekli. */}
              <input
                type="range"
                min={1}
                max={100}
                value={kriter.agirlik}
                onChange={(e) => kriteriDegistir(kriter.anahtar, 'agirlik', Number(e.target.value))}
                aria-label={`${sira + 1}. kriterin ağırlık kaydırıcısı`}
                style={{ accentColor: renk || undefined }}
              />
            </div>
          )
        })}
      </div>

      <div className="kriter-araclar">
        <button
          type="button"
          className="btn-ghost"
          onClick={kriterEkle}
          disabled={kriterler.length >= EN_COK_KRITER}
        >
          + Kriter ekle
        </button>
        <button
          type="button"
          className="btn-ghost"
          onClick={() => setKriterler(esitDagit(kriterler))}
        >
          Eşit dağıt
        </button>
      </div>

      {/* Toplam puan göstergesi: ödevin en katı kuralı, en görünür yerde. */}
      <div className={`puan-toplami${toplamAgirlik === TOPLAM_AGIRLIK ? ' tamam' : ' gecersiz'}`}>
        <div className="puan-cubugu">
          <span style={{ width: `${Math.min(100, toplamAgirlik)}%` }} />
        </div>
        <strong>{toplamAgirlik}</strong> / {TOPLAM_AGIRLIK} puan
        {toplamAgirlik !== TOPLAM_AGIRLIK && (
          <span className="muted">
            {' · '}
            {toplamAgirlik < TOPLAM_AGIRLIK
              ? `${TOPLAM_AGIRLIK - toplamAgirlik} puan eksik`
              : `${toplamAgirlik - TOPLAM_AGIRLIK} puan fazla`}
          </span>
        )}
      </div>

      <button
        type="button"
        className="btn-primary genis"
        onClick={calistir}
        disabled={Boolean(engel) || yukleniyor}
        title={engel || 'Analizi başlat'}
      >
        {yukleniyor ? 'Analiz ediliyor…' : 'Analizi Başlat'}
      </button>

      {/* Düğme neden kapalı? Yazmasaydık kullanıcı tıklayıp hiçbir şey
          olmamasına bakardı — kapalı düğmenin en can sıkıcı hâli. */}
      {engel && !yukleniyor && <p className="analiz-engel">{engel}</p>}
      {hata && <p className="analiz-hata">{hata}</p>}

      {/* ---------- ③ SONUÇ ---------- */}
      {sonuc && (
        <div className="konum-sonuc">
          <h3 className="analiz-adim">3 · Sonuç</h3>

          <p className="analiz-toplam">
            <DunyaIkonu size={13} /> <strong>{sonuc.alanAdi}</strong>
            <span className="muted">
              {' · '}{sonuc.alanKm2.toLocaleString('tr-TR')} km²
              {' · '}{sonuc.toplamPoi.toLocaleString('tr-TR')} POI
            </span>
          </p>

          {sonuc.toplamPoi === 0 && (
            <p className="analiz-engel">
              Seçilen alanda bu kriterlere uyan POI yok — yüzey boş çıktı.
              Daha geniş bir alan seçin ya da kriterleri değiştirin.
            </p>
          )}

          <ul className="kriter-ozet">
            {sonuc.kriterler.map((k) => (
              <li key={k.kategoriId}>
                <span className="dot" style={{
                  background: kategoriRenkleri.get(k.kategoriId) || 'var(--metin-soluk)',
                }} />
                <span className="kriter-ad" title={k.kategoriYolu}>{k.kategoriAdi}</span>
                <span className="kriter-agirlik">%{k.agirlik}</span>
                <span className={`kriter-sayi${k.poiSayisi === 0 ? ' bos' : ''}`}>
                  {k.poiSayisi} POI
                </span>
              </li>
            ))}
          </ul>

          {/* Lejant: renk → PUAN. Rampa 0'dan en yüksek skora yayıldığı için
              sağ uçtaki etiket sabit "100" değil, o analizdeki en iyi puan. */}
          <div className="uygunluk-lejanti">
            <div className="lejant-cubugu" style={{ background: UYGUNLUK_GRADYANI }} />
            <div className="lejant-etiketler">
              <span>0 puan</span>
              <span>düşük</span>
              <span>{Math.round(sonuc.izgara.enYuksekSkor * 100)} puan (en iyi)</span>
            </div>
            <p className="muted">
              Hücre ≈ {Math.round(sonuc.izgara.hucreMetre)} m ·
              etki yarıçapı ≈ {(sonuc.izgara.etkiYaricapiMetre / 1000).toFixed(1)} km
            </p>
          </div>

          {sonuc.adaylar.length > 0 && (
            <>
              <h4 className="analiz-alt-baslik">Önerilen konumlar</h4>
              <ol className="aday-listesi">
                {sonuc.adaylar.map((aday) => (
                  <li key={aday.sira}>
                    <button type="button" onClick={() => onAdayaGit(aday)}>
                      <span
                        className="aday-sira"
                        style={{
                          background: uygunlukRengiCss(
                            sonuc.izgara.enYuksekSkor > 0
                              ? aday.skor / 100 / sonuc.izgara.enYuksekSkor
                              : 0,
                          ),
                        }}
                      >
                        {aday.sira}
                      </span>
                      <span className="aday-govde">
                        <strong>{aday.skor.toFixed(1)} puan</strong>
                        <small>
                          {aday.mesafeler.map((m) => (
                            <span key={m.kategoriId}>
                              {m.kategoriAdi}: {mesafeMetni(m.mesafeMetre)}
                            </span>
                          ))}
                        </small>
                      </span>
                    </button>
                  </li>
                ))}
              </ol>
            </>
          )}

          <button type="button" className="btn-ghost genis" onClick={onTemizle}>
            Analizi temizle
          </button>
        </div>
      )}

      <p className="tool-hint muted">
        Yüzey <strong>tarayıcıda değil sunucuda</strong> hesaplanıyor: her kriterin
        yoğunluğu kendi içinde 0–1'e ölçekleniyor, sonra ağırlıklarla toplanıyor.
        Normalleştirme olmasaydı sayıca çok olan kategori (örn. eczane)
        ağırlığı ne olursa olsun sonucu belirlerdi.
        <br />
        Analiz alanı çizim yetkinizden bağımsızdır: POI'ler ortak referans
        verisidir, herkes hepsini görür.
      </p>
    </section>
  )
}
