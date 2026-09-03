// ============================================================================
//  Tur modülü API çağrıları.
//
//  Hepsi authFetch üzerinden gider: token'ı ekler, 401 gelirse oturumu kapatıp
//  login'e yönlendirir (ulasimApi.js / poiApi.js ile aynı sözleşme).
// ============================================================================

import { authFetch } from './auth'

/** Ortak istek yardımcısı — ulasimApi.js'teki ile birebir aynı davranış. */
async function istek(url, options, onUnauthorized) {
  const res = await authFetch(url, options, onUnauthorized)

  if (!res.ok) {
    const hata = new Error(await hataMesaji(res))
    hata.status = res.status
    throw hata
  }

  // 204 No Content — gövde yok, JSON çözmeye çalışmak hata verirdi.
  if (res.status === 204) return null
  return res.json()
}

async function hataMesaji(response) {
  try {
    const body = await response.json()
    if (body?.message) return body.message
    // ASP.NET model doğrulama hataları { errors: { alan: [mesaj] } } biçiminde.
    if (body?.errors) return Object.values(body.errors).flat().join(' ')
  } catch {
    /* gövde JSON değilse aşağıdaki genel mesaja düş */
  }
  return `Sunucu hatası (${response.status})`
}

const jsonGovde = (method, veri) => ({
  method,
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(veri),
})

/**
 * ROTA SERVİSİ — TourBuilder'da toplanan seçimlerden tur rotası önerir.
 *
 * Gövde `turPlani.js → turPayloadu` çıktısıdır; biçimi orada tanımlı ve tek
 * yerde durması bilinçli (sözleşme değişirse bakılacak tek dosya).
 *
 * POST (GET değil): sorgu bir NESNE — iç içe lokasyon ve süre alanları,
 * mekan tipi dizileri. Bunları adres satırına sıkıştırmak hem okunmaz bir URL
 * hem de uzunluk sınırı riski demek olurdu. Öneri istemek sunucuda bir kayıt
 * OLUŞTURMUYOR: dönen tur şablonu, kullanıcı "kaydet" diyene kadar yalnızca
 * bir cevap (konum analizi ucundaki desenin aynısı).
 *
 * @param {Object} payload turPayloadu() çıktısı
 * @returns {Promise<Object>} önerilen tur (TourDto biçiminde, id'siz taslak)
 */
export function turRotasiOner(payload, onUnauthorized) {
  return istek('/api/tur/rota-oner', jsonGovde('POST', payload), onUnauthorized)
}

/**
 * ELLE DÜZENLENMİŞ durak listesi için rotayı yeniden hesaplar.
 *
 * Kullanıcı öneriye POI ekleyip çıkardığında çağrılıyor. Mekan ARAMASI
 * yapmıyor — duraklar zaten belli, eksik olan yollara oturmuş çizgi; bu
 * yüzden maliyeti tek bir rota isteği (bkz. TurController.RotaHesapla).
 *
 * Rota çizilemezse İSTİSNA ATMIYOR: cevabın `uyari` alanı doluyor ve
 * `routeWkt` null geliyor. Rota olmadan da durak listesi geçerli bir tur.
 *
 * @param {{ulasimTipi: string, duraklar: Array<{lat:number, lon:number}>}} govde
 * @returns {Promise<{routeWkt: string|null, routeDistanceMeters: number|null,
 *                    routeDurationSeconds: number|null, uyari: string|null}>}
 */
export function turRotasiHesapla(govde, onUnauthorized) {
  return istek('/api/tur/rota-hesapla', jsonGovde('POST', govde), onUnauthorized)
}

// ---------------------------------------------------------------------------
//  Tur şablonu
// ---------------------------------------------------------------------------

