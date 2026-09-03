// ============================================================================
//  CANLI TURUN İLERLEMESİ — saf hesaplar
//
//  ActiveTourView'in gösterdiği üç sayı burada üretiliyor:
//      • mevcut durak ve orada kalan süre
//      • turun kalan toplam süresi
//      • sıradaki durağa mesafe / tahmini süre
//
//  ---- BU SAYILAR NEDEN SUNUCUDAN GELMİYOR? ----
//  Sunucunun gönderdiği oturum kaydı (TourSessionDto) hangi durakta
//  olunduğunu ve son konumu söylüyor; ama "sıradaki durağa 400 m" gibi bir
//  değer TAŞIMIYOR. Taşısaydı, her konum yayınında (saniyede bir) sunucunun
//  bu hesabı yeniden yapması ve mesajın büyümesi gerekirdi. Hesap ucuz ve
//  girdisi zaten istemcide: durakların koordinatları tur şablonunda, son
//  konum yayında.
//
//  ---- TAHMİNLER NE KADAR DOĞRU? ----
//  Mesafe KUŞ UÇUŞU hesaplanıp <see cref="YOL_KATSAYISI"/> ile çarpılıyor;
//  gerçek yol her zaman daha uzun. Google Directions'a sormak daha doğru
//  olurdu ama her konum güncellemesinde bir istek demekti — tur boyunca
//  yüzlerce faturalı çağrı. Ekranda "≈" işaretiyle gösteriliyor ki kullanıcı
//  bunun bir tahmin olduğunu bilsin.
// ============================================================================

import { ULASIM_TIPLERI } from './turPlani'

/** Dünya yarıçapı (metre) — haversine için. */
const DUNYA_YARICAPI = 6_371_000

/**
 * Kuş uçuşu mesafeyi gerçek yola yaklaştıran katsayı.
 *
 * Şehir içi ızgara sokak düzeninde yürüme mesafesi kuş uçuşunun tipik olarak
 * %25-40 üstünde. 1.3 ortada bir değer; katsayısız kullansaydık her tahmin
 * sistematik olarak İYİMSER olurdu ve tur hep gecikirdi.
 */
export const YOL_KATSAYISI = 1.3

/**
 * "POINT (32.85 39.93)" → { lat, lon }
 *
 * NEDEN geo.js'teki dönüştürücü kullanılmıyor? O, OpenLayers üzerinden
 * bir harita nesnesi (Feature) üretip projeksiyon dönüşümü yapıyor — bu
 * dosyanın işi ise iki sayıyı okumak. Harita kütüphanesini saf bir hesap
 * modülüne sokmak, testlerin de onu yüklemesi demek olurdu.
 *
 * @returns {{lat: number, lon: number}|null} biçim tanınmazsa null
 */
export function noktaCoz(wkt) {
  if (typeof wkt !== 'string') return null

  const eslesme = wkt.match(/POINT\s*\(\s*(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)\s*\)/i)
  if (!eslesme) return null

  // WKT sırası: boylam enlem (x y).
  const lon = Number(eslesme[1])
  const lat = Number(eslesme[2])

  return Number.isFinite(lat) && Number.isFinite(lon) ? { lat, lon } : null
}

/**
 * İki nokta arasındaki kuş uçuşu mesafe (metre) — haversine.
 *
 * Düz Pisagor da kullanılabilirdi (şehir ölçeğinde farkı küçük) ama enlem
 * arttıkça boylam dereceleri daralıyor: Türkiye'de 1° boylam ≈ 85 km, 1°
 * enlem ≈ 111 km. Bunu görmezden gelmek doğu-batı mesafelerini %30 fazla
 * gösterirdi.
 */
export function mesafeMetre(a, b) {
  if (!a || !b) return null

  const rad = (derece) => (derece * Math.PI) / 180

  const dLat = rad(b.lat - a.lat)
  const dLon = rad(b.lon - a.lon)

  const h = Math.sin(dLat / 2) ** 2
    + Math.cos(rad(a.lat)) * Math.cos(rad(b.lat)) * Math.sin(dLon / 2) ** 2

  return 2 * DUNYA_YARICAPI * Math.asin(Math.min(1, Math.sqrt(h)))
}

