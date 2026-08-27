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

/**
 * Isı haritası — Ödev 9 / Madde 2.
 * İç içe geçmiş üç halka: merkezde yoğun, dışa doğru seyrelen bir ısı lekesi.
 * Analiz ikonundan (kesikli çerçeve + büyüteç) bilerek farklı: o "say", bu "yoğunluk".
 */
export function IsiIkonu({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeLinecap="round" aria-hidden="true">
      <circle cx="12" cy="12" r="9"   strokeWidth="1.4" opacity="0.35" />
      <circle cx="12" cy="12" r="5.8" strokeWidth="1.7" opacity="0.65" />
      <circle cx="12" cy="12" r="2.6" strokeWidth="2" fill="currentColor" />
    </svg>
  )
}

/** Saat — kalan oturum süresi rozeti. */
export function SaatIkonu({ size = 13 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2.2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3 2" />
    </svg>
  )
}

/** Kullanıcı — oturum açan kişi rozeti. */
export function KullaniciIkonu({ size = 13 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2.2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <circle cx="12" cy="8" r="3.6" />
      <path d="M4.5 20a7.5 7.5 0 0 1 15 0" />
    </svg>
  )
}

/** Kullanıcı topluluğu — yönetim panelindeki "Kullanıcı Listesi" menüsü. */
export function KullanicilarIkonu({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="1.9" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <circle cx="9" cy="8" r="3.4" />
      <path d="M2.5 20a6.5 6.5 0 0 1 13 0" />
      <path d="M16 5.3a3.4 3.4 0 0 1 0 5.4M17.5 14.2A6.5 6.5 0 0 1 21.5 20" />
    </svg>
  )
}

/** Kalkan — "Rol Listesi" menüsü ve yetki rozetleri. */
export function RolIkonu({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="1.9" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <path d="M12 3l7.5 3v5.5c0 4.4-3.1 8.3-7.5 9.5-4.4-1.2-7.5-5.1-7.5-9.5V6Z" />
      <path d="m9 12 2.2 2.2L15.2 10" />
    </svg>
  )
}

/** Anahtar — yetki matrisi başlığı. */
export function YetkiIkonu({ size = 16 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="1.9" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <circle cx="8" cy="14" r="4" />
      <path d="m11 11 8-8M17 5l2 2M15 7l2 2" />
    </svg>
  )
}

/** Kilit — rolden gelen, değiştirilemeyen yetkiyi işaretler. */
export function KilitIkonu({ size = 13 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2.2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <rect x="4.5" y="10.5" width="15" height="10" rx="2" />
      <path d="M8 10.5V7a4 4 0 0 1 8 0v3.5" />
    </svg>
  )
}

/** Sola ok — haritaya dönüş bağlantısı. */
export function GeriIkonu({ size = 16 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2.2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <path d="M19 12H5M11 6l-6 6 6 6" />
    </svg>
  )
}

/** Artı — "yeni kayıt" düğmeleri. */
export function EkleIkonu({ size = 15 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2.4" strokeLinecap="round"
         aria-hidden="true">
      <path d="M12 5v14M5 12h14" />
    </svg>
  )
}

/** Katlanmış harita — coğrafi yetki tanımlama düğmesi (Ödev 7). */
export function HaritaIkonu({ size = 15 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="1.9" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <path d="M9 4 3 6.5v14L9 18l6 2.5 6-2.5v-14L15 6 9 4Z" />
      <path d="M9 4v14M15 6.5v14" />
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

/**
 * POI (ilgi noktası) — Ödev 12.
 *
 * Nokta çizim aracının iğnesinden bilerek FARKLI: ikisi de haritada nokta
 * koyuyor ama biri serbest çizim, diğeri kategorili bir kayıt. Aynı simgeyi
 * kullansaydık araç çubuğunda iki özdeş düğme görünürdü.
 */
export function PoiIkonu({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <path d="M12 21s7-6.2 7-11a7 7 0 1 0-14 0c0 4.8 7 11 7 11Z" />
      <path d="M12 7.2v5.6M9.2 10h5.6" />
    </svg>
  )
}

/** Kategori ağacı — kök ve ondan ayrılan iki dal. */
export function KategoriIkonu({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <rect x="9" y="3" width="6" height="4" rx="1" />
      <rect x="3" y="17" width="6" height="4" rx="1" />
      <rect x="15" y="17" width="6" height="4" rx="1" />
      <path d="M12 7v4M6 17v-2h12v2" />
    </svg>
  )
}

// ---------------------------------------------------------------------------
//  Ödev 16 — ulaşım modülü
//
//  Aynı çizim dili: 24'lük kutu, 2 birim kontur, dolgu yok. Böylece menüde
//  ve araç çubuğunda diğerlerinin yanında aynı ağırlıkta duruyorlar.
// ---------------------------------------------------------------------------

/** Durak — durağın tabelası (direk + levha). */
export function DurakIkonu({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <rect x="4" y="3" width="16" height="9" rx="2" />
      <path d="M8 7h8" />
      <path d="M12 12v9" />
      <path d="M9 21h6" />
    </svg>
  )
}

/** Güzergah — durakları birbirine bağlayan kırık hat. */
export function GuzergahIkonu({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <circle cx="5" cy="18" r="2.4" />
      <circle cx="12" cy="7" r="2.4" />
      <circle cx="19" cy="16" r="2.4" />
      <path d="M6.4 16.1 10.6 9M13.5 8.6 17.6 14" />
    </svg>
  )
}

/** Sürükleme tutamağı — altı nokta; "bu satır taşınabilir" işareti. */
export function SurukleIkonu({ size = 16 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor"
         aria-hidden="true">
      <circle cx="9" cy="6" r="1.7" />
      <circle cx="15" cy="6" r="1.7" />
      <circle cx="9" cy="12" r="1.7" />
      <circle cx="15" cy="12" r="1.7" />
      <circle cx="9" cy="18" r="1.7" />
      <circle cx="15" cy="18" r="1.7" />
    </svg>
  )
}

// ---------------------------------------------------------------------------
//  Tema düğmesi ikonları
//
//  Üç durum, üç simge: sistem (ekran), aydınlık (güneş), karanlık (ay).
//  Diğerleriyle aynı çizim dili — 24'lük kutu, 2 birim kontur, dolgu yok —
//  böylece üst barda yan yana durduklarında ağırlıkları eşleşiyor.
// ---------------------------------------------------------------------------

/** Aydınlık tema — güneş. */
export function GunesIkonu({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
    </svg>
  )
}

/** Karanlık tema — hilal. Tek yol: içi boş bırakılırsa "C" gibi okunuyor. */
export function AyIkonu({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <path d="M20 14.5A8.5 8.5 0 0 1 9.5 4a7 7 0 1 0 10.5 10.5Z" />
    </svg>
  )
}

/** Sistem tercihi — ekran. "Ben karışmıyorum, işletim sistemi bilsin." */
export function SistemIkonu({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth="2" strokeLinecap="round"
         strokeLinejoin="round" aria-hidden="true">
      <rect x="3" y="4" width="18" height="12" rx="2" />
      <path d="M8 20h8M12 16v4" />
    </svg>
  )
}
