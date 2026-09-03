import { describe, it, expect } from 'vitest'

import {
  BESLENME_KISITLARI,
  SINIRLAR,
  TUR_TEMALARI,
  ULASIM_TIPLERI,
  bosTurFormu,
  rotaIstegiHazirla,
  sureMetni,
  toplamDakika,
  turFormunuDogrula,
  turPayloadu,
} from '../turPlani'
import { MEKAN_TIPLERI } from '../turDurumu'

// ============================================================================
//  TourBuilder'ın kuralları — saf sınav
//
//  Bileşeni hiç çizmeden sınanıyor: kurallar React'e bağlı değil. Buradaki
//  testlerin çoğu SESSİZ hataları koruyor — bölge değişince eski şehrin
//  seçili kalması, gün sayısının 24 saatle çarpılması, boş ilçenin sunucuya
//  "" olarak gitmesi. Hepsi ekranda doğru görünüp yanlış rota üretecek
//  cinsten hatalar.
// ============================================================================

const ILLER = [
  { plaka: 6, ad: 'Ankara', bolge: 'İç Anadolu' },
  { plaka: 34, ad: 'İstanbul', bolge: 'Marmara' },
  { plaka: 35, ad: 'İzmir', bolge: 'Ege' },
]

/** Geçerli bir form — testler bunun üstüne tek alan değiştirerek yazılıyor. */
const gecerliForm = (ek = {}) => ({ ...bosTurFormu(), ilPlaka: 6, ...ek })

const dogrula = (ek) => turFormunuDogrula(gecerliForm(ek), { iller: ILLER })

describe('kataloglar', () => {
  it('ödevde istenen üç ulaşım tipini taşır', () => {
    expect(ULASIM_TIPLERI.map((u) => u.deger)).toEqual(['Yaya', 'Arac', 'TopluTasima'])
  })

  it('beslenme kısıtları tek seçimlik ve "Yok" seçeneği var', () => {
    expect(BESLENME_KISITLARI.map((b) => b.deger)).toEqual(['Yok', 'Vejetaryen', 'Vegan'])
    expect(BESLENME_KISITLARI[0].poiEtiketleri).toEqual([])
  })

  it('her temanın mekan tipleri sunucudaki VenueType değerlerinden seçilmiş', () => {
    // Tema açılımı payload'a giriyor ve sunucunun POI süzgecine veriliyor;
    // uydurma bir tip sessizce hiçbir POI eşleştirmezdi.
    const gecerliTipler = new Set(MEKAN_TIPLERI.map((t) => t.deger))

    for (const tema of TUR_TEMALARI) {
      expect(tema.mekanTipleri.length).toBeGreaterThan(0)
      for (const tip of tema.mekanTipleri) {
        expect(gecerliTipler.has(tip), `${tema.deger} → ${tip}`).toBe(true)
      }
    }
  })
})

describe('süre hesabı', () => {
  it('günübirlik süreyi saat cinsinden dakikaya çevirir', () => {
    expect(toplamDakika(gecerliForm({ sureBirimi: 'Saat', sureDegeri: 4 }))).toBe(240)
  })

  it('çok günlük turda GÜNLÜK GEZİ SAATİYLE çarpar (24 ile değil)', () => {
    // 3 gün × 8 saat = 24 saat. 24 saatle çarpmak geceleri de gezilen bir
    // rota önerirdi — bu testin varlık sebebi tam olarak bu.
    const dakika = toplamDakika(gecerliForm({ sureBirimi: 'Gun', sureDegeri: 3, gunlukSaat: 8 }))
    expect(dakika).toBe(3 * 8 * 60)
  })

  it('süreyi okunur metne çevirir', () => {
    expect(sureMetni(240)).toBe('4 sa')
    expect(sureMetni(390)).toBe('6 sa 30 dk')
    expect(sureMetni(45)).toBe('45 dk')
  })
})

describe('doğrulama — lokasyon', () => {
  it('şehir seçilmeden form geçersiz', () => {
    const { gecerli, hatalar } = dogrula({ ilPlaka: 0 })
    expect(gecerli).toBe(false)
    expect(hatalar.ilPlaka).toMatch(/Şehir/)
  })

  it('şehir bölgeyle çelişiyorsa reddeder', () => {
    // Bölge süzgeci değişince eski şehir seçili kalabiliyor; sessizce
    // göndermek kullanıcının gördüğü bölgeyle turun bölgesini ayırırdı.
    const { gecerli, hatalar } = dogrula({ bolge: 'Ege', ilPlaka: 6 })
    expect(gecerli).toBe(false)
    expect(hatalar.ilPlaka).toContain('Ege')
  })

  it('bölge boşken her şehir kabul edilir', () => {
    expect(dogrula({ bolge: '', ilPlaka: 34 }).gecerli).toBe(true)
  })

  it('il listesi henüz yüklenmediyse plakayı listede aramaz', () => {
    // Liste gelmeden formu "geçersiz" saymak, açılışta düğmeyi sebepsiz
    // kapatırdı.
    expect(turFormunuDogrula(gecerliForm({ ilPlaka: 6 }), {}).gecerli).toBe(true)
  })

  it('ilçe isteğe bağlı ama uzunluk sınırı var', () => {
    expect(dogrula({ ilce: '' }).gecerli).toBe(true)
    expect(dogrula({ ilce: 'Çankaya' }).gecerli).toBe(true)
    expect(dogrula({ ilce: 'x'.repeat(SINIRLAR.ilceEnCokKarakter + 1) }).hatalar.ilce)
      .toMatch(/karakter/)
  })
})

