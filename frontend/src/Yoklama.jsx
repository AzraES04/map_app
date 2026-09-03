import { useCallback, useEffect, useState } from 'react'

import { yoklamaBaslat, yoklamaDurumu, yoklamaBitir, misafirYoklamaCevabi } from './turApi'
import { misafirAnahtari } from './misafirAnahtari'

// ============================================================================
//  YOKLAMA — "şu an kimler burada?"
//
//  İki yüzü olan tek bir özellik:
//
//    REHBER    → soruyu sorar, grup sayısını girer, cevapları sayı olarak
//                görür: "15 kişinin 13'ü buradayım, 1 değil, 1 acil".
//    MİSAFİR   → üç düğmeden birine basar. Başka kimsenin cevabını görmez.
//
//  ---- NEDEN ÜÇ SEÇENEK, SERBEST METİN DEĞİL? ----
//  Serbest metin, kimliksiz bir mesaj kutusu demek: sayılamaz, denetlenemez
//  ve rehberin sahada okuyacak vakti yok. Kapalı küme hem tek dokunuşta
//  cevaplanıyor hem de doğrudan sayıya dönüşüyor.
//
//  "Acil" ayrı bir seçenek ve ayrı sayılıyor: geride kalan biri ile yardım
//  isteyen biri aynı kefeye konsaydı, rehberin ilk bakması gereken sayı
//  diğerlerinin içinde erirdi.
// ============================================================================

/** Rehberin ekranı kaç saniyede bir yenileniyor. */
const YENILEME_MS = 5_000

const CEVAPLAR = [
  { deger: 'Buradayim', etiket: 'Buradayım', sinif: 'olumlu' },
  { deger: 'Degilim', etiket: 'Burada değilim', sinif: 'olumsuz' },
  { deger: 'Acil', etiket: 'Acil durum', sinif: 'acil' },
]

// ---------------------------------------------------------------------------
//  REHBER
// ---------------------------------------------------------------------------

