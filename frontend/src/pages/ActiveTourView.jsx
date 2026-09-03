// ============================================================================
//  ACTIVE TOUR VIEW — canlı turun izleme ekranı
//
//  Gösterdiği üç şey:
//      • mevcut durak ve orada kalan süre
//      • turun kalan süresi
//      • sıradaki durağa mesafe / tahmini süre
//
//  ---- İKİ ROL, İKİ EKRAN (ama tek bileşen) ----
//      Guide       → "Sonraki Durağa Geç": oturumun durumunu ilerletir.
//      Participant → sadece izler + "Yol Tarifi Al": cihazın harita
//                    uygulamasına yönlendirir.
//
//  Ayrı iki bileşen de yazılabilirdi. Yazmadık çünkü ekranın %90'ı ortak
//  (aynı durak kartları, aynı sayaçlar) ve ikiye bölünseydi her düzeltme iki
//  dosyada tekrarlanırdı — biri unutulduğunda rehber ile katılımcı aynı turda
//  FARKLI sayılar görürdü. Rol farkı tek bir yerde, eylem çubuğunda.
//
//  ---- ROL NEREDEN OKUNUYOR? ----
//  `rehberMi(oturum)` → sunucunun gönderdiği `myRole` alanı. Kendi
//  kullanıcı id'mizi guideUserId ile karşılaştırmıyoruz (bkz. turDurumu.js).
//  Ve bu yalnızca GÖRÜNÜRLÜK: düğmeyi tarayıcıdan zorla açmak işe yaramaz,
//  sunucu isteği yapanın rolüne bakıp reddediyor.
//
//  ---- CANLI GÜNCELLEME ----
//  Bileşen kendi başına yayın dinlemiyor. Gerek de yok: SignalR'dan gelen
//  oturum mesajı da, buradaki düğmenin ürettiği cevap da AYNI eylemle
//  (EYLEM.OTURUM_GUNCELLENDI) duruma giriyor. Yani ekran, güncellemenin
//  hangi kanaldan geldiğini bilmek zorunda değil.
// ============================================================================

import { useEffect, useMemo, useState } from 'react'

import { DurakIkonu, GuzergahIkonu, HaritaIkonu, SaatIkonu } from '../icons'
import { yolTarifiniAc } from '../haritaLinki'
import TurSimulasyonKontrolu from '../TurSimulasyonKontrolu.jsx'
import { useKonumPaylasimi } from '../useKonumPaylasimi'
import { YoklamaRehber } from '../Yoklama.jsx'
import {
  sonrakiDuragaGec as sonrakiDuragaGecUcu,
  turuSonlandir as turuSonlandirUcu,
} from '../turApi'
import {
  EYLEM,
  OTURUM_DURUMU,
  bagliKatilimcilar,
  izlenenOturum,
  mekanTipi,
  rehberMi,
} from '../turDurumu'
import {
  dakikaMetni,
  durakSirasi,
  durakKalanDakika,
  durakYuzdesi,
  gecenDakika,
  kalanTurDakika,
  mesafeMetni,
  mevcutDurak,
  noktaCoz,
  sonrakiDurak,
  sonrakiDurakTahmini,
} from '../turIlerleme'

/**
 * Sayaçların tazelenme sıklığı (ms).
 *
 * Kalan süreler DAKİKA cinsinden gösteriliyor; saniyede bir yeniden çizmek
 * aynı sayıyı 60 kez yazmak olurdu. 30 saniye, dakika değişimini gözle fark
 * edilir bir gecikme olmadan yakalıyor.
 */
const TIK_MS = 30_000

/** Durum → ekranda gösterilecek etiket. */
const DURUM_ETIKETLERI = {
  [OTURUM_DURUMU.PLANLANDI]: 'Başlamadı',
  [OTURUM_DURUMU.YAYINDA]: 'Yayında',
  [OTURUM_DURUMU.DURAKLATILDI]: 'Duraklatıldı',
  [OTURUM_DURUMU.TAMAMLANDI]: 'Tamamlandı',
  [OTURUM_DURUMU.IPTAL]: 'İptal edildi',
}

