import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App.jsx'
import ErrorBoundary from './ErrorBoundary.jsx'
import { temayiUygula } from './tema'
import './index.css'

// Temayı React'ten ÖNCE uyguluyoruz.
//
// Bir bileşenin useEffect'ine bıraksaydık ilk kare varsayılan (karanlık)
// palette çizilir, hemen ardından aydınlığa geçerdi: sayfa her açılışta
// gözle görülür şekilde "yanıp sönerdi" (flash of wrong theme). Burada
// <html> özniteliği React daha çalışmadan yazılıyor, ilk kare doğru geliyor.
temayiUygula()

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    {/* Hata sınırı en dışta: altındaki HERHANGİ bir bileşen render sırasında
        patlarsa beyaz ekran yerine anlaşılır bir kart gösterilir.
        Not: yalnızca ALT BİLEŞENLERİN render'ındaki hatalar yakalanır —
        buraya verilen JSX oluşturulurken fırlayan hata sınırın dışında kalır. */}
    <ErrorBoundary>
      <App />
    </ErrorBoundary>
  </React.StrictMode>,
)