export function YoklamaRehber({ oturumId, onUnauthorized }) {
  const [durum, setDurum] = useState(null)
  const [soru, setSoru] = useState('Şu an grupta mısınız?')
  const [grupBoyu, setGrupBoyu] = useState(15)
  const [hata, setHata] = useState(null)
  const [mesgul, setMesgul] = useState(false)

  const oku = useCallback(async () => {
    if (!oturumId) return

    try {
      // 204 → açık yoklama yok; istek yardımcısı bunu null'a çeviriyor.
      setDurum(await yoklamaDurumu(oturumId, onUnauthorized))
    } catch {
      /* geçici ağ hatası ekrandaki sayıları silmesin */
    }
  }, [oturumId, onUnauthorized])

  useEffect(() => { oku() }, [oku])

  // Yalnızca AÇIK yoklamada yenileniyor: kapalıyken saniyede bir istek
  // atmak, hiçbir şeyin değişmediği bir ekranı sürekli sorgulamak olurdu.
  useEffect(() => {
    if (!durum) return undefined

    const zamanlayici = setInterval(oku, YENILEME_MS)
    return () => clearInterval(zamanlayici)
  }, [durum, oku])

  const baslat = async () => {
    setMesgul(true)
    setHata(null)

    try {
      setDurum(await yoklamaBaslat(oturumId, soru, Number(grupBoyu), onUnauthorized))
    } catch (err) {
      setHata(err.message || 'Yoklama başlatılamadı.')
    } finally {
      setMesgul(false)
    }
  }

  const bitir = async () => {
    setMesgul(true)
    try {
      await yoklamaBitir(oturumId, onUnauthorized)
      setDurum(null)
    } catch (err) {
      setHata(err.message || 'Yoklama kapatılamadı.')
    } finally {
      setMesgul(false)
    }
  }

  return (
    <div className="yoklama">
      <h3>Yoklama</h3>

      {!durum ? (
        <>
          <label className="yoklama-alan">
            <span>Soru</span>
            <input
              type="text"
              value={soru}
              maxLength={120}
              onChange={(e) => setSoru(e.target.value)}
            />
          </label>

          {/* GRUP SAYISINI REHBER GİRİYOR: misafirler kayıtlı olmadığı için
              sunucu toplamı bilemiyor. "15 kişinin 13'ü" cümlesindeki 15
              bu alandan geliyor. */}
          <label className="yoklama-alan">
            <span>Grup kaç kişi?</span>
            <input
              type="number"
              min={1}
              max={500}
              value={grupBoyu}
              onChange={(e) => setGrupBoyu(e.target.value)}
            />
          </label>

          <button type="button" className="btn-primary genis" onClick={baslat} disabled={mesgul}>
            {mesgul ? 'Başlatılıyor…' : 'Yoklama başlat'}
          </button>
        </>
      ) : (
        <>
          <p className="yoklama-soru">{durum.soru}</p>

          {/* ÖZET CÜMLE — kullanıcının istediği biçim.
              Sayıları tek tek okumak yerine tek bakışta anlaşılan bir
              cümle: rehber sahada, elinde telefonla ve acelesi var. */}
          <p className="yoklama-ozet">
            <strong>{durum.grupBoyu} kişinin {durum.buradayim}’i</strong> buradayım dedi.
          </p>

          <div className="yoklama-sayilar">
            <span className="olumlu">{durum.buradayim} buradayım</span>
            <span className="olumsuz">{durum.degilim} değil</span>
            {/* Acil SIFIRKEN DE gösteriliyor: rehber "acil var mı?" diye
                aradığında yerinin sabit olması, olmadığında da bakacağı
                yeri bilmesi demek. */}
            <span className={durum.acil > 0 ? 'acil vurgulu' : 'acil'}>
              {durum.acil} acil
            </span>
            <span className="muted">{durum.cevapsiz} cevapsız</span>
          </div>

          {/* KİŞİ DÖKÜMÜ — isim ve (acilse) telefon.
              Yalnızca CEVAP VERENLER listeleniyor; boş satırlarla listeyi
              uzatmanın kimseye faydası yok. Acil en üstte geliyor çünkü
              sunucu zaten öyle sıralayıp gönderiyor. */}
          {durum.kisiler.length > 0 && (
            <ul className="yoklama-kisiler">
              {durum.kisiler.map((k, i) => (
                <li key={i} className={k.cevap === 'Acil' ? 'acil' : undefined}>
                  <span className="yoklama-kisi-ad">
                    {k.ad || <span className="muted">İsimsiz</span>}
                  </span>
                  <span className={`yoklama-kisi-rozet ${
                    k.cevap === 'Buradayim' ? 'olumlu' : k.cevap === 'Acil' ? 'acil' : 'olumsuz'
                  }`}
                  >
                    {k.cevap === 'Buradayim' ? 'Buradayım' : k.cevap === 'Acil' ? 'Acil' : 'Değil'}
                  </span>

                  {/* Telefon yalnızca VARSA görünüyor — çoğunlukla Acil'de.
                      tel: bağlantısı rehberin telefonundaki arama/kayıtlı
                      kişiler uygulamasını doğrudan açıyor. */}
                  {k.telefon && (
                    <a className="yoklama-kisi-telefon" href={`tel:${k.telefon}`}>
                      {k.telefon} — Ara
                    </a>
                  )}
                </li>
              ))}
            </ul>
          )}

          <div className="yoklama-dugmeler">
            <button type="button" className="btn-ghost kucuk" onClick={baslat} disabled={mesgul}>
              Yeniden sor
            </button>
            <button type="button" className="btn-ghost kucuk" onClick={bitir} disabled={mesgul}>
              Yoklamayı kapat
            </button>
          </div>
        </>
      )}

      {hata && <p className="analiz-engel">{hata}</p>}
    </div>
  )
}

// ---------------------------------------------------------------------------
//  MİSAFİR
// ---------------------------------------------------------------------------

// Ad ve telefon TARAYICIDA saklanıyor (misafirAnahtari.js ile aynı gerekçe):
// aynı kişi başka bir yoklamaya ya da sayfayı yeniledikten sonra cevap
// verirken adını yeniden yazmak zorunda kalmasın.
const AD_ANAHTARI = 'staj_yoklama_ad'
const TELEFON_ANAHTARI = 'staj_yoklama_telefon'

function depodanOku(anahtar) {
  try {
    return localStorage.getItem(anahtar) ?? ''
  } catch {
    return ''
  }
}

