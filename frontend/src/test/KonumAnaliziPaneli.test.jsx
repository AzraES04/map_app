import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import KonumAnaliziPaneli from '../pages/KonumAnaliziPaneli'

// ============================================================================
//  Ödev 14'ün en katı kuralının bileşen testi
//
//    "Her kritere 100 üzerinden bir ağırlık puanı verilmeli ve tüm
//     kriterlerin puanları toplamı tam olarak 100 olmalıdır. Puan toplamı
//     100'den farklı ise analiz başlatılmamalıdır."
//
//  Kural SUNUCUDA da var (KonumAnaliziService.KriterleriDogrula) ve orada
//  testli. Peki arayüzde ayrıca test etmenin değeri ne?
//
//  Çünkü ödev "analiz BAŞLATILMAMALIDIR" diyor — yani istek hiç gitmemeli.
//  Sunucu tarafı 400 dönerek kuralı KORUYOR ama arayüz isteği gönderiyorsa
//  ödevin maddesi karşılanmamış olur. Burada sınanan tam olarak bu:
//  onCalistir ÇAĞRILMIYOR mu?
// ============================================================================

const KATEGORILER = [
  { id: 8, ad: 'Sağlık', parentId: null, seviye: 0, poiSayisi: 0 },
  { id: 10, ad: 'Eczane', parentId: 8, seviye: 1, poiSayisi: 702 },
  { id: 11, ad: 'Eğitim', parentId: null, seviye: 0, poiSayisi: 0 },
  { id: 12, ad: 'Okul', parentId: 11, seviye: 1, poiSayisi: 636 },
  { id: 3, ad: 'Kafe', parentId: null, seviye: 0, poiSayisi: 622 },
]

const STILLER = [
  { kategoriId: 8, renk: '#d64550' },
  { kategoriId: 11, renk: '#2e9e63' },
  { kategoriId: 3, renk: '#d9822b' },
]

const ILLER = [
  { plaka: 6, ad: 'Ankara', bolge: 'İç Anadolu' },
  { plaka: 34, ad: 'İstanbul', bolge: 'Marmara' },
]

function panelCiz(ekstra = {}) {
  const onCalistir = vi.fn()

  const varsayilan = {
    kategoriler: KATEGORILER,
    stiller: STILLER,
    iller: ILLER,
    illerHatasi: null,
    cizilenAlanWkt: null,
    cizimAktif: false,
    onCizimBaslat: vi.fn(),
    onCizimIptal: vi.fn(),
    sonuc: null,
    yukleniyor: false,
    hata: null,
    onCalistir,
    onTemizle: vi.fn(),
    onAdayaGit: vi.fn(),
  }

  render(<KonumAnaliziPaneli {...varsayilan} {...ekstra} />)
  return { onCalistir, ...varsayilan, ...ekstra }
}

const baslatDugmesi = () => screen.getByRole('button', { name: /Analizi Başlat/i })
const puanKutusu = (sira) => screen.getByLabelText(`${sira}. kriterin ağırlık puanı`)
const kategoriSecici = (sira) => screen.getByLabelText(`${sira}. kriterin kategorisi`)

/**
 * Toplam göstergesi tek metin düğümü değil: `<strong>100</strong> / 100 puan`.
 * getByText parçalanmış metni bulamadığı için kutunun tamamını okuyoruz.
 * Bilerek sınıfa bağlandı: rakamı kalın yazan biçimlendirme değişse bile test
 * çalışmaya devam etsin.
 */
const toplamMetni = () => document.querySelector('.puan-toplami').textContent

/** Sayı kutusunu temizleyip yeni değeri yazar. */
async function puanYaz(kullanici, sira, deger) {
  const kutu = puanKutusu(sira)
  await kullanici.clear(kutu)
  await kullanici.type(kutu, String(deger))
}

/** Alan + iki kategori seçip analizi başlatılabilir hâle getirir. */
async function gecerliFormKur(kullanici) {
  await kullanici.click(screen.getByRole('checkbox', { name: /Ankara/ }))
  await kullanici.selectOptions(kategoriSecici(1), '10')
  await kullanici.selectOptions(kategoriSecici(2), '12')
}

