// Token yönetimi: saklama, geçerlilik kontrolü, otomatik çıkış zamanlayıcısı.
// Not: Sayfa yenilenince oturumun düşmemesi için localStorage kullanıyoruz.

const TOKEN_KEY = 'staj_token'
const EXPIRES_KEY = 'staj_token_expires'
const USER_KEY = 'staj_username'

/**
 * Açılış sahnesinin bu oturumda oynatıldığını işaretleyen anahtar.
 * Login ekranı ile harita ekranı ortak kullandığı için burada duruyor.
 */
export const GIRIS_ANIMASYON_ANAHTARI = 'staj_giris_animasyonu'

/**
 * Girişten sonra açılış sahnesi MUTLAKA oynasın diye bayrağı sıfırlar.
 * Böylece login ekranındaki uzay teması, haritadaki "dünyadan Türkiye'ye
 * iniş" sahnesiyle kesintisiz devam eder.
 */
export function girisAnimasyonunuSifirla() {
  sessionStorage.removeItem(GIRIS_ANIMASYON_ANAHTARI)
}

/** Açılış sahnesi şu an oynatılmalı mı? (yan etkisi yoktur, sadece sorar) */
export function girisAnimasyonuOynasinMi() {
  return (
    !window.matchMedia('(prefers-reduced-motion: reduce)').matches &&
    !sessionStorage.getItem(GIRIS_ANIMASYON_ANAHTARI) &&
    // Arka plan sekmesinde tarayıcı animasyon karesi üretmez; sahne görülmeden
    // "oynatıldı" sayılmasın diye hiç başlatmıyoruz.
    document.visibilityState === 'visible'
  )
}

let logoutTimer = null

export function saveSession({ token, expiresAt, username }) {
  localStorage.setItem(TOKEN_KEY, token)
  localStorage.setItem(EXPIRES_KEY, expiresAt)
  localStorage.setItem(USER_KEY, username)
}

export function getToken() {
  return localStorage.getItem(TOKEN_KEY)
}

export function getUsername() {
  return localStorage.getItem(USER_KEY)
}

export function getExpiresAt() {
  const raw = localStorage.getItem(EXPIRES_KEY)
  return raw ? new Date(raw) : null
}

/** Token var mı ve süresi geçmemiş mi? */
export function isAuthenticated() {
  const token = getToken()
  const expiresAt = getExpiresAt()
  if (!token || !expiresAt) return false
  return expiresAt.getTime() > Date.now()
}

/** Oturumu temizle. */
export function clearSession() {
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(EXPIRES_KEY)
  localStorage.removeItem(USER_KEY)
  if (logoutTimer) {
    clearTimeout(logoutTimer)
    logoutTimer = null
  }
}

/**
 * Token süresi dolduğu AN otomatik çıkış: kalan süre kadar zamanlayıcı kurar,
 * süre bitince oturumu temizleyip verilen callback'i (login'e yönlendirme) çağırır.
 */
export function scheduleAutoLogout(onLogout) {
  const expiresAt = getExpiresAt()
  if (!expiresAt) return
  const remainingMs = expiresAt.getTime() - Date.now()
  if (logoutTimer) clearTimeout(logoutTimer)
  logoutTimer = setTimeout(() => {
    clearSession()
    onLogout()
  }, Math.max(remainingMs, 0))
}

/** Korumalı API çağrıları: token'ı ekler; 401 dönerse oturumu kapatıp login'e atar. */
export async function authFetch(url, options = {}, onUnauthorized) {
  const response = await fetch(url, {
    ...options,
    headers: {
      ...(options.headers || {}),
      Authorization: `Bearer ${getToken()}`,
    },
  })
  if (response.status === 401) {
    clearSession()
    if (onUnauthorized) onUnauthorized()
    throw new Error('Oturum süresi doldu')
  }
  return response
}
