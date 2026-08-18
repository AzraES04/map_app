import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { clearSession, getUsername } from '../auth'
import { KullanicilarIkonu, RolIkonu, GeriIkonu, KullaniciIkonu } from '../icons'

// ============================================================================
//  Yönetim paneli çerçevesi (Ödev 6 / Madde 1)
//
//  Sol tarafa DİKEY yaslanmış navigasyon menüsü + sağda değişen içerik.
//  İçerik <Outlet /> ile geliyor: React Router, adrese göre hangi ekranın
//  oraya yerleşeceğine karar veriyor. Menü ise sabit kalıyor — sayfa
//  değiştiğinde yeniden kurulmadığı için geçişte titreme olmuyor.
//
//  Menü ile içeriği ayırmanın alternatifi, her ekranın kendi menüsünü
//  çizmesiydi; o zaman menüye bir madde eklemek iki dosya değiştirmek
//  demek olurdu ve iki kopya kolayca birbirinden ayrı düşerdi.
// ============================================================================

/** Menü maddeleri tek bir listede: yeni ekran eklemek buraya bir satır. */
const MENU = [
  {
    yol: '/admin/users',
    baslik: 'Kullanıcı Listesi',
    altyazi: 'Ekle / Güncelle / Çıkar',
    Ikon: KullanicilarIkonu,
  },
  {
    yol: '/admin/roles',
    baslik: 'Rol Listesi',
    altyazi: 'Ekle / Güncelle / Sil',
    Ikon: RolIkonu,
  },
]

export default function AdminLayout() {
  const navigate = useNavigate()

  const cikisYap = () => {
    clearSession()
    navigate('/login', { replace: true })
  }

  return (
    <div className="admin-layout">
      {/* ---------------- Sol dikey navbar ---------------- */}
      <nav className="admin-navbar" aria-label="Yönetim menüsü">
        <div className="admin-marka">
          <span className="admin-marka-nokta" aria-hidden="true" />
          <span>
            <strong>Yönetim Paneli</strong>
            <small>Harita Uygulaması</small>
          </span>
        </div>

        <ul className="admin-menu">
          {MENU.map(({ yol, baslik, altyazi, Ikon }) => (
            <li key={yol}>
              {/*
                NavLink, Link'ten farklı olarak "şu an bu adrestesin" bilgisini
                kendisi verir. className'e fonksiyon geçince isActive parametresi
                geliyor; seçili maddeyi vurgulamak için elle adres karşılaştırmak
                gerekmiyor.
              */}
              <NavLink
                to={yol}
                className={({ isActive }) => `admin-menu-link${isActive ? ' active' : ''}`}
              >
                <span className="admin-menu-ikon"><Ikon /></span>
                <span className="admin-menu-metin">
                  <strong>{baslik}</strong>
                  <small>{altyazi}</small>
                </span>
              </NavLink>
            </li>
          ))}
        </ul>

        {/* Menünün dibi: haritaya dönüş ve oturum bilgisi */}
        <div className="admin-navbar-alt">
          <NavLink to="/map" className="admin-menu-link ikincil">
            <span className="admin-menu-ikon"><GeriIkonu /></span>
            <span className="admin-menu-metin"><strong>Haritaya dön</strong></span>
          </NavLink>

          <div className="admin-oturum">
            <span className="badge user"><KullaniciIkonu /> {getUsername()}</span>
            <button type="button" className="logout-btn" onClick={cikisYap}>Çıkış</button>
          </div>
        </div>
      </nav>

      {/* ---------------- İçerik ---------------- */}
      <main className="admin-icerik">
        <Outlet />
      </main>
    </div>
  )
}
