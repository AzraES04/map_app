import { describe, it, expect } from 'vitest'

import {
  izdusum, gorunurMu, kitaGorunurMu, yol, MERKEZ, ORTA, YARICAP,
} from '../ortografik'
import {
  KITALAR, AVRASYA, AFRIKA, AVUSTRALYA, KUZEY_AMERIKA, GUNEY_AMERIKA,
} from '../dunyaKitalari'

// ============================================================================
//  Ortografik izdüşüm — açılış sahnesindeki gezegen
//
//  ---- NEDEN TEST? ----
//
//  Bu formül sessizce yanlış olabilecek cinsten:
//    • bir işaret hatası dünyayı AYNADA gösterir,
//    • y'yi ters çevirmeyi unutmak KUZEYİ AŞAĞI çevirir,
//    • merkez kaydırması yanlış eksende olursa Afrika kadrajdan çıkar.
//  Hiçbiri hata mesajı üretmiyor. Sahne iki buçuk saniye sürüyor; yanlış bir
//  dünya günlük kullanımda gözden kaçabilir ama jüri önünde kaçmaz.
//
//  Testler "şu piksel şu renk" demiyor — COĞRAFİ İLİŞKİLERİ sınıyor:
//  kuzey yukarıda mı, doğu sağda mı, Avustralya Türkiye'nin güneydoğusunda mı.
//  Bunlar tasarım değişse de doğru kalması gereken şeyler.
// ============================================================================

/** Bilinen yerler — testleri okunur kılmak için. */
const YER = {
  merkez: [MERKEZ.lon, MERKEZ.lat],
  ankara: [32.9, 39.9],
  kahire: [31.2, 30.0],
  capeTown: [18.4, -33.9],
  londra: [-0.1, 51.5],
  tokyo: [139.7, 35.7],
  sydney: [151.2, -33.9],
  antipot: [MERKEZ.lon + 180, -MERKEZ.lat],
}

/** Kürenin merkezinden uzaklık (0 = tam orta, 50 = ufuk). */
const uzaklik = ([x, y]) => Math.hypot(x - ORTA, y - ORTA)

describe('temel geometri', () => {
  it('merkez nokta tam ortaya düşüyor', () => {
    const [x, y] = izdusum(YER.merkez)

    expect(x).toBeCloseTo(ORTA, 6)
    expect(y).toBeCloseTo(ORTA, 6)
  })

  it('hiçbir nokta kürenin DIŞINA taşmıyor', () => {
    // Ortografik izdüşümde |(x,y)| ≤ 1; taşan bir nokta formülün bozulduğunu
    // gösterir ve SVG'de kırpıldığı için gözle fark edilmezdi.
    for (const kita of KITALAR) {
      for (const nokta of kita) {
        expect(uzaklik(izdusum(nokta))).toBeLessThanOrEqual(YARICAP + 1e-9)
      }
    }
  })

  it('arka yüzdeki nokta UFKA sabitleniyor, atılmıyor', () => {
    // Merkezin tam karşısı (antipot) görünmez. Atsaydık ufku aşan kıtalar
    // yarısı kesilmiş ve çokgeni bozulmuş hâlde çizilirdi.
    expect(gorunurMu(YER.antipot)).toBe(false)
    expect(uzaklik(izdusum(YER.antipot))).toBeCloseTo(YARICAP, 6)
  })
})

describe('yön doğruluğu — asıl sınav', () => {
  it('KUZEY yukarıda', () => {
    // y ters çevrilmeseydi dünya baş aşağı çizilirdi ve hiçbir hata alınmazdı.
    const kuzey = izdusum([MERKEZ.lon, MERKEZ.lat + 20])
    const guney = izdusum([MERKEZ.lon, MERKEZ.lat - 20])

    expect(kuzey[1]).toBeLessThan(ORTA)      // SVG'de küçük y = yukarı
    expect(guney[1]).toBeGreaterThan(ORTA)
  })

  it('DOĞU sağda', () => {
    const dogu = izdusum([MERKEZ.lon + 30, MERKEZ.lat])
    const bati = izdusum([MERKEZ.lon - 30, MERKEZ.lat])

    expect(dogu[0]).toBeGreaterThan(ORTA)
    expect(bati[0]).toBeLessThan(ORTA)
  })

  it('Ankara merkezin SAĞ ÜSTÜNDE — inişin gideceği yer', () => {
    const [x, y] = izdusum(YER.ankara)

    expect(x).toBeGreaterThan(ORTA)
    expect(y).toBeLessThan(ORTA)
  })

  it('Cape Town Kahire\'nin GÜNEYİNDE', () => {
    // İki nokta neredeyse aynı boylamda; fark yalnızca enlemde.
    expect(izdusum(YER.capeTown)[1]).toBeGreaterThan(izdusum(YER.kahire)[1])
  })

  it('Londra Ankara\'nın KUZEYBATISINDA', () => {
    const londra = izdusum(YER.londra)
    const ankara = izdusum(YER.ankara)

    expect(londra[0]).toBeLessThan(ankara[0])
    expect(londra[1]).toBeLessThan(ankara[1])
  })

  it('Sydney Tokyo\'nun GÜNEYİNDE', () => {
    expect(izdusum(YER.sydney)[1]).toBeGreaterThan(izdusum(YER.tokyo)[1])
  })
})

