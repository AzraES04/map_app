import { describe, it, expect } from 'vitest'

import {
  cizgiCoz,
  yolTablosu,
  oranKonumu,
  durakOranlari,
  simulasyonDurumu,
  simulasyonHazirla,
} from '../turSimulasyonu'

// ============================================================================
//  TUR SİMÜLASYONU
//
//  Oynatmanın kendisi bir animasyon; testlenen şey ARKASINDAKİ HESAP:
//  "rotanın %40'ında neredeyiz, hangi durağı geçtik, hangisine gidiyoruz".
//  Bu hesap bozulursa araç haritada yanlış yerde görünür ve kimse sebebini
//  animasyonda aramaz.
// ============================================================================

/** Doğu-batı doğrultusunda düz bir rota: oran hesabı elle doğrulanabilsin. */
const DUZ_ROTA = 'LINESTRING (32.80 39.90, 32.90 39.90)'

describe('cizgiCoz', () => {
  it('WKT çizgiyi koordinatlara çeviriyor', () => {
    expect(cizgiCoz(DUZ_ROTA)).toEqual([
      { lon: 32.8, lat: 39.9 },
      { lon: 32.9, lat: 39.9 },
    ])
  })

  it('bozuk girdide boş dizi veriyor', () => {
    // Rota yoksa çağıran taraf oynat düğmesini hiç göstermiyor; burada
    // patlasaydı tur paneli komple çizilemezdi.
    for (const bozuk of [null, undefined, '', 'POINT (32 39)', 'LINESTRING ()']) {
      expect(cizgiCoz(bozuk)).toEqual([])
    }
  })
})

describe('oranKonumu', () => {
  const tablo = yolTablosu(cizgiCoz(DUZ_ROTA))

  it('uçlar tam olarak uç noktalar', () => {
    expect(oranKonumu(tablo, 0).lon).toBeCloseTo(32.8, 6)
    expect(oranKonumu(tablo, 1).lon).toBeCloseTo(32.9, 6)
  })

  it('ortada ara nokta üretiyor', () => {
    // Köşeden köşeye zıplasaydı hareket kesik kesik görünürdü.
    expect(oranKonumu(tablo, 0.5).lon).toBeCloseTo(32.85, 4)
  })

  it('sınır dışı oranlar kırpılıyor', () => {
    expect(oranKonumu(tablo, -3).lon).toBeCloseTo(32.8, 6)
    expect(oranKonumu(tablo, 9).lon).toBeCloseTo(32.9, 6)
  })

  it('çok köşeli rotada da doğru parçayı buluyor', () => {
    // İkili aramanın sınavı: 100 parçalı bir çizgide %25.
    const noktalar = Array.from({ length: 101 }, (_, i) => ({
      lon: 32.8 + i * 0.001,
      lat: 39.9,
    }))
    const cok = yolTablosu(noktalar)

    expect(oranKonumu(cok, 0.25).lon).toBeCloseTo(32.825, 4)
  })
})

describe('durakOranlari', () => {
  const tablo = yolTablosu(cizgiCoz(DUZ_ROTA))

  it('durakları rota üzerindeki en yakın noktaya oturtuyor', () => {
    // Duraklar rotanın köşeleriyle aynı koordinatta değil: mekan yolun
    // birkaç metre kenarında duruyor.
    const oranlar = durakOranlari(tablo, [
      { order: 1, name: 'Başta', wkt: 'POINT (32.801 39.9001)' },
      { order: 2, name: 'Ortada', wkt: 'POINT (32.850 39.9002)' },
      { order: 3, name: 'Sonda', wkt: 'POINT (32.899 39.9001)' },
    ])

    expect(oranlar.map((o) => o.name)).toEqual(['Başta', 'Ortada', 'Sonda'])
    expect(oranlar[1].oran).toBeGreaterThan(0.4)
    expect(oranlar[1].oran).toBeLessThan(0.6)
  })

  it('oranlar ASLA geriye gitmiyor', () => {
    // Rota bir sokakta geri dönüyorsa en yakın nokta önceki durağın
    // gerisine düşebiliyordu; sonuç "3. durak 2. duraktan önce" oluyordu.
    const oranlar = durakOranlari(tablo, [
      { order: 1, name: 'İleride', wkt: 'POINT (32.88 39.90)' },
      { order: 2, name: 'Geride', wkt: 'POINT (32.81 39.90)' },
    ])

    expect(oranlar[1].oran).toBeGreaterThanOrEqual(oranlar[0].oran)
  })
})

describe('simulasyonDurumu', () => {
  const tablo = yolTablosu(cizgiCoz(DUZ_ROTA))
  const oranlar = durakOranlari(tablo, [
    { order: 1, name: 'Bir', wkt: 'POINT (32.80 39.90)' },
    { order: 2, name: 'İki', wkt: 'POINT (32.85 39.90)' },
    { order: 3, name: 'Üç', wkt: 'POINT (32.90 39.90)' },
  ])

  it('geçilen ve yaklaşılan durağı söylüyor', () => {
    const durum = simulasyonDurumu(tablo, oranlar, 0.7)

    expect(durum.oncekiDurak.name).toBe('İki')
    expect(durum.sonrakiDurak.name).toBe('Üç')
    expect(durum.yuzde).toBe(70)
  })

  it('başlangıçta ilk durak HEDEF, geçilmiş durak da o', () => {
    const durum = simulasyonDurumu(tablo, oranlar, 0)

    expect(durum.oncekiDurak.name).toBe('Bir')
    expect(durum.yuzde).toBe(0)
  })

  it('sonda sıradaki durak YOK', () => {
    const durum = simulasyonDurumu(tablo, oranlar, 1)

    expect(durum.sonrakiDurak).toBeNull()
    expect(durum.yuzde).toBe(100)
  })
})

describe('simulasyonHazirla', () => {
  it('rotası olan turdan oynatılabilir simülasyon çıkarıyor', () => {
    const sim = simulasyonHazirla({
      routeWkt: DUZ_ROTA,
      waypoints: [
        { order: 1, name: 'Bir', wkt: 'POINT (32.80 39.90)' },
        { order: 2, name: 'İki', wkt: 'POINT (32.90 39.90)' },
      ],
    })

    expect(sim).not.toBeNull()
    expect(sim.tablo.toplamMetre).toBeGreaterThan(0)
    expect(sim.oranlar).toHaveLength(2)
  })

  it('rotası olmayan tur oynatılamıyor', () => {
    // Duraklar arasına düz çizgi uydurmak, kullanıcıya var olmayan bir yol
    // göstermek olurdu — o yüzden null dönüp düğme hiç çizilmiyor.
    expect(simulasyonHazirla({ routeWkt: null, waypoints: [] })).toBeNull()
    expect(simulasyonHazirla({ routeWkt: 'LINESTRING (32.8 39.9)' })).toBeNull()
    expect(simulasyonHazirla(null)).toBeNull()
  })
})
