import { describe, it, expect } from 'vitest'

import {
  turMerkezi,
  sehirIcindekiler,
  poidenDurak,
  duragiEkle,
  duragiCikar,
  duragiTasi,
  rotaHesapGovdesi,
  ELLE_EKLENEN_KALIS,
} from '../turDuzenleme'

// ============================================================================
//  ROTA DÜZENLEME
//
//  Buradaki kuralların ortak özelliği: bozulduklarında HATA VERMİYORLAR,
//  yalnızca yanlış davranıyorlar. İki durak aynı numarayı taşırsa haritada
//  iki tane "3" görünür; aynı mekan iki kez girerse rota kendi üstüne döner.
//  Gözle fark edilmesi zor, testle kolay.
// ============================================================================

const durak = (order, ad, ek = {}) => ({
  order,
  name: ad,
  placeId: `google:${ad}`,
  dwellMinutes: 30,
  wkt: `POINT (32.8${order} 39.9${order})`,
  ...ek,
})

const LISTE = [durak(1, 'Bir'), durak(2, 'İki'), durak(3, 'Üç')]

describe('poidenDurak', () => {
  const POI = {
    id: 42,
    isim: 'Anadolu Medeniyetleri Müzesi',
    wkt: 'POINT (32.8639 39.9401)',
    kategoriYolu: 'Gezilecek Yer / Müze',
  }

  it('POI kaydını tur durağına çeviriyor', () => {
    const d = poidenDurak(POI)

    expect(d.name).toBe(POI.isim)
    expect(d.poiId).toBe(42)
    expect(d.dwellMinutes).toBe(ELLE_EKLENEN_KALIS)
    expect(d.elleEklendi).toBe(true)
  })

  it('PlaceId kaynağı belli edecek şekilde ön ekli', () => {
    // Ön ek olmasaydı 12 numaralı POI ile 12 numaralı bir Google mekanı
    // aynı kayıt sanılırdı.
    expect(poidenDurak(POI).placeId).toBe('poi:42')
  })

  it('konumu okunamayan POI durak olmuyor', () => {
    expect(poidenDurak({ id: 1, isim: 'X', wkt: 'POLYGON ((0 0))' })).toBeNull()
    expect(poidenDurak(null)).toBeNull()
  })
})

describe('duragiEkle', () => {
  it('sona ekliyor ve sıraları yeniden yazıyor', () => {
    const { duraklar, hata } = duragiEkle(LISTE, durak(0, 'Dört'))

    expect(hata).toBeNull()
    expect(duraklar.map((d) => d.order)).toEqual([1, 2, 3, 4])
    expect(duraklar.at(-1).name).toBe('Dört')
  })

  it('AYNI mekan iki kez girmiyor', () => {
    // Rota kendi üstüne dönerdi.
    const { duraklar, hata } = duragiEkle(LISTE, durak(9, 'İki'))

    expect(duraklar).toHaveLength(3)
    expect(hata).toMatch(/zaten turda/)
  })

  it('aynı POI id iki kez girmiyor', () => {
    const liste = [...LISTE, { ...durak(4, 'POI'), poiId: 7, placeId: 'poi:7' }]
    const { hata } = duragiEkle(liste, { ...durak(0, 'Başka ad'), poiId: 7, placeId: 'poi:7' })

    expect(hata).toMatch(/zaten turda/)
  })

  it('konumu okunamayan durak eklenmiyor', () => {
    const { duraklar, hata } = duragiEkle(LISTE, null)

    expect(duraklar).toBe(LISTE)
    expect(hata).toBeTruthy()
  })
})

describe('duragiCikar', () => {
  it('çıkarınca sıralar 1..N kalıyor', () => {
    // Eski numaraları korusaydık listede 1, 3, 4 görünürdü.
    const { duraklar } = duragiCikar([...LISTE, durak(4, 'Dört')], 2)

    expect(duraklar.map((d) => d.name)).toEqual(['Bir', 'Üç', 'Dört'])
    expect(duraklar.map((d) => d.order)).toEqual([1, 2, 3])
  })

  it('iki durağın altına düşürmüyor', () => {
    // Tek duraklı tur olmaz; sunucu da aynı kuralı uyguluyor.
    const { duraklar, hata } = duragiCikar(LISTE.slice(0, 2), 1)

    expect(duraklar).toHaveLength(2)
    expect(hata).toMatch(/en az iki durak/)
  })
})

