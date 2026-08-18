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