/**
 * Rota önerisini KALICI tur şablonuna çevirir.
 *
 * Paylaşılabilir bağlantının ön koşulu: bağlantı sunucuda duran bir şeye
 * işaret etmek zorunda. Öneri yalnızca bir cevaptı (id = 0), onu paylaşsaydık
 * bağlantı sadece onu üreten tarayıcıda anlamlı olurdu.
 */
export function turKaydet(tur, onUnauthorized) {
  return istek('/api/tur/turlar', jsonGovde('POST', tur), onUnauthorized)
}

/** Kayıtlı turlar (durakları sıralı). */
export function turlariGetir(onUnauthorized) {
  return istek('/api/tur/turlar', {}, onUnauthorized)
}

/**
 * Turu siler (yumuşak silme — çöp kutusundan geri alınabilir).
 *
 * Sunucu, CANLI OTURUMU olan turu reddediyor: silinen bir turun oturumunda
 * kalan katılımcıların ekranı boşa düşerdi.
 */
export function turSil(id, onUnauthorized) {
  return istek(`/api/tur/turlar/${id}`, { method: 'DELETE' }, onUnauthorized)
}

/** Tek tur — canlı oturum ekranı durakları buradan okuyor. */
export function turGetir(id, onUnauthorized) {
  return istek(`/api/tur/turlar/${id}`, {}, onUnauthorized)
}

// ---------------------------------------------------------------------------
//  Canlı tur oturumu
// ---------------------------------------------------------------------------

/**
 * Turdan canlı oturum açar. Cevapta KATILIM KODU var — yalnızca rehbere.
 *
 * @param {number} turId
 * @param {boolean} hemenBaslat false ise oturum "Planned" açılır: rehber kodu
 *   önceden dağıtıp turu sonra başlatabilir.
 */
export function oturumAc(turId, hemenBaslat = true, onUnauthorized) {
  return istek(
    '/api/tur/oturumlar',
    jsonGovde('POST', { tourId: turId, startNow: hemenBaslat }),
    onUnauthorized,
  )
}

/** Katılım koduyla oturuma katılır (rol: Participant). */
export function oturumaKatil(katilimKodu, onUnauthorized) {
  return istek(
    '/api/tur/oturumlar/katil',
    jsonGovde('POST', { joinCode: katilimKodu }),
    onUnauthorized,
  )
}

/** Giriş yapan kullanıcının katıldığı AÇIK oturumlar. */
export function oturumlarim(onUnauthorized) {
  return istek('/api/tur/oturumlar', {}, onUnauthorized)
}

/** Oturumun anlık durumu. */
export function oturumGetir(id, onUnauthorized) {
  return istek(`/api/tur/oturumlar/${id}`, {}, onUnauthorized)
}

/** Oturumdan ayrılır — katılım kaydı silinmiyor, damgalanıyor. */
export function oturumdanAyril(id, onUnauthorized) {
  return istek(`/api/tur/oturumlar/${id}/katilim`, { method: 'DELETE' }, onUnauthorized)
}

/**
 * MİSAFİR GÖRÜNÜMÜ — giriş yapmadan, yalnızca katılım koduyla.
 *
 * ---- NEDEN authFetch DEĞİL? ----
 * Buradaki bütün diğer çağrılar authFetch üzerinden gidiyor: token ekliyor
 * ve 401 gelince giriş ekranına atıyor. Bu uçta ikisi de YANLIŞ olurdu —
 * misafirin token'ı yok ve giriş ekranına atılması, düzeltmeye çalıştığımız
 * davranışın ta kendisi (kullanıcı: "kayıt yapmadan giriş yapmadan sadece
 * verilen kod kullanılarak misafir olarak görünsün").
 *
 * Bu yüzden düz `fetch`: kimlik başlığı yok, yönlendirme yok.
 *
 * @param {string} kod 6 karakterlik katılım kodu
 * @returns {Promise<object>} Misafir tur görünümü
 * @throws {Error} Kod geçersiz ya da tur sona ermişse
 */
