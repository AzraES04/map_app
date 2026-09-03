import { describe, it, expect } from 'vitest'

import {
  dakikaMetni,
  durakKalanDakika,
  durakSirasi,
  durakYuzdesi,
  gecenDakika,
  kalanTurDakika,
  mesafeMetni,
  mesafeMetre,
  mevcutDurak,
  noktaCoz,
  seyahatDakika,
  sonrakiDurak,
  sonrakiDurakTahmini,
} from '../turIlerleme'

// ============================================================================
//  Canlı turun ilerleme hesapları
//
//  Ekrandaki her sayı buradan geliyor ve hiçbiri "yanlışsa hata verir"
//  cinsinden değil: yanlış hesaplanan bir kalan süre, sadece yanlış bir
//  sayı olarak görünür. Bu yüzden kuralların kendisi burada sınanıyor.
// ============================================================================

const durak = (id, order, ad, lon, lat, dwell = 30) => ({
  id,
  tourId: 1,
  order,
  name: ad,
  venueType: 'Museum',
  dwellMinutes: dwell,
  wkt: `POINT (${lon} ${lat})`,
})

/** Üç duraklı tur; duraklar ~1 km aralıklarla doğuya diziliyor. */
const TUR = {
  id: 1,
  name: 'Ankara Turu',
  color: '#7b5cd6',
  waypoints: [
    durak(10, 1, 'Anıtkabir', 32.836, 39.925, 45),
    durak(11, 2, 'Ulus', 32.848, 39.925, 30),
    durak(12, 3, 'Kale', 32.860, 39.925, 20),
  ],
}

const oturum = (ek = {}) => ({
  id: 7,
  tourId: 1,
  status: 'Live',
  currentWaypointId: 10,
  currentWaypointOrder: 1,
  myRole: 'Participant',
  participants: [],
  ...ek,
})

describe('noktaCoz', () => {
  it('WKT POINT içindeki boylam/enlemi doğru sırayla okur', () => {
    // WKT "boylam enlem" sırasında yazılır; ters okumak konumu başka bir
    // kıtaya taşırdı.
    expect(noktaCoz('POINT (32.836 39.925)')).toEqual({ lat: 39.925, lon: 32.836 })
    expect(noktaCoz('POINT(-3.7 40.4)')).toEqual({ lat: 40.4, lon: -3.7 })
  })

  it('tanımadığı biçimde null döner', () => {
    expect(noktaCoz('LINESTRING (1 2, 3 4)')).toBeNull()
    expect(noktaCoz('')).toBeNull()
    expect(noktaCoz(null)).toBeNull()
  })
})

describe('mesafe ve süre', () => {
  it('haversine ile makul bir mesafe verir', () => {
    // 32.836 → 32.848 boylam farkı, 39.9 enleminde ≈ 1.0 km.
    const metre = mesafeMetre({ lat: 39.925, lon: 32.836 }, { lat: 39.925, lon: 32.848 })
    expect(metre).toBeGreaterThan(900)
    expect(metre).toBeLessThan(1_100)
  })

  it('seyahat süresi ulaşım tipine göre değişir', () => {
    const yaya = seyahatDakika(3_000, 'Yaya')
    const arac = seyahatDakika(3_000, 'Arac')

    expect(yaya).toBeGreaterThan(arac)
    expect(seyahatDakika(0, 'Yaya')).toBe(0)
    // En az 1 dakika: "0 dk" yazan bir bacak, varılmış gibi okunurdu.
    expect(seyahatDakika(50, 'Yaya')).toBe(1)
  })

  it('mesafe ve süre metinleri okunur biçimde', () => {
    expect(mesafeMetni(850)).toBe('850 m')
    expect(mesafeMetni(2_400)).toBe('2,4 km')
    expect(mesafeMetni(undefined)).toBe('—')

    expect(dakikaMetni(45)).toBe('45 dk')
    expect(dakikaMetni(95)).toBe('1 sa 35 dk')
    expect(dakikaMetni(120)).toBe('2 sa')
    expect(dakikaMetni(0)).toBe('0 dk')
  })
})

describe('mevcut ve sıradaki durak', () => {
  it('mevcut durağı id ile bulur', () => {
    expect(mevcutDurak(TUR, oturum({ currentWaypointId: 11 })).name).toBe('Ulus')
    expect(durakSirasi(TUR, oturum({ currentWaypointId: 11 }))).toBe(2)
  })

  it('id yoksa sıra numarasına düşer', () => {
    const o = oturum({ currentWaypointId: null, currentWaypointOrder: 3 })
    expect(mevcutDurak(TUR, o).name).toBe('Kale')
  })

  it('henüz varılmadıysa sıradaki durak İLK duraktır', () => {
    const o = oturum({ currentWaypointId: null, currentWaypointOrder: null })
    expect(mevcutDurak(TUR, o)).toBeNull()
    expect(sonrakiDurak(TUR, o).name).toBe('Anıtkabir')
    expect(durakSirasi(TUR, o)).toBe(0)
  })

  it('son durakta sıradaki durak yoktur', () => {
    expect(sonrakiDurak(TUR, oturum({ currentWaypointId: 12 }))).toBeNull()
  })

  it('durak yüzdesi sıraya göre hesaplanır', () => {
    expect(durakYuzdesi(TUR, oturum({ currentWaypointId: 10 }))).toBe(33)
    expect(durakYuzdesi(TUR, oturum({ currentWaypointId: 12 }))).toBe(100)
  })
})

