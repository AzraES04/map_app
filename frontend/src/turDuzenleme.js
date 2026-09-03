import { mesafeMetre, noktaCoz } from './turIlerleme'

// ============================================================================
//  TUR ROTASI DÜZENLEME — durak ekleme / çıkarma (saf hesap)
//
//  ---- NEDEN AYRI MODÜL? ----
//  Önerinin durak listesini değiştirmek göründüğünden fazla kural içeriyor:
//  sıra numaraları 1..N kalmalı, aynı mekan iki kez girmemeli, iki duraktan
//  az bir tur geçersiz. Bunlar bileşenin içinde kalsaydı test edilemezdi —
//  ve bir sıra numarası hatası, haritada durakların yanlış numaralanması
//  gibi SESSİZ bir hataya dönüşürdü.
//
//  ---- ROTA BURADA HESAPLANMIYOR ----
//  Yollara oturmuş çizgi sunucudan geliyor (POST /api/tur/rota-hesapla).
//  Burası yalnızca "hangi duraklar, hangi sırayla" sorusunu cevaplıyor.
// ============================================================================

/** Elle eklenen durağın varsayılan kalış süresi (dk). */
export const ELLE_EKLENEN_KALIS = 30

/**
 * Sıra numaralarını 1..N olarak yeniden yazar.
 *
 * Numaralar hem haritadaki etiketlerde hem günlük programda kullanılıyor;
 * araya durak eklendiğinde eskisini korusaydık iki durak aynı numarayı
 * taşırdı.
 */
const siralariDuzelt = (duraklar) =>
  duraklar.map((durak, i) => ({ ...durak, order: i + 1 }))

/**
 * POI kaydını tur durağına çevirir.
 *
 * ---- PlaceId NEDEN "poi:" ÖN EKLİ? ----
 * Waypoint.PlaceId tek kolonda birden çok kaynağı taşıyor: öneriden gelen
 * duraklar "google:", elle eklenenler "poi:". Ön ek olmasaydı 12 numaralı
 * POI ile 12 numaralı bir Google mekanı aynı kayıt sanılırdı.
 */
export function poidenDurak(poi) {
  const nokta = noktaCoz(poi?.wkt)
  if (!nokta) return null

  return {
    id: 0,
    tourId: 0,
    order: 0,                       // siralariDuzelt yazacak
    placeId: `poi:${poi.id}`,
    poiId: poi.id,
    name: poi.isim,
    venueType: 'Other',
    dwellMinutes: ELLE_EKLENEN_KALIS,
    wkt: poi.wkt,
    note: poi.kategoriYolu ?? null,
    isActive: true,
    // Elle eklendiğini arayüz rozetle gösteriyor: kullanıcı kendi
    // eklediğiyle önerinin bulduğunu ayırt edebilsin.
    elleEklendi: true,
  }
}

/**
 * Durağı listeye ekler.
 *
 * ---- NEREYE EKLENİYOR: SONA ----
 * "En yakın iki durağın arasına sok" denemesi daha zekice görünüyor ama
 * kullanıcının niyetini tahmin etmek zorunda: bir müzeyi öğleden sonraya
 * mı, sabaha mı istiyor? Sona eklemek öngörülebilir; sıra değiştirmek
 * ayrı bir işlem (yukarı/aşağı taşı).
 *
 * @returns {{duraklar: Array, hata: string|null}}
 */
export function duragiEkle(duraklar, yeni) {
  if (!yeni) {
    return { duraklar, hata: 'Bu kaydın konumu okunamadı.' }
  }

  // AYNI MEKAN İKİ KEZ girmesin: hem anlamsız hem de rota kendi üstüne
  // dönerdi.
  const varMi = duraklar.some(
    (d) => d.placeId === yeni.placeId
      || (d.poiId != null && d.poiId === yeni.poiId),
  )

  if (varMi) {
    return { duraklar, hata: `"${yeni.name}" zaten turda.` }
  }

  return { duraklar: siralariDuzelt([...duraklar, yeni]), hata: null }
}

/**
 * Durağı listeden çıkarır.
 *
 * İKİ DURAKTAN AZA düşürmeye izin YOK: tek duraklı tur olmaz, rota da
 * çizilemez (sunucu da aynı kuralı uyguluyor). Düğmeyi kapatmak yerine
 * mesaj döndürüyoruz ki kullanıcı sebebini bilsin.
 */
