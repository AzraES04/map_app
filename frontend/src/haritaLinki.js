// ============================================================================
//  HARİTA DERİN BAĞLANTISI (deep link) — "Yol Tarifi Al"
//
//  Katılımcı bir durağa nasıl gideceğini sorduğunda cevabı biz vermiyoruz:
//  cihazın kendi harita uygulamasına devrediyoruz. Sebep basit — o uygulama
//  kullanıcının konumunu, trafiği, sesli navigasyonu ve indirdiği çevrimdışı
//  haritaları zaten biliyor. Uygulamanın içinde ikinci bir navigasyon yazmak
//  hem gereksiz hem de daha kötü olurdu.
//
//  ---- NEDEN PLATFORMA GÖRE FARKLI ADRES? ----
//  Tek bir adres bütün cihazlarda "uygulamayı açan" bir bağlantı üretmiyor:
//
//    iOS      → maps.apple.com  : Apple Haritalar uygulamasında açılıyor.
//                                 Google adresi verilseydi Google Maps kurulu
//                                 değilse tarayıcıda açılırdı (uygulama değil).
//    Android  → google.com/maps : App Links sayesinde Google Maps
//                                 uygulamasında açılıyor.
//    Masaüstü → google.com/maps : Uygulama yok; tarayıcı sekmesi doğru cevap.
//
//  ---- NEDEN "geo:" ŞEMASI DEĞİL? ----
//  Android'de `geo:` gerçekten CİHAZIN VARSAYILAN harita uygulamasını açar ve
//  bu yönüyle daha doğru görünüyor. Ama `geo:` yalnızca bir NOKTA gösterebilir:
//  yol tarifi kipini (yaya/araç/toplu taşıma) ve "buradan oraya" bilgisini
//  taşıyamıyor. Düğmenin adı "Yol Tarifi Al" olduğu için tarifi taşıyan adres
//  tercih edildi. (`geo:` isteyen bir kurulum için tek değişiklik gereken yer
//  bu dosya — bağlantı üretimi tek noktada.)
//
//  ---- BAŞLANGIÇ NOKTASI GENELDE GÖNDERİLMİYOR ----
//  Varsayılan olarak adreste yalnızca HEDEF var: harita uygulaması başlangıç
//  olarak cihazın anlık konumunu kullanıyor — hem daha doğru (bizim
//  bildiğimiz son konum eski olabilir) hem de kullanıcının konumunu bir
//  URL'e yazmamış oluyoruz.
//
//  TEK İSTİSNA program ekranı: "3. duraktan 4. durağa nasıl gidilir"
//  sorusunda başlangıç kullanıcının konumu DEĞİL, önceki durak. Orada
//  `baslangic` veriliyor ve bu bir konum sızıntısı değil — iki tur durağı da
//  zaten herkese açık mekanlar.
// ============================================================================

/** Ulaşım tipi → Google Maps "travelmode" değeri. */
const GOOGLE_KIPLERI = {
  Yaya: 'walking',
  Arac: 'driving',
  TopluTasima: 'transit',
}

/**
 * Ulaşım tipi → Apple Haritalar "dirflg" değeri.
 * (w = walk, d = drive, r = transit/"rail")
 */
const APPLE_KIPLERI = {
  Yaya: 'w',
  Arac: 'd',
  TopluTasima: 'r',
}

/** Koordinatlarda kaç ondalık basamak yazılacağı — 6 basamak ≈ 11 cm. */
const BASAMAK = 6

/**
 * Çalışılan platformu bulur.
 *
 * Parametre olarak alıyor (global `navigator`'a doğrudan bakmıyor): saf bir
 * fonksiyon testte tarayıcı taklidi kurmadan sınanabiliyor.
 *
 * iPad tespiti özel: iPadOS 13'ten beri Safari kendini "Macintosh" olarak
 * tanıtıyor. Dokunmatik nokta sayısına bakmak, iPad'i masaüstü Mac'ten ayıran
 * pratikteki tek ipucu — buna bakmasaydık iPad kullanıcısı Apple Haritalar
 * yerine tarayıcı sekmesine düşerdi.
 *
 * @returns {'ios'|'android'|'diger'}
 */
export function platformBul(userAgent = '', maxTouchPoints = 0) {
  const ua = String(userAgent)

  if (/iPhone|iPad|iPod/i.test(ua)) return 'ios'
  if (/Android/i.test(ua)) return 'android'
  if (/Macintosh/i.test(ua) && maxTouchPoints > 1) return 'ios'

  return 'diger'
}

/** Tarayıcıdan platformu okur. Tarayıcı yoksa (SSR, test) 'diger'. */
export function mevcutPlatform() {
  if (typeof navigator === 'undefined') return 'diger'
  return platformBul(navigator.userAgent, navigator.maxTouchPoints ?? 0)
}

