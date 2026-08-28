import { KITALAR } from './dunyaKitalari'
import { SEHIRLER } from './sehirIsiklari'
import { yol, izdusum, gorunurMu, kitaGorunurMu } from './ortografik'

// ============================================================================
//  Açılış sahnesindeki GEZEGEN
//
//  ---- NEDEN AYRI BİR ÇİZİM, HARİTANIN KENDİSİ DEĞİL? ----
//
//  Sahne başlangıçta haritayı çok uzaktan gösteriyor ve üstüne bir renk
//  derecelendirmesi uyguluyordu. Sorun şu: OSM bir YOL HARİTASIDIR — karalar
//  neredeyse beyaz (#f2efe9). CSS süzgeçleri var olan rengi güçlendirebilir
//  ama YOKTAN renk üretemez. Sonuç uzaydan görülen bir gezegene değil, soluk
//  bir yol haritasına benziyordu.
//
//  ---- NEDEN ORTOGRAFİK İZDÜŞÜM? ----
//
//  Kıtalar gerçek boylam/enlem olarak duruyor (dunyaKitalari.js) ve
//  ortografik izdüşümle küreye yansıtılıyor (ortografik.js). Kenarlara doğru
//  kısalma matematikten kendiliğinden geliyor; elle çizilen eğrilerde bu
//  taklit edilemiyordu ve sonuç küre değil çıkartma gibi duruyordu.
//
//  ---- NEDEN GECE YÜZÜ? (ikinci tur) ----
//
//  İlk sürüm gündüz yüzüydü: mavi okyanus, YEŞİL karalar. Geri bildirim
//  "fake duruyor, özellikle yeşiller" oldu ve haklıydı. Sebebi şu:
//
//    Gerçek bir gündüz Dünya fotoğrafının inandırıcılığı BULUT örtüsünden,
//    atmosferin saçılmasından ve bitki örtüsünün binlerce tonundan geliyor.
//    Bunları tek bir düz gradyanla taklit etmek imkânsız — göz eksikliği
//    "çizim" olarak okuyor. Düz yeşil bir Afrika, Sahra'yı da orman
//    gösteriyor ve yanlışlık hemen fark ediliyor.
//
//    GECE YÜZÜNDE bu tuzak yok. Geceleyin karalar zaten neredeyse siyah;
//    inandırıcılığı taşıyan şey renk değil IŞIK DESENİ — ve o desen gerçek
//    metropol koordinatlarından geliyor (sehirIsiklari.js). Yani taklit
//    edilmesi zor olan kısmı veriyle çözüyoruz, uydurmayla değil.
//
//  Yan kazanç: sahne zaten bir UZAY sahnesi. Gece yüzü, çevresindeki siyahla
//  ve ince atmosfer kavsiyle aynı dili konuşuyor.
// ============================================================================

/**
 * Güneşin geldiği yön (birim vektör, ekran düzleminde).
 *
 * Sol üstten geliyor — .kure-isik'teki CSS ışığıyla AYNI yön. Ayrışsalardı
 * gezegenin bir yanı ışık alırken gölge başka yandan düşerdi ve göz bunu
 * "yanlış" diye okurdu, sebebini adlandıramadan.
 */
const GUNES = { x: -0.55, y: -0.5 }

/**
 * Bir noktanın gündüz tarafına ne kadar yakın olduğu (0 = tam gece, 1 = tam gündüz).
 *
 * Ortografik izdüşümde ekran koordinatı zaten kürenin yüzeyindeki konumu
 * taşıyor; güneş yönüyle skaler çarpım, terminatörün (gece/gündüz sınırı)
 * hangi tarafta olduğunu veriyor.
 */
function gunduzOrani(x, y) {
  const nx = (x - 50) / 50
  const ny = (y - 50) / 50
  return Math.max(0, Math.min(1, 0.5 - (nx * GUNES.x + ny * GUNES.y) * 0.9))
}

/**
 * @param {object} props
 * @param {boolean} props.solgun İniş (ekranı kaplama) aşamasında mı?
 */
