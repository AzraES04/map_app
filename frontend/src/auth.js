// Token yönetimi: saklama, geçerlilik kontrolü, otomatik çıkış zamanlayıcısı.
// Not: Sayfa yenilenince oturumun düşmemesi için localStorage kullanıyoruz.

const TOKEN_KEY = 'staj_token'
const EXPIRES_KEY = 'staj_token_expires'
const USER_KEY = 'staj_username'

/**
 * Eksik 5 — yenileme anahtarı.
 *
 * Erişim token'ı 10 dakika yaşıyor; bu, çalınan bir token'ın işe yarama
 * penceresini dar tutmak için bilinçli bir seçim (JWT iptal edilemiyor,
 * sunucu onu doğrularken veritabanına bakmıyor). Ama tek başına 10 dakika,
 * kullanıcıyı yarım kalan poligonun üstünde giriş ekranına atıyordu.
 *
 * Artık arkada uzun ömürlü bir yenileme anahtarı duruyor ve erişim token'ı
 * sessizce tazeleniyor. Kullanıcı ancak OTURUM süresi (7 gün) dolduğunda
 * ya da çıkış yaptığında giriş ekranına döner.
 *
 * ---- BU DEĞER NEDEN localStorage'DA? ----
 *
 * Doğrusu httpOnly çerez olurdu: JavaScript okuyamaz, dolayısıyla bir XSS
 * açığı anahtarı çalamaz. Burada kullanılmadı çünkü arayüz (5173) ile API
 * (5000) AYRI kaynaklar; çapraz kaynak çerez için SameSite=None gerekiyor,
 * o da HTTPS istiyor — localhost geliştirme kurulumunda kurulamayan bir
 * zincir. Yayına alınırken doğru adım bu anahtarı çereze taşımaktır.
 *
 * Yine de savunmasız değil: anahtar her kullanımda DÖNÜYOR ve çalınan bir
 * kopya ikinci kez kullanıldığında sunucu bunu fark edip kullanıcının bütün
 * oturumlarını kapatıyor (bkz. AuthService.RefreshAsync).
 */
const REFRESH_KEY = 'staj_refresh_token'
const REFRESH_EXPIRES_KEY = 'staj_refresh_expires'

/** Erişim token'ı bu kadar kalınca proaktif olarak yenilenir. */
const YENILEME_PAYI_MS = 60_000

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
let refreshTimer = null

