import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App.jsx'
import ErrorBoundary from './ErrorBoundary.jsx'
import './index.css'

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
