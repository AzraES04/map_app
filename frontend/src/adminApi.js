// ============================================================================
//  Yönetim paneli API çağrıları (Ödev 6).
//
//  api.js geometri uçlarına bakıyor; yönetim uçları ayrı bir dosyada duruyor.
//  Sebep: iki alanın sözleşmesi birbirinden bağımsız — harita ekranı yönetim
//  uçlarını hiç çağırmıyor, panel de geometri uçlarını. Ayrı dosya, hangi
//  ekranın neye dokunduğunu tek bakışta gösteriyor.
//
//  Hepsi authFetch üzerinden gider: token'ı ekler, 401 gelirse oturumu
//  kapatıp login'e yönlendirir.
// ============================================================================

import { authFetch } from './auth'

/**
 * Ortak istek yardımcısı.
 *
 * Backend hataları { message: "..." } biçiminde dönüyor; burada onu okuyup
 * Error nesnesine çeviriyoruz. HTTP durum kodunu da (err.status) taşıyoruz:
 * çağıran taraf 403'ü (yetkisiz) 404'ten (kayıt yok) koda bakarak ayırabilsin.
 */
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

/** JSON gövdeli istekler için ortak seçenekler. */
const jsonGovde = (method, veri) => ({
  method,
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(veri),
})

// ---------------------------------------------------------------------------
//  Yetkiler
// ---------------------------------------------------------------------------

/** Sistemdeki tüm yetkiler (permissions tablosu). */
export const yetkileriListele = (onUnauthorized) =>
  istek('/api/permissions', {}, onUnauthorized)

/**
 * Giriş yapmış kullanıcının kendi yetki matrisi.
 * Menüyü buna göre kısıyoruz; asıl kontrol yine sunucuda.
 */
export const kendiYetkilerim = (onUnauthorized) =>
  istek('/api/permissions/me', {}, onUnauthorized)

// ---------------------------------------------------------------------------
//  Kullanıcılar
// ---------------------------------------------------------------------------

export const kullanicilariListele = (onUnauthorized) =>
  istek('/api/admin/users', {}, onUnauthorized)

export const kullaniciEkle = (veri, onUnauthorized) =>
  istek('/api/admin/users', jsonGovde('POST', veri), onUnauthorized)

export const kullaniciGuncelle = (id, veri, onUnauthorized) =>
  istek(`/api/admin/users/${id}`, jsonGovde('PUT', veri), onUnauthorized)

export const kullaniciSil = (id, onUnauthorized) =>
  istek(`/api/admin/users/${id}`, { method: 'DELETE' }, onUnauthorized)

/** Kullanıcının yetki matrisi: tüm yetkiler + her birinin kaynağı. */
export const kullaniciYetkileri = (id, onUnauthorized) =>
  istek(`/api/admin/users/${id}/permissions`, {}, onUnauthorized)

/** Kullanıcının DOĞRUDAN yetkilerini kaydeder; güncel matrisi geri döner. */
export const kullaniciYetkileriniKaydet = (id, permissionIds, onUnauthorized) =>
  istek(`/api/admin/users/${id}/permissions`, jsonGovde('PUT', { permissionIds }), onUnauthorized)

// ---------------------------------------------------------------------------
//  Roller
// ---------------------------------------------------------------------------

export const rolleriListele = (onUnauthorized) =>
  istek('/api/admin/roles', {}, onUnauthorized)

export const rolEkle = (veri, onUnauthorized) =>
  istek('/api/admin/roles', jsonGovde('POST', veri), onUnauthorized)

export const rolGuncelle = (id, veri, onUnauthorized) =>
  istek(`/api/admin/roles/${id}`, jsonGovde('PUT', veri), onUnauthorized)

export const rolSil = (id, onUnauthorized) =>
  istek(`/api/admin/roles/${id}`, { method: 'DELETE' }, onUnauthorized)

// ---------------------------------------------------------------------------
//  Coğrafi yetki (Ödev 7 / Madde 2)
// ---------------------------------------------------------------------------

/**
 * Bir kullanıcının veya rolün tanımlı alanları.
 * İki parametreden yalnızca biri verilir; ikisi de boşsa tüm tanımlar döner.
 */
export const cografiYetkileriListele = ({ userId, roleId } = {}, onUnauthorized) => {
  const sorgu = userId ? `?userId=${userId}` : roleId ? `?roleId=${roleId}` : ''
  return istek(`/api/admin/geo-permissions${sorgu}`, {}, onUnauthorized)
}

/** Yeni alan tanımlar. Gövde: { name, userId|roleId, wkt } */
export const cografiYetkiEkle = (veri, onUnauthorized) =>
  istek('/api/admin/geo-permissions', jsonGovde('POST', veri), onUnauthorized)

