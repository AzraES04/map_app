import { describe, it, expect } from 'vitest'

import {
  bacakTahmini,
  dakikayiSaateCevir,
  programUret,
  saatiDakikayaCevir,
} from '../turProgrami'

// ============================================================================
//  TUR PROGRAMI — gün gün, saat saat bölme
//
//  Kullanıcının sorusu "kaçta neredeyim?". Düz durak listesi bunu
//  cevaplamıyordu; çok günlü turda hangi durağın hangi güne düştüğü de
//  görünmüyordu.
//
//  Buradaki testler iki sessiz hatayı koruyor:
//    • Günün kapasitesi aşılıp bütün turun tek güne sıkışması
//    • Yeni günün, önceki günün bitiş saatinden devam etmesi (sabah 07:20'de
//      başlayan bir "2. gün" kimsenin planı değildir)
// ============================================================================

const durak = (order, ad, lon, lat, dwell) => ({
  order,
  name: ad,
  venueType: 'Museum',
  dwellMinutes: dwell,
  wkt: `POINT (${lon} ${lat})`,
})

/** Ankara merkezinde, ~1 km aralıklarla dizilmiş duraklar. */
const duraklar = (adet, dwell = 60) =>
  Array.from({ length: adet }, (_, i) =>
    durak(i + 1, `Durak ${i + 1}`, 32.85 + i * 0.012, 39.93, dwell))

describe('saat çevirimleri', () => {
  it('metin ve dakika arasında gidip geliyor', () => {
    expect(saatiDakikayaCevir('09:00')).toBe(540)
    expect(saatiDakikayaCevir('00:30')).toBe(30)
    expect(dakikayiSaateCevir(540)).toBe('09:00')
    expect(dakikayiSaateCevir(1035)).toBe('17:15')
  })

  it('geçersiz saati reddediyor', () => {
    expect(saatiDakikayaCevir('25:00')).toBeNull()
    expect(saatiDakikayaCevir('9:70')).toBeNull()
    expect(saatiDakikayaCevir('sabah')).toBeNull()
    expect(saatiDakikayaCevir(null)).toBeNull()
  })

  it('gece yarısını aşan değeri SARMIYOR', () => {
    // Sarsaydık, günlük süresi aşırı verilmiş bir program sabaha dönmüş gibi
    // görünür ve hata fark edilmezdi.
    expect(dakikayiSaateCevir(1510)).toBe('25:10')
  })
})

describe('bacak tahmini', () => {
  it('iki durak arası mesafe ve süre üretiyor', () => {
    const [a, b] = duraklar(2)
    const bacak = bacakTahmini(a, b, 'Yaya')

    expect(bacak.metre).toBeGreaterThan(1_000)
    expect(bacak.dakika).toBeGreaterThan(0)
  })

  it('konumu okunamayan durakta null', () => {
    expect(bacakTahmini({ wkt: 'bozuk' }, duraklar(1)[0], 'Yaya')).toBeNull()
  })
})

describe('günübirlik program', () => {
  it('ilk durak başlangıç saatinde başlıyor', () => {
    const program = programUret(duraklar(3, 45), {
      baslangicSaati: '09:00',
      gunlukSaat: 8,
      ulasimTipi: 'Yaya',
    })

    const gun = program.gunler[0]

    expect(program.toplamGun).toBe(1)
    expect(gun.duraklar[0].varis).toBe('09:00')
    expect(gun.duraklar[0].ayrilis).toBe('09:45')

    // Günün ilk durağında geliş süresi YOK: sabah nereden çıkılacağını
    // bilmiyoruz, uydurmuyoruz.
    expect(gun.duraklar[0].gelis).toBeNull()
    expect(gun.duraklar[1].gelis).not.toBeNull()
  })

  it('sonraki durağın varışı, yol süresi kadar sonra', () => {
    const program = programUret(duraklar(2, 30), { baslangicSaati: '10:00', gunlukSaat: 8 })
    const [ilk, ikinci] = program.gunler[0].duraklar

    const ayrilis = saatiDakikayaCevir(ilk.ayrilis)
    const varis = saatiDakikayaCevir(ikinci.varis)

    expect(varis - ayrilis).toBe(ikinci.gelis.dakika)
  })

  it('gün bitişi son durağın ayrılış saati', () => {
    const program = programUret(duraklar(3, 30), { baslangicSaati: '09:00', gunlukSaat: 8 })
    const gun = program.gunler[0]

    expect(gun.bitis).toBe(gun.duraklar.at(-1).ayrilis)
  })
})

describe('çok günlü program', () => {
  it('günlük süre dolunca yeni gün açılıyor', () => {
    // 6 durak × 90 dk kalış + yollar → 4 saatlik güne iki durak sığar.
    const program = programUret(duraklar(6, 90), {
      baslangicSaati: '09:00',
      gunlukSaat: 4,
      ulasimTipi: 'Yaya',
    })

    expect(program.toplamGun).toBeGreaterThan(1)
    program.gunler.forEach((gun) => {
      expect(gun.toplamDakika).toBeLessThanOrEqual(4 * 60)
    })
  })

  it('HER GÜN aynı saatte başlıyor', () => {
    const program = programUret(duraklar(8, 100), {
      baslangicSaati: '08:30',
      gunlukSaat: 4,
    })

    // Önceki günün bitişinden devam etseydi "2. gün 07:20'de başlar" gibi
    // kimsenin uygulamayacağı bir program çıkardı.
    program.gunler.forEach((gun) => {
      expect(gun.baslangic).toBe('08:30')
      expect(gun.duraklar[0].varis).toBe('08:30')
    })
  })

  it('gün sayısına sığmayan duraklar AYRICA bildiriliyor', () => {
    const program = programUret(duraklar(10, 120), {
      baslangicSaati: '09:00',
      gunlukSaat: 4,
      enFazlaGun: 2,
    })

    expect(program.toplamGun).toBe(2)
    expect(program.sigmayan.length).toBeGreaterThan(0)

    // Sessizce atsaydık kullanıcı, önerdiği durakların bir kısmının programda
    // olmadığını fark etmezdi.
    expect(program.sigmayan[0].name).toBeTruthy()
  })

  it('bütün duraklar ya programda ya sığmayanlarda', () => {
    const hepsi = duraklar(9, 80)
    const program = programUret(hepsi, { gunlukSaat: 5, enFazlaGun: 2 })

    const programdaki = program.gunler.flatMap((g) => g.duraklar).length
    expect(programdaki + program.sigmayan.length).toBe(hepsi.length)
  })
})

