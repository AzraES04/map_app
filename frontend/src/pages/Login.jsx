import { useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import { saveSession } from '../auth'

export default function Login() {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(false)
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
      navigate('/map', { replace: true })
    } catch (err) {
      setError(`Giriş yapılamadı: ${err.message}`)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="login-wrapper">
      <form className="login-card" onSubmit={handleSubmit}>
        <div className="login-logo">🗺️</div>
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

        <button type="submit" disabled={loading}>
          {loading ? 'Giriş yapılıyor…' : 'Giriş Yap'}
        </button>

        {error && <p className="error-banner">{error}</p>}

        <p className="login-hint">Demo kullanıcı: <code>admin</code> / <code>staj123</code></p>
      </form>
    </div>
  )
}