function depoyaYaz(anahtar, deger) {
  try {
    if (deger) localStorage.setItem(anahtar, deger)
    else localStorage.removeItem(anahtar)
  } catch {
    /* depolama kapalıysa (gizli sekme) sessizce geç — özellik yine çalışır */
  }
}

export function YoklamaMisafir({ kod, soru }) {
  const [cevabim, setCevabim] = useState(null)
  const [hata, setHata] = useState(null)
  const [mesgul, setMesgul] = useState(false)

  const [ad, setAd] = useState(() => depodanOku(AD_ANAHTARI))
  const [telefon, setTelefon] = useState(() => depodanOku(TELEFON_ANAHTARI))
  // Telefon alanı yalnızca "Acil" seçilince açılıyor: diğer iki cevapta
  // istemek gereksiz bir engel, acilde ise rehberin geri araması için gerekli.
  const [telefonAcik, setTelefonAcik] = useState(false)

  const cevapla = async (deger) => {
    // ACİL seçildi ama telefon alanı henüz açılmadıysa: ÖNCE aç, gönderme.
    // Kullanıcı numarasını görmeden "Acil" basıp anında göndermiş olmasın —
    // bir sonraki tıklamada asıl isteği yapıyoruz.
    if (deger === 'Acil' && !telefonAcik) {
      setTelefonAcik(true)
      return
    }

    setMesgul(true)
    setHata(null)
    depoyaYaz(AD_ANAHTARI, ad.trim())
    depoyaYaz(TELEFON_ANAHTARI, telefon.trim())

    try {
      const sonuc = await misafirYoklamaCevabi(kod, misafirAnahtari(), deger, {
        ad: ad.trim(),
        telefon: deger === 'Acil' ? telefon.trim() : '',
      })

      // null → yoklama bu arada kapandı. Hata göstermek yerine durumu
      // söylüyoruz: kullanıcı yanlış bir şey yapmadı.
      setCevabim(sonuc ? deger : 'kapandi')
    } catch (err) {
      setHata(err.message || 'Cevap gönderilemedi.')
    } finally {
      setMesgul(false)
    }
  }

  if (cevabim === 'kapandi') {
    return <p className="yoklama-misafir muted">Yoklama kapandı.</p>
  }

  return (
    <div className="yoklama-misafir">
      <p className="yoklama-soru">{soru || 'Rehberiniz yoklama yapıyor'}</p>

      {cevabim ? (
        <p className="yoklama-onay">
          Cevabınız alındı: <strong>
            {CEVAPLAR.find((c) => c.deger === cevabim)?.etiket}
          </strong>
          {' · '}
          {/* Fikrini değiştirebilmeli: sunucu aynı anahtarın cevabını
              güncelliyor, sayı iki kez artmıyor. */}
          <button
            type="button"
            className="baglanti-dugme"
            onClick={() => { setCevabim(null); setTelefonAcik(false) }}
          >
            değiştir
          </button>
        </p>
      ) : (
        <>
          {/* AD İSTEĞE BAĞLI — rehberin listesinde "kim cevapladı" görünsün
              diye. Boş bırakılırsa rehber "İsimsiz" görür, cevap yine sayılır. */}
          <label className="yoklama-alan yoklama-alan-misafir">
            <span>Adınız (opsiyonel)</span>
            <input
              type="text"
              value={ad}
              onChange={(e) => setAd(e.target.value)}
              placeholder="Rehberin görmesi için"
              maxLength={60}
            />
          </label>

          {telefonAcik && (
            <label className="yoklama-alan yoklama-alan-misafir">
              <span>Telefon numaranız</span>
              <input
                type="tel"
                value={telefon}
                onChange={(e) => setTelefon(e.target.value)}
                placeholder="Rehber sizi geri arasın"
                maxLength={30}
                autoFocus
              />
            </label>
          )}

          <div className="yoklama-cevaplar">
            {CEVAPLAR.map((c) => (
              <button
                key={c.deger}
                type="button"
                className={`yoklama-cevap ${c.sinif}`}
                onClick={() => cevapla(c.deger)}
                disabled={mesgul}
              >
                {c.deger === 'Acil' && telefonAcik ? 'Acil durumu gönder' : c.etiket}
              </button>
            ))}
          </div>
        </>
      )}

      {hata && <p className="analiz-hata">{hata}</p>}
    </div>
  )
}