describe('günlük durak sınırı — canlıda çıkan 13+1 hatası', () => {
  // CANLIDA GÖRÜLEN: iki günlük Ankara turunun 13 durağı birinci güne
  // yığıldı, ikinci güne tek durak kaldı. Süre hesabı doğruydu — 30
  // dakikalık kalışlarla 13 durak sekiz saate gerçekten sığıyor. Yanlış
  // olan modeldi: sunucu "günde en fazla 6 durak" diyordu, bu hesap o
  // kuralı hiç bilmiyordu.

  it('gün, süre dolmadan da durak sayısıyla kapanıyor', () => {
    const program = programUret(duraklar(12, 30), {
      gunlukSaat: 8,
      enFazlaGun: 2,
      gunlukAzamiDurak: 6,
    })

    expect(program.toplamGun).toBe(2)
    expect(program.gunler.map((g) => g.duraklar.length)).toEqual([6, 6])

    // Sekiz saatlik güne 12 durak SÜRE olarak sığıyordu; sınır bu yüzden var.
    expect(program.gunler[0].toplamDakika).toBeLessThan(8 * 60)
  })

  it('duraklar günlere DENGELİ dağılıyor', () => {
    // Sırayla doldursaydık 6+6+2 çıkardı: iki dolu gün, bir yarım gün.
    const program = programUret(duraklar(14, 30), {
      gunlukSaat: 8,
      enFazlaGun: 3,
      gunlukAzamiDurak: 6,
    })

    expect(program.gunler.map((g) => g.duraklar.length)).toEqual([5, 5, 4])
  })

  it('istenen gün sayısı fazlaysa BOŞ GÜN açılmıyor', () => {
    // Kullanıcı 10 gün istedi ama bölgede 14 kayda değer mekan var. Yedi boş
    // gün göstermek yerine üç dolu gün üretiliyor; kaç gün istendiği
    // ayrıca bildiriliyor ki arayüz farkı söyleyebilsin.
    const program = programUret(duraklar(14, 30), {
      gunlukSaat: 8,
      enFazlaGun: 10,
      gunlukAzamiDurak: 6,
    })

    expect(program.toplamGun).toBe(3)
    expect(program.talepEdilenGun).toBe(10)
    expect(program.sigmayan).toHaveLength(0)
    program.gunler.forEach((gun) => expect(gun.duraklar.length).toBeGreaterThan(0))
  })

  it('hiçbir gün boş kalmıyor', () => {
    // Boş bir gün, "2. gün: hiçbir şey" diye görünürdü — kullanıcının
    // bildirdiği asıl şikâyet buydu.
    for (const durakSayisi of [2, 3, 5, 7, 9, 13, 20]) {
      const program = programUret(duraklar(durakSayisi, 30), {
        gunlukSaat: 8,
        enFazlaGun: 5,
        gunlukAzamiDurak: 6,
      })

      program.gunler.forEach((gun) => {
        expect(gun.duraklar.length).toBeGreaterThan(0)
      })
    }
  })

  it('süre yüzünden erken kapanan gün sonraki günlere yük bindirmiyor', () => {
    // 120 dakikalık kalışlarla 5 saatlik güne iki durak sığıyor: sınır süre.
    // Durak hedefi her gün yeniden hesaplandığı için dağılım yine dengeli.
    const program = programUret(duraklar(8, 120), {
      gunlukSaat: 5,
      enFazlaGun: 4,
      gunlukAzamiDurak: 6,
    })

    program.gunler.forEach((gun) => {
      expect(gun.toplamDakika).toBeLessThanOrEqual(5 * 60)
      expect(gun.duraklar.length).toBeGreaterThan(0)
    })
    expect(program.sigmayan).toHaveLength(0)
  })

  it('sınır verilmezse eski davranış korunuyor (yalnızca süre)', () => {
    // Kaydedilmiş turlarda öneri cevabı elimizde olmayabiliyor.
    const program = programUret(duraklar(12, 30), { gunlukSaat: 8, enFazlaGun: 2 })

    expect(program.gunler[0].duraklar.length).toBeGreaterThan(6)
  })
})

describe('sınır durumlar', () => {
  it('boş durak listesi boş program veriyor', () => {
    const program = programUret([], {})
    expect(program.gunler).toEqual([])
    expect(program.toplamGun).toBe(0)
  })

  it('geçersiz başlangıç saati varsayılana düşüyor', () => {
    const program = programUret(duraklar(2, 30), { baslangicSaati: 'sabah' })
    expect(program.gunler[0].baslangic).toBe('09:00')
  })

  it('duraklar sıraya göre işleniyor', () => {
    const karisik = [durak(3, 'Üç', 32.87, 39.93, 30), durak(1, 'Bir', 32.85, 39.93, 30)]
    const program = programUret(karisik, { gunlukSaat: 8 })

    expect(program.gunler[0].duraklar.map((d) => d.name)).toEqual(['Bir', 'Üç'])
  })
})
