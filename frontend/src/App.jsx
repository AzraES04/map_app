import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import Login from './pages/Login.jsx'
import MapPage from './pages/MapPage.jsx'
import AdminLayout from './pages/AdminLayout.jsx'
import AdminUsers from './pages/AdminUsers.jsx'
import AdminRoles from './pages/AdminRoles.jsx'
import AdminPoi from './pages/AdminPoi.jsx'
import AdminGuzergah from './pages/AdminGuzergah.jsx'
import Guvenlik from './pages/Guvenlik.jsx'
import CopKutusu from './pages/CopKutusu.jsx'
import { isAuthenticated } from './auth'

// Korumalı rota: geçerli token yoksa login'e yönlendirir
function RequireAuth({ children }) {
  return isAuthenticated() ? children : <Navigate to="/login" replace />
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<Login />} />

        {/* Güvenlik ekranı /admin ALTINDA DEĞİL — yönetim işlemi değil,
            herkesin kendi hesabı için kullandığı bir ayar. AdminLayout'un
            içine koysaydık yönetim yetkisi olmayan kullanıcı (örn.
            "Ulaşım Kullanıcısı") kendi korumasını hiç açamazdı. */}
        <Route
          path="/guvenlik"
          element={<RequireAuth><Guvenlik /></RequireAuth>}
        />

        {/* Çöp kutusu da /admin ALTINDA DEĞİL: liste herkese açık (silinmiş
            bir kaydın adı zaten herkesin görebildiği bir bilgiydi), geri alma
            ise kaydın TÜRÜNE göre yetki istiyor. AdminLayout'a koysaydık
            yönetim yetkisi olmayan kullanıcı kendi sildiği noktayı bile
            göremezdi. */}
        <Route
          path="/cop"
          element={<RequireAuth><CopKutusu /></RequireAuth>}
        />
        <Route
          path="/map"
          element={
            <RequireAuth>
              <MapPage />
            </RequireAuth>
          }
        />

        {/*
          Ödev 6 — Yönetim paneli.
          İÇ İÇE (nested) rota: /admin altındaki tüm adresler AdminLayout'un
          içinde açılır. Sol dikey navbar bir kez çizilir, sağdaki <Outlet />
          adrese göre değişir. Her ekrana ayrı ayrı navbar koymak yerine
          çerçeveyi tek yerde tutmanın yolu bu.
        */}
        <Route
          path="/admin"
          element={
            <RequireAuth>
              <AdminLayout />
            </RequireAuth>
          }
        >
          {/* index: tam olarak "/admin" istendiğinde açılacak ekran */}
          <Route index element={<Navigate to="/admin/users" replace />} />
          {/* Çöp kutusu YÖNETİM PANELİNİN İÇİNDE de açılıyor.
              Aynı ekran iki adreste: `/cop` (herkes, hesap menüsünden) ve
              `/admin/cop` (yönetim menüsünden, sol çubuk yerinde kalarak).
              Menü maddesi `/cop`'a gitseydi tıklayan kullanıcı panelden
              dışarı düşer, sol çubuk kaybolur ve bunu bir arıza sanardı. */}
          <Route path="cop" element={<CopKutusu />} />
          <Route path="users" element={<AdminUsers />} />
          <Route path="roles" element={<AdminRoles />} />
          {/* Ödev 12: POI listesi + kategori yönetimi tek ekranda */}
          <Route path="poi" element={<AdminPoi />} />
          {/* Ödev 16: güzergah tanımı + durakların sürükle-bırak sıralaması */}
          <Route path="guzergah" element={<AdminGuzergah />} />
        </Route>

        {/* Kök adres: giriş yapılmışsa haritaya, yapılmamışsa login'e */}
        <Route path="*" element={<Navigate to={isAuthenticated() ? '/map' : '/login'} replace />} />
      </Routes>
    </BrowserRouter>
  )
}
