import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import TourBuilder, { turRotasiIste } from '../pages/TourBuilder'

// ============================================================================
//  TourBuilder — ekranın sınavı
//
//  Kuralların kendisi turPlani.test.js'te saf olarak sınanıyor. Burada
//  sınanan tek şey EKRANIN O KURALLARA UYUP UYMADIĞI:
//
//    • geçersiz formda rota servisine istek GİTMİYOR mu?
//    • seçimler payload'a doğru taşınıyor mu?
//    • bölge değişince listede olmayan şehir seçili kalıyor mu?
//
//  Rota servisi prop olarak geçiliyor (sahte fonksiyon): global fetch'i
//  taklit etmek hem kırılgan hem de bu testlerin sorusuyla ilgisiz olurdu.
// ============================================================================

const ILLER = [
  { plaka: 6, ad: 'Ankara', bolge: 'İç Anadolu' },
  { plaka: 34, ad: 'İstanbul', bolge: 'Marmara' },
  { plaka: 35, ad: 'İzmir', bolge: 'Ege' },
]

function panelCiz(ekstra = {}) {
  const rotaServisi = vi.fn().mockResolvedValue({
    name: 'Ankara · Kültürel tur',
    waypoints: [{ id: 1 }, { id: 2 }],
    estimatedTotalMinutes: 240,
  })

  render(<TourBuilder iller={ILLER} rotaServisi={rotaServisi} {...ekstra} />)
  return { rotaServisi, kullanici: userEvent.setup() }
}

const gonderDugmesi = () => screen.getByRole('button', { name: /Rota Oluştur/i })

describe('TourBuilder — doğrulama', () => {
  it('şehir seçilmeden gönderilirse rota servisine İSTEK GİTMEZ', async () => {
    const { rotaServisi, kullanici } = panelCiz()

    await kullanici.click(gonderDugmesi())

    expect(rotaServisi).not.toHaveBeenCalled()
    expect(await screen.findByText('Şehir seçilmelidir.')).toBeInTheDocument()
    // Düğmenin neden kapalı olduğu da yazıyor — ama aynı cümle tekrarlanmadan.
    expect(screen.getByText(/yukarıda işaretlendi/)).toBeInTheDocument()
  })

  it('form açılışta hata göstermez (kullanıcı henüz bir şey yapmadı)', () => {
    panelCiz()
    expect(screen.queryByText('Şehir seçilmelidir.')).not.toBeInTheDocument()
    expect(gonderDugmesi()).toBeEnabled()
  })

  it('geçersiz süre girilirse istek gitmez ve alan hatası görünür', async () => {
    const { rotaServisi, kullanici } = panelCiz()

    await kullanici.selectOptions(screen.getByLabelText('Başlangıç şehri'), '6')
    const sure = screen.getByLabelText('Saat')
    await kullanici.clear(sure)
    await kullanici.type(sure, '20')
    await kullanici.click(gonderDugmesi())

    expect(rotaServisi).not.toHaveBeenCalled()
    expect(screen.getAllByText(/1-12 saat/).length).toBeGreaterThan(0)
  })
})