/** Ulaşım tipinin ortalama hızı (km/sa); tanınmazsa yaya hızı. */
const hizKmS = (ulasimTipi) =>
  ULASIM_TIPLERI.find((u) => u.deger === ulasimTipi)?.ortalamaHizKmS
  ?? ULASIM_TIPLERI[0].ortalamaHizKmS

/** Mesafeden tahmini seyahat süresi (dakika). */
export function seyahatDakika(metre, ulasimTipi) {
  if (!Number.isFinite(metre) || metre <= 0) return 0

  const km = (metre * YOL_KATSAYISI) / 1000
  return Math.max(1, Math.round((km / hizKmS(ulasimTipi)) * 60))
}

// ---------------------------------------------------------------------------
//  Duraklar
// ---------------------------------------------------------------------------

/** Turun durakları, sıraya dizili. */
const duraklar = (tur) => [...(tur?.waypoints ?? [])].sort((a, b) => a.order - b.order)

/**
 * Grubun ŞU AN bulunduğu durak.
 *
 * Önce id ile arıyoruz (sunucunun tek doğru kaynağı), id gelmezse sıra
 * numarasına düşüyoruz. İkisi de yoksa tur henüz ilk durağa varmamış demektir
 * ve null dönüyor — o durumda arayüz "tur başlıyor" durumunu gösteriyor.
 */
export function mevcutDurak(tur, oturum) {
  const liste = duraklar(tur)
  if (liste.length === 0 || !oturum) return null

  if (oturum.currentWaypointId) {
    const idIle = liste.find((d) => d.id === oturum.currentWaypointId)
    if (idIle) return idIle
  }

  if (oturum.currentWaypointOrder) {
    return liste.find((d) => d.order === oturum.currentWaypointOrder) ?? null
  }

  return null
}

/**
 * SIRADAKİ durak.
 *
 * Mevcut durak yoksa turun İLK durağı sıradakidir: grup henüz yolda ve
 * gideceği yer birinci durak. Son duraktaysa null — tur bitmek üzere.
 */
export function sonrakiDurak(tur, oturum) {
  const liste = duraklar(tur)
  if (liste.length === 0) return null

  const mevcut = mevcutDurak(tur, oturum)
  if (!mevcut) return liste[0]

  return liste.find((d) => d.order === mevcut.order + 1) ?? null
}

/** Turun kaçıncı durağındayız? (1 tabanlı; henüz varılmadıysa 0) */
export const durakSirasi = (tur, oturum) => mevcutDurak(tur, oturum)?.order ?? 0

/**
 * Sıradaki durağa mesafe ve tahmini süre.
 *
 * Başlangıç noktası olarak ÖNCE rehberin son bildirilen konumu kullanılıyor
 * (grup yolda olabilir), yoksa mevcut durağın koordinatı. Hiçbiri yoksa
 * hesap yapılamaz ve null dönüyor — sıfır yazmak "vardık" gibi okunurdu.
 *
 * @returns {{metre: number, dakika: number}|null}
 */
export function sonrakiDurakTahmini(tur, oturum, ulasimTipi = 'Yaya') {
  const hedef = sonrakiDurak(tur, oturum)
  if (!hedef) return null

  const hedefNokta = noktaCoz(hedef.wkt)
  if (!hedefNokta) return null

  const baslangic = Number.isFinite(oturum?.lastLat) && Number.isFinite(oturum?.lastLon)
    ? { lat: oturum.lastLat, lon: oturum.lastLon }
    : noktaCoz(mevcutDurak(tur, oturum)?.wkt)

  if (!baslangic) return null

  const metre = mesafeMetre(baslangic, hedefNokta) * YOL_KATSAYISI

  return {
    metre: Math.round(metre),
    dakika: seyahatDakika(mesafeMetre(baslangic, hedefNokta), ulasimTipi),
  }
}

// ---------------------------------------------------------------------------
//  Süreler
// ---------------------------------------------------------------------------

/**
 * Mevcut durakta kalan kalış süresi (dakika).
 *
 * Varış anı (currentWaypointArrivedUtc) biliniyorsa gerçekten geriye sayıyor;
 * bilinmiyorsa durağın tam kalış süresini döndürüyor. Eksi değere düşmüyor:
 * "−7 dk" göstermek yerine 0 yazıp "süre doldu" demek arayüzün işi.
 */
