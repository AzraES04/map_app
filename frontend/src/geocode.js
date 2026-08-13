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

/**
 * Metinle yer arar.
 * @param {string} sorgu Kullanıcının yazdığı metin
 * @param {AbortSignal} signal Yeni tuşa basılınca eski isteği iptal etmek için
 * @returns {Promise<Array<{id, ad, tamAd, lon, lat, tur}>>}
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

  return veri.map((yer) => ({
    id: `${yer.osm_type}-${yer.osm_id}`,
    // display_name uzun bir adres zinciri: "Anıtkabir, Anıttepe, Çankaya, Ankara, ..."
    // İlk parça yerin kendi adı, gerisi bulunduğu idari birimler.
    ad: yer.display_name.split(',')[0].trim(),
    tamAd: yer.display_name,
    lon: parseFloat(yer.lon),
    lat: parseFloat(yer.lat),
    tur: yer.type,          // "monument", "city", "road" ...
  }))
}
