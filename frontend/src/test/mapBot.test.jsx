import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import MapBot from '../MapBot'
import {
  cevapBul, devamSorulari, sadelestir, vurguluParcala,
  KONULAR, BILMIYORUM, ONERILEN_SORULAR,
} from '../mapBotBilgisi'

// ============================================================================
//  MAP BOT
//
//  Botun değeri CEVAPLARININ DOĞRU OLMASI. Bir dil modeline bağlamama
//  gerekçesi de buydu: uydurulmuş bir adım listesi, kullanıcıyı olmayan bir
//  düğmeyi aramaya gönderir.
//
//  O yüzden testler iki şeyi koruyor: doğru konunun seçilmesi ve
//  eşleşme yokken botun BİLMEDİĞİNİ SÖYLEMESİ.
// ============================================================================

describe('sadelestir', () => {
  it('Türkçe karakterleri ve noktalamayı sadeleştiriyor', () => {
    // "Şehir" yazan da "sehir" yazan da aynı cevabı almalı.
    expect(sadelestir('Şehir, Güzergâh?')).toBe('sehir guzergah')
    expect(sadelestir('  ÇOK   BOŞLUK  ')).toBe('cok bosluk')
  })

  it('boş girdide boş dönüyor', () => {
    expect(sadelestir(null)).toBe('')
    expect(sadelestir('')).toBe('')
  })
})

describe('cevapBul', () => {
  const durumlar = [
    ['merhaba', 'selam'],
    ['nasıl tur oluştururum', 'tur-olustur'],
    ['turu nasıl paylaşırım', 'paylasim'],
    ['katılım kodu nedir', 'paylasim'],
    ['yoklama nasıl yapılır', 'yoklama'],
    ['kimler burada', 'yoklama'],
    ['canlı konum paylaşabilir miyim', 'canli-konum'],
    ['vegan seçeneği var mı', 'beslenme'],
    ['serbest zaman nedir', 'mola-konaklama'],
    ['otel önerir mi', 'mola-konaklama'],
    ['poiler görünmüyor', 'poi-gorunmuyor'],
    ['rotayı oynatabilir miyim', 'simulasyon'],
    ['durak çıkarmak istiyorum', 'tur-duzenle'],
    // Sonradan eklenen konular — kapsamın genişlediğini burası koruyor.
    ['çöp kutusundan kalıcı sil', 'cop-kutusu'],
    ['davet kodu ne işe yarar', 'davet-kodu'],
    ['hesapsız nasıl girerim', 'misafir'],
    ['rehbere telefonla ulaşabilir miyim', 'rehbere-ulas'],
    ['toplu taşıma erişilebilirliği nedir', 'erisilebilirlik'],
    ['konum analizi nasıl yapılır', 'konum-analizi'],
    ['haritaya alan çizmek istiyorum', 'cizim'],
    ['güzergah alternatifleri', 'guzergah'],
    ['neler biliyorsun', 'ne-yapabilirsin'],
  ]

  it.each(durumlar)('"%s" → %s konusunu buluyor', (soru, beklenen) => {
    expect(cevapBul(soru).ad).toBe(beklenen)
  })

  it('alakasız soruda BİLMİYORUM diyor', () => {
    // Uydurmak, kullanıcıyı olmayan bir düğmeyi aramaya göndermek olurdu.
    expect(cevapBul('bugün hava nasıl').cevap).toBe(BILMIYORUM)
    expect(cevapBul('').cevap).toBe(BILMIYORUM)
  })

  it('her konunun anahtarı ve cevabı var', () => {
    // Boş bir konu, hiç eşleşmeyen ya da boş balon üreten bir kayıt demek.
    for (const konu of KONULAR) {
      expect(konu.anahtarlar.length).toBeGreaterThan(0)
      expect(konu.cevap.length).toBeGreaterThan(20)
    }
  })

  it('konu adları tekrar etmiyor', () => {
    // Aynı adı taşıyan iki konu, testlerde hangisinin eşleştiğini
    // belirsizleştirir ve biri sonsuza dek ölü kalabilir.
    const adlar = KONULAR.map((k) => k.ad)
    expect(new Set(adlar).size).toBe(adlar.length)
  })

  it('önerilen soruların HEPSİ bir cevaba düşüyor', () => {
    // Hazır soruya tıklayıp "bunu bilmiyorum" almak, botun kendi vitrinini
    // yalanlaması olurdu.
    for (const soru of ONERILEN_SORULAR) {
      expect(cevapBul(soru).cevap).not.toBe(BILMIYORUM)
    }
  })
})

