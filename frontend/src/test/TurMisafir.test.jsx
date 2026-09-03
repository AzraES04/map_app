import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Routes, Route } from 'react-router-dom'

// ============================================================================
//  MİSAFİR TUR EKRANI
//
//  Korunan kural tek cümleyle: HESAPSIZ BİRİ TURU GÖREBİLMELİ.
//
//  Bu ekran, tam da bunun çalışmadığı bir hatadan doğdu — paylaşılan bağlantı
//  giriş ekranına düşüyor, giriş yapılırsa da haritanın tamamına
//  yönlendiriyordu. Aşağıdaki testler o davranışın geri gelmesini
//  engelliyor: ekran giriş istemiyor, yönlendirmiyor ve turun kendisini
//  gösteriyor.
// ============================================================================

const api = vi.hoisted(() => ({
  misafirTuruGetir: vi.fn(),
}))

const harita = vi.hoisted(() => ({ yolTarifiniAc: vi.fn() }))

vi.mock('../turApi', () => api)
vi.mock('../haritaLinki', () => harita)

const { default: TurMisafir } = await import('../pages/TurMisafir.jsx')

const TUR = {
  tourName: 'Ankara Klasik Turu',
  color: '#7b5cd6',
  status: 'Live',
  guideUserName: 'ayse',
  progressPercent: 40,
  currentWaypointOrder: 2,
  currentWaypointName: 'Ankara Kalesi',
  nextWaypointOrder: 3,
  nextWaypointName: 'Anıtkabir',
  waypoints: [
    { order: 1, name: 'Etnografya Müzesi', venueType: 'Museum', dwellMinutes: 45, wkt: 'POINT (32.85 39.93)' },
    { order: 2, name: 'Ankara Kalesi', venueType: 'Monument', dwellMinutes: 40, wkt: 'POINT (32.86 39.94)' },
    { order: 3, name: 'Anıtkabir', venueType: 'Monument', dwellMinutes: 60, wkt: 'POINT (32.83 39.92)' },
  ],
}

beforeEach(() => {
  vi.clearAllMocks()
  api.misafirTuruGetir.mockResolvedValue(TUR)
})

/** Ekranı /tur/:kod adresinde çiziyor — gerçek rota eşleşmesiyle. */
const ekraniCiz = (yol = '/tur/K7M2PQ') => {
  const kullanici = userEvent.setup()

  render(
    <MemoryRouter initialEntries={[yol]}>
      <Routes>
        <Route path="/tur" element={<TurMisafir />} />
        <Route path="/tur/:kod" element={<TurMisafir />} />
        <Route path="/login" element={<p>GİRİŞ EKRANI</p>} />
        <Route path="/map" element={<p>HARİTA UYGULAMASI</p>} />
      </Routes>
    </MemoryRouter>,
  )

  return { kullanici }
}

describe('hesapsız erişim', () => {
  it('koddaki turu giriş istemeden gösteriyor', async () => {
    ekraniCiz()

    expect(await screen.findByText('Ankara Klasik Turu')).toBeInTheDocument()
    expect(api.misafirTuruGetir).toHaveBeenCalledWith('K7M2PQ')

    // Hatanın kendisi buydu: bağlantı giriş ekranına düşüyordu.
    expect(screen.queryByText('GİRİŞ EKRANI')).toBeNull()
  })

  it('harita uygulamasına YÖNLENDİRMİYOR', async () => {
    ekraniCiz()
    await screen.findByText('Ankara Klasik Turu')

    // Eski akış katılımdan sonra /map'e atıyordu; misafir, turla ilgisi
    // olmayan bir çizim/POI uygulamasının içinde buluyordu kendini.
    await waitFor(() => expect(screen.queryByText('HARİTA UYGULAMASI')).toBeNull())
  })

  it('rehberin adını ve durakları gösteriyor', async () => {
    ekraniCiz()
    await screen.findByText('Ankara Klasik Turu')

    expect(screen.getByText(/ayse/)).toBeInTheDocument()
    expect(screen.getByText('Etnografya Müzesi')).toBeInTheDocument()
    expect(screen.getByText('Anıtkabir')).toBeInTheDocument()
  })

  it('grubun ŞU AN bulunduğu durağı öne çıkarıyor', async () => {
    ekraniCiz()
    await screen.findByText('Ankara Klasik Turu')

    // Misafirin sayfayı açma sebebi tek bir soru: grup nerede?
    const simdi = document.querySelector('.misafir-simdi')
    expect(simdi.textContent).toMatch(/Şu an/)
    expect(simdi.textContent).toMatch(/Ankara Kalesi/)
    expect(simdi.textContent).toMatch(/Sırada: Anıtkabir/)
    expect(simdi.textContent).toMatch(/2 \/ 3 durak/)
  })

  it('geçilen duraklar soluk ama listede kalıyor', async () => {
    ekraniCiz()
    await screen.findByText('Ankara Klasik Turu')

    // "Nereden geldik" bilgisi turun parçası; listeden çıkarsaydık misafir
    // programın tamamını göremezdi.
    const satirlar = screen.getAllByRole('listitem')
    expect(satirlar[0].className).toMatch(/gecildi/)
    expect(satirlar[1].className).toMatch(/simdi/)
    expect(satirlar[2].className).not.toMatch(/gecildi/)
  })

  it('durak için cihazın harita uygulamasında yol tarifi açıyor', async () => {
    const { kullanici } = ekraniCiz()
    await screen.findByText('Ankara Klasik Turu')

    await kullanici.click(screen.getByRole('button', { name: /Anıtkabir için yol tarifi/ }))

    // Telefondaki asıl işlevsel düğme bu: adım adım tarif cihazın kendi
    // uygulamasında daha iyi.
    expect(harita.yolTarifiniAc).toHaveBeenCalledWith(
      expect.objectContaining({ ad: 'Anıtkabir', lat: 39.92, lon: 32.83 }),
    )
  })
})

