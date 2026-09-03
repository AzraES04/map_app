// ============================================================================
//  TUR PROGRAMI — turu GÜN GÜN ve SAAT SAAT bölen saf hesap
//
//  Girdi: turun sıralı durakları + "saat kaçta başlıyoruz, günde kaç saat
//  geziyoruz". Çıktı: her günün kendi başlangıç/bitiş saati ve her durağın
//  VARIŞ / AYRILIŞ saatiyle listesi.
//
//  ---- NEDEN GEREKLİ? ----
//  Tur önerisi bir DURAK LİSTESİ döndürüyor; kullanıcının sorduğu soru ise
//  "kaçta nerede olacağım?". Üç günlük bir turda düz liste bu soruyu
//  cevaplamıyor: hangi durağın hangi gün olduğu, sabah kaçta çıkılacağı,
//  bir yerde gecikince neyin kaydığı görünmüyor.
//
//  ---- NEDEN SUNUCUDA DEĞİL? ----
//  Hesabın girdisi (başlangıç saati, günlük süre) tamamen kullanıcının
//  ekrandaki seçimi; her değişiklikte sunucuya gitmek, saat kaydırmayı ağ
//  gecikmesine bağlamak olurdu. Duraklar ve süreler zaten istemcide.
//
//  ---- GÜNLER NEDEN DENGELİ BÖLÜNÜYOR? ----
//  İlk sürüm günü YALNIZCA SAATE göre kapatıyordu ve canlıda şu çıktı:
//
//      1. gün  09:00–16:41 · 13 durak
//      2. gün  09:00–09:30 ·  1 durak
//
//  Matematik doğruydu — 30 dakikalık kalışlarla 13 durak sekiz saate
//  gerçekten sığıyor. Yanlış olan MODELDİ: sunucu adayları seçerken "bir
//  günde en fazla 6 durak" kuralını uyguluyor (TurKatalogu.GunlukAzamiDurak),
//  bu hesap ise o kuralı hiç bilmiyordu. İki taraf farklı gün tanımı
//  kullanınca duraklar ilk güne yığıldı.
//
//  Artık gün İKİ sınırla kapanıyor — süre VE durak sayısı — ve duraklar
//  günlere dengeli dağıtılıyor: 14 durak 3 güne 6+6+2 değil 5+5+4 olarak
//  bölünüyor. Kalabalık bir gün ile boş bir gün, aynı turun iki ucu olamaz.
//
//  ---- SÜRELER TAHMİN ----
//  Duraklar arası süre kuş uçuşu mesafeden üretiliyor (turIlerleme.js).
//  Gerçek yol her zaman daha uzun; bu yüzden ekranda "≈" ile gösteriliyor.
//  Adım adım gerçek tarif, cihazın harita uygulamasına bırakılıyor
//  (haritaLinki.js) — orada trafik ve gerçek yol ağı var.
// ============================================================================

import { mesafeMetre, noktaCoz, seyahatDakika } from './turIlerleme'

/** Varsayılan başlangıç saati — turların çoğu sabah 9'da başlıyor. */
export const VARSAYILAN_BASLANGIC = '09:00'

/** "09:30" → 570 (gece yarısından itibaren dakika). Geçersizse null. */
export function saatiDakikayaCevir(saat) {
  if (typeof saat !== 'string') return null

  const eslesme = saat.match(/^(\d{1,2}):(\d{2})$/)
  if (!eslesme) return null

  const sa = Number(eslesme[1])
  const dk = Number(eslesme[2])

  if (sa > 23 || dk > 59) return null

  return sa * 60 + dk
}

/**
 * 570 → "09:30". 24 saati aşan değerler ERTESİ GÜNE SARMIYOR, olduğu gibi
 * yazılıyor ("25:10"): sarsaydık gece yarısını geçen bir program, sabaha
 * dönmüş gibi görünürdü. Program üretimi zaten günü kapatıyor, bu durum
 * yalnızca kullanıcı günlük süreyi aşırı büyüttüğünde ortaya çıkıyor.
 */
export function dakikayiSaateCevir(dakika) {
  if (!Number.isFinite(dakika)) return '--:--'

  const sa = Math.floor(dakika / 60)
  const dk = Math.round(dakika % 60)

  return `${String(sa).padStart(2, '0')}:${String(dk).padStart(2, '0')}`
}

