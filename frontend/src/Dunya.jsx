import { KITALAR } from './dunyaKitalari'
import { yol, kitaGorunurMu } from './ortografik'

// ============================================================================
//  Açılış sahnesindeki GEZEGEN
//
//  ---- NEDEN AYRI BİR ÇİZİM, HARİTANIN KENDİSİ DEĞİL? ----
//
//  Sahne başlangıçta haritayı çok uzaktan gösteriyor ve üstüne bir renk
//  derecelendirmesi uyguluyordu. Sorun şu: OSM bir YOL HARİTASIDIR — karalar
//  neredeyse beyaz (#f2efe9). CSS süzgeçleri var olan rengi güçlendirebilir
//  ama YOKTAN renk üretemez; beyaz karayı yeşile çeviremezsiniz. Sonuç
//  mavi-yeşil dünyaya değil, soluk bir yol haritasına benziyordu.
//
//  ---- NEDEN ORTOGRAFİK İZDÜŞÜM? ----
//
//  İlk deneme kıtaları serbest SVG eğrileriyle çiziyordu ve sonuç dünyaya
//  benzemedi: mavi bir dairenin üstünde rastgele yeşil lekeler. İki ayrı
//  sebepten:
//
//    1. ŞEKİLLER TANINMIYORDU. Göz bu ölçekte kıyı ayrıntısını görmüyor ama
//       oranları ve kıtaların birbirine göre konumunu hemen fark ediyor.
//    2. DÜZ ÇİZİMDİ. Küre üzerindeki bir şekil kenarlara doğru KISALIR;
//       bunu elle taklit etmek imkânsızdı, sonuç çıkartma gibi duruyordu.
//
//  Şimdi kıtalar gerçek boylam/enlem olarak duruyor (dunyaKitalari.js) ve
//  buradaki ortografik izdüşümle küreye yansıtılıyor. Kısalma matematikten
//  kendiliğinden geliyor.
//
//  Kamera Türkiye'ye iniyor, bu yüzden bize dönük yüz Avrupa–Afrika–Asya.
// ============================================================================

/**
 * @param {object} props
 * @param {boolean} props.solgun İniş (ekranı kaplama) aşamasında mı?
 */
export default function Dunya({ solgun = false }) {
  return (
    <div className={`kure-dunya${solgun ? ' solgun' : ''}`} aria-hidden="true">
      <svg viewBox="0 0 100 100">
        <defs>
          {/* Okyanus: ışığın geldiği sol üstte açık, kenarlara doğru derin.
              Düz tek renk okyanus "gezegen" değil "daire" gibi okunuyordu. */}
          <radialGradient id="dunya-okyanus" cx="35%" cy="30%" r="80%">
            <stop offset="0%" stopColor="#2e7ab0" />
            <stop offset="50%" stopColor="#17538a" />
            <stop offset="100%" stopColor="#092c4e" />
          </radialGradient>

          {/* Kara: kuzeyde yeşil, çöl kuşağında kurak sarı, ekvatorda yeniden
              yeşil. Tek düz yeşil, Sahra'yı da orman gibi gösteriyordu. */}
          <linearGradient id="dunya-kara" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#6f9b57" />
            <stop offset="28%" stopColor="#5d8f4a" />
            <stop offset="45%" stopColor="#a89055" />
            <stop offset="62%" stopColor="#4f8746" />
            <stop offset="100%" stopColor="#5c8f57" />
          </linearGradient>

          {/* Kırpma: SVG kare, gezegen daire. */}
          <clipPath id="dunya-kirp">
            <circle cx="50" cy="50" r="50" />
          </clipPath>
        </defs>

        <g clipPath="url(#dunya-kirp)">
          <circle cx="50" cy="50" r="50" fill="url(#dunya-okyanus)" />

          {/* ---------- KARALAR ---------- */}
          {/*
            İnce koyu bir kontur var: kıyı çizgisi. Olmadan kara ile deniz
            arasındaki sınır, ikisi de orta tonda olduğu için bulanıklaşıyor.
          */}
          <g
            fill="url(#dunya-kara)"
            stroke="#0d3a5c"
            strokeWidth="0.35"
            strokeLinejoin="round"
          >
            {KITALAR
              .filter(kitaGorunurMu)
              .map((kita, i) => <path key={i} d={yol(kita)} />)}
          </g>

          {/* ---------- KUTUP BUZU ----------
              Küçük ve KENARDA. İlk denemede kutuplar büyük yumuşak elipslerdi
              ve gezegenin üst yarısını sisli gösteriyordu. Ortografik
              izdüşümde kutup zaten kenara düşüyor; buz da oraya ait. */}
          <ellipse cx="50" cy="-6" rx="34" ry="18" fill="#eef4f8" opacity="0.85" />
          <ellipse cx="50" cy="104" rx="30" ry="16" fill="#eef4f8" opacity="0.8" />
        </g>
      </svg>
    </div>
  )
}
