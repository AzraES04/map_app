import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import ErisilebilirlikPaneli, { yuzdeMetni } from '../pages/ErisilebilirlikPaneli'

// ============================================================================
//  TOPLU TAŞIMA ERİŞİLEBİLİRLİK PANELİ
//
//  Canlıda görülen hata: Ankara + İstanbul birlikte seçilince "%0" çıktı ve
//  kullanıcı "hepsi uzak olamaz, analiz yanlış" diye bildirdi. Sayı aslında
//  doğruydu (25.000 km²'lik bir alanın kırsalı da sayılıyor) ama SUNUCUNUN
//  ürettiği bağlam notları (uyarilar) ekrana hiç basılmıyordu — kullanıcı
//  çıplak bir "%0" görüyordu. Buradaki testler o bağlamın gerçekten
//  ekrana çıktığını koruyor.
// ============================================================================

const ILLER = [
  { plaka: 6, ad: 'Ankara' },
  { plaka: 34, ad: 'İstanbul' },
]

function panelCiz(ekstra = {}) {
  const onCalistir = vi.fn()

  const varsayilan = {
    iller: ILLER,
    illerHatasi: null,
    sonuc: null,
    yukleniyor: false,
    hata: null,
    onCalistir,
    onTemizle: vi.fn(),
  }

  const kullanici = userEvent.setup()
  render(<ErisilebilirlikPaneli {...varsayilan} {...ekstra} />)

  return { kullanici, onCalistir }
}

describe('yuzdeMetni', () => {
  it('tam sıfırda "%0" diyor', () => {
    expect(yuzdeMetni(0)).toBe('%0’ı')
  })

  it('SIFIRDAN BÜYÜK ama çok küçük oranı "%0" diye YUTMUYOR', () => {
    // Bu testin varlık sebebi tam olarak canlıdaki yanılgı: %0,03 gibi bir
    // oran ekranda düz "%0" olarak görününce kullanıcı "hiç erişim yok"
    // sanıyordu, oysa haritada sarı adacıklar vardı.
    expect(yuzdeMetni(0.03)).not.toBe('%0’ı')
    expect(yuzdeMetni(0.03)).toMatch(/0,1/)
  })

  it('normal bir oranı Türkçe ondalıkla yazıyor', () => {
    // Ek bilerek ünlü uyumuna göre seçilmiyor — "…'i" tek biçim her
    // yüzdede kullanılıyor (bkz. yuzdeMetni'nin kendi yorumu).
    expect(yuzdeMetni(3.4)).toBe('%3,4’i')
  })
})

describe('ErisilebilirlikPaneli — sonuç okunurluğu', () => {
  it('SUNUCUNUN UYARILARINI gösteriyor', () => {
    // Bu, gerçek hatanın kendisiydi: backend notu üretiyordu, panel onu
    // hiç basmıyordu.
    panelCiz({
      sonuc: {
        alanAdi: 'Ankara, İstanbul',
        durakSayisi: 15,
        iyiErisimYuzdesi: 0,
        uyarilar: [
          'Oran ilin tamamı üzerinden hesaplanıyor; kırsal alanlar da ' +
            'sayıldığı için düşük çıkması doğaldır. Sarı adacıklar ' +
            'durakların çevresini gösterir.',
          'Bu bölge için sistemde yalnızca 15 durak kayıtlı. Analiz ' +
            'gerçek ağı değil, kayıtlı durakları ölçer.',
        ],
        izgara: { hucreMetre: 975 },
      },
    })

    expect(screen.getByText(/kırsal alanlar da/)).toBeInTheDocument()
    expect(screen.getByText(/yalnızca 15 durak kayıtlı/)).toBeInTheDocument()
  })

  it('uyarı YOKSA not listesini hiç basmıyor (boş <ul> yok)', () => {
    panelCiz({
      sonuc: {
        alanAdi: 'Çankaya',
        durakSayisi: 40,
        iyiErisimYuzdesi: 62,
        uyarilar: [],
        izgara: { hucreMetre: 200 },
      },
    })

    expect(document.querySelector('.erisilebilirlik-notlar')).toBeNull()
  })

  it('çok küçük ama sıfır olmayan oranı "%0" diye YAZDIRMIYOR', () => {
    panelCiz({
      sonuc: {
        alanAdi: 'Ankara, İstanbul',
        durakSayisi: 15,
        iyiErisimYuzdesi: 0.03,
        uyarilar: [],
        izgara: { hucreMetre: 975 },
      },
    })

    // "%0'ı bir durağa..." metni tek başına görünürse kullanıcı yine
    // "hiç erişim yok" okur.
    expect(screen.queryByText(/^%0’ı/)).toBeNull()
    expect(screen.getByText(/0,1’inden azı/)).toBeInTheDocument()
  })

  it('hücre çözünürlüğünü gösteriyor', () => {
    panelCiz({
      sonuc: {
        alanAdi: 'Ankara, İstanbul',
        durakSayisi: 15,
        iyiErisimYuzdesi: 0.23,
        uyarilar: [],
        izgara: { hucreMetre: 975 },
      },
    })

    expect(screen.getByText(/hücre ≈ 975 m/)).toBeInTheDocument()
  })

  it('durak sayısı sıfırsa yüzey yerine engel mesajı gösteriyor', () => {
    panelCiz({
      sonuc: { alanAdi: 'Kars', durakSayisi: 0, iyiErisimYuzdesi: 0, uyarilar: [], izgara: {} },
    })

    expect(screen.getByText(/kayıtlı durak yok/)).toBeInTheDocument()
  })

  it('il seçmeden Analiz Et çalışmıyor', async () => {
    const { kullanici, onCalistir } = panelCiz()

    await kullanici.click(screen.getByRole('button', { name: /Analiz Et/ }))

    expect(onCalistir).not.toHaveBeenCalled()
  })

  it('il seçilince onCalistir doğru gövdeyle çağrılıyor', async () => {
    const { kullanici, onCalistir } = panelCiz()

    await kullanici.click(screen.getByRole('checkbox', { name: 'Ankara' }))
    await kullanici.click(screen.getByRole('checkbox', { name: 'İstanbul' }))
    await kullanici.click(screen.getByRole('button', { name: /Analiz Et/ }))

    expect(onCalistir).toHaveBeenCalledWith({
      ilPlakalari: [6, 34],
      yalnizcaAktif: true,
    })
  })
})