/**
 * İki durak arasındaki tahmini bacak.
 * @returns {{metre: number, dakika: number}|null}
 */
export function bacakTahmini(a, b, ulasimTipi) {
  const nA = noktaCoz(a?.wkt)
  const nB = noktaCoz(b?.wkt)
  if (!nA || !nB) return null

  const kusUcusu = mesafeMetre(nA, nB)

  return {
    metre: Math.round(kusUcusu * 1.3),   // YOL_KATSAYISI (turIlerleme.js)
    dakika: seyahatDakika(kusUcusu, ulasimTipi),
  }
}

/**
 * Turu gün gün, saat saat bölüyor.
 *
 * ---- GÜN NASIL KAPANIYOR? İKİ SINIR VAR ----
 *
 *   1. SÜRE — bir durağın maliyeti "oraya gitme + orada kalma". Sıradaki
 *      durak günün kalan süresine sığmıyorsa gün kapanır.
 *
 *   2. DURAK SAYISI — süre sınırı tek başına yetmiyor. 30 dakikalık
 *      kalışlarla 13 durak sekiz saate sığıyor ama kimse bir günde 13 yer
 *      gezmiyor; canlıda üretilen ilk çok günlü tur tam olarak böyleydi
 *      (13 + 1). Sınır sunucudan geliyor (öneri cevabındaki
 *      gunlukAzamiDurak), çünkü ADAY SEÇERKEN de aynı sayı kullanılıyor.
 *
 * ---- NEDEN DENGELİ DAĞITIM? ----
 * Duraklar sırayla doldurulsaydı ilk günler tavana kadar dolar, son gün
 * artakalanı alırdı: 14 durak / 3 gün → 6+6+2. Bunun yerine her gün
 * açılırken hedef YENİDEN hesaplanıyor (kalan durak / kalan gün), böylece
 * 5+5+4 çıkıyor. Süre yüzünden erken kapanan bir gün olursa da kendini
 * düzeltiyor: sonraki günlerin hedefi otomatik yükseliyor.
 *
 * ---- İSTENEN GÜN SAYISI DOLMAYABİLİR ----
 * 10 günlük tur istenip bölgede yalnızca 14 kayda değer mekan varsa, 7 boş
 * gün üretmek yerine 3 dolu gün üretiyoruz. Kaç gün istendiği cevapta
 * (talepEdilenGun) duruyor ki arayüz farkı söyleyebilsin — sessizce
 * kısaltmak, kullanıcının programını eksik sanmasına yol açardı.
 *
 * @param {Array} duraklar Sıralı duraklar ({ order, name, dwellMinutes, wkt })
 * @param {{baslangicSaati?: string, gunlukSaat?: number, ulasimTipi?: string,
 *          enFazlaGun?: number, gunlukAzamiDurak?: number}} secenekler
 * @returns {{gunler: Array, sigmayan: Array, toplamGun: number,
 *            talepEdilenGun: number}}
 */
