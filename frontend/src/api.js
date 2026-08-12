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