/**
 * Sayıya çevirir ama BOŞ değerleri sayı saymaz.
 *
 * `Number(null)` ve `Number('')` sıfır döndürüyor. Doğrudan Number
 * kullansaydık, konumu olmayan bir durak (0, 0) noktasına — yani Gine
 * Körfezi'ne — geçerli bir yol tarifi üretirdi.
 */
const sayi = (deger) =>
  (deger === null || deger === undefined || deger === '' ? Number.NaN : Number(deger))

/** Koordinat gerçekten kullanılabilir mi? */
const gecerliKoordinat = (lat, lon) =>
  Number.isFinite(lat) && Number.isFinite(lon)
  && lat >= -90 && lat <= 90
  && lon >= -180 && lon <= 180

/**
 * Hedef durak için yol tarifi bağlantısı üretir.
 *
 * @param {{lat: number, lon: number, ad?: string, ulasimTipi?: string,
 *          platform?: 'ios'|'android'|'diger'}} hedef
 * @returns {string|null}
 *   Koordinat geçersizse null — (0, 0) noktasına, yani Gine Körfezi'ne yol
 *   tarifi vermektense düğmeyi hiç açmamak doğru davranış.
 */
export function haritaLinki({ lat, lon, ad, ulasimTipi = 'Yaya', platform, baslangic } = {}) {
  const enlem = sayi(lat)
  const boylam = sayi(lon)

  if (!gecerliKoordinat(enlem, boylam)) {
    return null
  }

  const hedefPlatform = platform ?? mevcutPlatform()
  const koordinat = `${enlem.toFixed(BASAMAK)},${boylam.toFixed(BASAMAK)}`

  // İsteğe bağlı başlangıç (program ekranındaki bacak tarifi). Geçersizse
  // yok sayılıyor: bozuk bir başlangıç yüzünden yol tarifini hiç açmamak
  // yerine, cihazın konumundan tarif vermek daha iyi.
  const baslangicLat = sayi(baslangic?.lat)
  const baslangicLon = sayi(baslangic?.lon)
  const baslangicVar = gecerliKoordinat(baslangicLat, baslangicLon)
  const baslangicKoordinati = baslangicVar
    ? `${baslangicLat.toFixed(BASAMAK)},${baslangicLon.toFixed(BASAMAK)}`
    : null

  if (hedefPlatform === 'ios') {
    const kip = APPLE_KIPLERI[ulasimTipi] ?? APPLE_KIPLERI.Yaya
    // daddr'a koordinatın YANINDA ad da yazılabilirdi; yazmıyoruz çünkü
    // Apple Haritalar adı ARAMA terimi gibi ele alıp koordinattan sapabiliyor.
    // Ad yalnızca etiket olarak "q" ile gidiyor.
    const parametreler = new URLSearchParams({ daddr: koordinat, dirflg: kip })
    if (baslangicKoordinati) parametreler.set('saddr', baslangicKoordinati)
    if (ad) parametreler.set('q', ad)

    return `https://maps.apple.com/?${parametreler.toString()}`
  }

  const kip = GOOGLE_KIPLERI[ulasimTipi] ?? GOOGLE_KIPLERI.Yaya

  // api=1 — Google'ın KARARLI, sürüm garantili yol tarifi adresi. Eski
  // "maps?daddr=" biçimleri hâlâ çalışıyor ama belgelenmiş değil ve
  // sessizce değişebiliyor.
  const parametreler = new URLSearchParams({
    api: '1',
    destination: koordinat,
    travelmode: kip,
  })

  if (baslangicKoordinati) parametreler.set('origin', baslangicKoordinati)

  return `https://www.google.com/maps/dir/?${parametreler.toString()}`
}

/**
 * Bağlantıyı açar.
 *
 * Açma işini dışarıdan alınabilir yapıyoruz (`ac` parametresi): test,
 * gerçekten bir sekme açmadan "doğru adres verildi mi?" diye sorabiliyor.
 *
 * `noopener,noreferrer`: açılan sayfa `window.opener` üzerinden bizim
 * sekmemizi yönlendirebilirdi (tabnabbing). Harita sitesine güveniyoruz ama
 * bu, güvene bırakılacak bir şey değil.
 *
 * @returns {boolean} bağlantı üretilip açılabildi mi?
 */
export function yolTarifiniAc(hedef, ac) {
  const adres = haritaLinki(hedef)
  if (!adres) return false

  const acici = ac ?? ((url) => {
    if (typeof window !== 'undefined') {
      window.open(url, '_blank', 'noopener,noreferrer')
    }
  })

  acici(adres)
  return true
}
