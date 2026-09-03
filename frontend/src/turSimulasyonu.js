import { mesafeMetre, noktaCoz } from './turIlerleme'

// ============================================================================
//  TUR SİMÜLASYONU — rotayı baştan sona OYNATAN saf hesap
//
//  ---- NE İŞE YARIYOR? ----
//  Ulaşım modülündeki simülasyon (Ödev 19) bir hattı araçla kat ediyor ve
//  yüzdesini gösteriyor. Aynı gösterim TUR için de isteniyor: rehber ve
//  bağlantıyı açan katılımcılar, turun rotasını haritada oynatarak
//  "önce nereye, sonra nereye" sorusunu tek bakışta görebilsin.
//
//  ---- NEDEN SUNUCUDA DEĞİL, İSTEMCİDE? ----
//  Ulaşım simülasyonu SUNUCUDA çalışıyor ve bunun sebebi var: orada araç
//  PAYLAŞILAN bir nesne — bir kullanıcı başlatıyor, herkes aynı aracı aynı
//  yerde görüyor ve başlatma yetkiye bağlı.
//
//  Turda istenen bu değil. Buradaki simülasyon bir ÖNİZLEME: her izleyici
//  kendi ekranında, kendi istediği anda oynatıyor. Sunucuya taşısaydık:
//
//    • katılımcının oynatma isteği rehberin ekranını da oynatırdı,
//    • duraklatma/başa sarma herkesi etkilerdi,
//    • ve turun canlı durumu (rehberin "sonraki durak" ilerletmesi) ile
//      oynatma karışırdı — ikisi ayrı şeyler: biri GERÇEK konum, diğeri
//      rotanın gösterimi.
//
//  Yan fayda: sunucuda durum yok, SignalR grubu yok, yeniden başlatınca
//  temizlenecek hayalet kayıt yok.
//
//  ---- SÜRE GERÇEK DEĞİL ----
//  İki günlük bir turu gerçek zamanlı oynatmak iki gün sürerdi. Ulaşım
//  simülasyonundaki kararın aynısı: sabit bir GÖSTERİM süresi (varsayılan
//  45 sn) ve yüzde, zamanın oranı.
// ============================================================================

/** Simülasyonun ekranda süreceği süre (ms). */
export const GOSTERIM_SURESI_MS = 45_000

/**
 * WKT LINESTRING → koordinat dizisi.
 *
 * Sunucu rotayı WKT olarak gönderiyor (TourDto.RouteWkt). Ayrı bir geometri
 * kütüphanesi çağırmıyoruz: biçim tek satırlık ve sabit, çözümlemesi
 * noktaCoz ile aynı desende.
 *
 * @returns {Array<{lon:number, lat:number}>} Bozuk/boş girdide boş dizi.
 */
export function cizgiCoz(wkt) {
  if (typeof wkt !== 'string') return []

  const eslesme = wkt.match(/LINESTRING\s*\(([^)]*)\)/i)
  if (!eslesme) return []

  return eslesme[1]
    .split(',')
    .map((ikili) => {
      const [lon, lat] = ikili.trim().split(/\s+/).map(Number)
      return Number.isFinite(lon) && Number.isFinite(lat) ? { lon, lat } : null
    })
    .filter(Boolean)
}

/**
 * Çizgiyi "kaç metrede nereye varılır" tablosuna çevirir.
 *
 * ---- NEDEN ÖNCEDEN HESAPLANIYOR? ----
 * Oynatma saniyede onlarca kare üretiyor. Her karede bütün çizgiyi baştan
 * ölçseydik uzun bir rotada (binlerce nokta) her kare aynı toplamı yeniden
 * hesaplardı. Tablo bir kez çıkıyor, sonra yalnızca ikili arama yapılıyor.
 *
 * @returns {{noktalar: Array, birikimli: number[], toplamMetre: number}}
 */
export function yolTablosu(noktalar) {
  const birikimli = [0]
  let toplam = 0

  for (let i = 1; i < noktalar.length; i++) {
    toplam += mesafeMetre(noktalar[i - 1], noktalar[i])
    birikimli.push(toplam)
  }

  return { noktalar, birikimli, toplamMetre: toplam }
}

/**
 * Rotanın belli bir ORANINDAKİ (0-1) konum.
 *
 * Ara noktalar arasında DOĞRUSAL geçiş var: iki köşe arasında araç düz
 * gidiyor. Rota zaten yollara oturmuş yüzlerce noktadan oluştuğu için bu,
 * gözle görülür bir sapma yaratmıyor — ama köşeden köşeye zıplasaydı
 * hareket kesik kesik görünürdü.
 */
export function oranKonumu(tablo, oran) {
  const { noktalar, birikimli, toplamMetre } = tablo

  if (!noktalar.length) return null
  if (noktalar.length === 1 || toplamMetre <= 0) return noktalar[0]

  const hedef = Math.min(1, Math.max(0, oran)) * toplamMetre

  // İkili arama: hedefin düştüğü parçayı bul.
  let alt = 0
  let ust = birikimli.length - 1

  while (alt < ust - 1) {
    const orta = (alt + ust) >> 1
    if (birikimli[orta] <= hedef) alt = orta
    else ust = orta
  }

  const parcaBoy = birikimli[ust] - birikimli[alt]
  const t = parcaBoy > 0 ? (hedef - birikimli[alt]) / parcaBoy : 0

  return {
    lon: noktalar[alt].lon + (noktalar[ust].lon - noktalar[alt].lon) * t,
    lat: noktalar[alt].lat + (noktalar[ust].lat - noktalar[alt].lat) * t,
  }
}

