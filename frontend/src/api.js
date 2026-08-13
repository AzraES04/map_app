// ============================================================================
//  Geometri API çağrıları.
//  Hepsi authFetch üzerinden gider: token'ı ekler, 401 gelirse oturumu
//  kapatıp login'e yönlendirir (mevcut projedeki davranış korunuyor).
// ============================================================================

import { authFetch } from './auth'

/** Sunucudan gelen hata gövdesini okuyup anlamlı bir mesaj üretir. */
async function hataMesaji(response) {
  try {
    const body = await response.json()
    // Backend hataları { message: "..." } biçiminde dönüyor.
    if (body?.message) return body.message
    // ASP.NET model doğrulama hataları { errors: { alan: [mesaj] } } biçiminde.
    if (body?.errors) return Object.values(body.errors).flat().join(' ')
  } catch {
    /* gövde JSON değilse aşağıdaki genel mesaja düş */
  }
  return `Sunucu hatası (${response.status})`
}

/** GET — bir tipin tüm kayıtlarını getirir. */
export async function listele(endpoint, onUnauthorized) {
  const res = await authFetch(endpoint, {}, onUnauthorized)
  if (!res.ok) throw new Error(await hataMesaji(res))
  return res.json()
}

/** POST — yeni geometri kaydeder. Gövde: { name, description, wkt } */
export async function kaydet(endpoint, veri, onUnauthorized) {
  const res = await authFetch(
    endpoint,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(veri),
    },
    onUnauthorized,
  )
  if (!res.ok) throw new Error(await hataMesaji(res))
  return res.json()
}

/** DELETE — soft delete (kayıt veritabanında kalır, is_deleted işaretlenir). */
export async function sil(endpoint, id, onUnauthorized) {
  const res = await authFetch(`${endpoint}/${id}`, { method: 'DELETE' }, onUnauthorized)
  if (!res.ok) throw new Error(await hataMesaji(res))
}

/**
 * POST /{id}/restore — silmeyi geri alır.
 * Bu uç ancak soft delete kullandığımız için mümkün: kayıt fiziksel olarak
 * silinseydi geri getirilecek bir veri kalmazdı.
 */
export async function geriAl(endpoint, id, onUnauthorized) {
  const res = await authFetch(`${endpoint}/${id}/restore`, { method: 'POST' }, onUnauthorized)
  if (!res.ok) throw new Error(await hataMesaji(res))
}

/**
 * POST /api/analysis/intersect — kesişim analizi (Ödev 4 / Görev 3).
 *
 * Gönderilen poligon veritabanına KAYDEDİLMEZ; sadece sorgu parametresidir.
 * Kesişim hesabı PostGIS'te (ST_Intersects) yapılır — envanterin tamamını
 * tarayıcıya indirip burada hesaplamak hem yavaş hem gereksiz olurdu.
 */
export async function kesisimAnalizi(wkt, haricTutulanPolygonId, onUnauthorized) {
  const res = await authFetch(
    '/api/analysis/intersect',
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ wkt, haricTutulanPolygonId: haricTutulanPolygonId ?? null }),
    },
    onUnauthorized,
  )
  if (!res.ok) throw new Error(await hataMesaji(res))
  return res.json()
}

/** PUT — kaydı günceller. Wkt gönderilirse geometri de değişir. */
export async function guncelle(endpoint, id, veri, onUnauthorized) {
  const res = await authFetch(
    `${endpoint}/${id}`,
    {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(veri),
    },
    onUnauthorized,
  )
  if (!res.ok) throw new Error(await hataMesaji(res))
  return res.json()
}

/** POST /{id}/active — kaydı askıya alır veya yeniden aktif eder (is_active). */
export async function aktiflikDegistir(endpoint, id, isActive, onUnauthorized) {
  const res = await authFetch(
    `${endpoint}/${id}/active`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ isActive }),
    },
    onUnauthorized,
  )
  if (!res.ok) throw new Error(await hataMesaji(res))
}
