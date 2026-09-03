import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import ActiveTourView from '../pages/ActiveTourView'
import { EYLEM, ILK_DURUM, TUR_ROLU, turReducer } from '../turDurumu'

// ============================================================================
//  ActiveTourView — rol ayrımının sınavı
//
//  Ekranın kendisi iki role de aynı bilgiyi gösteriyor; FARK eylem
//  çubuğunda:
//
//    Guide       → "Sonraki Durağa Geç" (oturum state'ini ilerletir)
//    Participant → "Yol Tarifi Al"      (cihazın haritasına deep link)
//
//  Buradaki testler tam da bu ayrımı ve state bağlamasını koruyor: katılımcı
//  ekranında ilerletme düğmesinin ÇIKMAMASI, rehber ekranında da yol tarifi
//  yerine ilerletmenin gelmesi. Rol kontrolü asıl olarak sunucuda; ama yanlış
//  düğmeyi göstermek, katılımcıya yapamayacağı bir işi vaat etmek olurdu.
// ============================================================================

const durak = (id, order, ad, lon, lat, dwell = 30) => ({
  id,
  tourId: 1,
  order,
  name: ad,
  venueType: 'Museum',
  dwellMinutes: dwell,
  wkt: `POINT (${lon} ${lat})`,
})

const TUR = {
  id: 1,
  name: 'Ankara Kale Turu',
  color: '#7b5cd6',
  waypoints: [
    durak(10, 1, 'Anıtkabir', 32.836, 39.925, 45),
    durak(11, 2, 'Ulus', 32.848, 39.925, 30),
    durak(12, 3, 'Ankara Kalesi', 32.860, 39.940, 20),
  ],
  isActive: true,
}

const OTURUM = {
  id: 7,
  tourId: 1,
  tourName: TUR.name,
  color: TUR.color,
  status: 'Live',
  guideUserId: 3,
  guideUserName: 'rehber',
  startedUtc: new Date(Date.now() - 60 * 60_000).toISOString(),
  currentWaypointId: 10,
  currentWaypointOrder: 1,
  progressPercent: 20,
  participantCount: 2,
  myRole: TUR_ROLU.KATILIMCI,
  participants: [
    { userId: 3, userName: 'rehber', role: TUR_ROLU.REHBER, joinedUtc: 'x', leftUtc: null },
    { userId: 4, userName: 'gezgin', role: TUR_ROLU.KATILIMCI, joinedUtc: 'x', leftUtc: null },
  ],
}

/** Reducer'ı gerçek eylemlerle kurup bileşene bağlar — state bağlaması da sınanmış oluyor. */
function ekraniCiz({ oturum = OTURUM, ...ekstra } = {}) {
  const durum = [
    { tur: EYLEM.TURLAR_GELDI, turlar: [TUR] },
    { tur: EYLEM.OTURUM_GUNCELLENDI, oturum },
    { tur: EYLEM.OTURUM_IZLENIYOR, oturumId: oturum.id },
  ].reduce(turReducer, ILK_DURUM)

  const dispatch = vi.fn()
  const sonrakiDuragaGec = vi.fn().mockResolvedValue({
    ...oturum,
    currentWaypointId: 11,
    currentWaypointOrder: 2,
  })
  const haritayiAc = vi.fn()
  const turuSonlandir = vi.fn().mockResolvedValue({ ...oturum, status: 'Completed' })

  render(
    <ActiveTourView
      durum={durum}
      dispatch={dispatch}
      sonrakiDuragaGec={sonrakiDuragaGec}
      turuSonlandir={turuSonlandir}
      haritayiAc={haritayiAc}
      ulasimTipi="Yaya"
      {...ekstra}
    />,
  )

  return { dispatch, sonrakiDuragaGec, turuSonlandir, haritayiAc, kullanici: userEvent.setup() }
}

const rehberOturumu = (ek = {}) => ({ ...OTURUM, myRole: TUR_ROLU.REHBER, ...ek })

