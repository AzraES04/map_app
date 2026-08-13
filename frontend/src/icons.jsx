// ============================================================================
//  Arayüz ikonları — satır içi (inline) SVG.
//
//  Neden emoji değil? Emoji her işletim sisteminde farklı çizilir (Windows,
//  macOS ve Linux'ta 📏 bambaşka görünür), rengi sabittir ve bazıları renkli
//  bazıları tek renk olduğu için bir arada tutarsız durur.
//
//  SVG ise: rengi CSS'ten (currentColor) alır, her yerde aynı çizilir,
//  boyutu keskinliğini kaybetmeden değişir.
// ============================================================================

/**
 * Geometri tipi ikonu.
 * @param {{tip: 'Point'|'LineString'|'Polygon', size?: number}} props
 */
export function TipIkonu({ tip, size = 18 }) {
  const ortak = {
    width: size,
    height: size,
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',      // rengi CSS belirler
    strokeWidth: 2,
    strokeLinecap: 'round',
    strokeLinejoin: 'round',
    'aria-hidden': true,
  }

  if (tip === 'Point') {
    // Harita iğnesi
    return (
      <svg {...ortak}>
        <path d="M12 21s7-6.2 7-11a7 7 0 1 0-14 0c0 4.8 7 11 7 11Z" />
        <circle cx="12" cy="10" r="2.4" fill="currentColor" stroke="none" />
      </svg>
    )
  }

  if (tip === 'LineString') {
    // Köşe noktalarıyla kırıklı çizgi
    return (
      <svg {...ortak}>
        <path d="M5 18 10 9l4 5 5-9" />
        <circle cx="5" cy="18" r="2" fill="currentColor" stroke="none" />
        <circle cx="10" cy="9" r="2" fill="currentColor" stroke="none" />
        <circle cx="14" cy="14" r="2" fill="currentColor" stroke="none" />
        <circle cx="19" cy="5" r="2" fill="currentColor" stroke="none" />
      </svg>
    )
  }

  // Polygon — köşeleri işaretli kapalı alan
  return (
    <svg {...ortak}>
      <path d="M6 5h12l3 8-6 6H6l-3-7Z" />
      <circle cx="6" cy="5" r="1.9" fill="currentColor" stroke="none" />
      <circle cx="18" cy="5" r="1.9" fill="currentColor" stroke="none" />
      <circle cx="15" cy="19" r="1.9" fill="currentColor" stroke="none" />
      <circle cx="6" cy="19" r="1.9" fill="currentColor" stroke="none" />
    </svg>
  )
}

/** Kaydedilmiş geometriyi sürükleyerek düzenleme aracı. */
export function DuzenleIkonu({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <path d="M12 20h9" />
      <path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z" />
    </svg>
  )
}

/** Dünya — açılış sahnesini tekrar oynatma düğmesi için. */
export function DunyaIkonu({ size = 16 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <circle cx="12" cy="12" r="9" />
      <path d="M3 12h18" />
      <path d="M12 3a14 14 0 0 1 0 18 14 14 0 0 1 0-18Z" />
    </svg>
  )
}

/** Envanter analizi — kesikli alan içinde büyüteç. */
export function AnalizIkonu({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      {/* Analiz alanı: kesikli çerçeve — "geçici, kaydedilmiyor" mesajı */}
      <path d="M3 8V3h5M21 8V3h-5M3 16v5h5M21 16v5h-5" strokeDasharray="3 2.5" />
      <circle cx="11.5" cy="11.5" r="3.6" />
      <path d="m14.3 14.3 3 3" />
    </svg>
  )
}

/** Çöp kutusu. */
export function SilIkonu({ size = 15 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <path d="M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6" />
      <path d="M10 11v6M14 11v6" />
    </svg>
  )
}