export function duragiCikar(duraklar, sira) {
  if (duraklar.length <= 2) {
    return { duraklar, hata: 'Turda en az iki durak kalmalı.' }
  }

  return {
    duraklar: siralariDuzelt(duraklar.filter((d) => d.order !== sira)),
    hata: null,
  }
}

/**
 * Durağı bir sıra yukarı/aşağı taşır.
 *
 * @param {number} yon -1 yukarı, +1 aşağı. Listenin dışına taşımaya çalışmak
 *   sessizce yok sayılıyor: uçtaki durakta düğme zaten kapalı.
 */
export function duragiTasi(duraklar, sira, yon) {
  const i = duraklar.findIndex((d) => d.order === sira)
  const hedef = i + yon

  if (i < 0 || hedef < 0 || hedef >= duraklar.length) {
    return { duraklar, hata: null }
  }

  const yeni = [...duraklar]
  ;[yeni[i], yeni[hedef]] = [yeni[hedef], yeni[i]]

  return { duraklar: siralariDuzelt(yeni), hata: null }
}

/**
 * Rota hesabı için sunucuya gidecek gövde.
 *
 * Koordinatı çözülemeyen durak ATLANMIYOR, hata veriyor: sessizce atlasaydık
 * rota o durağa uğramaz ama liste onu göstermeye devam ederdi — ekranla
 * harita ayrışırdı.
 */
export function rotaHesapGovdesi(duraklar, ulasimTipi) {
  const noktalar = duraklar.map((d) => noktaCoz(d.wkt))

  if (noktalar.some((n) => !n)) return null

  return {
    ulasimTipi: ulasimTipi ?? 'Yaya',
    duraklar: noktalar.map((n) => ({ lat: n.lat, lon: n.lon })),
  }
}

/**
 * Turun COĞRAFİ MERKEZİ — duraklarının ortalaması.
 *
 * Şehrin resmi merkezi sunucuda (SehirMerkezleri.cs) ama istemciye
 * gönderilmiyor; turun kendi duraklarının ortalaması aynı işi görüyor ve
 * daha da doğru: kullanıcı Çankaya turu istediyse merkez Çankaya olur,
 * Ankara'nın ağırlık merkezi değil.
 */
export function turMerkezi(duraklar) {
  const noktalar = (duraklar ?? []).map((d) => noktaCoz(d.wkt)).filter(Boolean)
  if (noktalar.length === 0) return null

  return {
    lat: noktalar.reduce((t, n) => t + n.lat, 0) / noktalar.length,
    lon: noktalar.reduce((t, n) => t + n.lon, 0) / noktalar.length,
  }
}

/** Durak eklerken kabul edilen en uzak POI (metre). */
export const ARAMA_YARICAPI_METRE = 40_000

/**
 * Arama sonuçlarını TURUN ŞEHRİYLE sınırlar.
 *
 * ---- NEDEN GEREKLİ? ----
 * POI arama ucu bütün tabloyu tarıyor ve tablo artık iki şehrin turistik
 * mekanlarını birden taşıyor. Ankara turuna durak eklerken "cami" araması
 * İstanbul'daki kayıtları da getiriyordu; listeden yanlışlıkla seçilen bir
 * kayıt rotayı 350 km uzatırdı.
 *
 * Süzme İSTEMCİDE: arama ucu metin araması yapıyor ve ona il parametresi
 * eklemek, uca tur modülüne özgü bir kavram sokmak olurdu. Sonuçlar zaten
 * en fazla 25 kayıt; mesafe hesabı ihmal edilebilir.
 *
 * Merkez bilinmiyorsa (durak yok) süzme YAPILMIYOR: boş liste göstermek,
 * kullanıcıya sebebi görünmeyen bir arıza gibi gelirdi.
 */
export function sehirIcindekiler(sonuclar, merkez, yaricap = ARAMA_YARICAPI_METRE) {
  if (!merkez) return sonuclar ?? []

  return (sonuclar ?? []).filter((poi) => {
    const nokta = noktaCoz(poi.wkt)
    return nokta ? mesafeMetre(merkez, nokta) <= yaricap : false
  })
}
