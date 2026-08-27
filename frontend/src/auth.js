// Token yönetimi: saklama, geçerlilik kontrolü, otomatik çıkış zamanlayıcısı.
// Not: Sayfa yenilenince oturumun düşmemesi için localStorage kullanıyoruz.

const TOKEN_KEY = 'staj_token'
const EXPIRES_KEY = 'staj_token_expires'
const USER_KEY = 'staj_username'

/**
 * Ödev 11 — hesap değiştirici.
 *
 * Bu makinede daha önce giriş yapmış hesapların listesi:
 *   [{ username, token, expiresAt }]
 *
 * ŞİFRE SAKLANMIYOR — bilerek. "Şifre kayıtlıysa direkt geçsin" isteğinin
 * güvenli karşılığı, şifreyi değil OTURUMU saklamak: token hâlâ geçerliyse
 * hesap arası geçiş tek tık, süresi dolmuşsa giriş ekranına düşülüyor
 * (kullanıcı adı dolu gelir, yalnızca şifre istenir). Şifreleri localStorage'a
 * yazsaydık, tarayıcıya erişen herkes düz metin şifreleri okurdu.
 */
const HESAPLAR_KEY = 'staj_hesaplar'

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

  hesabiKaydet({ username, token, expiresAt })
}

// ---------------------------------------------------------------------------
//  Hesap listesi (Ödev 11)
// ---------------------------------------------------------------------------

function hesaplariOku() {
  try {
    const ham = JSON.parse(localStorage.getItem(HESAPLAR_KEY) ?? '[]')
    return Array.isArray(ham) ? ham : []
  } catch {
    // Bozuk JSON tüm listeyi kullanılamaz yapmasın.
    return []
  }
}

function hesaplariYaz(liste) {
  localStorage.setItem(HESAPLAR_KEY, JSON.stringify(liste))
}

/** Girişten sonra hesabı listeye ekler / oturumunu tazeler. */
function hesabiKaydet(hesap) {
  const liste = hesaplariOku().filter((h) => h.username !== hesap.username)
  // En son kullanılan başa: liste "son kullanılan" sırasında dursun.
  hesaplariYaz([hesap, ...liste])
}

/** Kayıtlı bir oturumun süresi geçmiş mi? */
function oturumGecerliMi(hesap) {
  return !!hesap?.token && !!hesap?.expiresAt && new Date(hesap.expiresAt).getTime() > Date.now()
}

/**
 * Bu makinede bilinen hesaplar.
 * @returns {{username: string, aktif: boolean, oturumVar: boolean}[]}
 */
export function kayitliHesaplar() {
  const aktif = getUsername()
  return hesaplariOku().map((h) => ({
    username: h.username,
    aktif: h.username === aktif,
    oturumVar: oturumGecerliMi(h),
  }))
}

/**
 * Hesaba geçmeyi dener.
 * @returns {boolean} true → oturum devralındı; false → şifre gerekiyor.
 */
export function hesabaGec(username) {
  const hesap = hesaplariOku().find((h) => h.username === username)
  if (!oturumGecerliMi(hesap)) return false

  localStorage.setItem(TOKEN_KEY, hesap.token)
  localStorage.setItem(EXPIRES_KEY, hesap.expiresAt)
  localStorage.setItem(USER_KEY, hesap.username)

  // Listeyi de tazele ki bu hesap başa geçsin.
  hesabiKaydet(hesap)
  return true
}

/**
 * AKTİF oturumu bırakır ama hesabın kayıtlı oturumunu SİLMEZ.
 *
 * "Başka hesapla giriş yap" ve süresi dolmuş bir hesaba geçiş bunu kullanıyor.
 * clearSession() kullansaydık, o an açık olan hesabın token'ı da silinirdi ve
 * geri dönmek için yeniden şifre istenirdi — oysa oturumu hâlâ geçerli.
 * Instagram'daki gibi: hesap eklemek, açık olan hesabı kapatmaz.
 *
 * ÇIKIŞ düğmesi bunu DEĞİL clearSession()'ı çağırıyor; "çıkış" demek
 * o oturumu gerçekten bırakmak demek.
 */
export function aktifOturumuBirak() {
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(EXPIRES_KEY)
  localStorage.removeItem(USER_KEY)

  if (logoutTimer) {
    clearTimeout(logoutTimer)
    logoutTimer = null
  }
}

/** Hesabı listeden tamamen çıkarır. */
export function hesabiUnut(username) {
  hesaplariYaz(hesaplariOku().filter((h) => h.username !== username))
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
  // Çıkış yapan hesabın TOKEN'ı listeden düşüyor ama hesabın kendisi kalıyor:
  // bir dahaki sefere adı listede görünsün, sadece şifre istensin.
  const kullanici = getUsername()
  if (kullanici) {
    hesaplariYaz(hesaplariOku().map((h) => (h.username === kullanici
      ? { username: h.username }
      : h)))
  }

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