describe('devamSorulari', () => {
  it('cevabın ardından ilgili soruları veriyor', () => {
    const devam = devamSorulari('tur-olustur')

    expect(devam.length).toBeGreaterThan(0)
    expect(devam.length).toBeLessThanOrEqual(3)
  })

  it('devam sorularının HEPSİ bir cevaba düşüyor', () => {
    // Zincirin kopmaması gereken yer burası: bota kendi önerdiği soruyu
    // sorup "bunu bilmiyorum" almak, sohbeti duvara toslatır.
    for (const konu of KONULAR) {
      for (const soru of devamSorulari(konu.ad)) {
        expect(cevapBul(soru).cevap).not.toBe(BILMIYORUM)
      }
    }
  })

  it('bilinmeyen konuda boş dönüyor', () => {
    expect(devamSorulari('bilinmiyor')).toEqual([])
  })

  it('bir konu kendi devamı olarak önerilmiyor', () => {
    // Aynı cevabı tekrar veren bir düğme, sohbeti ilerletmiyor.
    for (const konu of KONULAR) {
      for (const soru of devamSorulari(konu.ad)) {
        expect(cevapBul(soru).ad).not.toBe(konu.ad)
      }
    }
  })
})

describe('vurguluParcala', () => {
  it('**kalın** işaretlerini ayırıyor', () => {
    expect(vurguluParcala('önce **Tur Planla** sonra')).toEqual([
      { kalin: false, metin: 'önce ' },
      { kalin: true, metin: 'Tur Planla' },
      { kalin: false, metin: ' sonra' },
    ])
  })

  it('işaretsiz metni tek parça bırakıyor', () => {
    expect(vurguluParcala('düz metin')).toEqual([{ kalin: false, metin: 'düz metin' }])
    expect(vurguluParcala('')).toEqual([])
  })

  it('hiçbir cevapta ekrana yıldız sızmıyor', () => {
    // Asıl korunan şey bu: kullanıcı balonda "**Tur Planla**" görüyordu.
    for (const konu of KONULAR) {
      const duz = vurguluParcala(konu.cevap).map((p) => p.metin).join('')
      expect(duz).not.toContain('**')
    }
  })
})

