// ============================================================================
//  Mesai planı — arayüz tarafı (Ödev 13 / Madde 3)
//
//  Sunucudaki karşılığı: backend/StajProject.Business/Mesai/MesaiPlani.cs
//  Aynı JSON şeması iki tarafta da geçerli:
//
//      { tip: "haftalik" | "surekli" | "resmi",
//        resmiTatilKapali: bool,
//        gunler: [{ gun: 1..7, acik, acilis: "09:00", kapanis: "18:00" }] }
//
//  ÖZET METİN NEDEN İKİ YERDE ÜRETİLİYOR?
//  Kaydedilen özet metni SUNUCU üretiyor; burada üretilen kopya yalnızca
//  formun altındaki CANLI ÖNİZLEME içindir ("Kaydedilecek: …"). Kullanıcı
//  bir kutuyu işaretlediğinde sonucu görmek için sunucuya sormak gerekseydi
//  her tıklama bir istek olurdu. Bilinçli bir tekrar; ödediğimiz bedel iki
//  fonksiyonu elle eşlemek, kazandığımız şey anlık geri bildirim.
//  (Aynı takas ISI_RAMPASI ile SLD arasında da yapıldı — bkz. wms.js.)
// ============================================================================

/** 1 = Pazartesi … 7 = Pazar (ISO sırası; backend'deki GunAdlari ile aynı). */
export const GUN_ADLARI = ['Pzt', 'Sal', 'Çar', 'Per', 'Cum', 'Cmt', 'Paz']

/** Uzun adlar — ekran okuyucular ve ipuçları için. */
export const GUN_TAM_ADLARI = [
  'Pazartesi', 'Salı', 'Çarşamba', 'Perşembe', 'Cuma', 'Cumartesi', 'Pazar',
]

export const MESAI_TIPLERI = {
  haftalik: 'haftalik',
  surekli: 'surekli',
  resmi: 'resmi',
}

/** Hafta içi açık, hafta sonu kapalı bir plan üretir. */
function haftaIci(acilis, kapanis, tip, tatilKapali) {
  return {
    tip,
    resmiTatilKapali: tatilKapali,
    gunler: GUN_ADLARI.map((_, i) => {
      const gun = i + 1
      const acik = gun <= 5
      return {
        gun,
        acik,
        acilis: acik ? acilis : null,
        kapanis: acik ? kapanis : null,
      }
    }),
  }
}

/** Formun açılış hâli: hafta içi 09:00–18:00. */
export const bosPlan = () => haftaIci('09:00', '18:00', MESAI_TIPLERI.haftalik, false)

/**
 * Resmî kurum şablonu (Ödev 13 / Madde 3): hafta içi 08:00–17:00, hafta sonu
 * kapalı, resmî tatillerde kapalı.
 *
 * Saatler kilitli DEĞİL — kurumdan kuruma yarım saat oynuyor. Kilitlenen tek
 * şey "resmî tatillerde kapalı" bayrağı: kipin tanımının parçası, seçenek değil.
 */
export const resmiKurumPlani = () => haftaIci('08:00', '17:00', MESAI_TIPLERI.resmi, true)

/**
 * Sunucudan gelen planı forma hazırlar: eksik günleri tamamlar, sıralar.
 * Plan yoksa (Ödev 12'den kalan kayıt) varsayılanı döndürür.
 */
export function planiNormallestir(plan) {
  if (!plan) return bosPlan()

  const gelen = new Map((plan.gunler ?? []).map((g) => [g.gun, g]))

  return {
    tip: plan.tip ?? MESAI_TIPLERI.haftalik,
    resmiTatilKapali: Boolean(plan.resmiTatilKapali),
    gunler: GUN_ADLARI.map((_, i) => {
      const gun = i + 1
      const kayit = gelen.get(gun)

      if (!kayit?.acik) return { gun, acik: false, acilis: null, kapanis: null }

      return {
        gun,
        acik: true,
        // Saat kutusu boş bir değerle çalışmaz; eksik gelirse varsayılana düşüyoruz.
        acilis: kayit.acilis || '09:00',
        kapanis: kayit.kapanis || '18:00',
      }
    }),
  }
}