export default function ActiveTourView({
  /** turDurumu.js reducer'ının durumu. */
  durum,
  /** Aynı reducer'ın dispatch'i — güncelleme buradan duruma giriyor. */
  dispatch,
  /** Turun ulaşım tipi; mesafe/süre tahminleri ve harita bağlantısı buna göre. */
  ulasimTipi = 'Yaya',
  /** Oturum servisi — testte sahte fonksiyon geçiliyor (bkz. TourBuilder). */
  sonrakiDuragaGec = sonrakiDuragaGecUcu,
  /** Turu sonlandıran servis; aynı gerekçeyle dışarıdan geçilebiliyor. */
  turuSonlandir = turuSonlandirUcu,
  /** Harita bağlantısını açan fonksiyon; testte gerçek sekme açılmasın diye ayrık. */
  haritayiAc = yolTarifiniAc,
  /**
   * Rotayı haritada oynatan simülasyon (useTurSimulasyonu çıktısı).
   *
   * MapPage'den geliyor çünkü aracı çizen katman orada; burada yalnızca
   * düğme var. Verilmezse kontrol hiç çizilmiyor — bileşen haritasız
   * bağlamlarda (testler) da çalışsın.
   */
  simulasyon = null,
  onUnauthorized,
}) {
  // Sayaçları ilerleten saat. Bileşen dışından enjekte edilmiyor: "şu an"
  // gerçekten şu an olmalı; testler sabit tarihli oturum verisiyle çalışıyor.
  const [simdi, setSimdi] = useState(() => new Date())
  const [gonderiliyor, setGonderiliyor] = useState(false)
  const [hata, setHata] = useState(null)

  useEffect(() => {
    const zamanlayici = setInterval(() => setSimdi(new Date()), TIK_MS)
    return () => clearInterval(zamanlayici)
  }, [])

  const oturum = izlenenOturum(durum)
  const tur = oturum ? durum.turlar[oturum.tourId] ?? null : null

  // Konum yayını — yalnızca rehberde kullanılıyor ama kanca KOŞULSUZ
  // çağrılıyor: React kancaları koşullu çağrılamaz.
  const konum = useKonumPaylasimi(oturum?.id, onUnauthorized)

  // Türetilmiş değerlerin TAMAMI tek yerde. Her biri ayrı hesaplansaydı
  // bileşenin farklı yerleri farklı "şu an"larla çalışabilirdi.
  const ilerleme = useMemo(() => {
    if (!oturum || !tur) return null

    return {
      mevcut: mevcutDurak(tur, oturum),
      sonraki: sonrakiDurak(tur, oturum),
      sira: durakSirasi(tur, oturum),
      yuzde: durakYuzdesi(tur, oturum),
      durakKalan: durakKalanDakika(tur, oturum, simdi),
      turKalan: kalanTurDakika(tur, oturum, ulasimTipi, simdi),
      gecen: gecenDakika(oturum, simdi),
      tahmin: sonrakiDurakTahmini(tur, oturum, ulasimTipi),
    }
  }, [oturum, tur, ulasimTipi, simdi])

  // Takip edilen tur yoksa ekran hiç çizilmiyor: boş bir kart göstermek
  // "bir şeyler yüklenmiyor" izlenimi verirdi.
  if (!oturum || !tur || !ilerleme) return null

  const rehber = rehberMi(oturum)
  const durakSayisi = tur.waypoints?.length ?? 0
  const kapandi = oturum.status === OTURUM_DURUMU.TAMAMLANDI
    || oturum.status === OTURUM_DURUMU.IPTAL

  /**
   * REHBERİN EYLEMİ — oturum state'ini ilerletir.
   *
   * İyimser güncelleme YOK: sunucunun döndürdüğü oturum kaydı duruma
   * yazılıyor. Önce yerel state'i ilerletseydik, istek reddedildiğinde
   * (rehberlik devredilmiş, tur kapanmış) rehber grubu olmayan bir durakta
   * sanırdı — üstelik katılımcıların ekranı ondan farklı olurdu.
   */
  const duragaGec = async () => {
    if (!ilerleme.sonraki || gonderiliyor) return

    setGonderiliyor(true)
    setHata(null)

    try {
      const guncel = await sonrakiDuragaGec(oturum.id, ilerleme.sonraki.id, onUnauthorized)
      dispatch({ tur: EYLEM.OTURUM_GUNCELLENDI, oturum: guncel })
    } catch (err) {
      setHata(err.message || 'Durak ilerletilemedi.')
    } finally {
      setGonderiliyor(false)
    }
  }

  /**
   * TURU SONLANDIRIR.
   *
   * ---- NEDEN ONAY SORULUYOR? ----
   * Geri alınamayan bir işlem: oturum kapanınca katılım kodu geçersizleşiyor
   * ve gruptaki herkesin ekranı "tur sona erdi"ye dönüyor. Yanlışlıkla
   * basılan bir düğme, sahadaki bir grubu takipsiz bırakırdı.
   */
  const sonlandir = async () => {
    if (gonderiliyor) return

    const kalanDurak = (tur.waypoints?.length ?? 0) - ilerleme.sira
    const mesaj = kalanDurak > 0
      ? `Turda ${kalanDurak} durak daha var. Yine de sonlandırılsın mı?`
      : 'Tur sonlandırılsın mı?'

    if (typeof window !== 'undefined' && !window.confirm(mesaj)) return

    setGonderiliyor(true)
    setHata(null)

    try {
      const guncel = await turuSonlandir(oturum.id, onUnauthorized)
      dispatch({ tur: EYLEM.OTURUM_GUNCELLENDI, oturum: guncel })
    } catch (err) {
      setHata(err.message || 'Tur sonlandırılamadı.')
    } finally {
      setGonderiliyor(false)
    }
  }

  /**
   * KATILIMCININ EYLEMİ — cihazın harita uygulamasına yönlendirir.
   *
   * Hedef SIRADAKİ durak: katılımcının sorduğu soru "şimdi nereye gidiyoruz".
   * Son duraktaysa (sıradaki yok) mevcut durağa yönlendiriliyor — geç kalan
   * biri gruba orada yetişecek.
   */
  const yolTarifi = () => {
    const hedefDurak = ilerleme.sonraki ?? ilerleme.mevcut
    const nokta = noktaCoz(hedefDurak?.wkt)
    if (!nokta) return

    haritayiAc({
      lat: nokta.lat,
      lon: nokta.lon,
      ad: hedefDurak.name,
      ulasimTipi,
    })
  }

  const yolTarifiHedefi = ilerleme.sonraki ?? ilerleme.mevcut

  return (
    <section className="panel-section aktif-tur">
      <h2>
        <span className="tool-icon"><GuzergahIkonu /></span>
        {tur.name}
      </h2>

      {/* ---------- Durum şeridi ---------- */}
      <div className="aktif-tur-ust">
        <span className={`aktif-tur-rozet ${oturum.status?.toLowerCase()}`}>
          {DURUM_ETIKETLERI[oturum.status] ?? oturum.status}
        </span>
        <span className="muted">
          {rehber ? 'Rehber' : 'Katılımcı'}
          {' · '}
          {bagliKatilimcilar(oturum).length} kişi
        </span>
      </div>

      {/* ---------- İlerleme ---------- */}
      <div className="aktif-tur-ilerleme">
        <div className="puan-cubugu">
          <span style={{ width: `${ilerleme.yuzde}%`, background: tur.color }} />
        </div>
        <p className="muted">
          <strong>{ilerleme.sira}</strong> / {durakSayisi} durak
          {' · '}başlayalı {dakikaMetni(ilerleme.gecen)}
        </p>
      </div>

      {/* ---------- Mevcut durak ---------- */}
      <div className="aktif-tur-kart mevcut">
        <span className="aktif-tur-etiket">Şu anda</span>

        {ilerleme.mevcut ? (
          <>
            <p className="aktif-tur-durak">
              <span className="tool-icon"><DurakIkonu size={15} /></span>
              <strong>{ilerleme.mevcut.name}</strong>
            </p>
            <p className="muted">
              {mekanTipi(ilerleme.mevcut.venueType).etiket}
              {' · '}
              {/* Kalış süresi dolduğunda "0 dk" yazmak yerine açıkça söylüyoruz:
                  rehberin karar vermesi gereken an tam olarak burası. */}
              {ilerleme.durakKalan > 0
                ? `kalkışa ${dakikaMetni(ilerleme.durakKalan)}`
                : 'planlanan süre doldu'}
            </p>
          </>
        ) : (
          <p className="muted">Tur ilk durağa doğru yolda.</p>
        )}
      </div>

      {/* ---------- Sıradaki durak ---------- */}
      <div className="aktif-tur-kart">
        <span className="aktif-tur-etiket">Sıradaki</span>

        {ilerleme.sonraki ? (
          <>
            <p className="aktif-tur-durak">
              <span className="tool-icon"><DurakIkonu size={15} /></span>
              <strong>{ilerleme.sonraki.name}</strong>
            </p>
            <p className="muted">
              {ilerleme.tahmin
                // "≈" bilinçli: mesafe kuş uçuşundan türetilmiş bir TAHMİN
                // (bkz. turIlerleme.js). Kesin bir sayı gibi göstermek,
                // olmadığı bir doğruluk iddia etmek olurdu.
                ? `≈ ${mesafeMetni(ilerleme.tahmin.metre)} · ≈ ${dakikaMetni(ilerleme.tahmin.dakika)}`
                : 'mesafe hesaplanamadı'}
            </p>
          </>
        ) : (
          <p className="muted">Son duraktasınız — tur tamamlanmak üzere.</p>
        )}
      </div>

      {/* ---------- Kalan süre ---------- */}
      <p className="tur-ozet">
        <SaatIkonu size={13} />
        Turun kalan süresi: <strong>{dakikaMetni(ilerleme.turKalan)}</strong>
      </p>

      {/* ---------- Role göre eylem ---------- */}
      {kapandi ? (
        <p className="analiz-durum ok">Tur sona erdi.</p>
      ) : rehber ? (
        <>
          {/* SON DURAKTA SIRALAMA DEĞİŞİYOR: ilerletme düğmesinin yapacağı iş
              kalmadığında sonlandırma birincil eylem oluyor. Kapalı bir düğmeyi
              ekranın en görünür yerinde bırakmak, kullanıcıyı "şimdi ne
              yapacağım?" diye bırakmak olurdu. */}
          {ilerleme.sonraki ? (
            <>
              <button
                type="button"
                className="btn-primary genis"
                onClick={duragaGec}
                disabled={gonderiliyor}
                title={`Sonraki durak: ${ilerleme.sonraki.name}`}
              >
                {gonderiliyor ? 'Geçiliyor…' : 'Sonraki Durağa Geç'}
              </button>

              {/* Tur her an sonlandırılabilir: grup dağılabilir, hava
                  bozabilir. Son durağı beklemek zorunda bırakmıyoruz. */}
              <button
                type="button"
                className="btn-ghost genis"
                onClick={sonlandir}
                disabled={gonderiliyor}
              >
                Turu Sonlandır
              </button>
            </>
          ) : (
            <button
              type="button"
              className="btn-primary genis"
              onClick={sonlandir}
              disabled={gonderiliyor}
              title="Son duraktasınız — turu bitir"
            >
              {gonderiliyor ? 'Sonlandırılıyor…' : 'Turu Sonlandır'}
            </button>
          )}
        </>
      ) : (
        <button
          type="button"
          className="btn-primary genis"
          onClick={yolTarifi}
          disabled={!noktaCoz(yolTarifiHedefi?.wkt)}
          title={yolTarifiHedefi
            ? `${yolTarifiHedefi.name} için yol tarifi`
            : 'Hedef durak yok'}
        >
          <span className="tool-icon"><HaritaIkonu size={14} /></span>
          Yol Tarifi Al
        </button>
      )}

      {!rehber && !kapandi && (
        <p className="tool-hint muted">
          Yol tarifi cihazınızın harita uygulamasında açılır; başlangıç noktası
          olarak anlık konumunuz kullanılır.
        </p>
      )}

      {/* ---------- YOKLAMA (yalnızca REHBER) ----------
          "Şu an kimler burada?" — rehber soruyor, bağlantıyı açan herkes tek
          dokunuşla cevaplıyor, sayılar burada birikiyor. */}
      {rehber && !kapandi && (
        <YoklamaRehber oturumId={oturum.id} onUnauthorized={onUnauthorized} />
      )}

      {/* ---------- CANLI KONUM (yalnızca REHBER) ----------
          Rehberin telefonu konumunu yayınlıyor; bağlantıyı açan misafirler
          grubu haritada canlı görüyor. Katılımcıda düğme yok: yayınlanan
          şey turun konumu ve onu turu yöneten kişi belirliyor.

          Kapatma yalnızca izlemeyi bırakmıyor, sunucudaki konumu da siliyor
          (bkz. useKonumPaylasimi). */}
      {rehber && !kapandi && (
        <div className="tur-konum-yayini">
          <button
            type="button"
            className={konum.paylasiliyor ? 'btn-ghost genis aktif' : 'btn-ghost genis'}
            onClick={konum.paylasiliyor ? konum.durdur : konum.baslat}
          >
            {konum.paylasiliyor ? 'Konum paylaşımını durdur' : 'Konumumu paylaş'}
          </button>

          {konum.paylasiliyor && (
            <p className="tool-hint muted">
              Grubunuz sizi haritada canlı görüyor
              {konum.sonKonum?.dogruluk
                ? ` (±${Math.round(konum.sonKonum.dogruluk)} m).`
                : '.'}
            </p>
          )}

          {konum.hata && <p className="analiz-engel">{konum.hata}</p>}
        </div>
      )}

      {/* ROTA SİMÜLASYONU — HER İKİ ROLDE DE VAR.
          Turu ilerletmek rehberin işi (gerçek durum değişikliği, herkesi
          etkiliyor); rotayı oynatmak ise yalnızca bir gösterim ve izleyicinin
          kendi ekranında kalıyor. Katılımcı "bugün nereleri gezeceğiz?"
          sorusunu bununla cevaplıyor. */}
      <TurSimulasyonKontrolu
        simulasyon={simulasyon}
        ipucu="Turun rotasını haritada baştan sona oynatın."
      />

      {hata && <p className="analiz-hata">{hata}</p>}
    </section>
  )
}
