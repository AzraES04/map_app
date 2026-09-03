import { describe, it, expect } from 'vitest'

import { alternatifEtiketi, bacakBul, cizgideIlerleme, parcayaIzdusum } from '../hatBacagi'

// ============================================================================
//  Ödev 18 (ek) — hat çizgisine tıklayınca hangi bacak seçilir?
//
//  Bu dosyanın varlık sebebi tek bir yanlış çözüm: "tıklanan noktaya EN YAKIN
//  durakları bul". Ekranda çoğu zaman doğru çalışır, kıvrımlı rotalarda
//  sessizce yanlış bacağı seçer — ve yanlış bacak, kullanıcıya BAŞKA bir
//  durağın yol alternatiflerini gösterir. Gözle fark edilmesi zor bir hata
//  olduğu için testi var.
// ============================================================================

/** İki ucu belli düz bir hat: (0,0) → (30,0), üç durak 0/10/20'de. */
const DUZ_ROTA = [[0, 0], [30, 0]]
const DUZ_DURAKLAR = [[0, 0], [10, 0], [20, 0]]

describe('parcayaIzdusum — nokta / doğru parçası', () => {
  it('parçanın ortasına dik inen noktayı 0.5 oranıyla bulur', () => {
    const { oran, uzaklikKare } = parcayaIzdusum([0, 0], [10, 0], [5, 3])
    expect(oran).toBeCloseTo(0.5)
    expect(uzaklikKare).toBeCloseTo(9)      // 3 birim uzaklık → karesi 9
  })

  it('parçanın DIŞINA düşen izdüşümü uca kırpar', () => {
    // (20, 0) parçanın ilerisinde; kırpma olmasaydı oran 2.0 çıkardı ve
    // ilerleme hesabı çizginin sonunu aşardı.
    expect(parcayaIzdusum([0, 0], [10, 0], [20, 0]).oran).toBe(1)
    expect(parcayaIzdusum([0, 0], [10, 0], [-5, 0]).oran).toBe(0)
  })

  it('sıfır uzunluklu parçada NaN üretmez', () => {
    const { oran, uzaklikKare } = parcayaIzdusum([4, 4], [4, 4], [4, 7])
    expect(oran).toBe(0)
    expect(uzaklikKare).toBeCloseTo(9)
  })
})

describe('cizgideIlerleme — çizgi boyunca kat edilen yol', () => {
  it('köşeli bir çizgide parça uzunluklarını toplar', () => {
    // (0,0) → (0,10) → (10,10): ikinci parçanın ortası = 10 + 5 = 15
    const sonuc = cizgideIlerleme([[0, 0], [0, 10], [10, 10]], [5, 10])
    expect(sonuc.ilerleme).toBeCloseTo(15)
    expect(sonuc.uzaklik).toBeCloseTo(0)
  })

  it('çizgiye uzak bir noktanın uzaklığını da döner', () => {
    expect(cizgideIlerleme(DUZ_ROTA, [10, 4]).uzaklik).toBeCloseTo(4)
  })

  it('geçersiz girdide null döner', () => {
    expect(cizgideIlerleme([[0, 0]], [0, 0])).toBeNull()
    expect(cizgideIlerleme(null, [0, 0])).toBeNull()
  })
})

describe('bacakBul — tıklanan bacağın durakları', () => {
  it('ilk bacağın ortasına tıklayınca 1. duraktan 2. durağa döner', () => {
    expect(bacakBul(DUZ_ROTA, DUZ_DURAKLAR, [5, 0])).toEqual({ kalkis: 0, varis: 1 })
  })

  it('ikinci bacağın ortasına tıklayınca bir sonraki bacağa geçer', () => {
    expect(bacakBul(DUZ_ROTA, DUZ_DURAKLAR, [15, 0])).toEqual({ kalkis: 1, varis: 2 })
  })

  it('ilk durağın gerisine tıklamak İLK bacağı seçer', () => {
    // Hattın ilk durağına GELEN bacak yok; oraya tıklayan kullanıcıya en
    // yakın anlamlı cevap 1 → 2 bacağıdır.
    expect(bacakBul(DUZ_ROTA, DUZ_DURAKLAR, [-3, 0])).toEqual({ kalkis: 0, varis: 1 })
  })

  it('son durağın ötesine tıklamak SON bacağı seçer', () => {
    expect(bacakBul(DUZ_ROTA, DUZ_DURAKLAR, [28, 0])).toEqual({ kalkis: 1, varis: 2 })
  })

  it('iki duraktan az olan hatta bacak yoktur', () => {
    expect(bacakBul(DUZ_ROTA, [[0, 0]], [5, 0])).toBeNull()
    expect(bacakBul(DUZ_ROTA, [], [5, 0])).toBeNull()
  })

  // ---- Asıl mesele: kıvrımlı rota ----------------------------------------
  //
  // Dar bir "U": yol yukarı çıkıyor, tepede 2 birim sağa kayıyor, geri iniyor.
  //
  //      B(0,10) ────── C(2,10)
  //        │              │
  //        │              │   ← burada iki bacak yan yana ve ARALARI 2 birim
  //        │              │
  //      A(0,0)         D(2,0)
  //
  // (1.9, 9) noktası İNİŞ bacağının (C → D) üstünde; ama kuş uçuşu en yakın
  // durak C, ikinci en yakın B. "En yakın iki durak" yaklaşımı bu tıklamayı
  // B → C bacağı sanırdı — oysa kullanıcı C → D bacağına tıkladı.
  const U_ROTA = [[0, 0], [0, 10], [2, 10], [2, 0]]
  const U_DURAKLAR = [[0, 0], [0, 10], [2, 10], [2, 0]]

  it('kıvrımlı rotada tıklanan bacağı ÇİZGİ BOYUNCA bulur', () => {
    expect(bacakBul(U_ROTA, U_DURAKLAR, [1.9, 9])).toEqual({ kalkis: 2, varis: 3 })
  })

  it('aynı yükseklikteki çıkış bacağını karıştırmaz', () => {
    expect(bacakBul(U_ROTA, U_DURAKLAR, [0.1, 9])).toEqual({ kalkis: 0, varis: 1 })
  })
})

describe('alternatifEtiketi — haritadaki süre rozeti', () => {
  // Etiket, listedeki sayıyla AYNI olmak zorunda: haritada "12 dk", listede
  // "13 dk" yazsaydı kullanıcı hangisine güveneceğini sorardı.
  it('en iyi alternatifte yalnızca süreyi yazar', () => {
    expect(alternatifEtiketi({ sureSaniye: 740, sureFarkiSaniye: 0, enIyi: true }))
      .toBe('12 dk')
  })

  it('diğerlerinde farkı parantez içinde ekler', () => {
    expect(alternatifEtiketi({ sureSaniye: 860, sureFarkiSaniye: 120, enIyi: false }))
      .toBe('14 dk (+2)')
  })

  it('bir dakikadan küçük farkı "+0" değil "+1" yazar', () => {
    // 20 saniyelik fark yuvarlanınca "+0 dk" olurdu: "daha uzun ama fark yok"
    // gibi okunan, hiçbir bilgi vermeyen bir etiket.
    expect(alternatifEtiketi({ sureSaniye: 760, sureFarkiSaniye: 20, enIyi: false }))
      .toBe('13 dk (+1)')
  })
})
