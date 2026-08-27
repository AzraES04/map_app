// ============================================================================
//  Yer arama (geocoding) — isimden koordinat bulma
//
//  Kaynak: Nominatim (OpenStreetMap'in resmî arama servisi). Ücretsiz ve
//  anahtar gerektirmiyor; buna karşılık kullanım kuralları var:
//    · saniyede en fazla 1 istek        → 450 ms'lik debounce ile sağlanıyor
//    · toplu/otomatik sorgu yapılmamalı → yalnızca kullanıcı yazarken çağrılıyor
//    · kaynak belli olmalı              → tarayıcı Referer başlığını kendisi ekler
//
//  Not: Üretim ortamında bu çağrı backend üzerinden yapılırdı (önbellekleme,
//  hız sınırını sunucuda yönetme, üçüncü parti adresini gizleme). Ödev
//  kapsamında doğrudan tarayıcıdan çağırmak yeterli ve daha basit.
// ============================================================================

const NOMINATIM = 'https://nominatim.openstreetmap.org/search'

const NOMINATIM_TERS = 'https://nominatim.openstreetmap.org/reverse'

/**
 * Metinle yer arar.
 * @param {string} sorgu Kullanıcının yazdığı metin
 * @param {AbortSignal} signal Yeni tuşa basılınca eski isteği iptal etmek için
 * @returns {Promise<Array<{id, ad, tamAd, lon, lat, tur, sinif}>>}
 */
export async function yerAra(sorgu, signal) {
  const parametreler = new URLSearchParams({
    q: sorgu,
    format: 'jsonv2',
    limit: '6',
    addressdetails: '0',
    'accept-language': 'tr',
  })

  const res = await fetch(`${NOMINATIM}?${parametreler}`, { signal })
  if (!res.ok) throw new Error(`Arama servisi yanıt vermedi (${res.status})`)

  const veri = await res.json()

  return veri.map(yeriCevir)
}

/**
 * TERS COĞRAFİ KODLAMA — koordinattan yer bulma (Ödev 13 / Madde 4).
 *
 * Kullanıcı POI aracıyla haritaya tıkladığında "burası neresi?" sorusunun
 * cevabını buradan alıyoruz: tıklanan noktada kayıtlı bir yer varsa adı ve
 * TÜRÜ dönüyor, form da ikisini birden dolduruyor (ad + önerilen kategori).
 *
 * zoom=18: sorgunun ayrıntı seviyesi. Küçük değerler mahalle/ilçe döndürür;
 * 18 bina/işletme seviyesidir. POI eklerken istediğimiz tam olarak bu —
 * "Çankaya" değil "Millî Kütüphane".
 *
 * Boş dönebilir: denizin ortasına tıklandığında kayıtlı bir yer yoktur.
 * O durumda null dönüyoruz ve form eskisi gibi boş açılıyor; uydurma bir ad
 * üretmek kullanıcıyı yanıltırdı.
 *
 * @returns {Promise<{ad, tamAd, lon, lat, tur, sinif} | null>}
 */
export async function yeriCoz(lon, lat, signal) {
  const parametreler = new URLSearchParams({
    lon: String(lon),
    lat: String(lat),
    format: 'jsonv2',
    zoom: '18',
    addressdetails: '0',
    'accept-language': 'tr',
  })

  const res = await fetch(`${NOMINATIM_TERS}?${parametreler}`, { signal })
  if (!res.ok) throw new Error(`Adres servisi yanıt vermedi (${res.status})`)

  const yer = await res.json()

  // Nominatim bulamadığında da 200 döner, gövdede { error: "..." } olur.
  if (!yer || yer.error || !yer.display_name) return null

  return yeriCevir(yer)
}

/**
 * Nominatim kaydını uygulamanın kullandığı sade biçime çevirir.
 *
 * `sinif` (OSM class) `tur` (OSM type) ile birlikte taşınıyor çünkü kategori
 * önerisi ikisine birden bakıyor: bazı yerlerde ayırt edici bilgi type'ta
 * (amenity/library), bazılarında class'ta (tourism/hotel) duruyor.
 */
function yeriCevir(yer) {
  return {
    id: `${yer.osm_type}-${yer.osm_id}`,
    // display_name uzun bir adres zinciri: "Anıtkabir, Anıttepe, Çankaya, Ankara, ..."
    // İlk parça yerin kendi adı, gerisi bulunduğu idari birimler.
    // name alanı varsa onu tercih ediyoruz: ters kodlamada display_name
    // bazen sokak numarasıyla başlıyor.
    ad: (yer.name && yer.name.trim()) || yer.display_name.split(',')[0].trim(),
    tamAd: yer.display_name,
    lon: parseFloat(yer.lon),
    lat: parseFloat(yer.lat),
    tur: yer.type,                       // "library", "restaurant", "city" ...
    sinif: yer.category ?? yer.class,    // "amenity", "tourism", "shop" ...
  }
}
