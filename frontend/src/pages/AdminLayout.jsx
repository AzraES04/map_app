import { useCallback, useEffect, useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { clearSession } from '../auth'
import { kendiYetkilerim } from '../adminApi'
import { YONETIM_EKRANLARI } from '../yonetimMenusu'
import { KullanicilarIkonu, RolIkonu, GeriIkonu, PoiIkonu, GuzergahIkonu, SilIkonu } from '../icons'
import HesapSecici from '../HesapSecici.jsx'
import TemaDugmesi from '../TemaDugmesi.jsx'

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

/**
 * Anahtar → ikon bileşeni.
 *
 * Menü TANIMI artık ../yonetimMenusu.js'te (harita ekranı da aynı listeye
 * bakıyor — gerekçesi o dosyanın başında). Orada JSX olamayacağı için
 * ikonlar metin anahtarıyla taşınıyor ve burada bileşene çevriliyor.
 */
const IKONLAR = {
  kullanicilar: KullanicilarIkonu,
  rol: RolIkonu,
  poi: PoiIkonu,
  guzergah: GuzergahIkonu,
  // Tur da güzergah simgesini kullanıyor: ikisi de "sıralı duraklardan
  // oluşan bir yol". Ayrı bir simge çizmek, aralarındaki akrabalığı
  // gizlemek olurdu.
  tur: GuzergahIkonu,
  cop: SilIkonu,
}

export default function AdminLayout() {
  const navigate = useNavigate()

  // Yetkisi olmayan ekranın menüde HİÇ görünmemesi kuralı (Ödev 7'den beri).
  //
  // null = "henüz okunmadı". O sırada bütün maddeler gösteriliyor: menüyü boş
  // çizip bir kare sonra doldurmak, her sayfa açılışında göze çarpan bir
  // sıçrama üretirdi. Yanlışlıkla gösterilen bir maddeye tıklandığında da
  // ekran sunucudan 403 alıp hatayı yazar — asıl kontrol zaten orada.
  const [yetkilerim, setYetkilerim] = useState(null)

  const oturumBitti = useCallback(
    () => navigate('/login', { replace: true, state: { expired: true } }),
    [navigate],
  )

  useEffect(() => {
    let iptal = false
    kendiYetkilerim(oturumBitti)
      .then((matris) => {
        if (!iptal) setYetkilerim(matris.permissions.filter((y) => y.granted).map((y) => y.name))
      })
      .catch(() => { /* okunamadı → maddeler görünür kalır, kontrol sunucuda */ })
    return () => { iptal = true }
  }, [oturumBitti])

  // yetki === null → HERKESE açık madde (bkz. yonetimMenusu.js → Çöp Kutusu).
  // Bu maddelerin ekranı tek bir yetkiyle eşleşmiyor; kural sunucuda ve
  // ekranın kendi içinde işliyor.
  const gorunur = (yetki) =>
    !yetki || yetkilerim === null || yetkilerim.includes(yetki)

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
          {YONETIM_EKRANLARI.filter((madde) => gorunur(madde.yetki)).map(({ yol, baslik, altyazi, ikon }) => {
            const Ikon = IKONLAR[ikon]
            return (
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
            )
          })}
        </ul>

        {/* Menünün dibi: haritaya dönüş ve oturum bilgisi */}
        <div className="admin-navbar-alt">
          <NavLink to="/map" className="admin-menu-link ikincil">
            <span className="admin-menu-ikon"><GeriIkonu /></span>
            <span className="admin-menu-metin"><strong>Haritaya dön</strong></span>
          </NavLink>

          {/* Ödev 11: hesap değiştirici burada da duruyor. Rozet haritada
              menüye dönüşüp panelde ölü kalsaydı, kullanıcı aynı öğenin bir
              ekranda tıklanıp diğerinde tıklanmamasını arıza sanardı. */}
          <div className="admin-oturum">
            <HesapSecici />
            {/* Tema tercihi iki ekranda da aynı yerde: harita ile panel
                arasında gidip gelen kullanıcı düğmeyi aramak zorunda kalmasın. */}
            <TemaDugmesi />
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