describe('doğrulama — süre', () => {
  it('günübirlik turda saat sınırlarını uygular', () => {
    expect(dogrula({ sureBirimi: 'Saat', sureDegeri: 0 }).gecerli).toBe(false)
    expect(dogrula({ sureBirimi: 'Saat', sureDegeri: 13 }).gecerli).toBe(false)
    expect(dogrula({ sureBirimi: 'Saat', sureDegeri: 12 }).gecerli).toBe(true)
  })

  it('çok günlük turda gün sınırlarını uygular', () => {
    expect(dogrula({ sureBirimi: 'Gun', sureDegeri: 0 }).gecerli).toBe(false)
    expect(dogrula({ sureBirimi: 'Gun', sureDegeri: 15 }).gecerli).toBe(false)
    expect(dogrula({ sureBirimi: 'Gun', sureDegeri: 3 }).gecerli).toBe(true)
  })

  it('ondalık süreyi reddeder', () => {
    expect(dogrula({ sureDegeri: 2.5 }).hatalar.sureDegeri).toMatch(/tam sayı/)
  })

  it('boş bırakılan süreyi reddeder', () => {
    // Girdi temizlenince değer '' oluyor; Number('') === 0 olduğu için
    // sayı kontrolü tek başına yetmezdi.
    expect(dogrula({ sureDegeri: '' }).gecerli).toBe(false)
  })

  it('günlük gezi süresini yalnızca çok günlük turda kontrol eder', () => {
    expect(dogrula({ sureBirimi: 'Saat', gunlukSaat: 99 }).gecerli).toBe(true)
    expect(dogrula({ sureBirimi: 'Gun', sureDegeri: 2, gunlukSaat: 99 }).gecerli).toBe(false)
  })

  it('kısa turu ENGELLEMEZ, uyarır', () => {
    const { gecerli, uyarilar } = dogrula({ sureBirimi: 'Saat', sureDegeri: 1 })
    expect(gecerli).toBe(true)
    expect(uyarilar.join(' ')).toMatch(/1-2 durak/)
  })

  it('yaya turda uzun günlük süreyi uyarı olarak bildirir', () => {
    const { gecerli, uyarilar } = dogrula({
      ulasimTipi: 'Yaya', sureBirimi: 'Gun', sureDegeri: 2, gunlukSaat: 10,
    })
    expect(gecerli).toBe(true)
    expect(uyarilar.join(' ')).toMatch(/yürüyüş/)
  })
})

describe('doğrulama — katalog dışı değerler', () => {
  it('tanınmayan ulaşım tipi, tema ve beslenme kısıtını reddeder', () => {
    expect(dogrula({ ulasimTipi: 'Helikopter' }).hatalar.ulasimTipi).toBeTruthy()
    expect(dogrula({ tema: 'Uzay' }).hatalar.tema).toBeTruthy()
    expect(dogrula({ beslenme: 'Etçil' }).hatalar.beslenme).toBeTruthy()
  })

  it('birden çok hatayı AYNI ANDA döner', () => {
    // Tek mesaj döndürseydik kullanıcı hataları birer birer görürdü.
    const { hatalar, ilkHata } = dogrula({ ilPlaka: 0, sureDegeri: 99, tema: 'Uzay' })
    expect(Object.keys(hatalar)).toEqual(['ilPlaka', 'sureDegeri', 'tema'])
    expect(ilkHata).toBe(hatalar.ilPlaka)
  })
})