describe('KonumAnaliziPaneli — açılış hâli', () => {
  beforeEach(() => panelCiz())

  it('iki kriter satırıyla açılır (ödevin alt sınırı)', () => {
    expect(screen.getByLabelText('1. kriterin kategorisi')).toBeInTheDocument()
    expect(screen.getByLabelText('2. kriterin kategorisi')).toBeInTheDocument()
    expect(screen.queryByLabelText('3. kriterin kategorisi')).not.toBeInTheDocument()
  })

  it('puanlar eşit dağıtılmış ve toplam 100', () => {
    expect(puanKutusu(1)).toHaveValue(50)
    expect(puanKutusu(2)).toHaveValue(50)
    expect(toplamMetni()).toMatch(/100 \/ 100 puan/)
  })

  it('alan seçilmeden başlatılamaz ve sebebi yazılı', () => {
    expect(baslatDugmesi()).toBeDisabled()
    expect(screen.getByText('En az bir il seçin.')).toBeInTheDocument()
  })
})

describe('KonumAnaliziPaneli — puan toplamı kuralı', () => {
  it('toplam 100 iken analiz başlatılabiliyor', async () => {
    const kullanici = userEvent.setup()
    const { onCalistir } = panelCiz()

    await gecerliFormKur(kullanici)

    expect(baslatDugmesi()).toBeEnabled()
    await kullanici.click(baslatDugmesi())
    expect(onCalistir).toHaveBeenCalledTimes(1)
  })

  it('toplam 100 DEĞİLKEN analiz BAŞLAMIYOR', async () => {
    const kullanici = userEvent.setup()
    const { onCalistir } = panelCiz()

    await gecerliFormKur(kullanici)
    await puanYaz(kullanici, 1, 70)   // 70 + 50 = 120

    expect(toplamMetni()).toMatch(/120 \/ 100 puan/)
    expect(baslatDugmesi()).toBeDisabled()

    // Ödevin cümlesi: "analiz başlatılmamalıdır" — istek HİÇ gitmemeli.
    await kullanici.click(baslatDugmesi())
    expect(onCalistir).not.toHaveBeenCalled()
  })

  it('eksik toplamda kaç puan kaldığını söylüyor', async () => {
    const kullanici = userEvent.setup()
    panelCiz()

    await gecerliFormKur(kullanici)
    await puanYaz(kullanici, 1, 30)   // 30 + 50 = 80

    expect(screen.getByText(/20 puan eksik/)).toBeInTheDocument()
    expect(screen.getByText(/Puan toplamı 100 olmalı \(şu an 80\)/)).toBeInTheDocument()
  })

  it('fazla toplamda "fazla" diyor — "eksik" değil', async () => {
    const kullanici = userEvent.setup()
    panelCiz()

    await gecerliFormKur(kullanici)
    await puanYaz(kullanici, 1, 80)   // 80 + 50 = 130

    expect(screen.getByText(/30 puan fazla/)).toBeInTheDocument()
  })

  it('"Eşit dağıt" toplamı tam 100 yapıyor (kalan ilk satıra)', async () => {
    const kullanici = userEvent.setup()
    panelCiz()

    await gecerliFormKur(kullanici)
    await kullanici.click(screen.getByRole('button', { name: '+ Kriter ekle' }))
    await kullanici.click(screen.getByRole('button', { name: 'Eşit dağıt' }))

    // 100 / 3 tam bölünmüyor → 34 + 33 + 33. Ondalık kabul edilseydi
    // "toplam tam 100" kuralı kayan nokta hatasına takılırdı.
    expect(puanKutusu(1)).toHaveValue(34)
    expect(puanKutusu(2)).toHaveValue(33)
    expect(puanKutusu(3)).toHaveValue(33)
    expect(toplamMetni()).toMatch(/100 \/ 100 puan/)
  })
})

describe('KonumAnaliziPaneli — kriter sayısı sınırları', () => {
  it('en fazla 5 kritere çıkıyor', async () => {
    const kullanici = userEvent.setup()
    panelCiz()

    const ekle = screen.getByRole('button', { name: '+ Kriter ekle' })
    for (let i = 0; i < 5; i += 1) {
      if (!ekle.disabled) await kullanici.click(ekle)
    }

    expect(screen.getByLabelText('5. kriterin kategorisi')).toBeInTheDocument()
    expect(screen.queryByLabelText('6. kriterin kategorisi')).not.toBeInTheDocument()
    expect(ekle).toBeDisabled()
  })

  it('2 kriterin altına inilemiyor', () => {
    panelCiz()

    const silDugmeleri = screen.getAllByTitle(/En az 2 kriter gerekli/)
    expect(silDugmeleri).toHaveLength(2)
    silDugmeleri.forEach((d) => expect(d).toBeDisabled())
  })
})