export function saveSession({ token, expiresAt, username, refreshToken, refreshTokenExpiresAt }) {
  localStorage.setItem(TOKEN_KEY, token)
  localStorage.setItem(EXPIRES_KEY, expiresAt)
  localStorage.setItem(USER_KEY, username)

  // Yenileme anahtarı KOŞULLU yazılıyor: sunucu bir gün bu alanı
  // göndermezse, eldeki geçerli anahtarın üstüne undefined yazıp oturumu
  // sessizce öldürmeyelim.
  if (refreshToken) {
    localStorage.setItem(REFRESH_KEY, refreshToken)
    localStorage.setItem(REFRESH_EXPIRES_KEY, refreshTokenExpiresAt)
  }

  hesabiKaydet({
    username,
    token,
    expiresAt,
    refreshToken: refreshToken ?? getRefreshToken(),
    refreshExpiresAt: refreshTokenExpiresAt ?? localStorage.getItem(REFRESH_EXPIRES_KEY),
  })
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

/**
 * Kayıtlı bir oturum hâlâ devralınabilir mi?
 *
 * Ölçüt artık ERİŞİM token'ı değil YENİLEME anahtarı: erişim token'ının
 * süresi dolmuş olabilir ama yenileme anahtarı duruyorsa oturum yaşıyor
 * demektir — hesaba geçildiğinde ilk istekte sessizce tazelenir.
 *
 * Eski ölçütte kalsaydık, 10 dakikadır dokunulmamış her hesap "süresi
 * dolmuş" görünüp gereksiz yere şifre isteyecekti.
 */
function oturumGecerliMi(hesap) {
  if (hesap?.refreshToken && hesap?.refreshExpiresAt) {
    return new Date(hesap.refreshExpiresAt).getTime() > Date.now()
  }
  // Yenileme anahtarı eklenmeden önce kaydedilmiş hesaplar için eski ölçüt.
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

  if (hesap.refreshToken) {
    localStorage.setItem(REFRESH_KEY, hesap.refreshToken)
    localStorage.setItem(REFRESH_EXPIRES_KEY, hesap.refreshExpiresAt)
  } else {
    // Devralınan hesabın yenileme anahtarı yoksa ÖNCEKİ hesabınki kalmasın:
    // yanlış sahibin anahtarıyla yenileme yapmak, sessizce başka birinin
    // oturumuna geçmek olurdu.
    localStorage.removeItem(REFRESH_KEY)
    localStorage.removeItem(REFRESH_EXPIRES_KEY)
  }

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

  // Yenileme anahtarı yalnızca AKTİF yuvadan siliniyor; hesap listesindeki
  // kopyası duruyor. Sunucuda da iptal EDİLMİYOR — bu "çıkış" değil,
  // "hesabı arka plana al". İptal etseydik geri dönerken şifre istenirdi.
  localStorage.removeItem(REFRESH_KEY)
  localStorage.removeItem(REFRESH_EXPIRES_KEY)

  zamanlayicilariDurdur()
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

export function getRefreshToken() {
  return localStorage.getItem(REFRESH_KEY)
}

/** Oturumun MUTLAK bitiş anı: bundan sonrası yeniden giriş ister. */
export function getRefreshExpiresAt() {
  const raw = localStorage.getItem(REFRESH_EXPIRES_KEY)
  return raw ? new Date(raw) : null
}

/**
 * Rozette gösterilecek kalan oturum süresi.
 *
 * Neden ayrı bir biçimlendirici? Sayaç eskiden erişim token'ına bakıyordu ve
 * hep 10 dakikanın altındaydı; m:ss yetiyordu. Artık oturum günlerce
 * sürebiliyor — aynı biçimle "10079:59" yazardı. Bir saatin altında dakika
 * ve saniye, üstünde gün/saat gösteriyoruz.
 */
export function kalanOturumMetni() {
  const biter = getRefreshExpiresAt() ?? getExpiresAt()
  if (!biter) return ''

  const ms = biter.getTime() - Date.now()
  if (ms <= 0) return '0:00'

  const dakika = Math.floor(ms / 60000)
  if (dakika >= 1440) return `${Math.floor(dakika / 1440)} gün`
  if (dakika >= 60) return `${Math.floor(dakika / 60)} sa`

  const saniye = Math.floor((ms % 60000) / 1000)
  return `${dakika}:${saniye.toString().padStart(2, '0')}`
}

/**
 * Oturum açık mı? (rota koruması bunu soruyor)
 *
 * Ölçüt YENİLEME anahtarı. Erişim token'ının süresi dolmuş olabilir — bu
 * artık "oturum bitti" demek değil; ilk API isteğinde sessizce tazelenecek.
 * Eski ölçütte kalsaydık, bilgisayarını 20 dakika bırakan kullanıcı
 * sunucuda oturumu apaçık geçerliyken giriş ekranına atılırdı.
 */
export function isAuthenticated() {
  const refreshExpiresAt = getRefreshExpiresAt()
  if (getRefreshToken() && refreshExpiresAt) {
    return refreshExpiresAt.getTime() > Date.now()
  }

  // Yenileme anahtarı yoksa (eski oturum) eski ölçüte düş.
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

  // Sunucudaki oturumu da kapat: yerel anahtarı silmek yeterli DEĞİL.
  // Silinen kopya, birinin daha önce ele geçirdiği kopyayı geçersiz kılmaz;
  // anahtar ancak sunucuda iptal edilince gerçekten ölür.
  const anahtar = getRefreshToken()
  if (anahtar) {
    // keepalive: kullanıcı çıkışın hemen ardından sekmeyi kapatabilir;
    // bu bayrak olmadan tarayıcı isteği yarıda keserdi.
    fetch('/api/auth/logout', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: anahtar }),
      keepalive: true,
    }).catch(() => {
      // Sunucuya ulaşılamasa bile yerel çıkış YAPILIYOR: kullanıcı çıkmak
      // istedi, ekranda kalmamalı. Anahtar zaten kendi süresinde ölecek.
    })
  }

  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(EXPIRES_KEY)
  localStorage.removeItem(USER_KEY)
  localStorage.removeItem(REFRESH_KEY)
  localStorage.removeItem(REFRESH_EXPIRES_KEY)

  zamanlayicilariDurdur()
}

/**
 * OTURUM süresi dolduğu an otomatik çıkış.
 *
 * Artık erişim token'ına değil YENİLEME anahtarına bakıyor. Eskisi gibi
 * kalsaydı kullanıcı, arkada sessizce tazelenen bir oturumun ortasında her
 * 10 dakikada bir dışarı atılırdı — yani yenileme hiç işe yaramazdı.
 *
 * Aynı çağrı proaktif yenilemeyi de kuruyor: ikisi tek yerden yönetilsin,
 * biri kurulup diğeri unutulmasın.
 */
export function scheduleAutoLogout(onLogout) {
  zamanlayicilariDurdur()

  const oturumBitis = getRefreshExpiresAt() ?? getExpiresAt()
  if (!oturumBitis) return

  logoutTimer = setTimeout(() => {
    clearSession()
    onLogout()
  }, Math.max(oturumBitis.getTime() - Date.now(), 0))

  yenilemeyiZamanla(onLogout)
}

