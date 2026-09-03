// ============================================================================
//  TOUR BUILDER — kullanıcı seçimlerini toplayan tur oluşturma paneli
//
//  Beş parametre, beş adım:
//      1 · Lokasyon        bölge → şehir → ilçe
//      2 · Ulaşım tipi     Yaya / Araç / Toplu Taşıma
//      3 · Toplam süre     günübirlik saat ya da gün sayısı
//      4 · Tur teması      kültürel / popüler / karma / doğa / gastronomi
//      5 · Beslenme kısıtı kısıt yok / vejetaryen / vegan
//
//  SORUMLULUK SINIRI (KonumAnaliziPaneli ile aynı ayrım):
//      burası      → girdiler, seçimler, hataların gösterimi
//      turPlani.js → kurallar: doğrulama, süre hesabı, payload üretimi
//      turApi.js   → taşıma: isteği rota servisine götürmek
//
//  Kurallar neden bileşende değil? Bileşen içinde yazılsaydı doğrulamayı
//  sınamak için her seferinde ekranı çizip tıklamak gerekirdi; ayrıca aynı
//  kurallar ileride başka bir ekrandan çağrılamazdı. Ayrımın karşılığı
//  testlerde de görünüyor: turPlani.test.js kuralları saf olarak, bu dosyanın
//  testi ise "geçersiz formda istek GİTMİYOR mu?" sorusunu sınıyor.
//
//  ROTA SERVİSİ DIŞARIDAN GEÇİLEBİLİYOR (`rotaServisi` prop'u): varsayılanı
//  gerçek uç, testte sahte bir fonksiyon. Bileşen doğrudan turApi'yi çağırsaydı
//  testte global fetch'i taklit etmek gerekirdi — kırılgan ve yavaş.
// ============================================================================

import { useEffect, useMemo, useState } from 'react'

import { GuzergahIkonu, SaatIkonu } from '../icons'
import { turRotasiOner } from '../turApi'
import {
  BESLENME_KISITLARI,
  SINIRLAR,
  SURE_BIRIMLERI,
  TUR_TEMALARI,
  ULASIM_TIPLERI,
  bosTurFormu,
  rotaIstegiHazirla,
  sureMetni,
  toplamDakika,
  turTemasi,
  ulasimTipi as ulasimTipiBul,
} from '../turPlani'

/**
 * FORMU TOPLAYIP ROTA SERVİSİNE GEÇEN FONKSİYON.
 *
 * Bileşenden ayrı ve dışa açık: "seçimleri al → doğrula → payload üret →
 * servise ver" zinciri React'e bağlı olmayan bir iş akışı. Böylece bir
 * betikten ya da testten de çağrılabiliyor.
 *
 * Doğrulama hatası İSTİSNA ATMIYOR, sonuç nesnesinde dönüyor: geçersiz form
 * beklenen bir durum, istisna ise beklenmeyen bir durumun aracı. Ağ hatası
 * (sunucu kapalı, 500) ise istisna olarak yukarı çıkıyor — çağıran taraf onu
 * kullanıcıya "sunucuya ulaşılamadı" diye göstermeli.
 *
 * @param {import('../turPlani').TurFormu} form
 * @param {{iller?: object[], rotaServisi?: Function, onUnauthorized?: Function}} [secenekler]
 * @returns {Promise<{gecerli: boolean, hatalar: Object<string,string>,
 *                    ilkHata: string|null, uyarilar: string[],
 *                    payload: Object|null, sonuc: Object|null}>}
 */
export async function turRotasiIste(form, secenekler = {}) {
  const {
    iller = [],
    rotaServisi = turRotasiOner,
    onUnauthorized,
  } = secenekler

  const hazir = rotaIstegiHazirla(form, { iller })

  // Geçersizse İSTEK HİÇ GİTMİYOR. Sunucu da aynı kuralları uyguluyor ama
  // geçersiz bir isteği yollamak, kullanıcıya sunucunun genel dilinde
  // ("The Sure field is required") hata göstermek olurdu.
  if (!hazir.gecerli) return { ...hazir, sonuc: null }

  const sonuc = await rotaServisi(hazir.payload, onUnauthorized)
  return { ...hazir, sonuc }
}

