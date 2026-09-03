// ============================================================================
//  SİMÜLASYON ARACININ SİMGESİ (Ödev 19 / Madde 1)
//
//  Araç, hattın KENDİ RENGİNDE çiziliyor: aynı anda iki hatta sefer varsa
//  hangi aracın hangi hatta ait olduğu tek bakışta anlaşılsın. Sabit bir
//  renk seçseydik, iki araç birbirinin ikizi olurdu.
//
//  Simge neden SVG data URI? poiIkon.js'teki kararın aynısı: ağ isteği yok,
//  dosya yok ve renk çalışma anında değiştirilebiliyor. Farkı, buradaki
//  çizimin backend'den GELMİYOR olması — POI simgeleri GeoServer'ın da
//  çizdiği ortak katalogdan besleniyordu, araç ise yalnızca istemcide var.
//
//  ÖNBELLEK ŞART: stil fonksiyonu saniyede iki kez (her yayın tikinde) ve
//  her araç için çağrılıyor. Simgeyi orada üretseydik her tikte yeniden
//  base64 kodlar ve yeni bir resim yükletirdik; araç gözle görülür şekilde
//  titrerdi. Renk başına bir kez üretiyoruz.
// ============================================================================

import Icon from 'ol/style/Icon'

const onbellek = new Map()

/**
 * Otobüs silueti — 24×24 kutuda, beyaz halka içinde.
 *
 * Halka bilinçli: araç hattın kendi rengiyle çiziliyor ve tam da o rengin
 * çizgisinin ÜSTÜNDE ilerliyor. Beyaz çerçeve olmasaydı araç, hattın
 * içinde eriyip kaybolurdu.
 */
function aracSvg(renk) {
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 40" width="40" height="40">`
    + `<circle cx="20" cy="20" r="15" fill="${renk}" stroke="#ffffff" stroke-width="3"/>`
    // Otobüs gövdesi
    + `<rect x="12" y="11" width="16" height="15" rx="3" fill="#ffffff"/>`
    // Camlar (tek şerit) ve gövde rengiyle boyanmış alt bant
    + `<rect x="14" y="13.5" width="12" height="5" rx="1.2" fill="${renk}"/>`
    + `<rect x="14" y="20.5" width="12" height="2" rx="1" fill="${renk}" opacity="0.45"/>`
    // Tekerlekler
    + `<circle cx="16" cy="27" r="2.2" fill="#ffffff"/>`
    + `<circle cx="24" cy="27" r="2.2" fill="#ffffff"/>`
    + `</svg>`
}

/** SVG metnini adrese çevirir (gerekçe: poiIkon.js → ikonAdresi). */
function adres(renk) {
  return 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(aracSvg(renk))))
}

/**
 * Hattın rengine göre OpenLayers simgesi. Aynı renk için hep aynı nesne
 * dönüyor — hem önbellek hem de OpenLayers'ın resmi yeniden yüklememesi için.
 *
 * @param {string} renk "#rrggbb"
 */
export function aracIkonu(renk = '#2d7dd2') {
  const mevcut = onbellek.get(renk)
  if (mevcut) return mevcut

  const ikon = new Icon({
    src: adres(renk),
    // İki katına çizip yarıya indirmek yerine 40px'lik tuvali 0.75 ölçeğiyle
    // kullanıyoruz: yüksek yoğunluklu ekranda (dpr 1.25+) simge net kalsın.
    scale: 0.75,
    anchor: [0.5, 0.5],
  })

  onbellek.set(renk, ikon)
  return ikon
}

/** Testler için: önbelleğin dolup dolmadığını görebilmek. */
export const onbellekBoyutu = () => onbellek.size
