// ============================================================================
//  Karanlık / aydınlık tema
//
//  NASIL ÇALIŞIYOR?
//  Tek bir öznitelik: <html data-tema="aydinlik">. CSS tarafında bütün renkler
//  zaten DEĞİŞKEN (--yuzey-*, --metin-*, --kenar…); tema değiştirmek o
//  değişkenlerin değerini değiştirmekten ibaret. Hiçbir bileşen "hangi
//  temadayım?" diye sormuyor, bilmesine de gerek yok.
//
//  NEDEN class DEĞİL de data-tema?
//  Sınıf listesi başka amaçlarla da kullanılıyor (uzay sahnesi, çizim modu);
//  temayı ayrı bir özniteliğe koymak "kim neyi ekledi?" karışıklığını
//  önlüyor. CSS tarafında da seçici daha okunur: :root[data-tema="aydinlik"].
//
//  ÜÇ DURUM VAR, İKİ DEĞİL:
//      'sistem'   → işletim sisteminin tercihi (varsayılan)
//      'karanlik' → kullanıcı açıkça seçti
//      'aydinlik' → kullanıcı açıkça seçti
//  "Sistem" ayrı bir durum olmasaydı, kullanıcının makinesi akşam karanlığa
//  geçtiğinde uygulama takip edemezdi. Açık seçim yapıldığı anda sistem
//  tercihi ARTIK DİNLENMİYOR — kullanıcının iradesi işletim sistemininkini
//  ezer.
// ============================================================================

const ANAHTAR = 'staj.tema'

export const TEMALAR = {
  sistem: 'sistem',
  aydinlik: 'aydinlik',
  karanlik: 'karanlik',
}

/** Kullanıcının kayıtlı tercihi; hiç seçim yapılmadıysa 'sistem'. */
export function temaTercihi() {
  try {
    const kayitli = localStorage.getItem(ANAHTAR)
    return kayitli in TEMALAR ? kayitli : TEMALAR.sistem
  } catch {
    // localStorage kapalı olabilir (gizli sekme, sıkı gizlilik ayarı).
    // Tema tercihi kritik bir veri değil; sessizce varsayılana düşüyoruz.
    return TEMALAR.sistem
  }
}

/** İşletim sistemi karanlık mı istiyor? */
function sistemKaranlikMi() {
  return typeof window.matchMedia === 'function'
    && window.matchMedia('(prefers-color-scheme: dark)').matches
}

/** Tercih + sistem birleşimi: ekranda GERÇEKTEN hangi tema var? */
export function etkinTema(tercih = temaTercihi()) {
  if (tercih === TEMALAR.sistem) {
    return sistemKaranlikMi() ? TEMALAR.karanlik : TEMALAR.aydinlik
  }
  return tercih
}

/**
 * Temayı belgeye uygular.
 *
 * Karanlık temada öznitelik HİÇ YAZILMIYOR (siliniyor). Sebep: CSS'te
 * varsayılan palet karanlık; aydınlık olan onu EZEN bir katman. Karanlıkta da
 * öznitelik yazsaydık iki yerde tanımlı bir "varsayılan" olurdu.
 *
 * color-scheme ayrıca tarayıcının KENDİ çizdiği parçaları da (kaydırma
 * çubuğu, saat seçici paneli, otomatik doldurma) temaya uyduruyor. Bu satır
 * olmadan aydınlık temada koyu bir kaydırma çubuğu kalıyordu.
 */
export function temayiUygula(tercih = temaTercihi()) {
  const etkin = etkinTema(tercih)
  const kok = document.documentElement

  if (etkin === TEMALAR.aydinlik) {
    kok.setAttribute('data-tema', 'aydinlik')
  } else {
    kok.removeAttribute('data-tema')
  }

  kok.style.colorScheme = etkin === TEMALAR.aydinlik ? 'light' : 'dark'
  return etkin
}

/** Tercihi kaydeder ve hemen uygular. */
export function temayiSec(tercih) {
  try {
    if (tercih === TEMALAR.sistem) {
      localStorage.removeItem(ANAHTAR)
    } else {
      localStorage.setItem(ANAHTAR, tercih)
    }
  } catch {
    /* saklanamadı; tema yine de bu oturum için uygulanıyor */
  }

  return temayiUygula(tercih)
}

/**
 * Sistem tercihi değişirse (kullanıcı işletim sistemini karanlığa alırsa)
 * uygulamayı takip ettirir. Yalnızca 'sistem' seçiliyken iş görüyor.
 *
 * @returns {() => void} dinleyiciyi kaldıran fonksiyon
 */
export function sistemTemasiniDinle(geriCagir) {
  if (typeof window.matchMedia !== 'function') return () => {}

  const sorgu = window.matchMedia('(prefers-color-scheme: dark)')

  const degisti = () => {
    if (temaTercihi() !== TEMALAR.sistem) return   // kullanıcı açıkça seçmiş
    geriCagir(temayiUygula(TEMALAR.sistem))
  }

  sorgu.addEventListener('change', degisti)
  return () => sorgu.removeEventListener('change', degisti)
}