/**
 * Planın okunur özeti — "Pzt-Cum 09:00-18:00 · Cmt 10:00-14:00 · Paz kapalı".
 * Backend'deki MesaiPlani.OzetMetin ile AYNI kuralı uyguluyor.
 */
export function planOzeti(plan) {
  if (!plan) return ''

  if (plan.tip === MESAI_TIPLERI.surekli) {
    return plan.resmiTatilKapali ? '7/24 · resmî tatillerde kapalı' : '7/24'
  }

  const gunler = [...(plan.gunler ?? [])].sort((a, b) => a.gun - b.gun)
  if (gunler.length === 0 || gunler.every((g) => !g.acik)) return 'Kapalı'

  const anahtar = (g) => (g.acik ? `${g.acilis}-${g.kapanis}` : 'kapali')
  const parcalar = []

  let i = 0
  while (i < gunler.length) {
    const bas = i
    // Aynı saatleri taşıyan ardışık günleri tek grupta topla.
    while (i + 1 < gunler.length && anahtar(gunler[i + 1]) === anahtar(gunler[i])) i++

    const arali = bas === i
      ? GUN_ADLARI[gunler[bas].gun - 1]
      : `${GUN_ADLARI[gunler[bas].gun - 1]}-${GUN_ADLARI[gunler[i].gun - 1]}`

    parcalar.push(gunler[bas].acik
      ? `${arali} ${gunler[bas].acilis}-${gunler[bas].kapanis}`
      : `${arali} kapalı`)

    i++
  }

  if (plan.resmiTatilKapali) parcalar.push('resmî tatillerde kapalı')

  return parcalar.join(' · ')
}

/**
 * Bir günü değiştirip YENİ plan döndürür (React state'i yerinde değiştirmiyoruz).
 * @param {number} gun 1..7
 * @param {object} degisiklik { acik?, acilis?, kapanis? }
 */
export function gunuDegistir(plan, gun, degisiklik) {
  return {
    ...plan,
    gunler: plan.gunler.map((g) => {
      if (g.gun !== gun) return g

      const yeni = { ...g, ...degisiklik }

      // Gün açıldığında saatleri boş bırakmıyoruz: sunucu "açık işaretlendi
      // ama saat boş" diye reddederdi ve kullanıcı hatayı ancak kaydederken
      // görürdü. Kapatıldığında da saatleri siliyoruz ki kaydedilen JSON
      // kullanılmayan değer taşımasın.
      if (yeni.acik) {
        yeni.acilis = yeni.acilis || '09:00'
        yeni.kapanis = yeni.kapanis || '18:00'
      } else {
        yeni.acilis = null
        yeni.kapanis = null
      }

      return yeni
    }),
  }
}

/**
 * Hafta içi (Pzt–Cum) beş güne aynı saatleri uygular.
 * "Beş kutuyu tek tek doldur" işini tek tıklamaya indiriyor — mesai
 * girişinde en sık yapılan hareket bu.
 */
export function haftaIcineUygula(plan, acilis, kapanis) {
  return {
    ...plan,
    gunler: plan.gunler.map((g) =>
      (g.gun <= 5 ? { ...g, acik: true, acilis, kapanis } : g)),
  }
}

/**
 * ŞU AN AÇIK MI? (Ödev 13 iyileştirmesi)
 *
 * Backend'de birebir aynı kural var (MesaiPlani.Durum) ama İŞİ FARKLI:
 *   • sunucudaki, arama sonuçlarının durum metnini üretiyor — o listede
 *     planın kendisi taşınmıyor, yalnızca hazır cevap gidiyor
 *   • buradaki, açık duran bilgi kartındaki rozeti CANLI tutuyor
 *
 * Neden ikisi birden? Sunucunun cevabı istek anına sabitlenir; saatlerce
 * açık kalan bir sekmede "Açık" yazan rozet gerçeği yansıtmaz. Rozeti
 * tarayıcının saatiyle hesaplamak bu sorunu tamamen ortadan kaldırıyor.
 * (Aynı bilinçli ikizlik planOzeti ↔ OzetMetin arasında da var; ödediğimiz
 * bedel iki fonksiyonu elle eşlemek.)
 *
 * @param {object|null} plan POI'nin mesai planı
 * @param {Array<{tarih, ad, yarimGun}>} tatiller Resmî tatil listesi
 * @param {Date} [simdi]
 * @returns {{acik: boolean, aciklama: string} | null} plan yoksa null
 */
