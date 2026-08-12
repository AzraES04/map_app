import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import Login from './pages/Login.jsx'
import MapPage from './pages/MapPage.jsx'
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
        {/* Kök adres: giriş yapılmışsa haritaya, yapılmamışsa login'e */}
        <Route path="*" element={<Navigate to={isAuthenticated() ? '/map' : '/login'} replace />} />
      </Routes>
    </BrowserRouter>
  )
}
