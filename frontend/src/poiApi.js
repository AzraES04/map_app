// ============================================================================
//  POI ve kategori API çağrıları (Ödev 12).
//
//  Neden üçüncü bir API dosyası? api.js geometri uçlarına, adminApi.js yönetim
//  uçlarına bakıyor. POI ikisinin de değil: harita ekranı da yönetim paneli de
//  aynı uçları kullanıyor (bkz. backend'de tek PoiController). Hangisinin
//  içine koysak diğer ekran "yabancı" bir dosyadan import etmek zorunda
//  kalırdı; modülün kendi dosyası olması hangi ekranın neye dokunduğunu
//  bozmuyor.
//
//  Hepsi authFetch üzerinden gider: token'ı ekler, 401 gelirse oturumu
//  kapatıp login'e yönlendirir.
// ============================================================================

import { authFetch } from './auth'

/** Ortak istek yardımcısı — adminApi.js'teki ile aynı sözleşme. */
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
//  POI
// ---------------------------------------------------------------------------

/**
 * Bütün POI'ler. Sahiplik süzgeci YOK — POI ortak referans verisidir.
 * Hem harita katmanı hem admin listesi bu ucu kullanıyor.
 */
export const poileriListele = (onUnauthorized) =>
  istek('/api/poi', {}, onUnauthorized)

/** Operatörün formundaki açılır liste: yalnızca AKTİF kategoriler, düz ve sıralı. */
export const poiKategorileriniListele = (onUnauthorized) =>
  istek('/api/poi/kategoriler', {}, onUnauthorized)

/**
 * Aramanın çalışmaya başladığı en kısa metin (Ödev 13 / Madde 2).
 * Sunucudaki PoiService.EnAzAramaUzunlugu ile aynı olmalı — buradaki kopya
 * yalnızca boşuna istek atmamak için; bağlayıcı olan sunucudaki.
 */
export const EN_AZ_ARAMA = 2

/**
 * POI ARAMA (Ödev 13 / Madde 2).
 *
 * Yetki gerektirmiyor: Kullanıcı (User) rolü de arayabiliyor. Süzme sunucuda
 * yapılıyor, bütün liste indirilip tarayıcıda elenmiyor.
 *
 * @param {string} sorgu Kullanıcının yazdığı metin
 * @param {AbortSignal} signal Yeni tuşa basılınca eski isteği iptal etmek için
 * @param {function} onUnauthorized 401 gelirse çağrılacak
 * @param {number} [enFazla] İstenen sonuç sayısı. Verilmezse sunucunun
 *   varsayılanı (8) kullanılır. SONUÇLARI SONRADAN SÜZEN çağıranlar (tur
 *   düzenlemedeki şehir süzgeci) daha geniş bir aday havuzu istemeli:
 *   sunucudan gelen 8 kaydın hepsi elenirse liste boş kalır ve kullanıcıya
 *   arama bozukmuş gibi görünür.
 */
export const poiAra = (sorgu, signal, onUnauthorized, enFazla) =>
  istek(
    `/api/poi/ara?q=${encodeURIComponent(sorgu)}${enFazla ? `&enFazla=${enFazla}` : ''}`,
    { signal },
    onUnauthorized,
  )

/**
 * POI stillerinin TANIMLARI: [{ stil, kategoriId, ad, tamYol, renk, sekil }]
 *
 * Tek kaynak, iki iş: WMS isteğinin STYLES parametresi ve paneldeki lejant.
 * Önceki sürümde renkler bu dosyanın yanındaki bir sabit listedeydi ve
 * SLD ile eşleşmesi el emeğine bağlıydı — "lejantta mavi, haritada yeşil"
 * hatası mümkündü. Artık ikisi de kategori tablosundan türüyor.
 *
 * GeoServer'a gitmiyor; sunucu kapalıyken de doğru cevap veriyor.
 */
export const poiStilleri = (onUnauthorized) =>
  istek('/api/poi/stiller', {}, onUnauthorized)

/**
 * Stilleri kategori tablosundan yeniden üretip GeoServer'a yazar.
 * "POI Yönetimi" yetkisi ister. Normalde gerekmez — kategori değişince
 * kendiliğinden çalışıyor; bu uç, o sırada GeoServer kapalıysa işi
 * tamamlamak için.
 */
export const poiStilleriniYenile = (onUnauthorized) =>
  istek('/api/admin/poi-categories/stilleri-yenile', { method: 'POST' }, onUnauthorized)

/**
 * Resmî tatil takvimi (Ödev 13 / Madde 3).
 * Liste backend'de tanımlı; "resmî kurum" kipi seçilince form bunu gösteriyor.
 */