export async function misafirTuruGetir(kod) {
  const res = await fetch(`/api/tur/misafir/${encodeURIComponent(kod)}`, {
    headers: { Accept: 'application/json' },
  })

  if (!res.ok) {
    const hata = new Error(await hataMesaji(res))
    hata.status = res.status
    throw hata
  }

  return res.json()
}

// ---------------------------------------------------------------------------
//  Yoklama — "şu an kimler burada?"
// ---------------------------------------------------------------------------

/** Yoklamayı başlatır/sıfırlar (rehber). */
export function yoklamaBaslat(oturumId, soru, grupBoyu, onUnauthorized) {
  return istek(
    `/api/tur/oturumlar/${oturumId}/yoklama`,
    jsonGovde('POST', { soru, grupBoyu }),
    onUnauthorized,
  )
}

/** Açık yoklamanın anlık sayıları (rehber); yoklama yoksa null. */
export function yoklamaDurumu(oturumId, onUnauthorized) {
  return istek(`/api/tur/oturumlar/${oturumId}/yoklama`, {}, onUnauthorized)
}

/** Yoklamayı kapatır (rehber). */
export function yoklamaBitir(oturumId, onUnauthorized) {
  return istek(`/api/tur/oturumlar/${oturumId}/yoklama`, { method: 'DELETE' }, onUnauthorized)
}

/**
 * MİSAFİRİN CEVABI — kimliksiz, katılım koduyla.
 *
 * authFetch KULLANMIYOR (misafir görünümüyle aynı gerekçe): misafirin
 * token'ı yok ve 401'de giriş ekranına atılması yanlış olurdu.
 *
 * @param {string} cevap "Buradayim" | "Degilim" | "Acil"
 * @param {{ad?:string, telefon?:string}} [ekstra] İkisi de isteğe bağlı;
 *   telefon özellikle "Acil" cevapta anlamlı — rehber geri arayabilsin diye.
 * @returns {Promise<object|null>} Yoklama kapandıysa null.
 */
export async function misafirYoklamaCevabi(kod, misafirAnahtari, cevap, ekstra = {}) {
  const res = await fetch(`/api/tur/misafir/${encodeURIComponent(kod)}/yoklama`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify({
      misafirAnahtari,
      cevap,
      ad: ekstra.ad || null,
      telefon: ekstra.telefon || null,
    }),
  })

  if (!res.ok) {
    const hata = new Error(await hataMesaji(res))
    hata.status = res.status
    throw hata
  }

  // 204: açık yoklama yok. Hata değil, normal bir durum.
  return res.status === 204 ? null : res.json()
}

/**
 * Katılım kodundan PAYLAŞILABİLİR BAĞLANTI üretir.
 *
 * Adresi sunucu değil istemci kuruyor: uygulamanın hangi adreste yayında
 * olduğunu (localhost mu, alan adı mı) yalnızca tarayıcı biliyor. Sunucuya
 * taban adres ayarı eklemek, aynı bilgiyi iki yerde tutmak olurdu.
 */
export function turBaglantisi(katilimKodu) {
  if (!katilimKodu) return null
  const kok = typeof window === 'undefined' ? '' : window.location.origin
  return `${kok}/tur/${katilimKodu}`
}

/**
 * Oturumun durumunu ve/veya bulunulan durağı günceller.
 *
 * Gövde sunucudaki <c>TourSessionUpdateDto</c>: { status, currentWaypointId }.
 * Tek uç, dört işlem (duraklat / sürdür / bitir / durak ilerlet): dördü de
 * aynı yetki ve aynı durum geçişi kontrolünden geçiyor, ayrı uçlar açsaydık
 * her biri o kontrolü tekrarlardı.
 *
 * ROL GÖVDEDE GİTMİYOR: sunucu, isteği yapanın o oturumdaki rolüne bakıp
 * Guide değilse reddediyor. Rolü istemciden alsaydık katılımcı kendini
 * rehber ilan edip grubu yanlış durağa taşıyabilirdi.
 */
