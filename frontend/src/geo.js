// ============================================================================
//  Coğrafi dönüşümlerin TEK merkezi (Ödev 3 / Görev 3)
//
//  Projede iki farklı koordinat sistemi dolaşıyor:
//
//    EPSG:3857 (Web Mercator)  → HARİTANIN dili.  Birim: metre.
//                                Ankara ≈ [3657463, 4855128]
//    EPSG:4326 (WGS84)         → VERİTABANININ dili. Birim: derece.
//                                Ankara ≈ [32.8597, 39.9334]
//
//  Kural: Dönüşüm tam sınırda yapılır. Haritadan veri çıkarken 3857→4326,
//  veritabanından veri girerken 4326→3857.
//
//  Bu dönüşümü her bileşende ayrı ayrı yazsaydık, bir yerde mutlaka
//  unutulurdu ve geometri sessizce yanlış konuma kaydedilirdi (hata mesajı
//  ALINMAZ — sadece nokta Ankara yerine Gine Körfezi'ne düşer). O yüzden
//  tüm dönüşüm mantığı sadece bu dosyada yaşıyor.
// ============================================================================

import WKTFormat from 'ol/format/WKT'

/** Haritanın (OpenLayers view'ının) çalıştığı projeksiyon. */
export const MAP_PROJECTION = 'EPSG:3857'

/** Veritabanının ve WKT metinlerinin projeksiyonu. */
export const DATA_PROJECTION = 'EPSG:4326'

/** WKT'de kaç ondalık basamak yazılacağı. 6 basamak ≈ 11 cm hassasiyet — fazlası gereksiz. */
const WKT_DECIMALS = 6

/**
 * Desteklenen çizim tipleri ve her birinin bağlı olduğu tablo/endpoint.
 *
 * DİKKAT: OpenLayers'ta çizgi tipinin adı 'Line' DEĞİL 'LineString'tir
 * (OGC standardı böyle). Ödev metnindeki "Line" ile kastedilen budur;
 * veritabanı tablosunun adı ise tbl_line.
 */
export const DRAW_TYPES = {
  Point: {
    key: 'Point',
    label: 'Nokta',
    icon: '📍',
    endpoint: '/api/points',
    color: '#2e8fa8',
    hint: 'Haritaya tıklayarak nokta ekleyin.',
  },
  LineString: {
    key: 'LineString',
    label: 'Çizgi',
    icon: '📏',
    endpoint: '/api/lines',
    color: '#e07b39',
    hint: 'Her tıklama bir kırılma noktası ekler. Bitirmek için çift tıklayın.',
  },
  Polygon: {
    key: 'Polygon',
    label: 'Poligon',
    icon: '⬟',
    endpoint: '/api/polygons',
    color: '#57a05a',
    hint: 'Köşeleri tıklayın; alanı kapatmak için çift tıklayın.',
  },
}

/** Sekmelerde ve döngülerde sabit sıra için. */
export const DRAW_TYPE_KEYS = ['Point', 'LineString', 'Polygon']

/**
 * Kayıt popup'ında sunulan hazır renkler (Ödev 4 / Görev 2).
 * Serbest renk seçici de var; bunlar sık kullanılanlar için kısayol.
 */
export const RENK_SECENEKLERI = [
  { deger: '#2e8fa8', ad: 'Petrol' },
  { deger: '#d9553f', ad: 'Kiremit' },
  { deger: '#57a05a', ad: 'Yeşil' },
  { deger: '#e07b39', ad: 'Turuncu' },
  { deger: '#6a4c93', ad: 'Mor' },
  { deger: '#1f7a8c', ad: 'Turkuaz' },
]

/** Analiz aracının geçici poligon rengi — kayıtlı hiçbir renge benzemesin. */
export const ANALIZ_RENGI = '#ff4d7d'

// Tek bir format nesnesini tekrar tekrar kullanıyoruz (her çağrıda yenisini
// oluşturmak gereksiz; nesne durum tutmuyor, yeniden kullanılması güvenli).
const wktFormat = new WKTFormat()

/**
 * HARİTADAN VERİTABANINA yön: OpenLayers geometrisi (3857) → WKT metni (4326).
 *
 * @param {import('ol/geom/Geometry').default} geometry Haritadan gelen geometri
 * @returns {string} Örn: "POINT(32.8597 39.9334)"
 */
export function geometryToWkt(geometry) {
  // ⚠️ clone() HAYATİ ÖNEMDE.
  // transform() geometriyi YERİNDE değiştirir (mutate eder). Klonlamadan
  // dönüştürseydik haritadaki feature'ın koordinatları da 4326'ya çevrilirdi;
  // harita 3857 beklediği için çizim ekranda Afrika açıklarına sıçrardı.
  const clone = geometry.clone()
  clone.transform(MAP_PROJECTION, DATA_PROJECTION)

  return wktFormat.writeGeometry(clone, { decimals: WKT_DECIMALS })
}

/**
 * VERİTABANINDAN HARİTAYA yön: WKT metni (4326) → OpenLayers feature (3857).
 *
 * Burada clone gerekmiyor: readFeature yeni bir nesne üretiyor ve
 * dataProjection/featureProjection seçenekleriyle dönüşümü kendisi yapıyor.
 *
 * @param {string} wkt Örn: "POINT(32.8597 39.9334)"
 */
export function wktToFeature(wkt) {
  return wktFormat.readFeature(wkt, {
    dataProjection: DATA_PROJECTION,      // metnin içindeki koordinatlar bu sistemde
    featureProjection: MAP_PROJECTION,    // haritada bu sistemde gösterilecek
  })
}

/**
 * Çizim bitince kullanıcıya gösterilecek özet bilgi.
 * Poligonda alan, çizgide uzunluk hesaplar — kullanıcı ne çizdiğini görsün.
 */
export function describeGeometry(geometry) {
  const type = geometry.getType()

  if (type === 'Point') {
    // 3857 metre cinsinden olduğu için okunabilir olması adına 4326'ya çeviriyoruz
    const clone = geometry.clone()
    clone.transform(MAP_PROJECTION, DATA_PROJECTION)
    const [lon, lat] = clone.getCoordinates()
    return `Boylam ${lon.toFixed(5)}° · Enlem ${lat.toFixed(5)}°`
  }

  if (type === 'LineString') {
    // getLength() 3857'de metre döner (Mercator'da enleme bağlı bir miktar
    // şişme olur; bilgi amaçlı gösterim için yeterli).
    const metre = geometry.getLength()
    const nokta = geometry.getCoordinates().length
    return `${nokta} nokta · ${formatUzunluk(metre)}`
  }

  if (type === 'Polygon') {
    const alan = geometry.getArea()
    const kose = geometry.getCoordinates()[0].length - 1   // kapanış noktası sayılmaz
    return `${kose} köşe · ${formatAlan(alan)}`
  }

  return type
}

function formatUzunluk(metre) {
  return metre >= 1000 ? `${(metre / 1000).toFixed(2)} km` : `${Math.round(metre)} m`
}

function formatAlan(m2) {
  return m2 >= 1_000_000 ? `${(m2 / 1_000_000).toFixed(2)} km²` : `${Math.round(m2)} m²`
}