export function programUret(duraklar, secenekler = {}) {
  const {
    baslangicSaati = VARSAYILAN_BASLANGIC,
    gunlukSaat = 8,
    ulasimTipi = 'Yaya',
    enFazlaGun = null,
    gunlukAzamiDurak = null,
  } = secenekler

  const sirali = [...(duraklar ?? [])].sort((a, b) => a.order - b.order)

  const baslangic = saatiDakikayaCevir(baslangicSaati) ?? saatiDakikayaCevir(VARSAYILAN_BASLANGIC)
  const gunKapasitesi = Math.max(60, (Number(gunlukSaat) || 8) * 60)

  // Durak tavanı gelmediyse sınır yok: hesap eskisi gibi yalnızca süreye
  // bakar. (Kaydedilmiş turlarda öneri cevabı elimizde olmayabiliyor.)
  const durakTavani = Number(gunlukAzamiDurak) > 0
    ? Number(gunlukAzamiDurak)
    : Number.POSITIVE_INFINITY

  // enFazlaGun VERİLMEZSE gün sayısı SINIRSIZ — kaydedilmiş bir turu
  // programlarken "kaç gün istendi" bilgisi elimizde olmuyor ve turu tek
  // güne sıkıştırmak, sekiz saate on saatlik program yazmak olurdu.
  const azamiGun = Number(enFazlaGun) > 0 ? Number(enFazlaGun) : Number.POSITIVE_INFINITY

  // ---- Dağıtımın temeli: kaç gün GEREKİYOR? ----
  //
  // İstenen gün sayısı değil, durakların gerektirdiği gün. 14 durak ve
  // günde 6 durak sınırıyla 3 gün gerekiyor; kullanıcı 10 gün istese de
  // 7'sini boş açmıyoruz.
  const gerekenGun = Number.isFinite(durakTavani)
    ? Math.max(1, Math.ceil(sirali.length / durakTavani))
    : 1
  let kalanGun = Math.max(1, Math.min(azamiGun, gerekenGun))

  const gunler = []
  const sigmayan = []

  let gun = null
  let gunHedefi = 0
  let saat = baslangic

  /** Açık günü kapatır ve dağıtım sayaçlarını ilerletir. */
  const gunuKapat = () => {
    if (!gun) return
    gun.bitisDakika = saat
    gun = null
    kalanGun = Math.max(1, kalanGun - 1)
  }

  sirali.forEach((durak, i) => {
    const kalis = durak.dwellMinutes ?? 0
    const oncekiDurak = i > 0 ? sirali[i - 1] : null

    // Bu durağa gelmenin süresi — günün İLK durağında sayılmıyor.
    const bacak = gun && oncekiDurak ? bacakTahmini(oncekiDurak, durak, ulasimTipi) : null
    const gelis = bacak?.dakika ?? 0

    if (gun) {
      const sureSigar = (saat - gun.baslangicDakika) + gelis + kalis <= gunKapasitesi
      const sayiSigar = gun.duraklar.length < gunHedefi

      // İKİ sınırdan biri dolduysa gün kapanıyor.
      if (!sureSigar || !sayiSigar) gunuKapat()
    }

    if (!gun) {
      if (gunler.length >= azamiGun) {
        // Kullanıcının verdiği gün sayısına sığmayan duraklar SESSİZCE
        // atılmıyor: arayüz "şu duraklar programa girmedi" diyebilsin.
        sigmayan.push(durak)
        return
      }

      // HEDEF HER GÜN YENİDEN: kalan durakları kalan güne bölüyoruz. Sabit
      // bir hedef olsaydı süre yüzünden erken kapanan bir günün yükü sona
      // birikirdi.
      const kalanDurak = sirali.length - i - sigmayan.length
      gunHedefi = Math.min(durakTavani, Math.max(1, Math.ceil(kalanDurak / kalanGun)))

      gun = {
        gun: gunler.length + 1,
        baslangicDakika: baslangic,
        bitisDakika: baslangic,
        duraklar: [],
      }
      gunler.push(gun)
      saat = baslangic
    }

    const varis = saat + (gun.duraklar.length === 0 ? 0 : gelis)
    const ayrilis = varis + kalis

    gun.duraklar.push({
      order: durak.order,
      name: durak.name,
      venueType: durak.venueType,
      wkt: durak.wkt,
      // Durağın ROLÜ ("Yemek molası", "Önerilen konaklama bölgesi") —
      // sunucu mola duraklarına bu notu yazıyor ve program listesi onu
      // rozet olarak gösteriyor. Taşımasaydık mola, sıradan bir gezi
      // durağından ayırt edilemezdi.
      note: durak.note ?? null,
      kalisDakika: kalis,
      varis: dakikayiSaateCevir(varis),
      ayrilis: dakikayiSaateCevir(ayrilis),
      // Önceki duraktan buraya geliş — günün ilk durağında null.
      gelis: gun.duraklar.length === 0 ? null : bacak,
      oncekiDurak: gun.duraklar.length === 0 ? null : oncekiDurak,
    })

    saat = ayrilis
    gun.bitisDakika = ayrilis
  })

  return {
    gunler: gunler.map((g) => ({
      gun: g.gun,
      baslangic: dakikayiSaateCevir(g.baslangicDakika),
      bitis: dakikayiSaateCevir(g.bitisDakika),
      toplamDakika: g.bitisDakika - g.baslangicDakika,
      duraklar: g.duraklar,
    })),
    sigmayan,
    toplamGun: gunler.length,
    // Kaç gün İSTENDİĞİ: arayüz "10 gün istendi, 3 gün doldu" diyebilsin.
    // Sınırsızsa istenen gün = üretilen gün (söylenecek bir fark yok).
    talepEdilenGun: Number.isFinite(azamiGun) ? azamiGun : gunler.length,
  }
}
