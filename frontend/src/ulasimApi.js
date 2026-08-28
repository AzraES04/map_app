// ============================================================================
//  Akıllı ulaşım modülü API çağrıları (Ödev 16).
//
//  Neden dördüncü bir API dosyası? api.js geometri, adminApi.js yönetim,
//  poiApi.js POI uçlarına bakıyor. Ulaşım hiçbirinin değil ama POI'yle aynı
//  ikilemde: hem harita ekranı hem yönetim paneli kullanıyor. POI'de verilen
//  kararın aynısı — modülün kendi dosyası olması, hangi ekranın neye
//  dokunduğunu bozmuyor.
//
//  Hepsi authFetch üzerinden gider: token'ı ekler, 401 gelirse oturumu
//  kapatıp login'e yönlendirir.
// ============================================================================

import { authFetch } from './auth'

/** Ortak istek yardımcısı — poiApi.js/adminApi.js ile aynı sözleşme. */
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

// ---------------------------------------------------------------------------
//  Güzergah
// ---------------------------------------------------------------------------

/**
 * Bütün güzergahlar — DURAKLARI SIRALI hâlde içinde.
 *
 * Durakları ayrı bir istekle çekmiyoruz: harita hattı ve yönetim ekranındaki
 * sürükle-bırak listesi ikisini birlikte istiyor, ayrı istekler arasında
 * sıranın değişmesi de mümkün olurdu.
 *
 * Yetki İSTEMEZ: güzergah ortak referans verisi, giriş yapan herkes görüyor
 * ("Ulaşım Kullanıcısı" rolünün hiç yetkisi yok ve tam da bu yüzden görüyor).
 */
export const guzergahlariListele = (onUnauthorized) =>
  istek('/api/ulasim/guzergahlar', {}, onUnauthorized)

/** Yeni güzergah. Gövde: { ad, renk, aciklama, isActive } — "Güzergah Yönetimi" ister. */
export const guzergahEkle = (veri, onUnauthorized) =>
  istek('/api/ulasim/guzergahlar', jsonGovde('POST', veri), onUnauthorized)

export const guzergahGuncelle = (id, veri, onUnauthorized) =>
  istek(`/api/ulasim/guzergahlar/${id}`, jsonGovde('PUT', veri), onUnauthorized)

/** Soft delete. Durağı olan güzergahta sunucu 400 döner. */
export const guzergahSil = (id, onUnauthorized) =>
  istek(`/api/ulasim/guzergahlar/${id}`, { method: 'DELETE' }, onUnauthorized)

/**
 * SÜRÜKLE-BIRAK sıralamasını sunucuya yazar (Ödev 16 / Madde 2).
 *
 * Tek tek "şunun sırası 3 oldu" demek yerine BÜTÜN listeyi yeni sırasıyla
 * gönderiyoruz: bir durağı taşımak aradaki hepsinin sırasını kaydırıyor ve
 * yarısı yazılıp yarısı yazılmayan bir liste tutarsız kalırdı.
 *
 * @param {number} guzergahId
 * @param {number[]} durakIdleri Durak id'leri, yeni sırasıyla
 */
export const siralamaKaydet = (guzergahId, durakIdleri, onUnauthorized) =>
  istek(
    `/api/ulasim/guzergahlar/${guzergahId}/sira`,
    jsonGovde('PUT', { durakIdleri }),
    onUnauthorized,
  )

// ---------------------------------------------------------------------------
//  Durak
// ---------------------------------------------------------------------------

/** Bütün duraklar (güzergah adı/rengi ve sırasıyla). */
export const duraklariListele = (onUnauthorized) =>
  istek('/api/ulasim/duraklar', {}, onUnauthorized)

/**
 * Yeni durak. Gövde: { ad, guzergahId, wkt, aciklama, sira? }
 * "Durak Ekleme" yetkisi ister; sira verilmezse durak sona eklenir.
 */
export const durakEkle = (veri, onUnauthorized) =>
  istek('/api/ulasim/duraklar', jsonGovde('POST', veri), onUnauthorized)