/**
 * Bir noktanın PARÇA üzerindeki dik izdüşümü.
 *
 * ---- NEDEN KÖŞE DEĞİL, PARÇA? ----
 * İlk sürüm rotadaki en yakın KÖŞE NOKTASINI arıyordu. Yollara oturmuş
 * gerçek rotalarda (yüzlerce köşe) sonuç kabul edilebilir çıkıyor, ama iki
 * köşeli bir rotada — OSRM'e ulaşılamayıp düz hat çizildiğinde tam olarak
 * böyle oluyor — aradaki BÜTÜN duraklar iki uçtan birine yapışıyordu.
 * Dik izdüşüm bu durumu da doğru çözüyor.
 *
 * Boylam enleme göre daralıyor (kutuplarda meridyenler birleşir), o yüzden
 * düzlem hesabından önce cos(enlem) ile ölçekleniyor. Ölçeklemeseydik
 * Türkiye enlemlerinde doğu-batı mesafeler ~%23 fazla sayılır ve izdüşüm
 * yanlış parçaya düşebilirdi.
 *
 * @returns {{t:number, mesafe:number}} t: parça üzerindeki oran (0-1).
 */
function parcayaIzdusum(nokta, a, b) {
  const olcek = Math.cos((a.lat * Math.PI) / 180)

  const ax = a.lon * olcek
  const ay = a.lat
  const bx = b.lon * olcek
  const by = b.lat
  const px = nokta.lon * olcek
  const py = nokta.lat

  const dx = bx - ax
  const dy = by - ay
  const uzunlukKare = dx * dx + dy * dy

  // Sıfır uzunluklu parça (aynı koordinat iki kez): izdüşüm başlangıçta.
  const t = uzunlukKare > 0
    ? Math.min(1, Math.max(0, ((px - ax) * dx + (py - ay) * dy) / uzunlukKare))
    : 0

  const ex = ax + dx * t - px
  const ey = ay + dy * t - py

  return { t, mesafe: Math.sqrt(ex * ex + ey * ey) }
}

/**
 * Durakların rota üzerindeki ORANLARI.
 *
 * Duraklar rotanın köşe noktalarıyla birebir aynı koordinatta DEĞİL: rota
 * yola oturtulmuş, durak ise mekanın kendi noktası (yolun birkaç metre
 * kenarında). Bu yüzden her durak rotaya dik olarak izdüşürülüyor.
 *
 * Oranlar artan sıraya zorlanıyor: rota bir sokakta geri dönüyorsa izdüşüm
 * önceki durağın gerisine düşebiliyor ve "3. durak 2. duraktan önce" gibi
 * bir sıra çıkıyordu.
 */
export function durakOranlari(tablo, duraklar) {
  const { noktalar, birikimli, toplamMetre } = tablo
  if (noktalar.length < 2 || toplamMetre <= 0) return []

  let enAzOran = 0

  return (duraklar ?? []).map((durak) => {
    const nokta = noktaCoz(durak.wkt)
    let enIyiOran = 0
    let enIyiMesafe = Infinity

    if (nokta) {
      for (let i = 1; i < noktalar.length; i++) {
        const { t, mesafe } = parcayaIzdusum(nokta, noktalar[i - 1], noktalar[i])

        if (mesafe < enIyiMesafe) {
          enIyiMesafe = mesafe
          const parcaBoy = birikimli[i] - birikimli[i - 1]
          enIyiOran = (birikimli[i - 1] + parcaBoy * t) / toplamMetre
        }
      }
    }

    const oran = Math.max(enAzOran, enIyiOran)
    enAzOran = oran

    return { order: durak.order, name: durak.name, oran }
  })
}

/**
 * Verilen orandaki ANLIK DURUM — haritadaki araç ve altındaki bilgi satırı
 * bunu okuyor.
 *
 * @returns {{lon,lat,yuzde,oncekiDurak,sonrakiDurak}|null}
 */
export function simulasyonDurumu(tablo, oranlar, oran) {
  const konum = oranKonumu(tablo, oran)
  if (!konum) return null

  // Geçilmiş SON durak ve yaklaşılan İLK durak.
  let onceki = null
  let sonraki = null

  for (const d of oranlar) {
    if (d.oran <= oran + 1e-9) onceki = d
    else if (!sonraki) sonraki = d
  }

  return {
    lon: konum.lon,
    lat: konum.lat,
    yuzde: Math.round(Math.min(1, Math.max(0, oran)) * 100),
    oncekiDurak: onceki,
    sonrakiDurak: sonraki,
  }
}

/**
 * Rotadan oynatılabilir bir simülasyon hazırlar.
 *
 * Rota yoksa (öneri kaydedilmemiş ya da Directions başarısız olmuş) null
 * dönüyor: çağıran taraf oynat düğmesini hiç göstermiyor. Duraklar arasına
 * düz çizgi uydurmak, kullanıcıya var olmayan bir yol göstermek olurdu.
 */
export function simulasyonHazirla(tur) {
  const noktalar = cizgiCoz(tur?.routeWkt)
  if (noktalar.length < 2) return null

  const tablo = yolTablosu(noktalar)
  if (tablo.toplamMetre <= 0) return null

  return { tablo, oranlar: durakOranlari(tablo, tur?.waypoints) }
}