describe('sıradaki durağa tahmin', () => {
  it('mesafe ve süre üretir', () => {
    const tahmin = sonrakiDurakTahmini(TUR, oturum({ currentWaypointId: 10 }), 'Yaya')

    // Kuş uçuşu ~1 km, yol katsayısı 1.3 → ~1.3 km.
    expect(tahmin.metre).toBeGreaterThan(1_100)
    expect(tahmin.metre).toBeLessThan(1_500)
    expect(tahmin.dakika).toBeGreaterThan(0)
  })

  it('son bilinen konum varsa onu başlangıç alır', () => {
    // Grup neredeyse Ulus'a varmış: mesafe, durak-durak hesabından KÜÇÜK olmalı.
    const yoldaki = sonrakiDurakTahmini(
      TUR,
      oturum({ currentWaypointId: 10, lastLat: 39.925, lastLon: 32.847 }),
      'Yaya',
    )
    const duraktan = sonrakiDurakTahmini(TUR, oturum({ currentWaypointId: 10 }), 'Yaya')

    expect(yoldaki.metre).toBeLessThan(duraktan.metre)
  })

  it('son durakta tahmin yoktur', () => {
    expect(sonrakiDurakTahmini(TUR, oturum({ currentWaypointId: 12 }), 'Yaya')).toBeNull()
  })
})

describe('süre sayaçları', () => {
  it('durakta kalan süre varış anından geriye sayar', () => {
    const simdi = new Date('2026-09-01T10:20:00Z')
    const o = oturum({
      currentWaypointId: 10,                              // 45 dakikalık durak
      currentWaypointArrivedUtc: '2026-09-01T10:00:00Z',  // 20 dakika önce
    })

    expect(durakKalanDakika(TUR, o, simdi)).toBe(25)
  })

  it('süre dolduğunda eksiye düşmez', () => {
    const simdi = new Date('2026-09-01T12:00:00Z')
    const o = oturum({
      currentWaypointId: 10,
      currentWaypointArrivedUtc: '2026-09-01T10:00:00Z',
    })

    expect(durakKalanDakika(TUR, o, simdi)).toBe(0)
  })

  it('varış anı bilinmiyorsa planlanan süreyi döner', () => {
    expect(durakKalanDakika(TUR, oturum({ currentWaypointId: 11 }))).toBe(30)
  })

  it('kalan tur süresi ilerledikçe azalır', () => {
    const simdi = new Date('2026-09-01T10:00:00Z')

    const bastaki = kalanTurDakika(TUR, oturum({ currentWaypointId: 10 }), 'Yaya', simdi)
    const ortadaki = kalanTurDakika(TUR, oturum({ currentWaypointId: 11 }), 'Yaya', simdi)
    const sondaki = kalanTurDakika(TUR, oturum({ currentWaypointId: 12 }), 'Yaya', simdi)

    expect(bastaki).toBeGreaterThan(ortadaki)
    expect(ortadaki).toBeGreaterThan(sondaki)

    // Son durakta kalan süre = o durağın kalış süresi (20 dk), yol yok.
    expect(sondaki).toBe(20)
  })

  it('kalan süre PLANDAN değil KALAN İŞTEN hesaplanır', () => {
    // Aynı durakta iki farklı varış anı: geç kalan grupta kalan süre daha az
    // çünkü durağın kalış süresinin bir kısmı tükenmiş.
    const simdi = new Date('2026-09-01T10:30:00Z')

    const yeniVaran = kalanTurDakika(
      TUR, oturum({ currentWaypointId: 10, currentWaypointArrivedUtc: '2026-09-01T10:25:00Z' }),
      'Yaya', simdi,
    )
    const oyalanan = kalanTurDakika(
      TUR, oturum({ currentWaypointId: 10, currentWaypointArrivedUtc: '2026-09-01T10:00:00Z' }),
      'Yaya', simdi,
    )

    expect(oyalanan).toBeLessThan(yeniVaran)
  })

  it('geçen süre başlangıçtan itibaren sayılır', () => {
    const simdi = new Date('2026-09-01T11:30:00Z')
    expect(gecenDakika(oturum({ startedUtc: '2026-09-01T10:00:00Z' }), simdi)).toBe(90)
    expect(gecenDakika(oturum({ startedUtc: null }), simdi)).toBe(0)
  })
})