describe('duragiTasi', () => {
  it('yukarı taşıyor', () => {
    const { duraklar } = duragiTasi(LISTE, 3, -1)
    expect(duraklar.map((d) => d.name)).toEqual(['Bir', 'Üç', 'İki'])
  })

  it('aşağı taşıyor', () => {
    const { duraklar } = duragiTasi(LISTE, 1, 1)
    expect(duraklar.map((d) => d.name)).toEqual(['İki', 'Bir', 'Üç'])
  })

  it('listenin dışına taşımıyor', () => {
    expect(duragiTasi(LISTE, 1, -1).duraklar).toEqual(LISTE)
    expect(duragiTasi(LISTE, 3, 1).duraklar).toEqual(LISTE)
  })

  it('taşıdıktan sonra sıralar yine 1..N', () => {
    const { duraklar } = duragiTasi(LISTE, 3, -1)
    expect(duraklar.map((d) => d.order)).toEqual([1, 2, 3])
  })
})

describe('rotaHesapGovdesi', () => {
  it('koordinatları sunucunun beklediği biçime çeviriyor', () => {
    const govde = rotaHesapGovdesi(LISTE, 'Arac')

    expect(govde.ulasimTipi).toBe('Arac')
    expect(govde.duraklar).toHaveLength(3)
    expect(govde.duraklar[0]).toEqual({ lat: 39.91, lon: 32.81 })
  })

  it('koordinatı çözülemeyen durak varsa null', () => {
    // Sessizce atlasaydık rota o durağa uğramaz ama liste onu göstermeye
    // devam ederdi — ekranla harita ayrışırdı.
    expect(rotaHesapGovdesi([...LISTE, durak(4, 'Bozuk', { wkt: 'yok' })], 'Yaya')).toBeNull()
  })
})

describe('şehir süzgeci', () => {
  const ANKARA = [
    { order: 1, name: 'Anıtkabir', wkt: 'POINT (32.8369 39.9250)' },
    { order: 2, name: 'Kale', wkt: 'POINT (32.8639 39.9401)' },
  ]

  it('turun merkezini duraklardan buluyor', () => {
    const m = turMerkezi(ANKARA)
    expect(m.lat).toBeCloseTo(39.9326, 3)
    expect(m.lon).toBeCloseTo(32.8504, 3)
  })

  it('durak yoksa merkez null', () => {
    expect(turMerkezi([])).toBeNull()
    expect(turMerkezi(null)).toBeNull()
  })

  it('BAŞKA ŞEHİRDEKİ sonucu eliyor', () => {
    // Canlıda: Ankara turuna durak eklerken "cami" araması İstanbul
    // kayıtlarını da getiriyordu; seçilen bir kayıt rotayı 350 km uzatırdı.
    const sonuclar = [
      { id: 1, isim: 'Hacı Bayram Veli Camii', wkt: 'POINT (32.8447 39.9435)' },
      { id: 2, isim: 'Sultanahmet Camii', wkt: 'POINT (28.9768 41.0054)' },
    ]

    const kalan = sehirIcindekiler(sonuclar, turMerkezi(ANKARA))

    expect(kalan.map((p) => p.id)).toEqual([1])
  })

  it('merkez bilinmiyorsa süzme YAPMIYOR', () => {
    // Boş liste göstermek, kullanıcıya sebebi görünmeyen bir arıza gibi gelirdi.
    const sonuclar = [{ id: 1, isim: 'X', wkt: 'POINT (28.97 41.00)' }]
    expect(sehirIcindekiler(sonuclar, null)).toHaveLength(1)
  })

  it('konumu okunamayan sonuç eleniyor', () => {
    const sonuclar = [{ id: 1, isim: 'Bozuk', wkt: 'yok' }]
    expect(sehirIcindekiler(sonuclar, turMerkezi(ANKARA))).toHaveLength(0)
  })
})