/** Segmentli seçim — üç seçenek için açılır listeden okunaklı. */
function Segment({ etiket, secenekler, deger, onChange, adSuffix }) {
  return (
    <div className="analiz-sekme" role="radiogroup" aria-label={etiket}>
      {secenekler.map((secenek) => (
        <button
          key={secenek.deger}
          type="button"
          role="radio"
          aria-checked={deger === secenek.deger}
          className={deger === secenek.deger ? 'aktif' : ''}
          onClick={() => onChange(secenek.deger)}
          title={secenek.aciklama}
        >
          {secenek.etiket}{adSuffix}
        </button>
      ))}
    </div>
  )
}

export default function TourBuilder({
  iller = [],
  illerHatasi = null,
  /** İlçe listesi: { [plaka]: ['Çankaya', 'Keçiören'] }. Yoksa alan serbest metin olur. */
  ilceler = null,
  /** Rota servisi — varsayılanı gerçek uç; testte sahte fonksiyon geçiliyor. */
  rotaServisi = turRotasiOner,
  onUnauthorized,
  /** Öneri geldiğinde haber verir; harita çizimini çağıran taraf yapıyor. */
  onSonuc,
}) {
  const [form, setForm] = useState(bosTurFormu)
  const [gonderildi, setGonderildi] = useState(false)
  const [yukleniyor, setYukleniyor] = useState(false)
  const [sunucuHatasi, setSunucuHatasi] = useState(null)
  const [sonuc, setSonuc] = useState(null)

  const alan = (ad, deger) => setForm((onceki) => ({ ...onceki, [ad]: deger }))

  // Bölgeler il listesinden türetiliyor: ayrı bir istek atmak, aynı veriyi
  // ikinci kez indirmek olurdu (GET /api/iller zaten bölgeyi taşıyor).
  const bolgeler = useMemo(
    () => [...new Set(iller.map((il) => il.bolge))].sort((a, b) => a.localeCompare(b, 'tr')),
    [iller],
  )

  const suzulmusIller = useMemo(() => {
    const liste = form.bolge ? iller.filter((il) => il.bolge === form.bolge) : iller
    return [...liste].sort((a, b) => a.ad.localeCompare(b.ad, 'tr'))
  }, [iller, form.bolge])

  // Bölge değişince, seçili şehir artık o bölgede değilse seçim DÜŞÜYOR.
  //
  // Bırakmak da mümkündü (doğrulama zaten yakalıyor) ama o zaman kullanıcı
  // listede göremediği bir şehrin hatasını okurdu. Seçimi düşürmek, hatayı
  // hiç doğurmuyor.
  useEffect(() => {
    if (!form.bolge || !form.ilPlaka) return
    const il = iller.find((i) => i.plaka === form.ilPlaka)
    if (il && il.bolge !== form.bolge) {
      setForm((onceki) => ({ ...onceki, ilPlaka: 0, ekIlPlakalari: [], ilce: '' }))
    }
  }, [form.bolge, form.ilPlaka, iller])

  // EK ŞEHİRLER de bölge süzgecine uymalı.
  //
  // Başlangıç şehriyle aynı gerekçe ama ayrı bir etki: orada seçim TAMAMEN
  // düşüyor, burada yalnızca bölge dışında kalanlar eleniyor — kullanıcı
  // "İç Anadolu" seçtiğinde Ankara + Eskişehir seçimini korumalı, yalnızca
  // İzmir listeden düşmeli.
  useEffect(() => {
    if (!form.bolge || (form.ekIlPlakalari ?? []).length === 0) return

    const kalanlar = form.ekIlPlakalari.filter(
      (plaka) => iller.find((i) => i.plaka === plaka)?.bolge === form.bolge,
    )

    // Referans karşılaştırması değil UZUNLUK: aynı diziyi her seferinde
    // yeniden yazmak sonsuz döngü olurdu.
    if (kalanlar.length !== form.ekIlPlakalari.length) {
      setForm((onceki) => ({ ...onceki, ekIlPlakalari: kalanlar }))
    }
  }, [form.bolge, form.ekIlPlakalari, iller])

  // Doğrulama HER ÇİZİMDE çalışıyor: düğmenin kapalılığı, alan hataları ve
  // uyarılar hep aynı kaynaktan besleniyor. İki ayrı yerde hesaplasaydık
  // "düğme açık ama alan kırmızı" gibi çelişkiler mümkün olurdu.
  const { gecerli, hatalar, ilkHata, uyarilar } = useMemo(
    () => rotaIstegiHazirla(form, { iller }),
    [form, iller],
  )

  // Hatalar ancak GÖNDERDİKTEN sonra gösteriliyor: form açılır açılmaz
  // "şehir seçilmelidir" diye kırmızıya boyamak, henüz hiçbir şey yapmamış
  // kullanıcıyı azarlamak olurdu.
  const hata = (ad) => (gonderildi ? hatalar[ad] : null)

  const dakika = toplamDakika(form)
  const ulasim = ulasimTipiBul(form.ulasimTipi)
  const tema = turTemasi(form.tema)

  /** Yaklaşık kat edilecek mesafe — seçimin ne anlama geldiğini gösteren özet. */
  const yaklasikKm = ulasim ? Math.round((dakika / 60) * ulasim.ortalamaHizKmS) : null

  const ilceSecenekleri = ilceler?.[form.ilPlaka] ?? null

  /**
   * Gönder düğmesi: seçimleri toplayıp rota servisine geçiyor.
   * İşin kendisi turRotasiIste'de; burada yalnızca ekranın durumu yönetiliyor.
   */
  const gonder = async () => {
    setGonderildi(true)
    if (!gecerli || yukleniyor) return

    setYukleniyor(true)
    setSunucuHatasi(null)

    try {
      const cevap = await turRotasiIste(form, { iller, rotaServisi, onUnauthorized })
      setSonuc(cevap.sonuc)
      onSonuc?.(cevap.sonuc, cevap.payload)
    } catch (err) {
      setSunucuHatasi(err.message || 'Rota önerisi alınamadı.')
      setSonuc(null)
    } finally {
      setYukleniyor(false)
    }
  }

  return (
    <section className="panel-section tur-olusturucu">
      <h2>
        <span className="tool-icon"><GuzergahIkonu /></span>
        Tur Oluştur
      </h2>

      {/* ---------- ① LOKASYON ---------- */}
      <h3 className="analiz-adim">1 · Lokasyon</h3>

      {illerHatasi && <p className="analiz-hata">{illerHatasi}</p>}

      <div className="tur-alan">
        <label htmlFor="tur-bolge">Bölge <span className="muted">(isteğe bağlı)</span></label>
        <select
          id="tur-bolge"
          value={form.bolge}
          onChange={(e) => alan('bolge', e.target.value)}
        >
          <option value="">Tüm bölgeler</option>
          {bolgeler.map((bolge) => (
            <option key={bolge} value={bolge}>{bolge}</option>
          ))}
        </select>
      </div>

      <div className="tur-alan">
        <label htmlFor="tur-sehir">Başlangıç şehri</label>
        <select
          id="tur-sehir"
          value={form.ilPlaka}
          onChange={(e) => alan('ilPlaka', Number(e.target.value))}
          aria-invalid={Boolean(hata('ilPlaka'))}
        >
          <option value={0}>Şehir seçin…</option>
          {suzulmusIller.map((il) => (
            <option key={il.plaka} value={il.plaka}>
              {String(il.plaka).padStart(2, '0')} · {il.ad}
            </option>
          ))}
        </select>
        {hata('ilPlaka') && <p className="tur-alan-hata">{hata('ilPlaka')}</p>}
      </div>

      {/* ---- ÇOK ŞEHİRLİ / BÖLGE TURU ----
          Kullanıcı isteği: "bölge ya da şehir seçip birden fazla şehirle tur
          yapılabilmesi". Bölge süzgeci zaten yukarıda; buradaki liste de o
          süzgeçten geçtiği için "Ege bölgesi" seçip iki Ege şehrini
          işaretlemek tek akış hâline geliyor.

          İŞARET KUTUSU LİSTESİ, çoklu <select> DEĞİL: çoklu select'te
          Ctrl+tık gerekiyor ve mobilde neredeyse kullanılamaz. Ayrıca
          seçilenler burada tek bakışta görünüyor.

          Başlangıç şehri listede YOK: zaten turda, bir de işaretlenecek
          bir şeymiş gibi durması kafa karıştırırdı. */}
      {form.ilPlaka > 0 && (
        <div className="tur-alan">
          <label>
            Tura eklenecek diğer şehirler <span className="muted">(isteğe bağlı)</span>
          </label>

          <p className="tur-ipucu muted">
            Birden fazla şehirde geçen bir tur planlayabilirsiniz; duraklar
            şehirler arasında paylaştırılır. En fazla {SINIRLAR.enCokSehir} şehir.
          </p>

          <div className="tur-sehir-listesi">
            {suzulmusIller
              .filter((il) => il.plaka !== form.ilPlaka)
              .map((il) => {
                const secili = (form.ekIlPlakalari ?? []).includes(il.plaka)
                // Sınıra ulaşıldıysa SEÇİLİ OLMAYANLAR kapanıyor; seçili
                // olanlar açık kalmalı, yoksa kullanıcı seçimini geri
                // alamaz ve listede kilitli kalırdı.
                const doldu = (form.ekIlPlakalari ?? []).length + 1 >= SINIRLAR.enCokSehir

                return (
                  <label key={il.plaka} className="tur-sehir-satiri">
                    <input
                      type="checkbox"
                      checked={secili}
                      disabled={!secili && doldu}
                      onChange={() => alan(
                        'ekIlPlakalari',
                        secili
                          ? form.ekIlPlakalari.filter((p) => p !== il.plaka)
                          : [...(form.ekIlPlakalari ?? []), il.plaka],
                      )}
                    />
                    <span>{il.ad}</span>
                  </label>
                )
              })}
          </div>

          {hata('ekIlPlakalari') && (
            <p className="tur-alan-hata">{hata('ekIlPlakalari')}</p>
          )}
        </div>
      )}

      <div className="tur-alan">
        <label htmlFor="tur-ilce">İlçe <span className="muted">(isteğe bağlı)</span></label>
        {/* Sistemde ilçe REFERANS VERİSİ yok (iller tablosu 81 ille sınırlı).
            Bu yüzden alan serbest metin; ilçe listesi bir gün geldiğinde
            `ilceler` prop'u dolu geçilerek aynı alan açılır listeye dönüyor —
            bileşende başka değişiklik gerekmiyor. */}
        {ilceSecenekleri ? (
          <select
            id="tur-ilce"
            value={form.ilce}
            onChange={(e) => alan('ilce', e.target.value)}
          >
            <option value="">Tüm ilçeler</option>
            {ilceSecenekleri.map((ilce) => (
              <option key={ilce} value={ilce}>{ilce}</option>
            ))}
          </select>
        ) : (
          <input
            id="tur-ilce"
            type="text"
            value={form.ilce}
            maxLength={SINIRLAR.ilceEnCokKarakter}
            placeholder="Örn. Çankaya"
            onChange={(e) => alan('ilce', e.target.value)}
            aria-invalid={Boolean(hata('ilce'))}
          />
        )}
        {hata('ilce') && <p className="tur-alan-hata">{hata('ilce')}</p>}
      </div>

      {/* ---------- ② ULAŞIM TİPİ ---------- */}
      <h3 className="analiz-adim">2 · Ulaşım tipi</h3>

      <Segment
        etiket="Ulaşım tipi"
        secenekler={ULASIM_TIPLERI}
        deger={form.ulasimTipi}
        onChange={(deger) => alan('ulasimTipi', deger)}
      />

      {/* ---------- ③ TOPLAM SÜRE ---------- */}
      <h3 className="analiz-adim">3 · Toplam süre</h3>

      <Segment
        etiket="Süre birimi"
        secenekler={SURE_BIRIMLERI}
        deger={form.sureBirimi}
        onChange={(deger) => setForm((onceki) => ({
          ...onceki,
          sureBirimi: deger,
          // Birim değişince değer de makul bir yere çekiliyor: "12" saatken
          // gün'e geçen kullanıcı 12 GÜNLÜK bir tur istemiş olmaz.
          sureDegeri: deger === 'Gun' ? 2 : 4,
        }))}
      />

      <div className="tur-satir">
        <div className="tur-alan">
          <label htmlFor="tur-baslangic">Başlangıç saati</label>
          {/* Program bu saatten itibaren kuruluyor; çok günlü turda HER GÜN
              bu saatte başlıyor (bkz. turProgrami.js). */}
          <input
            id="tur-baslangic"
            type="time"
            value={form.baslangicSaati}
            onChange={(e) => alan('baslangicSaati', e.target.value)}
            aria-invalid={Boolean(hata('baslangicSaati'))}
          />
        </div>

        <div className="tur-alan">
          <label htmlFor="tur-sure">
            {form.sureBirimi === 'Gun' ? 'Gün sayısı' : 'Saat'}
          </label>
          <input
            id="tur-sure"
            type="number"
            value={form.sureDegeri}
            min={form.sureBirimi === 'Gun' ? SINIRLAR.enAzGun : SINIRLAR.enAzSaat}
            max={form.sureBirimi === 'Gun' ? SINIRLAR.enCokGun : SINIRLAR.enCokSaat}
            step={1}
            onChange={(e) => alan('sureDegeri', e.target.value === '' ? '' : Number(e.target.value))}
            aria-invalid={Boolean(hata('sureDegeri'))}
          />
        </div>

        {form.sureBirimi === 'Gun' && (
          <div className="tur-alan">
            <label htmlFor="tur-gunluk">Günlük gezi süresi (saat)</label>
            <input
              id="tur-gunluk"
              type="number"
              value={form.gunlukSaat}
              min={SINIRLAR.enAzGunlukSaat}
              max={SINIRLAR.enCokGunlukSaat}
              step={1}
              onChange={(e) => alan('gunlukSaat', e.target.value === '' ? '' : Number(e.target.value))}
              aria-invalid={Boolean(hata('gunlukSaat'))}
            />
          </div>
        )}
      </div>

      {hata('baslangicSaati') && <p className="tur-alan-hata">{hata('baslangicSaati')}</p>}
      {hata('sureDegeri') && <p className="tur-alan-hata">{hata('sureDegeri')}</p>}
      {hata('gunlukSaat') && <p className="tur-alan-hata">{hata('gunlukSaat')}</p>}

      {/* Seçimin karşılığı: kullanıcı "3 gün" derken kaç saatlik bir gezi
          istediğini görmeli — çok günlük turda toplam, günlük saatle çarpılıyor. */}
      <p className="tur-ozet">
        <SaatIkonu size={13} />
        <strong>{sureMetni(dakika)}</strong> gezi
        {yaklasikKm !== null && (
          <span className="muted">
            {' · '}yaklaşık {yaklasikKm.toLocaleString('tr-TR')} km ({ulasim.etiket.toLocaleLowerCase('tr')})
          </span>
        )}
      </p>

      {/* ---------- ④ TUR TEMASI ---------- */}
      <h3 className="analiz-adim">4 · Tur teması</h3>

      <div className="tur-alan">
        <label htmlFor="tur-tema">Tema</label>
        <select
          id="tur-tema"
          value={form.tema}
          onChange={(e) => alan('tema', e.target.value)}
          aria-invalid={Boolean(hata('tema'))}
        >
          {TUR_TEMALARI.map((t) => (
            <option key={t.deger} value={t.deger}>{t.etiket}</option>
          ))}
        </select>
        {tema && <p className="tool-hint muted">{tema.aciklama}</p>}
        {hata('tema') && <p className="tur-alan-hata">{hata('tema')}</p>}
      </div>

      {/* ---------- ⑤ BESLENME KISITI ---------- */}
      <h3 className="analiz-adim">5 · Beslenme kısıtı</h3>

      <Segment
        etiket="Beslenme kısıtı"
        secenekler={BESLENME_KISITLARI}
        deger={form.beslenme}
        onChange={(deger) => alan('beslenme', deger)}
      />
      <p className="tool-hint muted">
        Yeme-içme durakları bu kısıta göre süzülür; kısıt yoksa süzme yapılmaz.
      </p>

      {/* ---------- ⑥ MOLALAR ---------- */}
      <h3 className="analiz-adim">6 · Molalar</h3>

      <label className="tur-secenek">
        <input
          type="checkbox"
          checked={form.yemekMolasi}
          onChange={(e) => alan('yemekMolasi', e.target.checked)}
        />
        <span>
          <strong>Yemek molası ekle</strong>
          <span className="muted">
            Her güne bir öğle molası; yöresel lokantalardan seçilir
            {form.beslenme !== 'Yok' && ` (${form.beslenme.toLowerCase()} uygun)`}.
          </span>
        </span>
      </label>

      <label className="tur-secenek">
        <input
          type="checkbox"
          checked={form.serbestZaman}
          onChange={(e) => alan('serbestZaman', e.target.checked)}
        />
        <span>
          <strong>Serbest zaman bırak</strong>
          <span className="muted">
            Her güne bir saat gezinme payı; çarşı, park ve tarihi doku
            duraklarına eklenir. Yeni durak açılmaz, o durakta daha uzun
            kalınır.
          </span>
        </span>
      </label>

      {/* KONAKLAMA YALNIZCA ÇOK GÜNLÜ TURDA ÇİZİLİYOR.
          Günübirlik turda akşam eve dönülüyor; kutuyu göstermek "otel de
          önerebilirim" demek olurdu ve seçilse bile sunucu yok sayardı. */}
      {form.sureBirimi === 'Gun' && Number(form.sureDegeri) > 1 && (
        <label className="tur-secenek">
          <input
            type="checkbox"
            checked={form.konaklama}
            onChange={(e) => alan('konaklama', e.target.checked)}
          />
          <span>
            <strong>Gece konaklaması ekle</strong>
            <span className="muted">
              Son gün hariç her günün sonuna konaklama bölgesi önerilir.
              Puan verisi olmadığı için belirli bir otel değil, o çevredeki
              konaklama noktası gösterilir.
            </span>
          </span>
        </label>
      )}

      {/* ---------- GÖNDER ---------- */}
      {uyarilar.map((uyari) => (
        <p key={uyari} className="analiz-engel">{uyari}</p>
      ))}

      <button
        type="button"
        className="btn-primary genis"
        onClick={gonder}
        disabled={yukleniyor || (gonderildi && !gecerli)}
        title={gonderildi && ilkHata ? ilkHata : 'Rota önerisi al'}
      >
        {yukleniyor ? 'Rota hazırlanıyor…' : 'Rota Oluştur'}
      </button>

      {/* Düğme neden kapalı? Yazmasaydık kullanıcı tıklayıp hiçbir şey
          olmamasına bakardı (KonumAnaliziPaneli'ndeki kuralın aynısı).
          Mesajın kendisi burada TEKRARLANMIYOR: her hata zaten kendi alanının
          altında yazıyor, aynı cümleyi iki kez göstermek hangisinin
          düzeltileceğini bulmayı kolaylaştırmaz. */}
      {gonderildi && ilkHata && !yukleniyor && (
        <p className="analiz-engel">
          {Object.keys(hatalar).length === 1
            ? 'Bir alan eksik veya hatalı — yukarıda işaretlendi.'
            : `${Object.keys(hatalar).length} alan eksik veya hatalı — yukarıda işaretlendi.`}
        </p>
      )}
      {sunucuHatasi && <p className="analiz-hata">{sunucuHatasi}</p>}

      {sonuc && (
        <>
          <p className="analiz-durum ok">
            <strong>{sonuc.name ?? 'Tur önerisi'}</strong>
            {' · '}{sonuc.waypoints?.length ?? 0} durak
            {sonuc.estimatedTotalMinutes
              ? ` · ${sureMetni(sonuc.estimatedTotalMinutes)}`
              : ''}
          </p>

          {/* Sunucunun uyarıları: "süreye sığmayan 3 durak çıkarıldı" gibi.
              Öneriyi geçersiz kılmıyorlar ama sessiz kalmak, kullanıcının
              eksik bir turu tam sanmasına yol açardı. */}
          {sonuc.uyarilar?.map((uyari) => (
            <p key={uyari} className="analiz-engel">{uyari}</p>
          ))}
        </>
      )}
    </section>
  )
}
