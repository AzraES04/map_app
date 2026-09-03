import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

// ============================================================================
//  TUR YÖNETİMİ ekranı
//
//  Ekranın işi kaydedilmiş turları LİSTELEMEK ve her grup için yeniden
//  başlatmak. Testlerin koruduğu şey iki kural:
//
//    1. Canlı oturumu olan turda "Başlat" değil "Sonlandır" görünür —
//       aksi hâlde aynı turda ikinci bir grup açılmaya çalışılırdı.
//    2. Canlı oturumu olan tur SİLİNEMEZ — sahadaki grubun ekranı boşa
//       düşerdi.
//
//  İkisi de hata vermeyen, yalnızca yanlış davranan cinsten.
// ============================================================================

const api = vi.hoisted(() => ({
  turlariGetir: vi.fn(),
  turSil: vi.fn(),
  oturumAc: vi.fn(),
  oturumlarim: vi.fn(),
  turuSonlandir: vi.fn(),
  turBaglantisi: vi.fn((kod) => `http://localhost/tur/${kod}`),
}))

vi.mock('../turApi', () => api)

const { default: AdminTur } = await import('../pages/AdminTur.jsx')

const TUR = {
  id: 1,
  name: 'Ankara Klasik Turu',
  color: '#7b5cd6',
  waypointCount: 6,
  routeDistanceMeters: 12_400,
  routeDurationSeconds: 3_600,
  guideUserName: 'rehber',
}

const IKINCI_TUR = { ...TUR, id: 2, name: 'İstanbul Tarihi Yarımada' }

const OTURUM = {
  id: 9,
  tourId: 1,
  status: 'Live',
  joinCode: 'K7M2PQ',
  participantCount: 4,
}

beforeEach(() => {
  vi.clearAllMocks()
  api.turlariGetir.mockResolvedValue([TUR, IKINCI_TUR])
  api.oturumlarim.mockResolvedValue([])
  api.oturumAc.mockResolvedValue(OTURUM)
  api.turuSonlandir.mockResolvedValue({ ...OTURUM, status: 'Completed' })
  api.turSil.mockResolvedValue(null)
})

const ekraniCiz = async () => {
  const kullanici = userEvent.setup()
  render(<AdminTur />)
  await screen.findByText(TUR.name)
  return { kullanici }
}

describe('liste', () => {
  it('kaydedilmiş turları rehberi ve durak sayısıyla gösteriyor', async () => {
    await ekraniCiz()

    expect(screen.getByText(IKINCI_TUR.name)).toBeInTheDocument()
    expect(screen.getAllByText(/6 durak/)).toHaveLength(2)
    expect(screen.getAllByText(/rehber/)).not.toHaveLength(0)
  })

  it('hiç tur yoksa nereden oluşturulacağını söylüyor', async () => {
    api.turlariGetir.mockResolvedValue([])
    render(<AdminTur />)

    // Boş bir liste tek başına "özellik çalışmıyor" gibi okunur.
    expect(await screen.findByText(/Henüz kaydedilmiş tur yok/)).toBeInTheDocument()
    expect(screen.getByText(/Tur Planla/)).toBeInTheDocument()
  })
})

describe('turu yeniden başlatma', () => {
  it('oturum açıp paylaşım bağlantısı veriyor', async () => {
    const { kullanici } = await ekraniCiz()

    await kullanici.click(screen.getAllByRole('button', { name: /Başlat ve paylaş/ })[0])

    await waitFor(() => expect(api.oturumAc).toHaveBeenCalledWith(1, true))

    // HAZIR TURUN ANLAMI BU: aynı tur, yeni bir grup için yeni bir kod.
    const baglanti = await screen.findByLabelText('Paylaşım bağlantısı')
    expect(baglanti).toHaveValue('http://localhost/tur/K7M2PQ')
  })

  it('canlı oturumu olan turda BAŞLAT değil SONLANDIR var', async () => {
    api.oturumlarim.mockResolvedValue([OTURUM])
    await ekraniCiz()

    // Aynı turda ikinci bir grup açılmaya çalışılmasın.
    const satirlar = screen.getAllByRole('listitem')
    expect(satirlar[0]).toHaveTextContent(/Canlı · 4 kişi/)
    expect(satirlar[0]).toHaveTextContent(/K7M2PQ/)
    expect(satirlar[0].querySelector('button').textContent).toMatch(/Turu sonlandır/)
  })

  it('sonlandırma onay istiyor', async () => {
    api.oturumlarim.mockResolvedValue([OTURUM])
    const { kullanici } = await ekraniCiz()
    vi.spyOn(window, 'confirm').mockReturnValue(false)

    await kullanici.click(screen.getByRole('button', { name: /Turu sonlandır/ }))

    // Katılım kodu geçersizleşiyor: geri alınamaz.
    expect(api.turuSonlandir).not.toHaveBeenCalled()
  })
})

describe('silme', () => {
  it('canlı oturumu olan tur silinmiyor', async () => {
    api.oturumlarim.mockResolvedValue([OTURUM])
    const { kullanici } = await ekraniCiz()

    await kullanici.click(screen.getByRole('button', { name: /Ankara Klasik Turu turunu sil/ }))

    // Sahadaki grubun ekranı boşa düşerdi.
    expect(api.turSil).not.toHaveBeenCalled()
    expect(await screen.findByText(/Canlı oturumu olan tur silinemez/)).toBeInTheDocument()
  })

  it('boştaki tur onaydan sonra siliniyor', async () => {
    const { kullanici } = await ekraniCiz()
    vi.spyOn(window, 'confirm').mockReturnValue(true)

    await kullanici.click(screen.getByRole('button', { name: /Ankara Klasik Turu turunu sil/ }))

    await waitFor(() => expect(api.turSil).toHaveBeenCalledWith(1))
    await waitFor(() => expect(screen.queryByText(TUR.name)).toBeNull())
  })
})