export default function Dunya({ solgun = false }) {
  // Görünür şehirleri bir kez hesaplıyoruz. Arka yüzdekiler elenmese
  // izdüşüm onları ufka sabitler ve kenarda sahte bir ışık şeridi oluşurdu.
  const isiklar = SEHIRLER
    .filter(([lon, lat]) => gorunurMu([lon, lat]))
    .map(([lon, lat, parlaklik]) => {
      const [x, y] = izdusum([lon, lat])
      return { x, y, parlaklik, gunduz: gunduzOrani(x, y) }
    })
    // Gündüz tarafındaki şehirler görünmez: güneş ışığı sokak lambasını
    // yutar. Süzmeseydik gezegenin aydınlık yüzünde de sarı noktalar
    // olurdu ve bu, gerçek uydu görüntülerinde olmayan bir şey.
    .filter((s) => s.gunduz < 0.62)

  return (
    <div className={`kure-dunya${solgun ? ' solgun' : ''}`} aria-hidden="true">
      <svg viewBox="0 0 100 100">
        <defs>
          {/*
            OKYANUS — gece yüzü. Neredeyse siyah ama tamamen değil: çok az
            mavi kalıyor ki küre, arkasındaki uzaydan ayrılabilsin. Tam
            siyah olsaydı gezegen görünmez, yalnızca atmosfer halkası
            kalırdı.
          */}
          <radialGradient id="dunya-okyanus" cx="34%" cy="30%" r="82%">
            <stop offset="0%" stopColor="#123a5e" />
            <stop offset="45%" stopColor="#0a2340" />
            <stop offset="100%" stopColor="#03101f" />
          </radialGradient>

          {/*
            KARA — okyanustan biraz DAHA KOYU ve daha nötr.
            Geceleyin kara, denizden daha karanlıktır (deniz ayı ve yıldızları
            yansıtır, kara yutar). Bu ince fark, kıtaların silüetini yeşile
            hiç başvurmadan okunur kılıyor.
          */}
          <radialGradient id="dunya-kara" cx="34%" cy="30%" r="82%">
            <stop offset="0%" stopColor="#12283c" />
            <stop offset="55%" stopColor="#0b1a29" />
            <stop offset="100%" stopColor="#050c15" />
          </radialGradient>

          {/*
            TERMİNATÖR — gündüz/gece sınırı. Sol üstten gelen ışığın
            aydınlattığı ince kuşak. Sert bir sınır yerine yumuşak geçiş:
            gerçek terminatör atmosferde saçıldığı için keskin değildir.
          */}
          <radialGradient id="dunya-gunduz" cx="22%" cy="18%" r="72%">
            <stop offset="0%" stopColor="#4d90c4" stopOpacity="0.55" />
            <stop offset="42%" stopColor="#2a6796" stopOpacity="0.22" />
            <stop offset="72%" stopColor="#123a5e" stopOpacity="0.04" />
            <stop offset="100%" stopColor="#000000" stopOpacity="0" />
          </radialGradient>

          {/*
            ŞEHİR IŞIĞI — tek bir nokta değil, ortası sıcak beyaz, çevresi
            turuncu bir hâle. Düz sarı daireler "piksel" gibi duruyordu;
            hâle onları "ışık" gibi okutuyor.
          */}
          <radialGradient id="dunya-isik">
            <stop offset="0%" stopColor="#fff6d8" stopOpacity="0.95" />
            <stop offset="35%" stopColor="#ffcf7a" stopOpacity="0.55" />
            <stop offset="100%" stopColor="#ff9c3c" stopOpacity="0" />
          </radialGradient>

          {/* Kırpma: SVG kare, gezegen daire. */}
          <clipPath id="dunya-kirp">
            <circle cx="50" cy="50" r="50" />
          </clipPath>
        </defs>

        <g clipPath="url(#dunya-kirp)">
          <circle cx="50" cy="50" r="50" fill="url(#dunya-okyanus)" />

          {/* ---------- KARALAR ----------
              Kontur YOK. Gündüz sürümünde kıyı çizgisi gerekiyordu çünkü kara
              ile deniz benzer parlaklıktaydı. Gecede fark zaten karanlık
              tonlarında; kontur eklemek "çizim" hissini geri getirirdi. */}
          <g fill="url(#dunya-kara)">
            {KITALAR
              .filter(kitaGorunurMu)
              .map((kita, i) => <path key={i} d={yol(kita)} />)}
          </g>

          {/* ---------- ŞEHİR IŞIKLARI ----------
              Karalardan SONRA çiziliyor ki üstlerinde kalsınlar. */}
          <g>
            {isiklar.map((s, i) => (
              <circle
                key={i}
                cx={s.x}
                cy={s.y}
                // Yarıçap parlaklığa göre: büyük metropol daha geniş bir
                // hâle bırakıyor. Hepsi aynı boyutta olsaydı desen düzenli
                // ve yapay görünürdü.
                r={1.1 + s.parlaklik * 2.2}
                fill="url(#dunya-isik)"
                // Terminatöre yaklaşan şehirler sönümleniyor: alacakaranlıkta
                // ışıklar henüz tam görünmez. Sert bir kesme yerine yumuşak
                // geçiş, gece/gündüz sınırını inandırıcı kılıyor.
                opacity={(1 - s.gunduz / 0.62) * (0.45 + s.parlaklik * 0.55)}
              />
            ))}
          </g>

          {/* ---------- GÜNDÜZ KUŞAĞI ----------
              EN ÜSTTE: hem karayı hem denizi hem ışıkları birlikte
              aydınlatıyor. Altında kalsaydı ışıklar gündüz tarafında da
              parlar, terminatör anlamını yitirirdi. */}
          <circle cx="50" cy="50" r="50" fill="url(#dunya-gunduz)" />
        </g>
      </svg>
    </div>
  )
}