/** Durak günceller. wkt boş gönderilirse konum değişmez. */
export const durakGuncelle = (id, veri, onUnauthorized) =>
  istek(`/api/ulasim/duraklar/${id}`, jsonGovde('PUT', veri), onUnauthorized)

/** Soft delete — kalan durakların sırası sunucuda 1..N'e sıkıştırılır. */
export const durakSil = (id, onUnauthorized) =>
  istek(`/api/ulasim/duraklar/${id}`, { method: 'DELETE' }, onUnauthorized)

// ---------------------------------------------------------------------------
//  Rota (Ödev 17 / Madde 1)
// ---------------------------------------------------------------------------

/**
 * "Rota Oluştur" — güzergahın duraklarından geçen sürüş rotasını OSRM'e
 * hesaplatır ve veritabanına yazar. Güncel güzergahı döner.
 *
 * POST, GET DEĞİL: uç veritabanını değiştiriyor ve dış bir servise iş
 * yaptırıyor. Gövde yok — hesaplanacak her şey (durak dizilimi) zaten
 * sunucuda.
 *
 * "Güzergah Yönetimi" yetkisi ister.
 *
 * ELLE çağrılan yol bu. Durak eklendiğinde/taşındığında/silindiğinde ve
 * SIRA değiştiğinde rota sunucuda kendiliğinden yenileniyor; arayüzün ayrıca
 * bu fonksiyonu çağırmasına gerek yok.
 */
/**
 * `viaNoktalar`: `[{ durakId, wkt }]` — rotanın geçmesi zorunlu ara
 * noktalar. `durakId`, noktanın HANGİ DURAĞA GELİRKEN kullanılacağını
 * söylüyor; sunucu onu o durağın hemen önüne yerleştiriyor. Boş liste =
 * OSRM serbest (Ödev 17 davranışı).
 */
export const rotaOlustur = (guzergahId, viaNoktalar = [], onUnauthorized) =>
  istek(
    `/api/ulasim/guzergahlar/${guzergahId}/rota`,
    jsonGovde('POST', { viaNoktalar }),
    onUnauthorized,
  )

/**
 * Ödev 18 — bir durağa GİDEN yolların alternatifleri.
 *
 * Bacak = bir önceki durak → bu durak. Kullanıcı haritada bir durağa tıklayıp
 * "buraya nasıl gidilir?" diye soruyor; cevap hattın tamamının değil, o
 * durağa varan parçanın alternatifleri.
 *
 * Yetki İSTEMEZ: hiçbir şeyi değiştirmiyor, yalnızca hesaplayıp gösteriyor.
 * Değiştiren adım seçimi kaydeden `rotaOlustur` ve o "Güzergah Yönetimi"
 * istiyor.
 *
 * Alternatif üretilemediğinde de 200 döner: `alternatifler` boş, `mesaj`
 * sebebini söyler (hattın ilk durağı / OSRM kapalı / tek makul yol var).
 */
/**
 * Ödev 18 — ÖNİZLEME: seçilen ara noktalarla hattın tamamı nasıl görünürdü?
 *
 * Hiçbir şey kaydetmiyor; kullanıcı alternatifler arasında gezinirken
 * çağrılıyor. Kalıcı hâle getiren `rotaOlustur` ayrı bir uç ve "Güzergah
 * Yönetimi" yetkisi istiyor.
 *
 * POST, çünkü ara nokta listesi adres satırına sığdırılacak bir şey değil —
 * "değiştiriyorum" demek değil, "gövdesi olan bir sorgu" demek.
 */
export const rotaOnizle = (guzergahId, viaNoktalar = [], onUnauthorized) =>
  istek(
    `/api/ulasim/guzergahlar/${guzergahId}/rota/onizleme`,
    jsonGovde('POST', { viaNoktalar }),
    onUnauthorized,
  )

export const durakAlternatifleri = (durakId, onUnauthorized) =>
  istek(`/api/ulasim/duraklar/${durakId}/alternatifler`, {}, onUnauthorized)