describe('TourBuilder — seçimlerin payload\'a taşınması', () => {
  it('beş parametreyi de rota servisine geçirir', async () => {
    const { rotaServisi, kullanici } = panelCiz()

    await kullanici.selectOptions(screen.getByLabelText(/Bölge/), 'İç Anadolu')
    await kullanici.selectOptions(screen.getByLabelText('Başlangıç şehri'), '6')
    await kullanici.type(screen.getByLabelText(/İlçe/), 'Çankaya')
    await kullanici.click(screen.getByRole('radio', { name: 'Toplu Taşıma' }))
    await kullanici.click(screen.getByRole('radio', { name: /Çok günlük/ }))
    await kullanici.selectOptions(screen.getByLabelText('Tema'), 'Kulturel')
    await kullanici.click(screen.getByRole('radio', { name: 'Vegan' }))

    await kullanici.click(gonderDugmesi())

    await waitFor(() => expect(rotaServisi).toHaveBeenCalledTimes(1))

    const [payload] = rotaServisi.mock.calls[0]
    expect(payload.lokasyon).toEqual({
      bolge: 'İç Anadolu', ilPlaka: 6, ilAdi: 'Ankara', ekIlPlakalari: [], ilce: 'Çankaya',
    })
    expect(payload.ulasimTipi).toBe('TopluTasima')
    expect(payload.sure).toEqual({
      birim: 'Gun', deger: 2, gunlukSaat: 8, toplamDakika: 2 * 8 * 60,
      baslangicSaati: '09:00',
    })
    expect(payload.tema).toBe('Kulturel')
    expect(payload.beslenmeKisiti).toBe('Vegan')

    expect(payload.beslenmeEtiketleri).toEqual(['vegan'])
  })

  it('seçilen EK ŞEHİRLER payload’a giriyor (çok şehirli tur)', async () => {
    const { rotaServisi, kullanici } = panelCiz()

    await kullanici.selectOptions(screen.getByLabelText('Başlangıç şehri'), '6')
    await kullanici.click(screen.getByRole('checkbox', { name: 'İstanbul' }))
    await kullanici.click(gonderDugmesi())

    await waitFor(() => expect(rotaServisi).toHaveBeenCalledTimes(1))

    const [payload] = rotaServisi.mock.calls[0]
    expect(payload.lokasyon.ilPlaka).toBe(6)
    expect(payload.lokasyon.ekIlPlakalari).toEqual([34])
  })

  it('ek şehir listesinde BAŞLANGIÇ şehri yok', async () => {
    // Zaten turda; bir de işaretlenecek bir şeymiş gibi durması kafa
    // karıştırırdı (ve seçilseydi aynı şehir iki kez aranırdı).
    const { kullanici } = panelCiz()

    await kullanici.selectOptions(screen.getByLabelText('Başlangıç şehri'), '6')

    expect(screen.queryByRole('checkbox', { name: 'Ankara' })).toBeNull()
    expect(screen.getByRole('checkbox', { name: 'İstanbul' })).toBeInTheDocument()
  })

  it('şehir sınırına ulaşınca seçilmemiş kutular kapanıyor', async () => {
    const { kullanici } = panelCiz()

    await kullanici.selectOptions(screen.getByLabelText('Başlangıç şehri'), '6')
    await kullanici.click(screen.getByRole('checkbox', { name: 'İstanbul' }))
    await kullanici.click(screen.getByRole('checkbox', { name: 'İzmir' }))

    // Ankara + İstanbul + İzmir = 3, sınır dolu. Seçili olanlar AÇIK
    // kalmalı, yoksa kullanıcı seçimini geri alamaz.
    expect(screen.getByRole('checkbox', { name: 'İstanbul' })).toBeEnabled()
    expect(screen.getByRole('checkbox', { name: 'İzmir' })).toBeEnabled()
  })

  it('sonuç gelince özetini gösterir ve onSonuc ile haber verir', async () => {
    const onSonuc = vi.fn()
    const { kullanici } = panelCiz({ onSonuc })

    await kullanici.selectOptions(screen.getByLabelText('Başlangıç şehri'), '6')
    await kullanici.click(gonderDugmesi())

    expect(await screen.findByText('Ankara · Kültürel tur')).toBeInTheDocument()
    expect(onSonuc).toHaveBeenCalledTimes(1)
  })

  it('sunucu hatasını kullanıcıya gösterir, çökmez', async () => {
    const rotaServisi = vi.fn().mockRejectedValue(new Error('Sunucu hatası (500)'))
    render(<TourBuilder iller={ILLER} rotaServisi={rotaServisi} />)
    const kullanici = userEvent.setup()

    await kullanici.selectOptions(screen.getByLabelText('Başlangıç şehri'), '6')
    await kullanici.click(gonderDugmesi())

    expect(await screen.findByText('Sunucu hatası (500)')).toBeInTheDocument()
  })
})