describe('payload', () => {
  it('formu servis sözleşmesine çevirir', () => {
    const payload = turPayloadu(
      gecerliForm({
        bolge: 'İç Anadolu',
        ilPlaka: 6,
        ilce: '  Çankaya  ',
        ulasimTipi: 'TopluTasima',
        sureBirimi: 'Gun',
        sureDegeri: 2,
        gunlukSaat: 6,
        tema: 'Kulturel',
        beslenme: 'Vegan',
      }),
      { iller: ILLER },
    )

    expect(payload).toEqual({
      lokasyon: {
        bolge: 'İç Anadolu',
        ilPlaka: 6,
        ilAdi: 'Ankara',
        ekIlPlakalari: [],
        ilce: 'Çankaya',
      },
      ulasimTipi: 'TopluTasima',
      sure: { birim: 'Gun', deger: 2, gunlukSaat: 6, toplamDakika: 720, baslangicSaati: '09:00' },
      tema: 'Kulturel',
      tercihEdilenMekanTipleri: ['Museum', 'Monument', 'ReligiousSite', 'Viewpoint'],
      beslenmeKisiti: 'Vegan',
      beslenmeEtiketleri: ['vegan'],
      yemekMolasi: true,
      konaklama: true,
      serbestZaman: true,
    })
  })

  it('yemek molası VARSAYILAN AÇIK gidiyor', () => {
    // Bir günlük gezi programında öğle yemeği istisna değil, kural.
    const payload = turPayloadu(gecerliForm(), { iller: ILLER })
    expect(payload.yemekMolasi).toBe(true)
  })

  it('konaklama YALNIZCA çok günlü turda gidiyor', () => {
    // Form durumu birimden bağımsız yaşıyor: kullanıcı "3 gün" seçip
    // konaklamayı işaretledikten sonra "6 saat"e dönerse bayrak açık
    // kalırdı ve akşam eve dönülen bir turda otel durağı çıkardı.
    const gunluk = turPayloadu(
      gecerliForm({ sureBirimi: 'Saat', sureDegeri: 6, konaklama: true }),
      { iller: ILLER },
    )
    expect(gunluk.konaklama).toBe(false)

    const tekGun = turPayloadu(
      gecerliForm({ sureBirimi: 'Gun', sureDegeri: 1, konaklama: true }),
      { iller: ILLER },
    )
    expect(tekGun.konaklama).toBe(false)

    const cokGun = turPayloadu(
      gecerliForm({ sureBirimi: 'Gun', sureDegeri: 3, konaklama: true }),
      { iller: ILLER },
    )
    expect(cokGun.konaklama).toBe(true)
  })

  it('ek şehirler payload’a taşınıyor', () => {
    const payload = turPayloadu(
      gecerliForm({ ilPlaka: 6, ekIlPlakalari: [34, 35] }),
      { iller: ILLER },
    )
    expect(payload.lokasyon.ekIlPlakalari).toEqual([34, 35])
  })

  it('başlangıç şehri ek şehirler listesinden ELENİYOR', () => {
    // Aynı şehir iki kez gezilmez; sunucu da eliyor ama gövdede tekrar
    // görmek "başlangıç iki kez mi?" sorusunu doğururdu.
    const payload = turPayloadu(
      gecerliForm({ ilPlaka: 6, ekIlPlakalari: [6, 34] }),
      { iller: ILLER },
    )
    expect(payload.lokasyon.ekIlPlakalari).toEqual([34])
  })

  it('boş bölge ve boş ilçe null gider ("" değil)', () => {
    const payload = turPayloadu(gecerliForm({ bolge: '', ilce: '   ' }), { iller: ILLER })
    expect(payload.lokasyon.bolge).toBeNull()
    expect(payload.lokasyon.ilce).toBeNull()
  })

  it('günübirlik turda gunlukSaat null gider', () => {
    const payload = turPayloadu(gecerliForm({ sureBirimi: 'Saat', sureDegeri: 5 }), { iller: ILLER })
    expect(payload.sure).toEqual({
      birim: 'Saat', deger: 5, gunlukSaat: null, toplamDakika: 300, baslangicSaati: '09:00',
    })
  })

  it('kısıt yokken beslenme etiketleri boş dizi', () => {
    expect(turPayloadu(gecerliForm({ beslenme: 'Yok' }), { iller: ILLER }).beslenmeEtiketleri)
      .toEqual([])
  })
})

describe('başlangıç saati', () => {
  it('saat verilmemişse istek ENGELLENMİYOR, varsayılana düşüyor', () => {
    // Saat yalnızca programın gösterimini etkiliyor; rota servisinin ona
    // ihtiyacı yok. Zorunlu tutmak, saati doldurmayan kullanıcının rota
    // önerisini komple engellemek olurdu.
    const { gecerli, payload } = rotaIstegiHazirla(
      { ...gecerliForm(), baslangicSaati: '' }, { iller: ILLER },
    )

    expect(gecerli).toBe(true)
    expect(payload.sure.baslangicSaati).toBe('09:00')
  })

  it('biçimi bozuk saat REDDEDİLİYOR', () => {
    // Sessizce 09:00'a düşürseydik, "sabah 9" yazan kullanıcı girdisinin
    // dikkate alınmadığını fark etmezdi.
    expect(dogrula({ baslangicSaati: 'sabah 9' }).hatalar.baslangicSaati).toBeTruthy()
    expect(dogrula({ baslangicSaati: '25:00' }).hatalar.baslangicSaati).toBeTruthy()
    expect(dogrula({ baslangicSaati: '08:30' }).gecerli).toBe(true)
  })
})

describe('rotaIstegiHazirla', () => {
  it('geçerli formda payload üretir', () => {
    const { gecerli, payload } = rotaIstegiHazirla(gecerliForm(), { iller: ILLER })
    expect(gecerli).toBe(true)
    expect(payload.lokasyon.ilPlaka).toBe(6)
  })

  it('geçersiz formda payload ÜRETMEZ', () => {
    // Yarım payload dönseydi, `gecerli` bayrağını kontrol etmeyi unutan bir
    // çağıran onu sunucuya gönderirdi.
    const { gecerli, payload } = rotaIstegiHazirla(gecerliForm({ ilPlaka: 0 }), { iller: ILLER })
    expect(gecerli).toBe(false)
    expect(payload).toBeNull()
  })
})
