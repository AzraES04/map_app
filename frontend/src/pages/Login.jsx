import { useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import { saveSession, girisAnimasyonunuSifirla } from '../auth'

// Girişten sonra haritaya geçmeden önceki çıkış animasyonunun süresi.
// CSS'teki .login-wrapper.cikis geçişleriyle aynı tutulmalı.
const CIKIS_SURESI = 850

/**
 * Sunucudan gelen hata gövdesini okur. Backend hataları { message } biçiminde
 * dönüyor; gövde JSON değilse duruma göre genel bir cümleye düşüyoruz.
 */
async function hataMesaji(res, varsayilan) {
  try {
    const govde = await res.json()
    if (govde?.message) return govde.message
    if (govde?.errors) return Object.values(govde.errors).flat().join(' ')
  } catch {
    /* JSON değil */
  }
  return varsayilan
}

export default function Login() {
  const location = useLocation()

  // Ödev 10: aynı kart iki iş yapıyor — giriş ve kayıt.
  // Ayrı sayfa yapmadık: kullanıcı "kayıt olayım mı, giriş mi yapayım" diye
  // düşünürken sayfa değiştirmek zorunda kalmasın, iki adım da bir tık uzakta.
  const [mod, setMod] = useState('giris')

  // Ödev 11: hesap değiştiriciden gelindiyse kullanıcı adı DOLU başlıyor,
  // yalnızca şifre isteniyor. Şifre saklanmıyor (bkz. auth.js).
  const [username, setUsername] = useState(() => location.state?.username ?? '')
  const [password, setPassword] = useState('')
  const [error, setError] = useState(null)
  const [bilgi, setBilgi] = useState(null)
  const [loading, setLoading] = useState(false)
  // Giriş başarılı: kart soluyor, gezegen yaklaşıyor, ekran kararıp haritaya devrediyor
  const [cikis, setCikis] = useState(false)
  const navigate = useNavigate()

  // Token süresi dolup login'e yönlendirildiysek bilgi mesajı göster
  const sessionExpired = location.state?.expired

  // Hesap değiştiriciden gelindiyse odak doğrudan şifre alanına gitsin.
  const hesapDegistirme = !!location.state?.username

  const modDegistir = (yeni) => {
    if (yeni === mod) return
    setMod(yeni)
    setError(null)
    setBilgi(null)
    setPassword('')   // şifre alanı modlar arasında taşınmasın
  }

  const girisYap = async () => {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    })

    if (res.status === 401) {
      setError('Kullanıcı adı veya şifre hatalı.')
      return
    }

    // Ödev 10: şifre doğru ama hesap yönetici onayı bekliyor.
    // Sunucu bu durumu 403 ile ayırıyor; mesajı olduğu gibi gösteriyoruz.
    if (res.status === 403) {
      setError(await hataMesaji(res, 'Hesabınız henüz onaylanmadı.'))
      return
    }

    if (res.status === 429) {
      setError(await hataMesaji(res, 'Çok fazla deneme yaptınız, biraz bekleyin.'))
      return
    }

    if (!res.ok) throw new Error(await sunucuHatasi(res))

    saveSession(await res.json())

    // Haritadaki açılış sahnesi MUTLAKA oynasın: login'deki uzay teması
    // "dünyadan Türkiye'ye iniş" ile kesintisiz devam etsin.
    girisAnimasyonunuSifirla()

    // Önce çıkış animasyonu, sonra sayfa geçişi. Ekran karardığı an harita
    // ekranı da uzay sahnesiyle açıldığı için arada görsel kopukluk olmuyor.
    setCikis(true)
    setTimeout(() => navigate('/map', { replace: true }), CIKIS_SURESI)
  }

  /**
   * Beklenmeyen bir cevabın mesajı.
   *
   * NEDEN ÖZEL BİR DURUM VAR?
   * Geliştirmede en sık karşılaşılan hata "backend henüz ayağa kalkmadı"dır:
   * Vite'ın vekili arkadaki sunucuya bağlanamayınca KENDİ ürettiği bir 500
   * döndürüyor ve gövdesi JSON değil. Ekranda "Sunucu hatası: 500" yazıyordu —
   * teknik olarak doğru ama yanıltıcı: sunucu hata vermiyor, sunucu HENÜZ YOK.
   * Kullanıcı da haklı olarak kodda hata arıyordu.
   *
   * Ayrımı GÖVDEDEN yapıyoruz: uygulamamızın ürettiği her hata cevabı
   * { message } taşıyan bir JSON'dur. JSON gelmiyorsa cevap bizden çıkmamıştır.
   */
  const sunucuHatasi = async (res) => {
    try {
      const govde = await res.json()
      if (govde?.message) return govde.message
    } catch {
      /* JSON değil — aşağıda ele alınıyor */
    }

    // 502/503/504 zaten "ara sunucu arkadakine ulaşamadı" demek; gövdesi
    // JSON olmayan 500'ü de aynı kümeye alıyoruz.
    if ([500, 502, 503, 504].includes(res.status)) {
      return 'Sunucuya ulaşılamıyor. Backend penceresi hazır olana kadar bekleyip '
        + 'tekrar deneyin (ilk açılış birkaç saniye sürebilir).'
    }

    return `Sunucu beklenmeyen bir cevap verdi (${res.status}).`
  }

  const kayitOl = async () => {
    const res = await fetch('/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    })

    if (!res.ok) {
      // 500/502/503/504 + JSON olmayan gövde = backend ayakta değil;
      // "Kayıt yapılamadı" demek sebebi gizlerdi.
      setError([500, 502, 503, 504].includes(res.status)
        ? await sunucuHatasi(res)
        : await hataMesaji(res, 'Kayıt yapılamadı.'))
      return
    }

    const data = await res.json()

    // Kayıttan sonra otomatik giriş YOK — hesap zaten onay bekliyor.
    // Kullanıcıyı giriş sekmesine alıp ne olduğunu tek cümleyle söylüyoruz.
    setMod('giris')
    setPassword('')
    setBilgi(data.message)
  }

  const handleSubmit = async (e) => {
    e.preventDefault()
    setLoading(true)
    setError(null)
    setBilgi(null)
    try {
      if (mod === 'giris') await girisYap()
      else await kayitOl()
    } catch (err) {
      setError(`İşlem tamamlanamadı: ${err.message}`)
    } finally {
      setLoading(false)
    }
  }

  const kayitModu = mod === 'kayit'

  return (
    <div className={`login-wrapper${cikis ? ' cikis' : ''}`}>
      {/* Yıldız alanı: üç katman, farklı boyut ve hızda. Tek katman düz bir
          desen gibi görünürdü; farklı hızlar derinlik (paralaks) hissi veriyor.
          aria-hidden — ekran okuyucuya anlatılacak bir bilgi taşımıyor. */}
      <div className="yildiz-alani" aria-hidden="true">
        <div className="yildiz-katman uzak" />
        <div className="yildiz-katman orta" />
        <div className="yildiz-katman yakin" />
        {/* Ara ara geçen kayan yıldız — hareket sürekli olmasın diye
            uzun aralıklı bir animasyon. */}
        <div className="kayan-yildiz" />
        <div className="kayan-yildiz ikinci" />
      </div>

      {/* Ekranın altından yükselen gezegen kavsi: dev bir dairenin yalnızca
          üst kenarı görünüyor, üstündeki ince parlak çizgi atmosfer. */}
      <div className="login-gezegen" aria-hidden="true" />

      <form className="login-card" onSubmit={handleSubmit}>
        <div className="login-logo" aria-hidden="true">
          <svg viewBox="0 0 48 48" width="46" height="46" fill="none"
               stroke="currentColor" strokeWidth="1.5" strokeLinecap="round">
            <circle cx="24" cy="24" r="19" />
            <path d="M5 24h38" />
            <path d="M24 5a30 30 0 0 1 0 38 30 30 0 0 1 0-38Z" />
            <path d="M9 14c6 2.6 10 3.8 15 3.8S33 16.6 39 14M9 34c6-2.6 10-3.8 15-3.8S33 31.4 39 34"
                  opacity="0.5" />
          </svg>
        </div>

        <h1>Harita Uygulaması</h1>
        <p className="login-subtitle">
          {kayitModu ? 'Yeni hesap oluşturun' : 'Devam etmek için giriş yapın'}
        </p>

        {/* Ödev 10: giriş / kayıt seçimi */}
        <div className="login-sekme" role="tablist" aria-label="Giriş veya kayıt">
          <button
            type="button"
            role="tab"
            aria-selected={!kayitModu}
            className={!kayitModu ? 'active' : ''}
            onClick={() => modDegistir('giris')}
          >
            Giriş Yap
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={kayitModu}
            className={kayitModu ? 'active' : ''}
            onClick={() => modDegistir('kayit')}
          >
            Kayıt Ol
          </button>
        </div>

        {sessionExpired && !bilgi && (
          <p className="info-banner">Oturum süreniz doldu, lütfen tekrar giriş yapın.</p>
        )}

        {hesapDegistirme && !bilgi && !sessionExpired && (
          <p className="info-banner">
            <strong>{location.state.username}</strong> hesabının oturumu kapanmış.
            Devam etmek için şifrenizi girin.
          </p>
        )}

        {bilgi && <p className="info-banner">{bilgi}</p>}

        <label htmlFor="username">Kullanıcı Adı</label>
        <input
          id="username"
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          placeholder={kayitModu ? 'en az 3 karakter' : 'admin'}
          autoComplete="username"
          minLength={kayitModu ? 3 : undefined}
          required
        />

        <label htmlFor="password">Şifre</label>
        <input
          id="password"
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          placeholder={kayitModu ? 'en az 6 karakter' : '••••••••'}
          // eslint-disable-next-line jsx-a11y/no-autofocus -- kullanıcı adı
          // zaten dolu geldiyse tek eksik şifre; odağı elle taşımak gereksiz adım.
          autoFocus={hesapDegistirme}
          // Tarayıcının şifre yöneticisine "bu yeni bir şifre" demek:
          // kayıt sırasında kayıtlı şifreyi doldurmaya çalışmasın.
          autoComplete={kayitModu ? 'new-password' : 'current-password'}
          minLength={kayitModu ? 6 : undefined}
          required
        />

        <button type="submit" disabled={loading || cikis}>
          {cikis ? 'Harita hazırlanıyor…'
            : loading ? (kayitModu ? 'Kaydediliyor…' : 'Giriş yapılıyor…')
              : (kayitModu ? 'Kayıt Ol' : 'Giriş Yap')}
        </button>

        {error && <p className="error-banner">{error}</p>}

        <p className="login-hint">
          {kayitModu
            ? 'Kaydınız yönetici onayından sonra kullanıma açılır.'
            : <>Demo kullanıcı: <code>admin</code> / <code>staj123</code></>}
        </p>
      </form>

      {/* Çıkışta ekranı karartıp haritaya devreden perde */}
      <div className="login-karartma" aria-hidden="true" />
    </div>
  )
}