export function suAnDurum(plan, tatiller, simdi = new Date()) {
  if (!plan) return null   // "bilmiyoruz" ile "kapalı" farklı şeyler

  const dakika = simdi.getHours() * 60 + simdi.getMinutes()
  const daki = (hhmm) => {
    const [s, d] = String(hhmm).split(':').map(Number)
    return (s * 60) + d
  }

  // ---- 1) Resmî tatil her şeyin önünde ----
  const tatil = plan.resmiTatilKapali ? bugunTatilMi(tatiller, simdi) : null
  if (tatil) {
    const bugun = gunuBul(plan, simdi)
    // Yarım günde öğleden ÖNCE açık: kanundaki tatil 13.00'te başlıyor.
    if (tatil.yarimGun && bugun?.acik && dakika < 13 * 60 && dakika >= daki(bugun.acilis)) {
      return { acik: true, aciklama: `Açık · 13:00 kapanıyor (${tatil.ad})` }
    }
    return { acik: false, aciklama: `Kapalı · ${tatil.ad}` }
  }

  // ---- 2) 7/24 ----
  if (plan.tip === MESAI_TIPLERI.surekli) {
    return { acik: true, aciklama: 'Açık · 7/24' }
  }

  // ---- 3) Bugünün planı ----
  const bugun = gunuBul(plan, simdi)
  if (bugun?.acik) {
    if (dakika < daki(bugun.acilis)) {
      return { acik: false, aciklama: `Kapalı · bugün ${bugun.acilis} açılıyor` }
    }
    if (dakika < daki(bugun.kapanis)) {
      return { acik: true, aciklama: `Açık · ${bugun.kapanis} kapanıyor` }
    }
  }

  // ---- 4) Sıradaki açılış ----
  const bugunNo = isoGunNo(simdi)
  for (let ileri = 1; ileri <= 7; ileri++) {
    const gunNo = (((bugunNo - 1 + ileri) % 7) + 1)
    const gun = (plan.gunler ?? []).find((g) => g.gun === gunNo)
    if (!gun?.acik) continue

    const ad = ileri === 1 ? 'yarın' : GUN_ADLARI[gunNo - 1]
    return { acik: false, aciklama: `Kapalı · ${ad} ${gun.acilis} açılıyor` }
  }

  return { acik: false, aciklama: 'Kapalı' }
}

/** JS'in pazar=0 sayacını ISO'ya (1 = Pazartesi) çevirir. */
function isoGunNo(tarih) {
  const g = tarih.getDay()
  return g === 0 ? 7 : g
}

function gunuBul(plan, tarih) {
  return (plan.gunler ?? []).find((g) => g.gun === isoGunNo(tarih)) ?? null
}

/**
 * Bugün resmî tatil mi? Tatil listesi backend'den geliyor.
 * @param {Array<{tarih, ad, yarimGun}>} tatiller
 * @param {Date} [tarih]
 */
export function bugunTatilMi(tatiller, tarih = new Date()) {
  // Yerel tarihi ISO'ya elle çeviriyoruz: toISOString() UTC'ye kaydırıyor ve
  // Türkiye saatiyle gece yarısından sonra bir önceki günü gösteriyor.
  const iso = [
    tarih.getFullYear(),
    String(tarih.getMonth() + 1).padStart(2, '0'),
    String(tarih.getDate()).padStart(2, '0'),
  ].join('-')

  return (tatiller ?? []).find((t) => t.tarih === iso) ?? null
}
