import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

// ============================================================================
//  YOKLAMA
//
//  İki kural test edilecek kadar önemli, ikisi de sessizce yanlış davranacak
//  cinsten:
//
//    1. "Acil" seçilince telefon SORULMADAN istek gitmiyor — kullanıcı
//       numarasını görmeden yanlışlıkla acil bildirimi göndermemeli.
//    2. Rehber ekranı kişi dökümünü (ad + varsa telefon) gösteriyor.
// ============================================================================

const api = vi.hoisted(() => ({
  misafirYoklamaCevabi: vi.fn(),
  yoklamaBaslat: vi.fn(),
  yoklamaDurumu: vi.fn(),
  yoklamaBitir: vi.fn(),
}))

vi.mock('../turApi', () => api)

const { YoklamaMisafir, YoklamaRehber } = await import('../Yoklama.jsx')

beforeEach(() => {
  vi.clearAllMocks()
  localStorage.clear()
  api.misafirYoklamaCevabi.mockResolvedValue({ kisiler: [] })
})

describe('YoklamaMisafir', () => {
  it('Buradayım tek tıkla gönderiliyor', async () => {
    const kullanici = userEvent.setup()
    render(<YoklamaMisafir kod="K7M2PQ" soru="Otobüse döndünüz mü?" />)

    await kullanici.click(screen.getByRole('button', { name: 'Buradayım' }))

    await waitFor(() => expect(api.misafirYoklamaCevabi).toHaveBeenCalledWith(
      'K7M2PQ', expect.any(String), 'Buradayim', expect.objectContaining({ telefon: '' }),
    ))
  })

  it('Acil İLK TIKLA telefon alanını açıyor, İSTEK GİTMİYOR', async () => {
    const kullanici = userEvent.setup()
    render(<YoklamaMisafir kod="K7M2PQ" soru="s" />)

    await kullanici.click(screen.getByRole('button', { name: 'Acil durum' }))

    // Kullanıcı numarasını görmeden yanlışlıkla acil bildirimi gitmemeli.
    expect(api.misafirYoklamaCevabi).not.toHaveBeenCalled()
    expect(screen.getByLabelText(/Telefon numaranız/)).toBeInTheDocument()
  })

  it('Acil İKİNCİ TIKLA telefonla birlikte gönderiliyor', async () => {
    const kullanici = userEvent.setup()
    render(<YoklamaMisafir kod="K7M2PQ" soru="s" />)

    await kullanici.click(screen.getByRole('button', { name: 'Acil durum' }))
    await kullanici.type(screen.getByLabelText(/Telefon numaranız/), '0532 000 00 00')
    await kullanici.click(screen.getByRole('button', { name: 'Acil durumu gönder' }))

    await waitFor(() => expect(api.misafirYoklamaCevabi).toHaveBeenCalledWith(
      'K7M2PQ', expect.any(String), 'Acil',
      expect.objectContaining({ telefon: '0532 000 00 00' }),
    ))
  })

  it('Buradayım cevabında telefon GÖNDERİLMİYOR', async () => {
    const kullanici = userEvent.setup()
    render(<YoklamaMisafir kod="K7M2PQ" soru="s" />)

    await kullanici.type(screen.getByLabelText(/Adınız/), 'Zeynep')
    await kullanici.click(screen.getByRole('button', { name: 'Buradayım' }))

    // Telefon alanı hiç açılmadı; "Acil" dışındaki cevaplarda telefon
    // istemek gereksiz bir engel olurdu.
    await waitFor(() => expect(api.misafirYoklamaCevabi).toHaveBeenCalledWith(
      'K7M2PQ', expect.any(String), 'Buradayim',
      { ad: 'Zeynep', telefon: '' },
    ))
  })
})

describe('YoklamaRehber', () => {
  it('kişi dökümünü isim ve telefonla gösteriyor', async () => {
    api.yoklamaDurumu.mockResolvedValue({
      soru: 'Buradayız?',
      grupBoyu: 3,
      buradayim: 1,
      degilim: 1,
      acil: 1,
      cevapsiz: 0,
      kisiler: [
        { ad: 'Zeynep', cevap: 'Acil', telefon: '0532 000 00 00' },
        { ad: 'Ali', cevap: 'Buradayim', telefon: null },
        { ad: null, cevap: 'Degilim', telefon: null },
      ],
    })

    render(<YoklamaRehber oturumId={7} />)

    expect(await screen.findByText('Zeynep')).toBeInTheDocument()
    expect(screen.getByText(/0532 000 00 00/)).toBeInTheDocument()
    expect(screen.getByText('İsimsiz')).toBeInTheDocument()
  })
})
