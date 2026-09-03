import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'

import { misafirTuruGetir, oturumaKatil } from '../turApi'
import { getToken } from '../auth'
import { yolTarifiniAc } from '../haritaLinki'
import { noktaCoz, mesafeMetre, mesafeMetni } from '../turIlerleme'
import { GuzergahIkonu, HaritaIkonu, KullaniciIkonu, TelefonIkonu } from '../icons'
import MisafirHarita from '../MisafirHarita.jsx'
import { YoklamaMisafir } from '../Yoklama.jsx'

// ============================================================================
//  MİSAFİR TUR EKRANI (/tur/:kod)
//
//  ---- BU EKRAN NEDEN BAŞTAN YAZILDI? ----
//  Kullanıcı geri bildirimi aynen şöyleydi: "bağlantıyı kopyalayıp
//  paylaştığınızda direkt harita uygulaması açılıyor, sadece bildirimle tura
//  girmiş olduğumu söylüyor, kodu gireceğim bir alan çıkmıyor... Ben kayıt
//  yapmadan giriş yapmadan sadece verilen kod kullanılarak misafir olarak
//  görünsün ve ilgili tur bilgilerini görebilsin istiyordum."
//
//  Eski akış (TuraKatil.jsx) üç yanlış şeyi birden yapıyordu:
//    1. Rota RequireAuth altındaydı → bağlantı önce GİRİŞ ekranına düşüyordu.
//       Turu görmek için hesap açmak gerekiyordu; paylaşımın anlamı kalmıyordu.
//    2. Katılınca haritanın TAMAMINA yönlendiriyordu → misafir, turla ilgisi
//       olmayan bir çizim/POI/yönetim uygulamasının içinde buluyordu kendini.
//    3. Turun kendisini hiç göstermiyordu; yalnızca "katıldınız" diyordu.
//
//  Bu ekran kendi başına duruyor: giriş yok, harita uygulaması yok, yazma
//  yok. Yalnızca turun bilgisi.
//
//  ---- NEDEN HARİTA ÇİZMİYOR? ----
//  Misafirlerin çoğu telefonda ve tur sırasında bakıyor. Ekrana OpenLayers
//  haritası koymak hem ağır (uygulamanın en büyük paketi) hem de gereksiz:
//  telefonda yol tarifi zaten cihazın kendi harita uygulamasında daha iyi.
//  Her durakta "Yol tarifi" düğmesi var; asıl soru olan "şu an neredeyiz,
//  sırada ne var" ise listede işaretli duruyor.
//
//  ---- NEDEN DÜZENLİ ARALIKLA SORUYOR? ----
//  Rehber "sonraki durağa geç" dediğinde misafirin ekranı da ilerlemeli.
//  SignalR kullanmadık: yayın kanalı kimlik doğrulaması üzerine kurulu ve
//  misafirin kimliği yok.
//
//  Aralık İKİ KADEMELİ: rehber konumunu yayınlarken 8 saniye, yayın kapalıyken
//  20 saniye. Sebep, verinin değişme hızı — yürüyen bir grup 20 saniyede bir
//  sokak ilerliyor ve harita geride kalıyor; oysa "sonraki durak" bilgisi
//  saatte birkaç kez değişiyor. Tek aralık seçseydik ya harita takılırdı ya
//  da boşuna istek atardık.
// ============================================================================

/** Yoklama aralığı (ms) — rehber konum yayınlarken / yayınlamazken. */
const YOKLAMA_CANLI_MS = 8_000
const YOKLAMA_MS = 20_000

