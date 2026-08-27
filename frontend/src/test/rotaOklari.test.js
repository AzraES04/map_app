import { describe, it, expect } from 'vitest'

import {
  cizgiUzunlugu, okSayisi, okOranlari, okAcisi, soluklastir, rotaOzeti,
  EN_AZ_OK, EN_COK_OK, ORNEK_ADIMI,
} from '../rotaOklari'

// ============================================================================
//  Ödev 17 — rota yön okları
//
//  Bu dosyanın varlık sebebi tek bir satır:
//
//      rotation = π/2 − atan2(dy, dx)
//
//  İki farklı konvansiyonun arasında duruyor (matematik açısı ↔ OpenLayers
//  dönüşü) ve yanlış yazılırsa HATA MESAJI ALINMAZ: oklar sessizce 90°
//  kaymış ya da ayna görüntüsü hâlinde çizilir. Haritaya bakan biri bunu
//  ancak yolu tanıyorsa fark eder — yani gözle test edilemez.
// ============================================================================

/**
 * Düz bir çizgi üzerinde orana karşılık koordinat veren fonksiyon.
 * OpenLayers'ın LineString.getCoordinateAt'ının yerini tutuyor; gerçek
 * geometri sınıfını kullanmak testi OpenLayers'a bağlardı.
 */
const duzCizgi = (baslangic, bitis) => (oran) => [
  baslangic[0] + (bitis[0] - baslangic[0]) * oran,
  baslangic[1] + (bitis[1] - baslangic[1]) * oran,
]

/** Radyanı dereceye çevirip 0–360 aralığına normalleştirir. */
const derece = (radyan) => ((radyan * 180) / Math.PI % 360 + 360) % 360

describe('okAcisi — yön dönüşümü', () => {
  // OpenLayers'ta RegularShape üçgeni YUKARI bakar ve rotation SAAT
  // YÖNÜNDE artar. Yani beklenen değerler pusula açısı gibi okunuyor:
  //   kuzey = 0°, doğu = 90°, güney = 180°, batı = 270°
  //
  // Harita koordinatlarında +y YUKARI (kuzey), +x SAĞA (doğu).

  it('doğuya giden çizgide ok DOĞUYU gösteriyor (90°)', () => {
    const aci = okAcisi(duzCizgi([0, 0], [100, 0]), 0.5)
    expect(derece(aci)).toBeCloseTo(90, 6)
  })

  it('kuzeye giden çizgide ok KUZEYİ gösteriyor (0°)', () => {
    const aci = okAcisi(duzCizgi([0, 0], [0, 100]), 0.5)
    expect(derece(aci)).toBeCloseTo(0, 6)
  })

  it('batıya giden çizgide ok BATIYI gösteriyor (270°)', () => {
    const aci = okAcisi(duzCizgi([0, 0], [-100, 0]), 0.5)
    expect(derece(aci)).toBeCloseTo(270, 6)
  })

  it('güneye giden çizgide ok GÜNEYİ gösteriyor (180°)', () => {
    const aci = okAcisi(duzCizgi([0, 0], [0, -100]), 0.5)
    expect(derece(aci)).toBeCloseTo(180, 6)
  })

  it('kuzeydoğu 45° veriyor — işaret hatası burada yakalanır', () => {
    // Dört ana yön, işaret hatalarının bir kısmını kaçırabiliyor
    // (örn. yalnızca x ekseni ters çevrilse kuzey ve güney doğru kalır).
    // Ara yön ikisini birden sınıyor.
    const aci = okAcisi(duzCizgi([0, 0], [100, 100]), 0.5)
    expect(derece(aci)).toBeCloseTo(45, 6)
  })

  it('YÖN TERSİNE dönünce açı 180° değişiyor', () => {
    // Ödevin istediği şey "yön"; çizgi ters çizilirse oklar da ters
    // bakmalı. Açı yönden bağımsız hesaplansaydı (örn. mutlak değerle)
    // bu test kırılırdı.
    const ileri = derece(okAcisi(duzCizgi([0, 0], [100, 0]), 0.5))
    const geri = derece(okAcisi(duzCizgi([100, 0], [0, 0]), 0.5))

    expect(Math.abs(ileri - geri)).toBeCloseTo(180, 6)
  })

  it('çizginin BAŞINDA ve SONUNDA da açı üretiyor', () => {
    // oran 0 ve 1'de örnekleme adımı aralığın dışına taşıyor; kod bunu
    // kırpıyor. Kırpmasaydı getCoordinateAt(-0.01) çağrılır ve
    // OpenLayers'ta tanımsız davranışa düşerdi.
    const cizgi = duzCizgi([0, 0], [100, 0])

    expect(derece(okAcisi(cizgi, 0))).toBeCloseTo(90, 6)
    expect(derece(okAcisi(cizgi, 1))).toBeCloseTo(90, 6)
  })

  it('örnekleme adımı çizginin iki YANINDAN alınıyor', () => {
    // Komşu köşe yerine adımlı örnekleme kullanmamızın sebebi buydu:
    // OSRM rotasında ardışık noktalar santimetrelerce yakın olabiliyor.
    // Bu test, fonksiyonun gerçekten iki AYRI noktayı sorduğunu doğruluyor.
    const sorulanlar = []
    const izleyici = (oran) => { sorulanlar.push(oran); return [oran * 100, 0] }

    okAcisi(izleyici, 0.5)

    expect(sorulanlar).toEqual([0.5 - ORNEK_ADIMI, 0.5 + ORNEK_ADIMI])
  })
})

