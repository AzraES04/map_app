// ============================================================================
//  ORTOGRAFİK İZDÜŞÜM — küre yüzeyindeki nokta → ekran koordinatı
//
//  Sonsuz uzaktan bakan bir gözlemcinin göreceği düzlem. Uzaydan çekilmiş
//  bir gezegen fotoğrafının geometrisi budur: merkeze yakın yerler gerçek
//  boyutunda, kenarlara doğru kısalıyor.
//
//  ---- NEDEN AYRI DOSYA VE TESTLİ? ----
//
//  Bu formül sessizce yanlış olabilecek cinsten. İşaret hatası dünyayı
//  AYNADA gösterir, y'yi ters çevirmeyi unutmak KUZEYİ AŞAĞI çevirir — ve
//  ikisi de hata mesajı üretmez. Açılış sahnesi iki buçuk saniye sürüyor;
//  yanlış bir dünya gözden kaçabilir ama jüriye gösterildiğinde kaçmaz.
//
//  Görsel doğrulama bu oturumda mümkün olmadığı için (tarayıcı paneli
//  OpenLayers/canvas karesi üretmiyor), doğruluğun ölçütü testler.
// ============================================================================

/** Derece → radyan. */
const DERECE = Math.PI / 180

/**
 * Kürenin merkezine gelen coğrafi nokta.
 *
 * Türkiye (39K, 33D) tam merkezde olsaydı sahne "dünya" değil "Türkiye'nin
 * uydu görüntüsü" gibi dururdu. Biraz güneybatı, Afrika ile Avrupa'yı da
 * kadraja sokuyor; Anadolu merkezin hemen sağ üstünde kalıyor — inişin
 * gideceği yer.
 */
export const MERKEZ = { lon: 22, lat: 22 }

/** viewBox 0–100; küre merkezi (50,50), yarıçapı 50. */
export const YARICAP = 50
export const ORTA = 50

/**
 * Bir noktanın kürenin GÖRÜNEN yüzünde olup olmadığı.
 *
 * cos c ≥ 0 → görünür. Negatifse nokta gezegenin arkasında.
 */
export function gorunurMu([lon, lat]) {
  return kosinusC(lon, lat) >= 0
}

function kosinusC(lon, lat) {
  const p = lat * DERECE
  const l = (lon - MERKEZ.lon) * DERECE
  const p0 = MERKEZ.lat * DERECE

  return Math.sin(p0) * Math.sin(p) + Math.cos(p0) * Math.cos(p) * Math.cos(l)
}

/**
 * [boylam, enlem] → [x, y] (viewBox koordinatı).
 *
 * Formül (standart kartografi):
 *   x = cos φ · sin(λ − λ₀)
 *   y = cos φ₀ · sin φ − sin φ₀ · cos φ · cos(λ − λ₀)
 *
 * ARKA YÜZDEKİ NOKTALAR ATILMIYOR, UFKA SABİTLENİYOR. Atsaydık ufku aşan
 * bir kıta (örn. Kuzey Amerika) yarısı kesilmiş ve çokgeni bozulmuş hâlde
 * çizilirdi; sabitlemek onu kürenin kenarına yaslanan doğal bir siluete
 * çeviriyor.
 *
 * y TERS çevriliyor: coğrafyada kuzey yukarı, SVG'de y aşağı doğru büyür.
 * Bu satır unutulursa dünya baş aşağı çizilir ve hiçbir hata alınmaz.
 */
export function izdusum([lon, lat]) {
  const p = lat * DERECE
  const l = (lon - MERKEZ.lon) * DERECE
  const p0 = MERKEZ.lat * DERECE

  let x = Math.cos(p) * Math.sin(l)
  let y = Math.cos(p0) * Math.sin(p) - Math.sin(p0) * Math.cos(p) * Math.cos(l)

  if (kosinusC(lon, lat) < 0) {
    const uzunluk = Math.hypot(x, y) || 1
    x /= uzunluk
    y /= uzunluk
  }

  return [ORTA + x * YARICAP, ORTA - y * YARICAP]
}

/**
 * Bir kıtanın herhangi bir noktası görünüyor mu?
 *
 * TAMAMEN arka yüzdeki kıtalar hiç çizilmemeli. Çizilirlerse bütün noktaları
 * ufka sabitlendiği için çokgen, kürenin kenarında ince bir yeşil yaya
 * dönüşüyor — coğrafi karşılığı olmayan bir çizim artığı.
 *
 * Bu merkezden (22D, 22K) Avustralya, Japonya ve Yeni Gine tamamen arkada
 * kalıyor. Onları VERİDEN silmek de mümkündü ama silmedik: MERKEZ değişirse
 * kendiliğinden doğru yerde belirsinler.
 */
export function kitaGorunurMu(noktalar) {
  return noktalar.some(gorunurMu)
}

/** Nokta listesini kapalı bir SVG yoluna çevirir. */
export function yol(noktalar) {
  return noktalar
    .map((nokta, i) => {
      const [x, y] = izdusum(nokta)
      return `${i === 0 ? 'M' : 'L'}${x.toFixed(2)} ${y.toFixed(2)}`
    })
    .join(' ') + ' Z'
}