export function oturumGuncelle(oturumId, govde, onUnauthorized) {
  return istek(`/api/tur/oturumlar/${oturumId}`, jsonGovde('PUT', govde), onUnauthorized)
}

/**
 * REHBERİN CANLI KONUMUNU bildirir.
 *
 * Yalnızca o oturumun rehberi çağırabiliyor; sunucu kontrol ediyor.
 * Konum, oturum kapandığında ya da yayın durdurulduğunda siliniyor —
 * kalıcı bir iz bırakmıyor.
 *
 * @param {{lat:number, lon:number, dogrulukMetre?:number}} konum
 */
export function konumBildir(oturumId, konum, onUnauthorized) {
  return istek(`/api/tur/oturumlar/${oturumId}/konum`, jsonGovde('PUT', konum), onUnauthorized)
}

/** Konum yayınını durdurur ve kayıtlı konumu siler. */
export function konumYayininiDurdur(oturumId, onUnauthorized) {
  return istek(`/api/tur/oturumlar/${oturumId}/konum`, { method: 'DELETE' }, onUnauthorized)
}

/**
 * Turu SONLANDIRIR.
 *
 * Durum "Completed" olunca oturum kapanıyor: katılım kodu geçersizleşiyor
 * (kısmi benzersiz index yalnızca açık oturumları kapsıyor), ekran "tur sona
 * erdi" diyor ve rehberin eylem düğmeleri kalkıyor.
 *
 * "Cancelled" değil "Completed": ikisi ayrı şeyler — biri turun bittiğini,
 * diğeri hiç yapılmadığını söylüyor ve geçmiş kaydında bu ayrım anlamlı.
 */
export function turuSonlandir(oturumId, onUnauthorized) {
  return oturumGuncelle(oturumId, { status: 'Completed' }, onUnauthorized)
}

/**
 * "Sonraki Durağa Geç" — rehberin tek tıkla yaptığı ilerletme.
 *
 * Hangi durağa geçileceğini İSTEMCİ söylüyor (durak id'si), "bir sonraki"
 * demiyoruz: sunucunun "sonraki" tanımı ile ekrandaki liste ayrışabilir
 * (rehber ekranı açıkken tura durak eklenmiş olabilir). Açık id göndermek,
 * rehberin GÖRDÜĞÜ durağa geçmesini garanti ediyor.
 *
 * Durum da birlikte gidiyor: duraklatılmış bir oturumda "sonraki durak"
 * demek, turu aynı anda sürdürmek anlamına geliyor.
 */
export function sonrakiDuragaGec(oturumId, durakId, onUnauthorized) {
  return oturumGuncelle(
    oturumId,
    { status: 'Live', currentWaypointId: durakId },
    onUnauthorized,
  )
}

// ---------------------------------------------------------------------------
//  Turistik POI içe aktarımı (yönetim)
// ---------------------------------------------------------------------------

/**
 * Seçilen şehirlerin turistik mekanlarını OpenStreetMap'ten POI tablosuna aktarır.
 *
 * TEK SEFERLİK bir işlem ama TEKRAR ÇALIŞTIRILABİLİR: sunucu aynı adla yakında
 * duran kaydı atlıyor, yani ikinci çalıştırma yalnızca eksikleri tamamlıyor.
 *
 * Uzun sürebilir (şehir başına bir Overpass sorgusu); çağıran taraf düğmeyi
 * kilitleyip "aktarılıyor" demeli.
 *
 * @param {number[]} ilPlakalari örn. [6, 34]
 * @returns {Promise<{eklenen: number, atlanan: number, sehirler: object,
 *                    kategoriBazinda: object, uyarilar: string[]}>}
 */
export function turistikPoiAktar(ilPlakalari, onUnauthorized) {
  return istek('/api/tur/poi-aktar', jsonGovde('POST', { ilPlakalari }), onUnauthorized)
}
