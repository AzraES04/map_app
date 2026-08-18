import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import Login from './pages/Login.jsx'
import MapPage from './pages/MapPage.jsx'
import AdminLayout from './pages/AdminLayout.jsx'
import AdminUsers from './pages/AdminUsers.jsx'
import AdminRoles from './pages/AdminRoles.jsx'
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
          <Route path="users" element={<AdminUsers />} />
          <Route path="roles" element={<AdminRoles />} />
        </Route>

        {/* Kök adres: giriş yapılmışsa haritaya, yapılmamışsa login'e */}
        <Route path="*" element={<Navigate to={isAuthenticated() ? '/map' : '/login'} replace />} />
      </Routes>
    </BrowserRouter>
  )
}
