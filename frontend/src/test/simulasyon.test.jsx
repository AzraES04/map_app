import { describe, it, expect, vi, beforeEach } from 'vitest'
import Feature from 'ol/Feature'
import PointGeom from 'ol/geom/Point'

import { aracIkonu, onbellekBoyutu } from '../aracIkonu'
import { aracStili } from '../pages/MapPage'

// ============================================================================
//  Ödev 19 — araç simülasyonu (istemci tarafı)
//
//  Buradaki testler iki "sessizce yanlış çalışan" noktayı hedefliyor:
//
//  1. SİMGE ÖNBELLEĞİ. Stil fonksiyonu saniyede iki kez, her araç için
//     çağrılıyor. Simge her çağrıda yeniden üretilseydi harita takılırdı —
//     ama ekranda hiçbir hata görünmezdi, yalnızca "biraz yavaş" olurdu.
//
//  2. SIGNALR GRUPLARI. Bağlantı koptuğunda SignalR yeniden bağlanıyor ama
//     grup üyelikleri sunucuda KALMIYOR (yeni bağlantı = yeni kimlik).
//     Yeniden katılmayı unutan uygulamada "bir süre sonra araç durdu"
//     hatası yaşanır ve sebebini bulmak çok zordur.
// ============================================================================

describe('aracIkonu — hattın rengiyle çizilen simge', () => {
  it('aynı renk için AYNI nesneyi döner (önbellek)', () => {
    const bir = aracIkonu('#d64550')
    const iki = aracIkonu('#d64550')

    expect(bir).toBe(iki)
  })

  it('farklı renk için ayrı simge üretir', () => {
    const onceki = onbellekBoyutu()

    const kirmizi = aracIkonu('#d64550')
    const yesil = aracIkonu('#3fbf88')

    expect(kirmizi).not.toBe(yesil)
    expect(onbellekBoyutu()).toBeGreaterThan(onceki)
  })

  it('simgenin içinde hattın rengi geçiyor', () => {
    // Renk gövdeye gerçekten yazılıyor mu? Yazılmasaydı bütün araçlar
    // varsayılan mavi çizilir ve iki hat birbirinden ayırt edilemezdi.
    const svg = atob(aracIkonu('#8e44ad').getSrc().split(',')[1])

    expect(svg).toContain('#8e44ad')
    expect(svg).toContain('<svg')
  })
})

describe('aracStili — yüzde rozeti', () => {
  const arac = (ozellikler) => {
    const f = new Feature({ geometry: new PointGeom([0, 0]) })
    f.setProperties({ renk: '#d64550', yuzde: 42.4, ...ozellikler })
    return f
  }

  it('yüzdeyi yuvarlayıp aracın altına yazıyor', () => {
    const stil = aracStili()(arac())

    expect(stil.getText().getText()).toBe('%42')
    expect(stil.getImage()).toBe(aracIkonu('#d64550'))   // önbellekteki simge
  })

  it('yüzde bilgisi yoksa sıfır yazıyor, çökmüyor', () => {
    const stil = aracStili()(arac({ yuzde: undefined }))

    expect(stil.getText().getText()).toBe('%0')
  })

  it('rozetin çerçevesi hattın renginde', () => {
    // Rozet ile araç aynı renk ailesinde olmalı: iki hat aynı anda sefer
    // yaparken hangi yüzdenin hangi araca ait olduğu buradan okunuyor.
    const stil = aracStili()(arac({ renk: '#3fbf88' }))

    expect(stil.getText().getBackgroundStroke().getColor()).toBe('#3fbf88')
  })
})

// ---------------------------------------------------------------------------
//  SignalR sarmalayıcısı — gerçek kütüphane taklit ediliyor
// ---------------------------------------------------------------------------

const sahteBaglanti = {
  state: 'Disconnected',
  start: vi.fn(async () => { sahteBaglanti.state = 'Connected' }),
  stop: vi.fn(async () => { sahteBaglanti.state = 'Disconnected' }),
  invoke: vi.fn(async () => {}),
  on: vi.fn(),
  onreconnected: vi.fn(),
}

vi.mock('@microsoft/signalr', () => {
  class SahteKurucu {
    withUrl() { return this }

    withAutomaticReconnect() { return this }

    configureLogging() { return this }

    build() { return sahteBaglanti }
  }

  return {
    HubConnectionBuilder: SahteKurucu,
    // Modül bu iki sabiti de okuyor; string değerler yeterli.
    HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected' },
    LogLevel: { Warning: 3 },
  }
})

// auth.js'in TAMAMI değil yalnızca token okuyucusu taklit ediliyor:
// aynı modülden MapPage başka şeyler de alıyor (açılış animasyonu bayrağı,
// oturum sayacı). Tamamını sahtelemek onları da silerdi.
vi.mock('../auth', async (asliniAl) => ({
  ...(await asliniAl()),
  getToken: () => 'sahte-token',
}))

describe('simulasyonHub — grup üyelikleri', () => {
  let hub

  beforeEach(async () => {
    vi.clearAllMocks()
    sahteBaglanti.state = 'Disconnected'
    // Modül düzeyinde durum tutuyor (bağlantı + gruplar); her testte
    // taze bir kopya alıyoruz ki testler birbirini etkilemesin.
    vi.resetModules()
    hub = await import('../simulasyonHub')
  })

  it('"Takip Et" sunucudaki Katil metodunu çağırıyor', async () => {
    await hub.guzergahiTakipEt(7)

    expect(sahteBaglanti.invoke).toHaveBeenCalledWith('Katil', 7)
    expect(hub.takipEdilenGruplar()).toEqual([7])
  })

  it('"Takibi Bırak" gruptan çıkıyor', async () => {
    await hub.guzergahiTakipEt(7)
    await hub.takibiBirak(7)

    expect(sahteBaglanti.invoke).toHaveBeenCalledWith('Ayril', 7)
    expect(hub.takipEdilenGruplar()).toEqual([])
  })

  it('YENİDEN BAĞLANINCA gruplara tekrar katılıyor', async () => {
    await hub.guzergahiTakipEt(7)
    await hub.guzergahiTakipEt(9)
    sahteBaglanti.invoke.mockClear()

    // Kütüphanenin "yeniden bağlandım" bildirimini elle tetikliyoruz.
    const geriCagir = sahteBaglanti.onreconnected.mock.calls[0][0]
    geriCagir()

    expect(sahteBaglanti.invoke).toHaveBeenCalledWith('Katil', 7)
    expect(sahteBaglanti.invoke).toHaveBeenCalledWith('Katil', 9)
  })

  it('bağlantı kurulamazsa takip false döner, çökmez', async () => {
    sahteBaglanti.start.mockRejectedValueOnce(new Error('sunucu kapalı'))

    expect(await hub.guzergahiTakipEt(7)).toBe(false)
  })

  it('dinleyiciye gelen konum mesajı iletiliyor', async () => {
    const gelenler = []
    hub.konumDinle((durum) => gelenler.push(durum))
    await hub.hubaBaglan()

    // Sunucudan mesaj geldi: kütüphanenin çağıracağı geri çağrımı bulup
    // elle tetikliyoruz.
    const [olayAdi, geriCagir] = sahteBaglanti.on.mock.calls[0]
    geriCagir({ guzergahId: 7, yuzde: 12 })

    expect(olayAdi).toBe('KonumGuncellendi')
    expect(gelenler).toEqual([{ guzergahId: 7, yuzde: 12 }])
  })
})
