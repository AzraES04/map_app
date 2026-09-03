import { describe, it, expect } from 'vitest'

import { yildizlariUret } from '../yildizUretimi'

// ============================================================================
//  YILDIZ ALANI
//
//  Görsel bir şeyin testi neyi koruyabilir? Görünüşü değil — ama görünüşün
//  DAYANDIĞI kuralları koruyabilir:
//
//    1. Aynı tohum aynı gökyüzünü verir (sahne her açılışta değişmesin).
//    2. Yıldızlar alana yayılır, bir köşede kümelenmez.
//    3. Kırpışan yıldızlar AZINLIKTADIR (yoksa sahne ekran koruyucusuna döner).
//
//  Üçü de gözle bakınca "bir tuhaf" denip sebebi bulunamayacak cinsten
//  bozulmalar; sayıyla bakınca hemen görünüyorlar.
// ============================================================================

describe('yildizlariUret', () => {
  it('aynı tohum aynı gökyüzünü veriyor', () => {
    // Kullanıcı "Dünya" düğmesine ikinci kez bastığında AYNI sahneyi görmeli;
    // değişen bir arka plan "bir şey mi bozuldu?" hissi verir.
    expect(yildizlariUret({ tohum: 7 })).toEqual(yildizlariUret({ tohum: 7 }))
  })

  it('farklı tohum farklı gökyüzü veriyor', () => {
    expect(yildizlariUret({ tohum: 7 })).not.toEqual(yildizlariUret({ tohum: 8 }))
  })

  it('yıldızlar alanın içinde kalıyor', () => {
    // viewBox 0-100; dışına taşan yıldız hiç çizilmez, boşuna DOM elemanı olur.
    for (const y of yildizlariUret()) {
      expect(y.x).toBeGreaterThanOrEqual(0)
      expect(y.x).toBeLessThanOrEqual(100)
      expect(y.y).toBeGreaterThanOrEqual(0)
      expect(y.y).toBeLessThanOrEqual(100)
      expect(y.boyut).toBeGreaterThan(0)
    }
  })

  it('dört bölgenin hiçbiri boş kalmıyor', () => {
    // KATMANLI ÖRNEKLEME'nin sınavı. Düz rastgele serpiştirme kümelenir ve
    // bir çeyrek bomboş kalabilir; boş köşe "eksik çizilmiş" gibi durur.
    const yildizlar = yildizlariUret()
    const bolge = { su: 0, sa: 0, au: 0, aa: 0 }

    for (const y of yildizlar) {
      const anahtar = (y.y < 50 ? 'a' : 's') + (y.x < 50 ? 'u' : 'a')
      bolge[anahtar]++
    }

    // Dengeli dağılımda her çeyrek toplamın ~%25'i; %15 alt sınırı, bozulmayı
    // yakalayacak kadar dar, doğal dalgalanmaya takılmayacak kadar geniş.
    for (const sayi of Object.values(bolge)) {
      expect(sayi).toBeGreaterThan(yildizlar.length * 0.15)
    }
  })

  it('kırpışma azınlıkta kalıyor', () => {
    const yildizlar = yildizlariUret()
    const kirpisan = yildizlar.filter((y) => y.kirpisir).length

    // Hepsi kırpışsaydı ekran titrer, sahne "ekran koruyucu" gibi görünürdü.
    expect(kirpisan).toBeGreaterThan(0)
    expect(kirpisan).toBeLessThan(yildizlar.length * 0.4)
  })

  it('parlak yıldızlar az sayıda', () => {
    const yildizlar = yildizlariUret()
    const parlak = yildizlar.filter((y) => y.parlak).length

    // Hâle yalnızca bunlara çiziliyor (Yildizlar.jsx); çoğalırlarsa gök
    // pusluya döner ve yıldızlar tek tek seçilemez.
    expect(parlak).toBeGreaterThan(0)
    expect(parlak).toBeLessThan(yildizlar.length * 0.15)
  })

  it('kırpışma süreleri birbirinden farklı', () => {
    // Eşit olsalardı yıldızlar aynı anda sönüp yanar, ortak bir nabız duyulurdu.
    const sureler = yildizlariUret().filter((y) => y.kirpisir).map((y) => y.sure)
    expect(new Set(sureler).size).toBeGreaterThan(sureler.length * 0.9)
  })
})