/**
 * Erişim token'ının süresi dolmadan bir dakika önce sessizce yeniler.
 *
 * NEDEN sadece 401'i beklemiyoruz? Beklerdik de çalışırdı — authFetch zaten
 * 401'de yeniliyor. Ama o yol her seferinde bir isteğin BAŞARISIZ olmasını
 * gerektiriyor: kullanıcı yavaşlamayı hisseder ve daha kötüsü, authFetch'ten
 * geçmeyen istekler (haritanın doğrudan çektiği WMS karoları) sessizce boş
 * döner. Proaktif yenileme normal durumda 401'in hiç oluşmamasını sağlıyor;
 * 401 yolu ise ağ kesintisi, uyuyan sekme gibi durumlar için ağ olarak
 * duruyor.
 */
function yenilemeyiZamanla(onLogout) {
  if (refreshTimer) clearTimeout(refreshTimer)

  const erisimBitis = getExpiresAt()
  if (!erisimBitis || !getRefreshToken()) return

  const sure = erisimBitis.getTime() - Date.now() - YENILEME_PAYI_MS

  refreshTimer = setTimeout(async () => {
    const oldu = await oturumuYenile()
    if (oldu) {
      // Zincirin devamı: yeni token'ın kendi süresi için yeniden kur.
      yenilemeyiZamanla(onLogout)
    } else if (onLogout) {
      clearSession()
      onLogout()
    }
  }, Math.max(sure, 0))
}

function zamanlayicilariDurdur() {
  if (logoutTimer) { clearTimeout(logoutTimer); logoutTimer = null }
  if (refreshTimer) { clearTimeout(refreshTimer); refreshTimer = null }
}

/**
 * Erişim token'ını yenileme anahtarıyla tazeler.
 *
 * ---- NEDEN TEK UÇUŞ (single flight)? ----
 *
 * Harita açılırken onlarca istek aynı anda gidiyor. Token süresi dolmuşsa
 * hepsi birden 401 alır ve hepsi birden yenilemeye kalkar. Anahtar HER
 * KULLANIMDA DÖNDÜĞÜ için ilk çağrı başarılı olur, diğerleri artık iptal
 * edilmiş anahtarı sunar — sunucu bunu hırsızlık sayıp kullanıcının bütün
 * oturumlarını kapatır. Yani korumanın kendisi, kendi kullanıcımızı dışarı
 * atardı.
 *
 * Çözüm: uçuştaki sözü paylaşmak. İlk çağıran isteği başlatır, sonrakiler
 * aynı sözü bekler; sunucuya tek istek gider.
 *
 * @returns {Promise<boolean>} true → oturum tazelendi.
 */
let yenilemeSozu = null

export function oturumuYenile() {
  if (yenilemeSozu) return yenilemeSozu

  const anahtar = getRefreshToken()
  if (!anahtar) return Promise.resolve(false)

  yenilemeSozu = (async () => {
    try {
      const res = await fetch('/api/auth/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: anahtar }),
      })

      if (!res.ok) return false

      const oturum = await res.json()
      if (!oturum?.token) return false

      // Sunucu YENİ bir yenileme anahtarı da verdi; saklamazsak bir sonraki
      // yenileme ölmüş anahtarla denenir ve hırsızlık alarmını tetikler.
      saveSession(oturum)
      return true
    } catch {
      // Ağ hatası oturumun bittiği anlamına GELMEZ (tünelden geçen tren,
      // uyuyan wifi). Burada oturumu silmiyoruz; çağıran taraf isteği
      // yine de başarısız görecek ama kullanıcı geri döndüğünde oturumu
      // yerinde duruyor olacak.
      return false
    } finally {
      yenilemeSozu = null
    }
  })()

  return yenilemeSozu
}

/**
 * Korumalı API çağrıları.
 *
 * 401 gelirse artık DOĞRUDAN çıkış yapılmıyor: önce oturum yenilenmeye
 * çalışılıyor, başarılıysa istek yeni token'la BİR KEZ tekrarlanıyor.
 * Yalnızca yenileme de başarısız olursa oturum kapanıyor.
 *
 * Neden bir kez? İkinci 401, "token eskiydi" ile açıklanamaz — taze bir
 * token'la da reddedildiyse sorun yetkide ya da oturumun kendisinde
 * demektir. Sınırsız denemek, sunucu ısrarla 401 döndüğünde sonsuz döngü
 * olurdu.
 *
 * İsteğin gövdesi `options` içinde metin olarak duruyor, bu yüzden tekrar
 * göndermek güvenli. Gövde bir akış (stream) olsaydı ilk denemede tükenir
 * ve tekrar boş giderdi.
 */
export async function authFetch(url, options = {}, onUnauthorized) {
  const gonder = () => fetch(url, {
    ...options,
    headers: {
      ...(options.headers || {}),
      Authorization: `Bearer ${getToken()}`,
    },
  })

  let response = await gonder()

  if (response.status === 401 && getRefreshToken()) {
    if (await oturumuYenile()) {
      response = await gonder()
    }
  }

  if (response.status === 401) {
    clearSession()
    if (onUnauthorized) onUnauthorized()
    throw new Error('Oturum süresi doldu')
  }

  return response
}
