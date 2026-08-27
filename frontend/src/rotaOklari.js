// ============================================================================
//  Rota yön okları (Ödev 17 / Madde 1)
//
//  Ödev metni: "Rota yönü harita üzerinde ok işaretleri ile gösterilmelidir."
//
//  NEDEN AYRI DOSYA? Buradaki hesap saf geometri: bir çizgi ve bir oran
//  alıyor, bir açı döndürüyor. MapPage.jsx'in içinde kalsaydı test etmek
//  için 5000 satırlık bir bileşeni ve bütün OpenLayers'ı yüklemek gerekirdi.
//
//  Ve test edilmesi GEREKİYOR: açı dönüşümü iki farklı konvansiyonun
//  arasında duruyor (matematik açısı ↔ OpenLayers dönüşü) ve yanlış olursa
//  hata mesajı ALINMAZ — oklar sessizce yanlış yöne bakar. Haritaya bakan
//  biri bunu ancak yolu tanıyorsa fark eder.
// ============================================================================

/**
 * Çizginin harita birimlerindeki uzunluğu.
 *
 * Gerçek mesafe DEĞİL (EPSG:3857 metreleri enleme göre şişiyor); yalnızca
 * kaç ok konacağına karar vermek için kullanılıyor, o yüzden düzlem
 * yaklaşımı yeterli.
 */
export function cizgiUzunlugu(koordinatlar) {
  let toplam = 0
  for (let i = 1; i < koordinatlar.length; i += 1) {
    toplam += Math.hypot(
      koordinatlar[i][0] - koordinatlar[i - 1][0],
      koordinatlar[i][1] - koordinatlar[i - 1][1],
    )
  }
  return toplam
}

/** Ok sayısı sınırları — dışarıdan da okunabilsin diye açık. */
export const EN_AZ_OK = 3
export const EN_COK_OK = 24

/**
 * Çizgiye kaç ok konacağı.
 *
 * Sabit bir sayı verseydik kısa hatlarda oklar üst üste biner, uzun hatlarda
 * arada kaybolurdu. Uzunluğa oranlıyoruz ama iki uçtan da sınırlıyoruz:
 * alt sınır olmadan çok kısa bir hatta hiç ok çıkmaz, üst sınır olmadan
 * şehirlerarası bir hatta yüzlerce ok çizilirdi.
 */
export function okSayisi(uzunluk) {
  return Math.max(EN_AZ_OK, Math.min(EN_COK_OK, Math.round(uzunluk / 900)))
}

/**
 * Okların çizgi üzerindeki konumları (0–1 arası oranlar).
 *
 * Uçlara ok KONMUYOR: 0 ve 1 oranları durakların tam üstüne denk geliyor ve
 * ok, durak simgesinin altında kaybolurdu. n ok için aralık (n+1) parçaya
 * bölünüyor, oklar parça sınırlarına düşüyor.
 */
export function okOranlari(adet) {
  const oranlar = []
  for (let i = 1; i <= adet; i += 1) {
    oranlar.push(i / (adet + 1))
  }
  return oranlar
}

/** Örnekleme adımı — bkz. {@link okAcisi}. */
export const ORNEK_ADIMI = 0.01

/**
 * Okun dönüş açısı (radyan), OpenLayers'ın `RegularShape.rotation`
 * beklentisine göre.
 *
 * ---- İKİ KONVANSİYON ----
 *
 *   Math.atan2(dy, dx) → DOĞU = 0, saat yönünün TERSİNE artar.
 *   RegularShape       → YUKARI = 0, saat yönünde artar.
 *
 * Dönüşüm: `rotation = π/2 − açı`
 *
 * Bu satır yanlış yazılsaydı oklar 90° kaymış ya da ayna görüntüsü hâlinde
 * çizilirdi — ve KOD HATA VERMEZDİ. Testler tam olarak bu dönüşümü sınıyor.
 *
 * ---- NEDEN KOMŞU KÖŞELER KULLANILMIYOR? ----
 *
 * OSRM rotasında ardışık iki nokta bazen santimetrelerce yakın; o kadar kısa
 * bir vektörün açısı gürültüye boğuluyor ve oklar titriyordu. Bunun yerine
 * okun iki yanından ORNEK_ADIMI kadar uzakta iki nokta örnekleniyor.
 *
 * @param {(oran: number) => number[]} noktaAl Orana karşılık koordinat veren
 *   fonksiyon (OpenLayers'ta `LineString.getCoordinateAt`).
 * @param {number} oran 0–1 arası konum.
 */
export function okAcisi(noktaAl, oran) {
  const a = noktaAl(Math.max(0, oran - ORNEK_ADIMI))
  const b = noktaAl(Math.min(1, oran + ORNEK_ADIMI))

  return Math.PI / 2 - Math.atan2(b[1] - a[1], b[0] - a[0])
}

/**
 * Rengi soluklaştırır — "rota güncel değil" durumunun görsel karşılığı.
 *
 * Rotayı SİLMİYORUZ (elimizdeki en iyi bilgi o) ama güncelmiş gibi de
 * göstermiyoruz. Solgunluk, "bu veri var ama ona güvenme" demenin sessiz
 * yolu.
 */
export function soluklastir(hex) {
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16))
  return `rgba(${r}, ${g}, ${b}, 0.42)`
}

/**
 * Rota özeti: "12.4 km · ~23 dk sürüş".
 *
 * "SÜRÜŞ" kelimesi bilerek yazılı: OSRM'in verdiği süre duraklarda bekleme,
 * yolcu iniş-binişi ve trafik yoğunluğunu içermiyor. "23 dk" demek onu sefer
 * süresi gibi okutur ve hat planlamasında yanıltıcı olurdu.
 */
export function rotaOzeti({ rotaMesafeMetre, rotaSureSaniye }) {
  const km = (rotaMesafeMetre ?? 0) / 1000
  const dk = Math.round((rotaSureSaniye ?? 0) / 60)
  return `${km.toFixed(1)} km · ~${dk} dk sürüş`
}