describe('kod girme', () => {
  it('kodsuz adreste kod alanı çıkıyor', async () => {
    ekraniCiz('/tur')

    // Kullanıcı şikâyeti: "herhangi bir kodu gireceğim bir alan çıkmıyor."
    expect(await screen.findByLabelText('Katılım kodu')).toBeInTheDocument()
    expect(api.misafirTuruGetir).not.toHaveBeenCalled()
  })

  it('girilen kod büyük harfe çevriliyor', async () => {
    const { kullanici } = ekraniCiz('/tur')

    await kullanici.type(screen.getByLabelText('Katılım kodu'), 'k7m2pq')
    await kullanici.click(screen.getByRole('button', { name: /Turu göster/ }))

    await waitFor(() => expect(api.misafirTuruGetir).toHaveBeenCalledWith('K7M2PQ'))
  })

  it('geçersiz kodda hata gösterip kod alanına dönüyor', async () => {
    api.misafirTuruGetir.mockRejectedValue(new Error('Bu katılım kodu geçerli değil ya da tur sona ermiş.'))
    ekraniCiz()

    expect(await screen.findByText(/katılım kodu geçerli değil/)).toBeInTheDocument()
    // Kullanıcı burada mahsur kalmasın: kodu düzeltip yeniden deneyebilmeli.
    expect(screen.getByLabelText('Katılım kodu')).toBeInTheDocument()
  })

  it('çok kısa kod sunucuya hiç gitmiyor', async () => {
    const { kullanici } = ekraniCiz('/tur')

    await kullanici.type(screen.getByLabelText('Katılım kodu'), 'AB')
    await kullanici.click(screen.getByRole('button', { name: /Turu göster/ }))

    expect(api.misafirTuruGetir).not.toHaveBeenCalled()
    expect(screen.getByText(/6 karakterdir/)).toBeInTheDocument()
  })
})

describe('canlı takip', () => {
  it('rehber ilerleyince ekran güncelleniyor', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })

    try {
      ekraniCiz()
      await screen.findByText('Ankara Klasik Turu')

      api.misafirTuruGetir.mockResolvedValue({
        ...TUR,
        currentWaypointOrder: 3,
        currentWaypointName: 'Anıtkabir',
        nextWaypointName: null,
        nextWaypointOrder: null,
      })

      await vi.advanceTimersByTimeAsync(16_000)

      // SignalR kullanmadık: yayın kanalı kimlik doğrulaması üzerine kurulu
      // ve misafirin kimliği yok. Yoklama, saatlerle ölçülen bir tur programı
      // için fazlasıyla sık.
      await waitFor(() => {
        expect(document.querySelector('.misafir-simdi').textContent).toMatch(/Anıtkabir/)
      })
    } finally {
      vi.useRealTimers()
    }
  })

  it('yenileme hatası ekrandaki turu SİLMİYOR', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })

    try {
      ekraniCiz()
      await screen.findByText('Ankara Klasik Turu')

      api.misafirTuruGetir.mockRejectedValue(new Error('ağ hatası'))
      await vi.advanceTimersByTimeAsync(16_000)

      // Ağ bir an kesildiğinde elindeki bilgiyi kaybettirmek, misafiri tur
      // ortasında ekransız bırakmak olurdu.
      expect(screen.getByText('Ankara Klasik Turu')).toBeInTheDocument()
    } finally {
      vi.useRealTimers()
    }
  })
})

describe('rehberin canlı konumu', () => {
  it('yayın açıkken CANLI rozeti gösteriyor', async () => {
    api.misafirTuruGetir.mockResolvedValue({
      ...TUR, guideLat: 39.935, guideLon: 32.855,
      guidePositionUtc: new Date().toISOString(),
    })

    ekraniCiz()

    expect(await screen.findByText(/Rehberin konumu haritada canlı/)).toBeInTheDocument()
  })

  it('yayın kapalıyken bunu SÖYLÜYOR', async () => {
    // Boş bir harita "bozuk mu?" diye okunur; sebebi yazmak gerekiyor.
    ekraniCiz()

    expect(await screen.findByText(/Rehber konum paylaşmıyor/)).toBeInTheDocument()
  })

  it('canlı yayında DAHA SIK yokluyor', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })

    try {
      api.misafirTuruGetir.mockResolvedValue({
        ...TUR, guideLat: 39.935, guideLon: 32.855,
      })

      ekraniCiz()
      await screen.findByText('Ankara Klasik Turu')

      const ilk = api.misafirTuruGetir.mock.calls.length

      // Yürüyen bir grup 20 saniyede bir sokak ilerliyor; harita geride
      // kalırdı. Yayın açıkken aralık 8 saniye.
      await vi.advanceTimersByTimeAsync(9_000)

      expect(api.misafirTuruGetir.mock.calls.length).toBeGreaterThan(ilk)
    } finally {
      vi.useRealTimers()
    }
  })
})
