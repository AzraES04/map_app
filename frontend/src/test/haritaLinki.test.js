import { describe, it, expect, vi } from 'vitest'

import { haritaLinki, platformBul, yolTarifiniAc } from '../haritaLinki'

// ============================================================================
//  Harita derin bağlantısı
//
//  Bu dosyanın koruduğu şey gözle denetlenmesi zor bir ayrıntı: koordinatın
//  SIRASI ve BİÇİMİ. WKT "boylam enlem" yazar, harita adresleri "enlem,boylam"
//  ister. İkisini karıştırmak hata vermez — kullanıcıyı Ankara yerine
//  Somali açıklarına gönderir. Tam olarak bu yüzden testi var.
// ============================================================================

/** Anıtkabir'e yakın bir nokta. */
const ANITKABIR = { lat: 39.925, lon: 32.836, ad: 'Anıtkabir' }

describe('platformBul', () => {
  it('iPhone ve Android kullanıcı aracılarını tanır', () => {
    expect(platformBul('Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)')).toBe('ios')
    expect(platformBul('Mozilla/5.0 (Linux; Android 14; Pixel 8)')).toBe('android')
    expect(platformBul('Mozilla/5.0 (Windows NT 10.0; Win64; x64)')).toBe('diger')
  })

  it('iPadOS kendini Mac olarak tanıtsa da dokunmatikten yakalanır', () => {
    // iPadOS 13+ Safari "Macintosh" diyor; dokunmatik nokta sayısına
    // bakmasaydık iPad kullanıcısı Apple Haritalar yerine tarayıcıya düşerdi.
    const ipad = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)'
    expect(platformBul(ipad, 5)).toBe('ios')
    expect(platformBul(ipad, 0)).toBe('diger')   // gerçek Mac
  })
})

describe('haritaLinki', () => {
  it('iOS için Apple Haritalar adresi üretir', () => {
    const adres = haritaLinki({ ...ANITKABIR, platform: 'ios', ulasimTipi: 'Yaya' })
    const url = new URL(adres)

    expect(url.hostname).toBe('maps.apple.com')
    expect(url.searchParams.get('daddr')).toBe('39.925000,32.836000')
    expect(url.searchParams.get('dirflg')).toBe('w')
    expect(url.searchParams.get('q')).toBe('Anıtkabir')
  })

  it('Android ve masaüstü için Google Maps yol tarifi adresi üretir', () => {
    for (const platform of ['android', 'diger']) {
      const url = new URL(haritaLinki({ ...ANITKABIR, platform }))

      expect(url.hostname).toBe('www.google.com')
      expect(url.pathname).toBe('/maps/dir/')
      expect(url.searchParams.get('api')).toBe('1')
      expect(url.searchParams.get('destination')).toBe('39.925000,32.836000')
    }
  })

  it('koordinatı ENLEM,BOYLAM sırasıyla yazar', () => {
    // WKT "boylam enlem" sırasında; adres tersini istiyor. Sıra bozulsaydı
    // 32.8 enlem / 39.9 boylam = Somali açıkları olurdu.
    const url = new URL(haritaLinki({ lat: 39.925, lon: 32.836, platform: 'diger' }))
    const [enlem, boylam] = url.searchParams.get('destination').split(',')

    expect(Number(enlem)).toBeCloseTo(39.925)
    expect(Number(boylam)).toBeCloseTo(32.836)
  })

  it('ulaşım tipini her iki sağlayıcının kip diline çevirir', () => {
    const google = (tip) =>
      new URL(haritaLinki({ ...ANITKABIR, platform: 'diger', ulasimTipi: tip }))
        .searchParams.get('travelmode')

    const apple = (tip) =>
      new URL(haritaLinki({ ...ANITKABIR, platform: 'ios', ulasimTipi: tip }))
        .searchParams.get('dirflg')

    expect(google('Yaya')).toBe('walking')
    expect(google('Arac')).toBe('driving')
    expect(google('TopluTasima')).toBe('transit')

    expect(apple('Yaya')).toBe('w')
    expect(apple('Arac')).toBe('d')
    expect(apple('TopluTasima')).toBe('r')
  })

  it('tanınmayan ulaşım tipinde yaya kipine düşer', () => {
    const url = new URL(haritaLinki({ ...ANITKABIR, platform: 'diger', ulasimTipi: 'Helikopter' }))
    expect(url.searchParams.get('travelmode')).toBe('walking')
  })

  it('başlangıç noktası GÖNDERMEZ', () => {
    // Konum harita uygulamasının kendi bildiği bir şey; URL'e yazmak hem
    // eski bir konumu dayatmak hem de gereksiz bir veri sızıntısı olurdu.
    const adres = haritaLinki({ ...ANITKABIR, platform: 'diger' })
    expect(adres).not.toContain('origin')
    expect(adres).not.toContain('saddr')
  })

  it('geçersiz koordinatta null döner', () => {
    expect(haritaLinki({ lat: undefined, lon: 32.8 })).toBeNull()
    expect(haritaLinki({ lat: 91, lon: 32.8 })).toBeNull()      // enlem sınırı
    expect(haritaLinki({ lat: 39.9, lon: 181 })).toBeNull()     // boylam sınırı
    expect(haritaLinki({ lat: Number.NaN, lon: 32.8 })).toBeNull()
    expect(haritaLinki()).toBeNull()
  })
})

describe('yolTarifiniAc', () => {
  it('üretilen adresi açıcıya verir', () => {
    const ac = vi.fn()
    const sonuc = yolTarifiniAc({ ...ANITKABIR, platform: 'diger' }, ac)

    expect(sonuc).toBe(true)
    expect(ac).toHaveBeenCalledTimes(1)
    expect(ac.mock.calls[0][0]).toContain('destination=39.925000%2C32.836000')
  })

  it('geçersiz hedefte hiçbir şey açmaz', () => {
    const ac = vi.fn()
    expect(yolTarifiniAc({ lat: null, lon: null }, ac)).toBe(false)
    expect(ac).not.toHaveBeenCalled()
  })
})