/** Alan tanımını kaldırır (soft delete). */
export const cografiYetkiSil = (id, onUnauthorized) =>
  istek(`/api/admin/geo-permissions/${id}`, { method: 'DELETE' }, onUnauthorized)

/**
 * Giriş yapan kullanıcının ÇALIŞMA ALANI.
 * { kisitli: bool, alanlar: [{ id, name, wkt, kaynak }] }
 */
export const calismaAlanim = (onUnauthorized) =>
  istek('/api/permissions/me/geo', {}, onUnauthorized)

// ============================================================================
//  Ödev 10 — il / bölge referans verisi
// ============================================================================

/** 81 il (plaka, ad, bölge). Geometri İÇERMEZ. */
export function illeriGetir(onUnauthorized) {
  return istek('/api/iller', {}, onUnauthorized)
}

/** Yedi coğrafi bölge ve il sayıları. */
export function bolgeleriGetir(onUnauthorized) {
  return istek('/api/iller/bolgeler', {}, onUnauthorized)
}

/**
 * İl sınırları (WKT). Cevap ~250 KB olduğu için MODÜL DÜZEYİNDE saklanıyor:
 * coğrafi yetki modalı her açıldığında yeniden indirmenin anlamı yok, il
 * sınırları oturum boyunca değişmiyor.
 *
 * Aynı anda iki çağrı gelirse ikisi de AYNI sözü (promise) bekliyor —
 * "yükleniyor mu?" bayrağıyla uğraşmadan çift indirme önleniyor.
 */
let sinirSozu = null

export function ilSinirlariGetir(onUnauthorized) {
  sinirSozu ??= istek('/api/iller/sinirlar', {}, onUnauthorized)
    .catch((err) => {
      // Başarısız sözü saklamıyoruz; yoksa tek bir ağ hatası, sonraki tüm
      // denemeleri de kalıcı olarak başarısız yapardı.
      sinirSozu = null
      throw err
    })

  return sinirSozu
}

/**
 * Bir kullanıcının iki adımlı doğrulamasını SIFIRLAR — kilitlenme kurtarması.
 *
 * TOTP'de gizli anahtar yalnızca kullanıcının telefonunda ve sunucuda duruyor.
 * Telefon kaybolursa kullanıcı KALICI olarak kilitlenir: şifresini bilse bile
 * ikinci adımı geçemez ve korumayı kapatmak da giriş yapmayı gerektirir.
 *
 * "Kullanıcı Yönetimi" yetkisi ister.
 */
export function ikiAdimliSifirla(id, onUnauthorized) {
  return istek(`/api/admin/users/${id}/2fa/sifirla`, { method: 'POST' }, onUnauthorized)
}

/** Kullanıcıyı onaylar (Ödev 10 — kayıt olma). */
export function kullaniciOnayla(id, onUnauthorized) {
  return istek(`/api/admin/users/${id}/approve`, { method: 'POST' }, onUnauthorized)
}

/**
 * Yetki alanı olarak seçilebilecek KAYITLI poligonlar (Ödev 11).
 * Sahibi kim olursa olsun hepsi geliyor: yönetici herhangi bir alanı
 * referans alabilmeli.
 */
export function secilebilirAlanlariGetir(onUnauthorized) {
  return istek('/api/admin/geo-permissions/poligonlar', {}, onUnauthorized)
}

// ---------------------------------------------------------------------------
//  İki adımlı doğrulama (TOTP) — KENDİ hesabının ayarı
//
//  Bu dosyada duruyorlar ama "admin" işlemleri DEĞİL: her kullanıcı kendi
//  hesabı için çağırıyor, yetki gerekmiyor. Ayrı bir dosya açmak beş satır
//  için yeni bir modül olurdu; ortak yardımcılar (istek, jsonGovde) zaten
//  burada.
// ---------------------------------------------------------------------------

/** Giriş yapmış kullanıcının iki adımlı doğrulama durumu. */
export const ikiAdimliDurum = (onUnauthorized) =>
  istek('/api/auth/2fa/durum', {}, onUnauthorized)

/**
 * Kurulumu BAŞLATIR — gizli anahtarı ve otpauth adresini döner.
 * Koruma henüz AÇILMAZ; açmak için kod doğrulanmalı.
 */
export const ikiAdimliBaslat = (onUnauthorized) =>
  istek('/api/auth/2fa/baslat', { method: 'POST' }, onUnauthorized)

/** Kurulumu tamamlar: kod doğruysa koruma açılır. */
export const ikiAdimliDogrula = (kod, onUnauthorized) =>
  istek('/api/auth/2fa/dogrula', jsonGovde('POST', { kod }), onUnauthorized)

/** Korumayı kapatır. ŞİFRE ister — kod değil (bkz. TotpKapatDto). */
export const ikiAdimliKapat = (sifre, onUnauthorized) =>
  istek('/api/auth/2fa/kapat', jsonGovde('POST', { sifre }), onUnauthorized)