describe('KonumAnaliziPaneli — kategori seçimi', () => {
  it('bir satırda seçilen kategori diğerinde KAPALI', async () => {
    const kullanici = userEvent.setup()
    panelCiz()

    await kullanici.selectOptions(kategoriSecici(1), '10')

    // Aynı kategori (Eczane) ikinci satırda seçilemez olmalı: iki kez
    // sayılması sessizce yanlış bir sonuç üretirdi.
    const ikinci = kategoriSecici(2)
    const eczane = within(ikinci).getByRole('option', { name: /Eczane/ })
    expect(eczane).toBeDisabled()
  })

  it('kategori seçilmemişse sebebi yazılı ve düğme kapalı', async () => {
    const kullanici = userEvent.setup()
    panelCiz()

    await kullanici.click(screen.getByRole('checkbox', { name: /Ankara/ }))

    expect(screen.getByText('Her kriter için bir kategori seçin.')).toBeInTheDocument()
    expect(baslatDugmesi()).toBeDisabled()
  })
})

describe('KonumAnaliziPaneli — sunucuya giden istek', () => {
  it('il kipinde ilPlakalari gönderiyor, wkt GÖNDERMİYOR', async () => {
    const kullanici = userEvent.setup()
    const { onCalistir } = panelCiz({ cizilenAlanWkt: 'POLYGON ((0 0, 1 0, 1 1, 0 0))' })

    await gecerliFormKur(kullanici)
    await kullanici.click(baslatDugmesi())

    // İkisini birden yollamak sunucudan 400 alırdı: hangi alanın geçerli
    // olduğunu sessizce seçmek, kullanıcının gördüğü alanla analizin
    // çalıştığı alanı ayırırdı.
    expect(onCalistir).toHaveBeenCalledWith({
      ilPlakalari: [6],
      wkt: null,
      kriterler: [
        { kategoriId: 10, agirlik: 50 },
        { kategoriId: 12, agirlik: 50 },
      ],
    })
  })

  it('birden çok il seçilince plakalar SIRALI gidiyor', async () => {
    const kullanici = userEvent.setup()
    const { onCalistir } = panelCiz()

    // Önce İstanbul (34), sonra Ankara (6) — tıklama sırası tersine.
    await kullanici.click(screen.getByRole('checkbox', { name: /İstanbul/ }))
    await kullanici.click(screen.getByRole('checkbox', { name: /Ankara/ }))
    await kullanici.selectOptions(kategoriSecici(1), '10')
    await kullanici.selectOptions(kategoriSecici(2), '12')

    await kullanici.click(baslatDugmesi())

    // Sıralı gönderiliyor ki aynı seçim her seferinde aynı isteği üretsin.
    expect(onCalistir.mock.calls[0][0].ilPlakalari).toEqual([6, 34])
  })
})

describe('KonumAnaliziPaneli — sonuç gösterimi', () => {
  const SONUC = {
    alanAdi: 'Ankara',
    alanKm2: 25712.8,
    toplamPoi: 53,
    kriterler: [
      { kategoriId: 10, kategoriAdi: 'Eczane', kategoriYolu: 'Sağlık › Eczane', agirlik: 60, poiSayisi: 27 },
      { kategoriId: 12, kategoriAdi: 'Okul', kategoriYolu: 'Eğitim › Okul', agirlik: 40, poiSayisi: 0 },
    ],
    izgara: { enYuksekSkor: 0.6184, hucreMetre: 2743, etkiYaricapiMetre: 16456 },
    adaylar: [
      { sira: 1, boylam: 32.9, enlem: 40.5, skor: 61.8, mesafeler: [
        { kategoriId: 10, kategoriAdi: 'Eczane', poiAdi: 'X Eczanesi', mesafeMetre: 2298 },
        { kategoriId: 12, kategoriAdi: 'Okul', poiAdi: null, mesafeMetre: null },
      ] },
    ],
  }

  it('kriter başına POI sayısını gösteriyor', () => {
    panelCiz({ sonuc: SONUC })

    expect(screen.getByText('27 POI')).toBeInTheDocument()
    expect(screen.getByText('0 POI')).toBeInTheDocument()
  })

  it('alanda hiç POI bulunmayan analizde uyarı çıkıyor', () => {
    panelCiz({ sonuc: { ...SONUC, toplamPoi: 0 } })

    expect(screen.getByText(/bu kriterlere uyan POI yok/)).toBeInTheDocument()
  })

  it('POI\'si olmayan kriterin mesafesi "—" olarak yazılıyor', () => {
    panelCiz({ sonuc: SONUC })

    // null mesafe "0 m" diye yazılsaydı "hemen yanında" gibi okunurdu.
    expect(screen.getByText(/Okul: —/)).toBeInTheDocument()
  })

  it('lejant en yüksek skoru puana çevirip gösteriyor', () => {
    panelCiz({ sonuc: SONUC })

    expect(screen.getByText(/62 puan \(en iyi\)/)).toBeInTheDocument()
  })
})