export default function TurMisafir() {
  const { kod: adrestekiKod } = useParams()
  const navigate = useNavigate()

  const [kod, setKod] = useState(adrestekiKod ?? '')
  const [tur, setTur] = useState(null)
  const [durum, setDurum] = useState(adrestekiKod ? 'yukleniyor' : 'kod-bekleniyor')
  const [hata, setHata] = useState(null)

  const getir = useCallback(async (aranan, sessiz = false) => {
    if (!aranan) return

    if (!sessiz) {
      setDurum('yukleniyor')
      setHata(null)
    }

    try {
      setTur(await misafirTuruGetir(aranan))
      setDurum('hazir')
    } catch (err) {
      // SESSİZ YENİLEMEDE HATA YUTULUYOR: ağ bir an kesildiğinde ekranda
      // duran turu silip hata göstermek, kullanıcının elindeki bilgiyi
      // geçici bir aksaklık yüzünden kaybettirmek olurdu.
      if (sessiz) return

      setHata(err.message || 'Tur bilgisi alınamadı.')
      setDurum('hata')
    }
  }, [])

  useEffect(() => {
    if (adrestekiKod) getir(adrestekiKod)
  }, [adrestekiKod, getir])

  // Rehber turu ilerlettiğinde misafirin ekranı da ilerlesin.
  useEffect(() => {
    if (durum !== 'hazir') return undefined

    const aralik = tur?.guideLat != null ? YOKLAMA_CANLI_MS : YOKLAMA_MS
    const zamanlayici = setInterval(() => getir(kod, true), aralik)
    return () => clearInterval(zamanlayici)
  }, [durum, kod, getir, tur?.guideLat])

  const kodGonder = (e) => {
    e.preventDefault()
    const temiz = kod.trim().toUpperCase()

    if (temiz.length < 4) {
      setHata('Katılım kodu 6 karakterdir.')
      return
    }

    // Adres çubuğuna da yazılıyor: misafir sayfayı yenilediğinde ya da
    // sekmeyi paylaştığında kodu bir daha girmek zorunda kalmasın.
    navigate(`/tur/${temiz}`, { replace: true })
  }

  // ---- Kod giriş ekranı -------------------------------------------------
  if (durum === 'kod-bekleniyor' || durum === 'hata') {
    return (
      <div className="misafir-sayfa">
        <div className="misafir-kart misafir-kod-kart">
          <h1>
            <span className="tool-icon"><GuzergahIkonu /></span>
            Tura katıl
          </h1>

          <p className="muted">
            Rehberinizin verdiği <strong>katılım kodunu</strong> girin.
            Hesap açmanıza ya da giriş yapmanıza gerek yok.
          </p>

          <form onSubmit={kodGonder}>
            <input
              type="text"
              value={kod}
              onChange={(e) => setKod(e.target.value.toUpperCase())}
              placeholder="ÖRN. K7M2PQ"
              maxLength={8}
              autoComplete="off"
              autoCapitalize="characters"
              aria-label="Katılım kodu"
              className="misafir-kod-girdi"
            />

            <button type="submit" className="btn-primary genis">Turu göster</button>
          </form>

          {hata && <p className="analiz-hata">{hata}</p>}
        </div>
      </div>
    )
  }

  if (durum === 'yukleniyor') {
    return (
      <div className="misafir-sayfa">
        <div className="misafir-kart"><p className="muted">Tur yükleniyor…</p></div>
      </div>
    )
  }

  // ---- Tur ekranı -------------------------------------------------------
  const mevcutSira = tur.currentWaypointOrder ?? null
  const duraklar = tur.waypoints ?? []

  /** Bu duraktan bir sonrakine kuş uçuşu mesafe — "ne kadar kaldı" hissi için. */
  const sonrakiMesafe = (i) => {
    const a = noktaCoz(duraklar[i]?.wkt)
    const b = noktaCoz(duraklar[i + 1]?.wkt)
    return a && b ? mesafeMetre(a, b) : null
  }

  return (
    <div className="misafir-sayfa">
      <div className="misafir-kart">
        <header className="misafir-baslik" style={{ borderColor: tur.color }}>
          <h1>{tur.tourName}</h1>
          <p className="muted">
            <KullaniciIkonu size={12} /> Rehber: {tur.guideUserName || '—'}
            {tur.status === 'Paused' && <span className="misafir-rozet">Ara verildi</span>}
          </p>
        </header>

        {/* REHBERİ ARA — gruptan ayrılan kişinin ilk ihtiyacı.
            Uygulamada mesajlaşma yok ve olmasına da gerek yok: kaybolan
            kişi bildirim beklemez, telefon eder.

            `tel:` bağlantısı cihazın kendi arama ekranını açıyor; numarayı
            ekranda da yazıyoruz çünkü bilgisayardan bakanda `tel:` çoğu
            zaman hiçbir şey yapmıyor — o kişi numarayı okuyup kendi
            telefonundan arayabilmeli.

            Düğme yalnızca rehber numarasını GİRDİYSE ve tur CANLIYSA
            çıkıyor; ikisinin de kararı sunucuda (MisafirTurDto.GuidePhone). */}
        {tur.guidePhone && (
          <a className="misafir-rehber-ara" href={`tel:${tur.guidePhone}`}>
            <TelefonIkonu size={15} />
            <span>
              <strong>Rehberi ara</strong>
              <small>{tur.guidePhone}</small>
            </span>
          </a>
        )}

        {/* ŞU AN NEREDEYİZ — ekranın en üstünde ve en büyük yazıda.
            Misafirin turu açma sebebi tek bir soru: "grup nerede?" */}
        <section className="misafir-simdi">
          {mevcutSira ? (
            <>
              <span className="misafir-etiket">Şu an</span>
              <strong>{tur.currentWaypointName}</strong>
              {tur.nextWaypointName && (
                <p className="muted">Sırada: {tur.nextWaypointName}</p>
              )}
            </>
          ) : (
            <>
              <span className="misafir-etiket">Tur henüz başlamadı</span>
              <strong>{duraklar[0]?.name ?? '—'}</strong>
              <p className="muted">İlk durak</p>
            </>
          )}

          <div className="misafir-ilerleme" aria-hidden="true">
            <span style={{ width: `${Math.round(tur.progressPercent ?? 0)}%`, background: tur.color }} />
          </div>
          <p className="muted">
            {mevcutSira ?? 0} / {duraklar.length} durak
          </p>
        </section>

        {/* AÇIK YOKLAMA EN ÜSTTE: rehber bir soru sorduysa misafirin
            yapacağı ilk iş onu cevaplamak, haritaya bakmak değil. */}
        {tur.acikYoklamaSorusu && (
          <YoklamaMisafir kod={kod} soru={tur.acikYoklamaSorusu} />
        )}

        {/* HARİTA — "şu an neredeyiz" sorusunun görsel cevabı.
            Rota, duraklar ve (yayın açıksa) rehberin canlı konumu. */}
        <MisafirHarita tur={tur} />

        {tur.guideLat != null ? (
          <p className="misafir-canli">
            <span className="misafir-canli-nokta" aria-hidden="true" />
            Rehberin konumu haritada canlı
          </p>
        ) : (
          <p className="misafir-alt muted">
            Rehber konum paylaşmıyor; harita turun rotasını gösteriyor.
          </p>
        )}

        <ol className="misafir-durak-listesi">
          {duraklar.map((durak, i) => {
            const gecildi = mevcutSira != null && durak.order < mevcutSira
            const simdi = durak.order === mevcutSira
            const mesafe = sonrakiMesafe(i)

            return (
              <li
                key={durak.order}
                className={`${gecildi ? 'gecildi' : ''}${simdi ? ' simdi' : ''}`}
              >
                <div className="misafir-durak">
                  <span className="misafir-sira" style={simdi ? { background: tur.color } : undefined}>
                    {durak.order}
                  </span>

                  <div className="misafir-durak-bilgi">
                    <strong>{durak.name}</strong>
                    <span className="muted">
                      {durak.dwellMinutes} dk
                      {durak.note ? ` · ${durak.note}` : ''}
                    </span>
                  </div>

                  {/* Telefondaki asıl işlevsel düğme: adım adım tarif
                      cihazın kendi harita uygulamasında açılıyor. */}
                  <button
                    type="button"
                    className="btn-ghost kucuk"
                    onClick={() => {
                      const n = noktaCoz(durak.wkt)
                      if (n) yolTarifiniAc({ lat: n.lat, lon: n.lon, ad: durak.name })
                    }}
                    aria-label={`${durak.name} için yol tarifi`}
                    title="Yol tarifi"
                  >
                    <HaritaIkonu size={13} />
                  </button>
                </div>

                {mesafe != null && (
                  <p className="misafir-bacak muted">↓ ≈ {mesafeMetni(Math.round(mesafe * 1.3))}</p>
                )}
              </li>
            )
          })}
        </ol>

        <p className="misafir-alt muted">
          Bu sayfa turu canlı takip eder; rehber sonraki durağa geçtiğinde
          kendiliğinden güncellenir.
        </p>

        {/* HESABI OLANLAR İÇİN İKİNCİ, İSTEĞE BAĞLI ADIM.
            Misafir görünümü kimseyi katılımcı listesine yazmıyor — bilerek:
            kimliksiz bir ziyaretçiyi "katılımcı" saymak rehberin gördüğü grup
            sayısını yalanlardı. Ama uygulamada zaten hesabı olan biri (örneğin
            yardımcı rehber) listede GÖRÜNMEK isteyebilir. Bağlantı yalnızca
            oturumu açık olanlara çıkıyor: token yoksa bu düğme giriş ekranına
            götüren bir tuzağa dönerdi ve düzelttiğimiz hatayı geri getirirdi. */}
        {getToken() && (
          <button
            type="button"
            className="btn-ghost genis"
            onClick={() => oturumaKatil(kod).then(() => navigate('/map')).catch(() => {})}
          >
            Hesabımla katıl (rehberin listesinde görün)
          </button>
        )}
      </div>
    </div>
  )
}
