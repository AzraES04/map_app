import { Component } from 'react'

/**
 * HATA SINIRI (Error Boundary)
 *
 * React'te bir bileşen render sırasında hata fırlatırsa, React tüm ağacı
 * söker ve ekran BOMBOŞ kalır. Kullanıcı ne olduğunu anlamaz, hata mesajı
 * yalnızca konsolda görünür.
 *
 * Bu bileşen o hatayı yakalayıp anlaşılır bir kart gösterir. Bu projede
 * gerçekten yaşandı: `ol/Map` importunun global `Map`'i gölgelemesi yüzünden
 * stil fonksiyonu patlamış ve harita beyaz ekrana dönmüştü.
 *
 * Neden sınıf bileşeni? Hata yakalama için gereken getDerivedStateFromError
 * ve componentDidCatch yaşam döngülerinin hook karşılığı YOK — React'te hata
 * sınırları hâlâ yalnızca sınıf bileşenleriyle yazılabiliyor.
 */
export default class ErrorBoundary extends Component {
  constructor(props) {
    super(props)
    this.state = { hata: null }
  }

  /** Hata olduğunda state'i günceller → yedek arayüz çizilir. */
  static getDerivedStateFromError(hata) {
    return { hata }
  }

  /** Günlüğe yazmak için: gerçek projede buradan hata izleme servisine gönderilir. */
  componentDidCatch(hata, bilgi) {
    console.error('Yakalanan arayüz hatası:', hata, bilgi?.componentStack)
  }

  render() {
    if (!this.state.hata) return this.props.children

    return (
      <div className="hata-sinir">
        <div className="hata-kart">
          <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor"
               strokeWidth="1.8" strokeLinecap="round" aria-hidden="true">
            <path d="M12 9v4M12 17h.01" />
            <path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z" />
          </svg>

          <h1>Bir şeyler ters gitti</h1>
          <p>
            Uygulama beklenmedik bir hatayla karşılaştı. Sayfayı yenilemek
            genellikle sorunu çözer.
          </p>

          {/* Hata ayrıntısı yalnızca geliştirme modunda: son kullanıcıya
              yığın izi göstermek hem anlamsız hem de bilgi sızdırır. */}
          {import.meta.env.DEV && (
            <details className="hata-detay">
              <summary>Teknik ayrıntı (geliştirme)</summary>
              <code>{String(this.state.hata?.message || this.state.hata)}</code>
            </details>
          )}

          <button type="button" className="btn-primary" onClick={() => window.location.reload()}>
            Sayfayı yenile
          </button>
        </div>
      </div>
    )
  }
}
