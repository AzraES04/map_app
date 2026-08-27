// ============================================================================
//  POI KATEGORİ SİMGELERİ — arayüz tarafı (Ödev 15)
//
//  DİKKAT: bu dosyada HİÇBİR ÇİZİM VERİSİ YOK.
//
//  Simgelerin SVG yolları backend'deki tek katalogda (Business/Poiler/
//  PoiIkonlari.cs) duruyor ve iki uçtan geliyor:
//      GET /api/poi/stiller  → kategori başına çizilen simge
//      GET /api/poi/ikonlar  → yönetim panelindeki seçicinin listesi
//
//  Buradaki iş yalnızca DÖNÜŞTÜRME: aynı parça listesini bir yerde
//  OpenLayers'ın istediği resme, bir yerde React'in basacağı <path>'lere
//  çeviriyoruz. Yolları burada da tanımlasaydık GeoServer'ın haritaya
//  bastığı simge ile paneldeki simge sessizce ayrışabilirdi — Ödev 13'te
//  renkler için tam olarak bu yaşandı ve stiller o yüzden tek kaynaktan
//  üretilir hâle getirildi.
// ============================================================================

import Icon from 'ol/style/Icon'

/** Backend'deki PoiIkonlari.ViewBox ile aynı — katalog ucu da bunu döndürüyor. */
export const IKON_VIEWBOX = '0 0 24 24'

/**
 * Parça listesini tek başına duran bir SVG belgesine çevirir.
 *
 * Beyaz kontur ÖNCE, dolgu SONRA çiziliyor: aynı yol iki kez basılıyor,
 * alttaki kalın beyaz çizgi simgeye harita zemininden ayıran bir kenar
 * veriyor. Sıra ters olsaydı kontur dolgunun üstünü örter, simge şişkin
 * ve bulanık görünürdü. (Aynı numara backend'deki SvgUret'te de var —
 * GeoServer'ın çizdiğiyle buradaki birebir aynı görünsün diye.)
 *
 * @param {{d: string, beyaz: boolean}[]} parcalar API'den gelen çizim parçaları
 * @param {string} renk Kategori rengi — "#rrggbb"
 * @param {number} boyut Kenar uzunluğu (piksel)
 */
export function ikonSvg(parcalar, renk, boyut = 24) {
  const hat = parcalar
    .filter((p) => !p.beyaz)
    .map((p) => `<path d="${p.d}" fill="none" stroke="#ffffff" stroke-width="2.4" stroke-linejoin="round"/>`)
    .join('')

  const govde = parcalar
    .map((p) => `<path fill="${p.beyaz ? '#ffffff' : renk}" d="${p.d}"/>`)
    .join('')

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${IKON_VIEWBOX}" `
    + `width="${boyut}" height="${boyut}">${hat}${govde}</svg>`
}

/**
 * SVG metnini <img>/OpenLayers'ın yükleyebileceği bir adrese çevirir.
 *
 * NEDEN base64, doğrudan `data:image/svg+xml,<svg…>` DEĞİL?
 * Ham SVG'yi adrese gömmek `#` ve `"` gibi karakterleri kaçırmayı gerektiriyor
 * ve renk kodundaki `#` unutulduğunda adres sessizce kesiliyor — simge
 * hiç yüklenmiyor, hata da vermiyor. base64 bu sınıf hataları tamamen
 * ortadan kaldırıyor.
 *
 * `encodeURIComponent` + `unescape` ikilisi Türkçe karakterler için:
 * `btoa` yalnızca Latin-1 kabul ediyor, önce UTF-8 baytlarına çevirmek
 * gerekiyor. (Simgelerde metin yok ama yardımcı genel olsun.)
 */
export function ikonAdresi(parcalar, renk, boyut = 24) {
  const svg = ikonSvg(parcalar, renk, boyut)
  return 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svg)))
}

/**
 * Kategori id → OpenLayers simgesi eşlemesi kurar.
 *
 * ÖNBELLEK ŞART: stil fonksiyonu her karede, her POI için çağrılıyor.
 * Simgeyi orada üretseydik saniyede binlerce base64 kodlaması ve o kadar
 * resim yüklemesi olurdu; harita gözle görülür şekilde takılırdı. Burada
 * kategori başına BİR kez üretiliyor, stiller değişene kadar da öyle kalıyor.
 *
 * @param {{kategoriId: number, renk: string, ikonParcalari: object[]}[]} stiller
 *        GET /api/poi/stiller cevabı
 * @param {number} boyut Simgenin ekrandaki kenar uzunluğu
 * @returns {Map<number, import('ol/style/Icon').default>}
 */
export function ikonHaritasiKur(stiller, boyut = 22) {
  const harita = new Map()

  for (const stil of stiller) {
    if (!stil.ikonParcalari?.length) continue

    harita.set(stil.kategoriId, new Icon({
      src: ikonAdresi(stil.ikonParcalari, stil.renk, boyut),
      // Etiketlerin simgenin üstüne binmemesi için: OpenLayers bu simgeyi
      // "engel" sayıyor ve çakışan yazıyı gizliyor (declutter grubu MapPage'de).
      declutterMode: 'obstacle',
    }))
  }

  return harita
}

/**
 * Bir kategorinin simgesini bulur; kategori listede yoksa YEDEK stile düşer.
 *
 * Yedek stil (kategoriId = 0) sunucu tarafından her zaman üretiliyor, yani
 * "hiç simge yok" durumu oluşmuyor — yine de null dönebilir: stiller henüz
 * indirilmediyse çağıran taraf eski dairesel görünüme düşüyor.
 */
export function ikonBul(ikonHaritasi, kategoriId) {
  return ikonHaritasi.get(kategoriId) ?? ikonHaritasi.get(0) ?? null
}