export const resmiTatilleriGetir = (yil, onUnauthorized) =>
  istek(`/api/poi/resmi-tatiller${yil ? `?yil=${yil}` : ''}`, {}, onUnauthorized)

/**
 * Seçilen yer için KATEGORİ ÖNERİSİ (Ödev 13 / Madde 4).
 *
 * Karşılığı bulunamazsa sunucu 204 döndürüyor; `istek` yardımcısı onu null'a
 * çeviriyor. Yani "öneri yok" bir HATA değil, geçerli bir cevap — çağıran
 * taraf try/catch'e sarmak zorunda kalmıyor.
 *
 * @param {{tur?: string, sinif?: string, isim?: string}} yer
 */
export const kategoriOner = ({ tur, sinif, isim }, signal, onUnauthorized) => {
  const p = new URLSearchParams()
  if (tur) p.set('tur', tur)
  if (sinif) p.set('sinif', sinif)
  if (isim) p.set('isim', isim)

  return istek(`/api/poi/kategori-oner?${p}`, { signal }, onUnauthorized)
}

/** Yeni POI. Gövde: { isim, kategoriId, mesaiSaatleri, wkt } */
export const poiEkle = (veri, onUnauthorized) =>
  istek('/api/poi', jsonGovde('POST', veri), onUnauthorized)

/** POI günceller. wkt boş gönderilirse konum değişmez. */
export const poiGuncelle = (id, veri, onUnauthorized) =>
  istek(`/api/poi/${id}`, jsonGovde('PUT', veri), onUnauthorized)

/** Soft delete — kayıt tabloda kalır, geri alınabilir. */
export const poiSil = (id, onUnauthorized) =>
  istek(`/api/poi/${id}`, { method: 'DELETE' }, onUnauthorized)

/** Silmeyi geri alır ("POI Yönetimi" yetkisi ister). */
export const poiGeriAl = (id, onUnauthorized) =>
  istek(`/api/poi/${id}/restore`, { method: 'POST' }, onUnauthorized)

/** Askıya alır / yeniden aktif eder. Pasif POI listede kalır. */
export const poiAktiflikDegistir = (id, isActive, onUnauthorized) =>
  istek(`/api/poi/${id}/active`, jsonGovde('POST', { isActive }), onUnauthorized)

// ---------------------------------------------------------------------------
//  Kategori yönetimi — "POI Yönetimi" yetkisi ister
// ---------------------------------------------------------------------------

/**
 * Seçilebilecek kategori SİMGELERİ ve çizimleri (Ödev 15).
 *
 * Yetki istemez, veritabanına gitmez: katalog sunucu kodunda sabit.
 * Frontend'de SVG yolu TANIMLANMIYOR — haritayı çizen GeoServer ile
 * panelin önizlemesi aynı kaynaktan besleniyor.
 */
export const poiIkonlariniGetir = (onUnauthorized) =>
  istek('/api/poi/ikonlar', {}, onUnauthorized)

/** Kategori AĞACI (pasifler dahil). Kökler döner, altlar `cocuklar` içinde. */
export const kategorileriListele = (onUnauthorized) =>
  istek('/api/admin/poi-categories', {}, onUnauthorized)

/** Yeni kategori. Gövde: { ad, aciklama, parentId, isActive } */
export const kategoriEkle = (veri, onUnauthorized) =>
  istek('/api/admin/poi-categories', jsonGovde('POST', veri), onUnauthorized)

export const kategoriGuncelle = (id, veri, onUnauthorized) =>
  istek(`/api/admin/poi-categories/${id}`, jsonGovde('PUT', veri), onUnauthorized)

/** Soft delete. Alt kategorisi veya bağlı POI'si varsa sunucu 400 döner. */
export const kategoriSil = (id, onUnauthorized) =>
  istek(`/api/admin/poi-categories/${id}`, { method: 'DELETE' }, onUnauthorized)

// ---------------------------------------------------------------------------
//  Yardımcılar
// ---------------------------------------------------------------------------

/**
 * Kategori ağacını DÜZ listeye çevirir (her düğümde `seviye` dolu).
 *
 * Yönetim ekranındaki tablo ve "üst kategori" açılır listesi ağacı satır satır
 * çiziyor; girintiyi `seviye` veriyor. Ağacı iki ayrı yerde dolaşmak yerine
 * tek yardımcı: sıralama her iki yerde de aynı olsun.
 */
export function agaciDuzlestir(dugumler, seviye = 0) {
  return dugumler.flatMap((dugum) => [
    { ...dugum, seviye, cocuklar: [] },
    ...agaciDuzlestir(dugum.cocuklar ?? [], seviye + 1),
  ])
}