describe('MapBot penceresi', () => {
  it('kapalıyken yalnızca düğme var', () => {
    render(<MapBot />)

    expect(screen.getByRole('button', { name: /Map Bot'u aç/ })).toBeInTheDocument()
    expect(screen.queryByLabelText("Map Bot'a sorunuz")).toBeNull()
  })

  it('açılınca kendini tanıtıyor', async () => {
    const kullanici = userEvent.setup()
    render(<MapBot />)

    await kullanici.click(screen.getByRole('button', { name: /Map Bot'u aç/ }))

    expect(screen.getByText(/adım Map Bot/)).toBeInTheDocument()
  })

  it('soruyu cevaplıyor', async () => {
    const kullanici = userEvent.setup()
    render(<MapBot />)

    await kullanici.click(screen.getByRole('button', { name: /Map Bot'u aç/ }))
    await kullanici.type(screen.getByLabelText("Map Bot'a sorunuz"), 'turu nasıl paylaşırım')
    await kullanici.click(screen.getByRole('button', { name: 'Sor' }))

    expect(await screen.findByText(/Turu Kaydet ve Paylaş/)).toBeInTheDocument()
  })

  it('boş soru gönderilmiyor', async () => {
    const kullanici = userEvent.setup()
    render(<MapBot />)

    await kullanici.click(screen.getByRole('button', { name: /Map Bot'u aç/ }))
    await kullanici.click(screen.getByRole('button', { name: 'Sor' }))

    // Yalnızca karşılama mesajı kalmalı.
    expect(document.querySelectorAll('.mapbot-mesaj')).toHaveLength(1)
  })

  it('hazır soruya tıklanınca cevap veriyor', async () => {
    const kullanici = userEvent.setup()
    render(<MapBot />)

    await kullanici.click(screen.getByRole('button', { name: /Map Bot'u aç/ }))
    await kullanici.click(screen.getByRole('button', { name: ONERILEN_SORULAR[0] }))

    // Soru akışa yazıldı ve cevabı geldi.
    expect(await screen.findByText(/Rota Oluştur/)).toBeInTheDocument()
  })

  it('hazır sorular ilk cevaptan sonra kayboluyor', async () => {
    const kullanici = userEvent.setup()
    render(<MapBot />)

    await kullanici.click(screen.getByRole('button', { name: /Map Bot'u aç/ }))
    expect(document.querySelectorAll('.mapbot-oneriler button').length)
      .toBe(ONERILEN_SORULAR.length)

    await kullanici.click(screen.getByRole('button', { name: ONERILEN_SORULAR[0] }))

    // Kullanıcı ne aradığını artık biliyor; liste sohbeti aşağı itmemeli.
    expect(document.querySelectorAll('.mapbot-oneriler button')).toHaveLength(0)
  })

  it('cevaptaki düğme adları kalın basılıyor, yıldızla değil', async () => {
    const kullanici = userEvent.setup()
    render(<MapBot />)

    await kullanici.click(screen.getByRole('button', { name: /Map Bot'u aç/ }))
    await kullanici.type(screen.getByLabelText("Map Bot'a sorunuz"), 'turu nasıl paylaşırım')
    await kullanici.click(screen.getByRole('button', { name: 'Sor' }))

    const vurgu = await screen.findByText('Turu Kaydet ve Paylaş')
    expect(vurgu.tagName).toBe('STRONG')

    // Ham işaret ekranda kalmamalı.
    expect(document.querySelector('.mapbot-akis').textContent).not.toContain('**')
  })

  it('cevabın altında paneli açan eylem düğmesi var', async () => {
    const kullanici = userEvent.setup()
    const eylemler = []
    render(<MapBot onEylem={(ad) => eylemler.push(ad)} />)

    await kullanici.click(screen.getByRole('button', { name: /Map Bot'u aç/ }))
    await kullanici.type(screen.getByLabelText("Map Bot'a sorunuz"), 'nasıl tur oluştururum')
    await kullanici.click(screen.getByRole('button', { name: 'Sor' }))

    const eylem = await screen.findByRole('button', { name: /Tur Planla/ })
    await kullanici.click(eylem)

    expect(eylemler).toEqual(['tur-panel'])
    // Eylem sonrası bot kapanıyor: kullanıcı artık panelde.
    expect(screen.getByRole('button', { name: /Map Bot'u aç/ })).toBeInTheDocument()
  })

  it('onEylem verilmezse eylem düğmesi çıkmıyor', async () => {
    const kullanici = userEvent.setup()
    render(<MapBot />)

    await kullanici.click(screen.getByRole('button', { name: /Map Bot'u aç/ }))
    await kullanici.type(screen.getByLabelText("Map Bot'a sorunuz"), 'nasıl tur oluştururum')
    await kullanici.click(screen.getByRole('button', { name: 'Sor' }))

    // Cevap geldi ama hiçbir yere gitmeyen bir düğme yok.
    await screen.findByText(/Rota Oluştur/)
    expect(document.querySelector('.mapbot-eylem')).toBeNull()
  })

  it('cevaptan sonra devam soruları çıkıyor', async () => {
    const kullanici = userEvent.setup()
    render(<MapBot />)

    await kullanici.click(screen.getByRole('button', { name: /Map Bot'u aç/ }))
    await kullanici.type(screen.getByLabelText("Map Bot'a sorunuz"), 'nasıl tur oluştururum')
    await kullanici.click(screen.getByRole('button', { name: 'Sor' }))
    await screen.findByText(/Rota Oluştur/)

    const oneriler = [...document.querySelectorAll('.mapbot-oneriler button')]
    expect(oneriler.length).toBeGreaterThan(0)

    // Açılış önerileri değil, konuya özel devam soruları olmalı.
    expect(oneriler.map((d) => d.textContent))
      .not.toEqual(expect.arrayContaining([ONERILEN_SORULAR[0]]))
  })

  it('kapatılabiliyor', async () => {
    const kullanici = userEvent.setup()
    render(<MapBot />)

    await kullanici.click(screen.getByRole('button', { name: /Map Bot'u aç/ }))
    await kullanici.click(screen.getByRole('button', { name: /Map Bot'u kapat/ }))

    expect(screen.getByRole('button', { name: /Map Bot'u aç/ })).toBeInTheDocument()
  })
})