describe('okSayisi — kaç ok', () => {
  it('çok kısa çizgide bile en az 3 ok var', () => {
    // Alt sınır olmasaydı kısa bir hatta hiç ok çıkmaz ve "yön gösterimi"
    // şartı o hatlarda karşılanmazdı.
    expect(okSayisi(0)).toBe(EN_AZ_OK)
    expect(okSayisi(50)).toBe(EN_AZ_OK)
  })

  it('çok uzun çizgide 24 okta duruyor', () => {
    // Üst sınır olmasaydı şehirlerarası bir hatta yüzlerce ok çizilir ve
    // çizgi okların altında kaybolurdu.
    expect(okSayisi(10_000_000)).toBe(EN_COK_OK)
  })

  it('uzunlukla birlikte artıyor', () => {
    expect(okSayisi(5000)).toBeGreaterThan(okSayisi(2000))
  })

  it('her zaman tam sayı', () => {
    for (const uzunluk of [0, 137, 900, 4321, 99_999]) {
      expect(Number.isInteger(okSayisi(uzunluk))).toBe(true)
    }
  })
})

describe('okOranlari — nereye', () => {
  it('uçlara ok KOYMUYOR', () => {
    // 0 ve 1 durakların tam üstüne denk geliyor; ok, durak simgesinin
    // altında kaybolurdu.
    const oranlar = okOranlari(4)

    expect(Math.min(...oranlar)).toBeGreaterThan(0)
    expect(Math.max(...oranlar)).toBeLessThan(1)
  })

  it('eşit aralıklı', () => {
    const oranlar = okOranlari(3)
    expect(oranlar).toEqual([0.25, 0.5, 0.75])
  })

  it('istenen sayıda ok üretiyor', () => {
    expect(okOranlari(7)).toHaveLength(7)
  })
})

describe('cizgiUzunlugu', () => {
  it('düz parçaları topluyor', () => {
    expect(cizgiUzunlugu([[0, 0], [3, 4]])).toBeCloseTo(5, 9)   // 3-4-5
    expect(cizgiUzunlugu([[0, 0], [3, 4], [3, 9]])).toBeCloseTo(10, 9)
  })

  it('tek noktalı çizgide 0', () => {
    expect(cizgiUzunlugu([[0, 0]])).toBe(0)
  })

  it('boş dizide 0', () => {
    expect(cizgiUzunlugu([])).toBe(0)
  })
})

describe('soluklastir', () => {
  it('rengi koruyup saydamlık ekliyor', () => {
    // "Rota güncel değil" durumunun görsel karşılığı: rota siliniyor DEĞİL,
    // güvenilmez olduğu söyleniyor. Rengin kendisi değişirse hangi hat
    // olduğu anlaşılmaz.
    expect(soluklastir('#d64550')).toBe('rgba(214, 69, 80, 0.42)')
  })

  it('siyah ve beyazı doğru çözüyor', () => {
    expect(soluklastir('#000000')).toBe('rgba(0, 0, 0, 0.42)')
    expect(soluklastir('#ffffff')).toBe('rgba(255, 255, 255, 0.42)')
  })
})

describe('rotaOzeti', () => {
  it('km ve dakikayı okunur biçimde veriyor', () => {
    expect(rotaOzeti({ rotaMesafeMetre: 12_400, rotaSureSaniye: 1380 }))
      .toBe('12.4 km · ~23 dk sürüş')
  })

  it('"sürüş" kelimesini MUTLAKA yazıyor', () => {
    // OSRM'in süresi duraklarda bekleme ve trafiği içermiyor. "23 dk"
    // demek onu SEFER süresi gibi okutur ve hat planlamasında yanıltıcı
    // olurdu.
    expect(rotaOzeti({ rotaMesafeMetre: 1000, rotaSureSaniye: 120 }))
      .toContain('sürüş')
  })

  it('eksik değerlerde patlamıyor', () => {
    // Rota hesaplanamamış bir hatta bu alanlar null geliyor.
    expect(rotaOzeti({})).toBe('0.0 km · ~0 dk sürüş')
    expect(rotaOzeti({ rotaMesafeMetre: null, rotaSureSaniye: null }))
      .toBe('0.0 km · ~0 dk sürüş')
  })
})