describe('ActiveTourView — ortak bilgiler', () => {
  it('mevcut durağı, sıradakini ve kalan süreyi gösterir', () => {
    ekraniCiz()

    expect(screen.getByText('Anıtkabir')).toBeInTheDocument()
    expect(screen.getByText('Ulus')).toBeInTheDocument()
    // Sayı <strong> içinde, gerisi düz metin: tek bir metin düğümünde
    // aranmıyor, paragrafın tamamına bakılıyor.
    expect(screen.getByText(
      (_, el) => el?.tagName === 'P' && /1\s*\/\s*3 durak/.test(el.textContent),
    )).toBeInTheDocument()
    expect(screen.getByText(/Turun kalan süresi/)).toBeInTheDocument()

    // Sıradaki durağa mesafe/süre TAHMİN olduğu için "≈" ile gösteriliyor.
    expect(screen.getByText(/≈/)).toBeInTheDocument()
  })

  it('takip edilen tur yoksa hiçbir şey çizmez', () => {
    const dispatch = vi.fn()
    const { container } = render(<ActiveTourView durum={ILK_DURUM} dispatch={dispatch} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('henüz ilk durağa varılmadıysa yolda olunduğunu söyler', () => {
    ekraniCiz({
      oturum: { ...OTURUM, currentWaypointId: null, currentWaypointOrder: null },
    })

    expect(screen.getByText(/ilk durağa doğru yolda/)).toBeInTheDocument()
    expect(screen.getByText('Anıtkabir')).toBeInTheDocument()   // sıradaki
  })
})

describe('Guide — sonraki durağa geçme', () => {
  it('rehbere ilerletme düğmesi gösterilir, yol tarifi gösterilmez', () => {
    ekraniCiz({ oturum: rehberOturumu() })

    expect(screen.getByRole('button', { name: /Sonraki Durağa Geç/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Yol Tarifi Al/ })).toBeNull()
  })

  it('düğme servisi SIRADAKİ durağın id\'siyle çağırır ve sonucu duruma yazar', async () => {
    const { sonrakiDuragaGec, dispatch, kullanici } = ekraniCiz({ oturum: rehberOturumu() })

    await kullanici.click(screen.getByRole('button', { name: /Sonraki Durağa Geç/ }))

    await waitFor(() => expect(sonrakiDuragaGec).toHaveBeenCalledTimes(1))

    // Sıradaki durak "Ulus" (id 11) — "bir sonraki" demek yerine açık id
    // gönderiliyor (bkz. turApi.sonrakiDuragaGec).
    expect(sonrakiDuragaGec).toHaveBeenCalledWith(7, 11, undefined)

    // STATE BAĞLAMASI: sunucunun döndürdüğü oturum kaydı, yayından gelen
    // mesajla AYNI eylemle duruma giriyor.
    await waitFor(() => expect(dispatch).toHaveBeenCalledTimes(1))
    const eylem = dispatch.mock.calls[0][0]
    expect(eylem.tur).toBe(EYLEM.OTURUM_GUNCELLENDI)
    expect(eylem.oturum.currentWaypointId).toBe(11)
  })

  it('son durakta ilerletme yerine SONLANDIRMA gösteriliyor', () => {
    ekraniCiz({ oturum: rehberOturumu({ currentWaypointId: 12, currentWaypointOrder: 3 }) })

    // Kapalı bir düğmeyi en görünür yerde bırakmak, kullanıcıyı "şimdi ne
    // yapacağım?" diye bırakırdı.
    expect(screen.queryByRole('button', { name: /Sonraki Durağa Geç/ })).toBeNull()
    expect(screen.getByRole('button', { name: /Turu Sonlandır/ })).toBeInTheDocument()
    expect(screen.getByText(/Son duraktasınız/)).toBeInTheDocument()
  })

  it('tur her an sonlandırılabiliyor', async () => {
    const { turuSonlandir, dispatch, kullanici } = ekraniCiz({ oturum: rehberOturumu() })
    vi.spyOn(window, 'confirm').mockReturnValue(true)

    // Daha ilk duraktayız ama grup dağılabilir, hava bozabilir: son durağı
    // beklemek zorunda değiliz.
    await kullanici.click(screen.getByRole('button', { name: /Turu Sonlandır/ }))

    await waitFor(() => expect(turuSonlandir).toHaveBeenCalledWith(7, undefined))
    await waitFor(() => expect(dispatch).toHaveBeenCalled())
  })

  it('onay verilmezse tur sonlandırılmıyor', async () => {
    const { turuSonlandir, kullanici } = ekraniCiz({ oturum: rehberOturumu() })
    vi.spyOn(window, 'confirm').mockReturnValue(false)

    // Geri alınamayan işlem: kod geçersizleşiyor, gruptaki herkesin ekranı
    // kapanıyor.
    await kullanici.click(screen.getByRole('button', { name: /Turu Sonlandır/ }))

    expect(turuSonlandir).not.toHaveBeenCalled()
  })

  it('katılımcıda sonlandırma düğmesi YOK', () => {
    ekraniCiz()   // varsayılan rol: katılımcı

    expect(screen.queryByRole('button', { name: /Turu Sonlandır/ })).toBeNull()
  })

  it('servis hata verirse mesaj gösterir ve duruma yazmaz', async () => {
    const sonrakiDuragaGec = vi.fn().mockRejectedValue(new Error('Yetkiniz yok.'))
    const { dispatch, kullanici } = ekraniCiz({
      oturum: rehberOturumu(),
      sonrakiDuragaGec,
    })

    await kullanici.click(screen.getByRole('button', { name: /Sonraki Durağa Geç/ }))

    expect(await screen.findByText('Yetkiniz yok.')).toBeInTheDocument()
    // İyimser güncelleme yok: reddedilen istek durumu değiştirmemeli.
    expect(dispatch).not.toHaveBeenCalled()
  })
})

describe('Participant — yol tarifi', () => {
  it('katılımcıya ilerletme düğmesi GÖSTERİLMEZ', () => {
    ekraniCiz()

    expect(screen.queryByRole('button', { name: /Sonraki Durağa Geç/ })).toBeNull()
    expect(screen.getByRole('button', { name: /Yol Tarifi Al/ })).toBeInTheDocument()
  })

  it('yol tarifi SIRADAKİ durağın koordinatıyla açılır', async () => {
    const { haritayiAc, kullanici } = ekraniCiz()

    await kullanici.click(screen.getByRole('button', { name: /Yol Tarifi Al/ }))

    expect(haritayiAc).toHaveBeenCalledTimes(1)
    expect(haritayiAc).toHaveBeenCalledWith({
      lat: 39.925,
      lon: 32.848,          // Ulus — sıradaki durak
      ad: 'Ulus',
      ulasimTipi: 'Yaya',
    })
  })

  it('son durakta yol tarifi MEVCUT durağa yönlendirir', async () => {
    const { haritayiAc, kullanici } = ekraniCiz({
      oturum: { ...OTURUM, currentWaypointId: 12, currentWaypointOrder: 3 },
    })

    await kullanici.click(screen.getByRole('button', { name: /Yol Tarifi Al/ }))

    // Geç kalan katılımcı gruba son durakta yetişsin.
    expect(haritayiAc.mock.calls[0][0].ad).toBe('Ankara Kalesi')
  })

  it('ulaşım tipi harita bağlantısına taşınır', async () => {
    const { haritayiAc, kullanici } = ekraniCiz({ ulasimTipi: 'TopluTasima' })

    await kullanici.click(screen.getByRole('button', { name: /Yol Tarifi Al/ }))

    expect(haritayiAc.mock.calls[0][0].ulasimTipi).toBe('TopluTasima')
  })
})

describe('Rota simülasyonu', () => {
  const sahteSim = (ek = {}) => ({
    oynatilabilir: true,
    oynuyor: false,
    durum: null,
    oynat: vi.fn(),
    durdur: vi.fn(),
    ...ek,
  })

  it('KATILIMCI da rotayı oynatabiliyor', async () => {
    // Turu ilerletmek rehberin işi — o gerçek bir durum değişikliği.
    // Simülasyon yalnızca bir gösterim: izleyicinin kendi ekranında kalıyor,
    // kimsenin ekranını değiştirmiyor. Bu yüzden yetkiye bağlı değil.
    const sim = sahteSim()
    const { kullanici } = ekraniCiz({ simulasyon: sim })

    await kullanici.click(screen.getByRole('button', { name: /Rotayı oynat/ }))

    expect(sim.oynat).toHaveBeenCalledTimes(1)
  })

  it('REHBER de oynatabiliyor', () => {
    ekraniCiz({ oturum: rehberOturumu(), simulasyon: sahteSim() })

    expect(screen.getByRole('button', { name: /Rotayı oynat/ })).toBeInTheDocument()
  })

  it('rotası olmayan turda düğme HİÇ çizilmiyor', () => {
    // Kapalı bir düğme "neden çalışmıyor?" diye sorulacak bir şey;
    // olmayan düğme sorulmuyor.
    ekraniCiz({ simulasyon: sahteSim({ oynatilabilir: false }) })

    expect(screen.queryByRole('button', { name: /oynat/i })).toBeNull()
  })

  it('simülasyon verilmezse ekran yine çiziliyor', () => {
    // Bileşen haritasız bağlamlarda da çalışmalı.
    ekraniCiz()

    expect(screen.getByText('Anıtkabir')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /oynat/i })).toBeNull()
  })

  it('oynarken yüzde ve sıradaki durak yazıyor', () => {
    ekraniCiz({
      simulasyon: sahteSim({
        oynuyor: true,
        durum: { yuzde: 42, sonrakiDurak: { order: 2, name: 'Ulus' } },
      }),
    })

    expect(screen.getByText(/%42/)).toBeInTheDocument()
    expect(screen.getByText(/sıradaki: Ulus/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Durdur/ })).toBeInTheDocument()
  })

  it('CANLI turda simge bulunulan durakta duruyor', () => {
    // Kullanıcı itirazı: "simülasyon rastgele bir şekilde değil de şu anda
    // bulunan tur noktasında görünecek şekilde olsun." Canlı turda düğme
    // "baştan oynat" değil, "sıradaki durağa git" demeli.
    ekraniCiz({
      simulasyon: sahteSim({
        canli: true,
        durum: { yuzde: 33, oncekiDurak: { order: 1, name: 'Anıtkabir' }, sonrakiDurak: { order: 2, name: 'Ulus' } },
      }),
    })

    expect(screen.getByRole('button', { name: /Sıradaki durağa git/ })).toBeInTheDocument()

    // Durum satırı bulunulan durağı ÖNCE yazıyor: canlı turda sorulan şey
    // "grup nerede", yüzde ikincil. ("Anıtkabir" durak kartında da geçtiği
    // için arama simülasyon satırıyla sınırlandırılıyor.)
    const satir = document.querySelector('.tur-simulasyon-durum')
    expect(satir.textContent).toMatch(/Anıtkabir/)
    expect(satir.textContent).toMatch(/sıradaki: Ulus/)
  })

  it('OYNAMAZKEN durdurma düğmesi yok', () => {
    // Canlı turda simge zaten haritada duruyor; "Durdur" onu kaldıracakmış
    // gibi okunurdu.
    ekraniCiz({
      simulasyon: sahteSim({ canli: true, durum: { yuzde: 0, sonrakiDurak: null } }),
    })

    expect(screen.queryByRole('button', { name: /Durdur/ })).toBeNull()
  })
})

describe('Kapanmış oturum', () => {
  it('tamamlanan turda hiçbir eylem düğmesi kalmaz', () => {
    ekraniCiz({ oturum: rehberOturumu({ status: 'Completed' }) })

    expect(screen.queryByRole('button', { name: /Sonraki Durağa Geç/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Yol Tarifi Al/ })).toBeNull()
    expect(screen.getByText(/Tur sona erdi/)).toBeInTheDocument()
  })
})
