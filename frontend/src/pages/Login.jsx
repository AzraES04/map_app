import { useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import { saveSession, girisAnimasyonunuSifirla } from '../auth'

// Girişten sonra haritaya geçmeden önceki çıkış animasyonunun süresi.
// CSS'teki .login-wrapper.cikis geçişleriyle aynı tutulmalı.
const CIKIS_SURESI = 850

export default function Login() {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(false)
  // Giriş başarılı: kart soluyor, gezegen yaklaşıyor, ekran kararıp haritaya devrediyor
  const [cikis, setCikis] = useState(false)
  const navigate = useNavigate()
  const location = useLocation()

  // Token süresi dolup login'e yönlendirildiysek bilgi mesajı göster
  const sessionExpired = location.state?.expired

  const handleSubmit = async (e) => {
    e.preventDefault()
    setLoading(true)
    setError(null)
    try {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password }),
      })
      if (res.status === 401) {
        setError('Kullanıcı adı veya şifre hatalı.')
        return
      }
      if (!res.ok) throw new Error(`Sunucu hatası: ${res.status}`)
      const data = await res.json()
      saveSession(data)

      // Haritadaki açılış sahnesi MUTLAKA oynasın: login'deki uzay teması
      // "dünyadan Türkiye'ye iniş" ile kesintisiz devam etsin.
      girisAnimasyonunuSifirla()

      // Önce çıkış animasyonu, sonra sayfa geçişi. Ekran karardığı an harita
      // ekranı da uzay sahnesiyle açıldığı için arada görsel kopukluk olmuyor.
      setCikis(true)
      setTimeout(() => navigate('/map', { replace: true }), CIKIS_SURESI)
    } catch (err) {
      setError(`Giriş yapılamadı: ${err.message}`)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className={`login-wrapper${cikis ? ' cikis' : ''}`}>
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
        <p className="login-subtitle">Devam etmek için giriş yapın</p>

        {sessionExpired && (
          <p className="info-banner">Oturum süreniz doldu, lütfen tekrar giriş yapın.</p>
        )}

        <label htmlFor="username">Kullanıcı Adı</label>
        <input
          id="username"
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          placeholder="admin"
          autoComplete="username"
          required
        />

        <label htmlFor="password">Şifre</label>
        <input
          id="password"
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          placeholder="••••••••"
          autoComplete="current-password"
          required
        />

        <button type="submit" disabled={loading || cikis}>
          {cikis ? 'Harita hazırlanıyor…' : loading ? 'Giriş yapılıyor…' : 'Giriş Yap'}
        </button>

        {error && <p className="error-banner">{error}</p>}

        <p className="login-hint">Demo kullanıcı: <code>admin</code> / <code>staj123</code></p>
      </form>

      {/* Çıkışta ekranı karartıp haritaya devreden perde */}
      <div className="login-karartma" aria-hidden="true" />
    </div>
  )
}