describe('kadraj — hangi kıta görünüyor', () => {
  it('Avrupa, Afrika ve Anadolu GÖRÜNÜR yüzde', () => {
    // Kameranın Türkiye'ye inmesi ancak bu yüz bize dönükse tutarlı okunur.
    expect(gorunurMu(YER.ankara)).toBe(true)
    expect(gorunurMu(YER.kahire)).toBe(true)
    expect(gorunurMu(YER.londra)).toBe(true)
    expect(gorunurMu(YER.capeTown)).toBe(true)
  })

  it('Afrika\'nın ÇOĞU görünüyor', () => {
    const gorunen = AFRIKA.filter(gorunurMu).length

    // Kıta kadrajın ortasında; yarısından azı görünüyorsa merkez yanlış.
    expect(gorunen / AFRIKA.length).toBeGreaterThan(0.9)
  })

  it('Avrasya kadrajın büyük kısmını dolduruyor', () => {
    const gorunen = AVRASYA.filter(gorunurMu).length

    // Sibirya ve Uzak Doğu ufkun ötesinde kalıyor — tamamı beklenmiyor.
    expect(gorunen / AVRASYA.length).toBeGreaterThan(0.5)
  })

  it('Amerika kıtaları KISMEN ufukta — doğru kadrajın işareti', () => {
    // Tamamen görünselerdi merkez fazla batıda, hiç görünmeselerdi fazla
    // doğuda demekti. Kısmi görünürlük, Atlantik kenarının kadrajda
    // olduğunu söylüyor.
    for (const kita of [KUZEY_AMERIKA, GUNEY_AMERIKA]) {
      const gorunen = kita.filter(gorunurMu).length

      expect(gorunen).toBeGreaterThan(0)
      expect(gorunen).toBeLessThan(kita.length)
    }
  })

  it('TAMAMEN arkadaki kıtalar hiç ÇİZİLMİYOR', () => {
    // Avustralya, Japonya ve Yeni Gine bu merkezden görünmüyor. Çizilselerdi
    // bütün noktaları ufka sabitlendiği için kürenin kenarında ince bir
    // yeşil yaya dönüşürlerdi — coğrafi karşılığı olmayan bir çizim artığı.
    expect(kitaGorunurMu(AVUSTRALYA)).toBe(false)

    const cizilen = KITALAR.filter(kitaGorunurMu)
    expect(cizilen).not.toContain(AVUSTRALYA)
    expect(cizilen.length).toBeLessThan(KITALAR.length)

    // Ama görünen kıtalar elenmemiş olmalı: filtre fazla agresifse dünya
    // boşalırdı.
    expect(cizilen).toContain(AVRASYA)
    expect(cizilen).toContain(AFRIKA)
  })
})

describe('kıta verisi', () => {
  it('her kıta kapalı bir çokgen kurabilecek kadar nokta taşıyor', () => {
    for (const kita of KITALAR) {
      expect(kita.length).toBeGreaterThanOrEqual(5)
    }
  })

  it('koordinatlar geçerli aralıkta', () => {
    for (const kita of KITALAR) {
      for (const [lon, lat] of kita) {
        expect(lon).toBeGreaterThanOrEqual(-180)
        expect(lon).toBeLessThanOrEqual(180)
        expect(lat).toBeGreaterThanOrEqual(-90)
        expect(lat).toBeLessThanOrEqual(90)
      }
    }
  })

  it('Avrasya en büyük kütle', () => {
    // Sıralama bozulursa (örn. diziler karışırsa) şekiller birbirinin yerine
    // geçer ve dünya tanınmaz hâle gelir.
    for (const kita of KITALAR) {
      if (kita !== AVRASYA) expect(kita.length).toBeLessThanOrEqual(AVRASYA.length)
    }
  })
})

describe('yol üretimi', () => {
  it('kapalı bir SVG yolu veriyor', () => {
    const d = yol(AFRIKA)

    expect(d.startsWith('M')).toBe(true)
    expect(d.endsWith('Z')).toBe(true)
    expect((d.match(/L/g) ?? []).length).toBe(AFRIKA.length - 1)
  })

  it('NaN üretmiyor', () => {
    // Tek bir NaN bütün yolu geçersiz kılar ve o kıta SESSİZCE hiç çizilmez.
    for (const kita of KITALAR) {
      expect(yol(kita)).not.toContain('NaN')
    }
  })
})