describe('TourBuilder — bölge ve şehir tutarlılığı', () => {
  it('bölge değişince o bölgede olmayan şehir seçimi düşer', async () => {
    const { kullanici } = panelCiz()

    await kullanici.selectOptions(screen.getByLabelText('Başlangıç şehri'), '6')  // Ankara
    await kullanici.selectOptions(screen.getByLabelText(/Bölge/), 'Ege')

    // Seçim düştüğü için kullanıcı "listede yok" hatası hiç görmüyor.
    await waitFor(() =>
      expect(screen.getByLabelText('Başlangıç şehri')).toHaveValue('0'))
  })

  it('bölge seçilince şehir listesi o bölgeyle sınırlanır', async () => {
    const { kullanici } = panelCiz()

    await kullanici.selectOptions(screen.getByLabelText(/Bölge/), 'Marmara')

    const sehirler = screen.getByLabelText('Başlangıç şehri')
    expect(within(sehirler).queryByRole('option', { name: /İstanbul/ })).toBeTruthy()
    expect(within(sehirler).queryByRole('option', { name: /Ankara/ })).toBeNull()
  })

  it('bölge değişince EK ŞEHİRLERDEN o bölgede olmayanlar düşer', async () => {
    const { kullanici } = panelCiz()

    // Başlangıç: Ankara (İç Anadolu), ek şehir: İstanbul (Marmara).
    await kullanici.selectOptions(screen.getByLabelText('Başlangıç şehri'), '6')
    await kullanici.click(screen.getByRole('checkbox', { name: 'İstanbul' }))
    expect(screen.getByRole('checkbox', { name: 'İstanbul' })).toBeChecked()

    // İç Anadolu'ya süzülünce İstanbul listeden de seçimden de düşmeli.
    await kullanici.selectOptions(screen.getByLabelText(/Bölge/), 'İç Anadolu')

    await waitFor(() =>
      expect(screen.queryByRole('checkbox', { name: 'İstanbul' })).toBeNull())
  })

  it('çok günlük tura geçilince günlük gezi süresi alanı açılır', async () => {
    const { kullanici } = panelCiz()

    expect(screen.queryByLabelText(/Günlük gezi süresi/)).toBeNull()
    await kullanici.click(screen.getByRole('radio', { name: /Çok günlük/ }))
    expect(screen.getByLabelText(/Günlük gezi süresi/)).toBeInTheDocument()
  })
})

describe('turRotasiIste — bileşenden bağımsız çağrı', () => {
  const form = {
    bolge: '', ilPlaka: 34, ilce: '', ulasimTipi: 'Yaya',
    sureBirimi: 'Saat', sureDegeri: 5, gunlukSaat: 8,
    tema: 'Karma', beslenme: 'Vejetaryen',
  }

  it('geçerli formda payload\'ı servise geçirir ve sonucu döner', async () => {
    const rotaServisi = vi.fn().mockResolvedValue({ name: 'öneri' })

    const cevap = await turRotasiIste(form, { iller: ILLER, rotaServisi })

    expect(rotaServisi).toHaveBeenCalledWith(cevap.payload, undefined)
    expect(cevap.payload.lokasyon.ilAdi).toBe('İstanbul')
    expect(cevap.sonuc).toEqual({ name: 'öneri' })
  })

  it('geçersiz formda servisi HİÇ çağırmaz ve istisna da atmaz', async () => {
    const rotaServisi = vi.fn()

    const cevap = await turRotasiIste({ ...form, ilPlaka: 0 }, { iller: ILLER, rotaServisi })

    expect(rotaServisi).not.toHaveBeenCalled()
    expect(cevap.gecerli).toBe(false)
    expect(cevap.sonuc).toBeNull()
    expect(cevap.hatalar.ilPlaka).toBeTruthy()
  })
})