export function durakKalanDakika(tur, oturum, simdi = new Date()) {
  const mevcut = mevcutDurak(tur, oturum)
  if (!mevcut) return 0

  const planlanan = mevcut.dwellMinutes ?? 0
  if (!oturum?.currentWaypointArrivedUtc) return planlanan

  const varis = new Date(oturum.currentWaypointArrivedUtc)
  if (Number.isNaN(varis.getTime())) return planlanan

  const gecen = (simdi.getTime() - varis.getTime()) / 60_000
  return Math.max(0, Math.round(planlanan - gecen))
}

/**
 * TURUN kalan süresi (dakika): şu andan son durağın sonuna kadar.
 *
 * Hesap = mevcut durakta kalan süre
 *       + kalan durakların kalış süreleri
 *       + aradaki bacakların tahmini seyahat süreleri
 *
 * NEDEN "toplam süre − geçen süre" DEĞİL? O hesap, turun plana göre gittiğini
 * varsayar. Gerçekte rehber bir durakta oyalanır ya da erken geçer; kalanı
 * KALAN İŞTEN hesaplamak, gecikmeyi de kendiliğinden yansıtıyor.
 */
export function kalanTurDakika(tur, oturum, ulasimTipi = 'Yaya', simdi = new Date()) {
  const liste = duraklar(tur)
  if (liste.length === 0) return 0

  const mevcut = mevcutDurak(tur, oturum)
  const mevcutSira = mevcut?.order ?? 0

  // Henüz varılmamış duraklar.
  const kalanlar = liste.filter((d) => d.order > mevcutSira)

  let toplam = durakKalanDakika(tur, oturum, simdi)

  // Şu anki konumdan (ya da mevcut duraktan) ilk kalan durağa.
  const ilkTahmin = sonrakiDurakTahmini(tur, oturum, ulasimTipi)
  if (ilkTahmin) toplam += ilkTahmin.dakika

  // Kalan durakların kalış süreleri + aralarındaki bacaklar.
  kalanlar.forEach((durak, i) => {
    toplam += durak.dwellMinutes ?? 0

    const sonraki = kalanlar[i + 1]
    if (!sonraki) return

    const a = noktaCoz(durak.wkt)
    const b = noktaCoz(sonraki.wkt)
    if (a && b) toplam += seyahatDakika(mesafeMetre(a, b), ulasimTipi)
  })

  return Math.round(toplam)
}

/** Oturum başlayalı kaç dakika oldu? Başlamadıysa 0. */
export function gecenDakika(oturum, simdi = new Date()) {
  if (!oturum?.startedUtc) return 0

  const baslangic = new Date(oturum.startedUtc)
  if (Number.isNaN(baslangic.getTime())) return 0

  return Math.max(0, Math.round((simdi.getTime() - baslangic.getTime()) / 60_000))
}

/**
 * Tamamlanan durak yüzdesi (0-100).
 *
 * Sunucu da bir <c>progressPercent</c> gönderiyor ve o ROTA MESAFESİNE
 * dayanıyor; bu ise DURAK SAYISINA. İkisi farklı şeyler ölçüyor: uzun bir
 * bacak mesafenin yarısını götürebilir ama tek durak eder. Arayüzde durak
 * sayısı okunuyor ("3 / 8 durak"), o yüzden çubuk da onunla aynı kaynaktan
 * besleniyor — iki farklı yüzde göstermek kafa karıştırırdı.
 */
export function durakYuzdesi(tur, oturum) {
  const liste = duraklar(tur)
  if (liste.length === 0) return 0

  return Math.round((durakSirasi(tur, oturum) / liste.length) * 100)
}

// ---------------------------------------------------------------------------
//  Biçimlendirme
// ---------------------------------------------------------------------------

/** 850 → "850 m", 2400 → "2,4 km" */
export function mesafeMetni(metre) {
  if (!Number.isFinite(metre)) return '—'

  return metre >= 1000
    ? `${(metre / 1000).toLocaleString('tr-TR', { maximumFractionDigits: 1 })} km`
    : `${Math.round(metre)} m`
}

/** 95 → "1 sa 35 dk" (turPlani.sureMetni ile aynı biçim). */
export function dakikaMetni(dakika) {
  if (!Number.isFinite(dakika) || dakika <= 0) return '0 dk'

  const saat = Math.floor(dakika / 60)
  const kalan = Math.round(dakika % 60)

  if (saat === 0) return `${kalan} dk`
  return kalan === 0 ? `${saat} sa` : `${saat} sa ${kalan} dk`
}
