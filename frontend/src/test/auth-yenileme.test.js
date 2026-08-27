import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'

import {
  saveSession, clearSession, isAuthenticated, authFetch,
  oturumuYenile, getToken, getRefreshToken, kalanOturumMetni,
} from '../auth'

// ============================================================================
//  Eksik 5 — yenileme anahtarı akışının arayüz tarafı
//
//  Buradaki en kritik test TEK UÇUŞ (single flight) testi ve sebebi ince:
//  anahtar her kullanımda DÖNÜYOR. Harita açılışında onlarca istek aynı anda
//  401 alıp hepsi birden yenilemeye kalkarsa, ilki dışındakiler artık iptal
//  edilmiş anahtarı sunar; sunucu bunu hırsızlık sayar ve kullanıcının bütün
//  oturumlarını kapatır. Yani koruma, kendi kullanıcımızı dışarı atar.
//
//  Bu davranış elle denenerek fark edilmez: yerel makinede istekler sıraya
//  girer ve yarış çoğu zaman hiç oluşmaz.
// ============================================================================

const DAKIKA = 60_000

/** Tarihi ISO metne çevirir (localStorage'a öyle yazılıyor). */
const an = (ms) => new Date(Date.now() + ms).toISOString()

function oturumKur({ erisimKalan = 10 * DAKIKA, yenilemeKalan = 7 * 24 * 60 * DAKIKA } = {}) {
  saveSession({
    token: 'erisim-1',
    expiresAt: an(erisimKalan),
    username: 'azra',
    refreshToken: 'yenileme-1',
    refreshTokenExpiresAt: an(yenilemeKalan),
  })
}

/** fetch taklidi: sıradaki cevapları önceden diziye koyuyoruz. */
function fetchKur(cevaplar) {
  const sahte = vi.fn(async (url) => {
    const kayit = cevaplar.find((c) => url.includes(c.yol))
    if (!kayit) throw new Error(`beklenmeyen istek: ${url}`)
    const cevap = typeof kayit.cevap === 'function' ? kayit.cevap() : kayit.cevap
    return cevap
  })
  vi.stubGlobal('fetch', sahte)
  return sahte
}

const yanit = (status, govde) => ({
  ok: status >= 200 && status < 300,
  status,
  json: async () => govde,
})

beforeEach(() => {
  localStorage.clear()
  vi.useRealTimers()
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('oturum saklama', () => {
  it('yenileme anahtarını da saklıyor', () => {
    oturumKur()

    expect(getToken()).toBe('erisim-1')
    expect(getRefreshToken()).toBe('yenileme-1')
  })

  it('sunucu anahtar göndermezse eldekini SİLMİYOR', () => {
    oturumKur()

    // Bu koşul olmasaydı undefined yazılır ve geçerli bir oturum sessizce
    // ölürdü.
    saveSession({ token: 'erisim-2', expiresAt: an(DAKIKA), username: 'azra' })

    expect(getRefreshToken()).toBe('yenileme-1')
  })
})

describe('isAuthenticated — ölçüt artık oturum', () => {
  it('erişim token\'ı ÖLMÜŞ ama yenileme anahtarı diriyse oturum açık', () => {
    oturumKur({ erisimKalan: -DAKIKA })

    // Eski ölçütte kalsaydık, bilgisayarını 20 dakika bırakan kullanıcı
    // sunucuda oturumu apaçık geçerliyken giriş ekranına atılırdı.
    expect(isAuthenticated()).toBe(true)
  })

  it('yenileme anahtarının süresi dolduysa oturum kapalı', () => {
    oturumKur({ yenilemeKalan: -DAKIKA })

    expect(isAuthenticated()).toBe(false)
  })

  it('hiç oturum yoksa false', () => {
    expect(isAuthenticated()).toBe(false)
  })
})

describe('authFetch — 401 sonrası sessiz yenileme', () => {
  it('401 alınca yeniliyor ve isteği YENİ token\'la tekrarlıyor', async () => {
    oturumKur()

    let veriCagrisi = 0
    const sahte = fetchKur([
      {
        yol: '/api/auth/refresh',
        cevap: () => yanit(200, {
          token: 'erisim-2',
          expiresAt: an(10 * DAKIKA),
          username: 'azra',
          refreshToken: 'yenileme-2',
          refreshTokenExpiresAt: an(7 * 24 * 60 * DAKIKA),
        }),
      },
      {
        yol: '/api/poi',
        cevap: () => { veriCagrisi += 1; return yanit(veriCagrisi === 1 ? 401 : 200, []) },
      },
    ])

    const res = await authFetch('/api/poi')

    expect(res.status).toBe(200)
    expect(veriCagrisi).toBe(2)

    // Tekrar YENİ token'la gitmeli; eskisiyle giderse yine 401 alırdı.
    const sonCagri = sahte.mock.calls.at(-1)
    expect(sonCagri[1].headers.Authorization).toBe('Bearer erisim-2')
  })

  it('yenilemeden gelen YENİ anahtarı saklıyor (döndürme)', async () => {
    oturumKur()
    fetchKur([{
      yol: '/api/auth/refresh',
      cevap: () => yanit(200, {
        token: 'erisim-2',
        expiresAt: an(10 * DAKIKA),
        username: 'azra',
        refreshToken: 'yenileme-2',
        refreshTokenExpiresAt: an(7 * 24 * 60 * DAKIKA),
      }),
    }])

    await oturumuYenile()

    // Saklamazsak bir sonraki yenileme ÖLMÜŞ anahtarla denenir ve sunucudaki
    // hırsızlık alarmını tetikler — yani bütün oturumlar kapanır.
    expect(getRefreshToken()).toBe('yenileme-2')
  })

  it('yenileme de reddedilirse oturumu kapatıp login\'e yolluyor', async () => {
    oturumKur()
    const onUnauthorized = vi.fn()

    fetchKur([
      { yol: '/api/auth/refresh', cevap: () => yanit(401, {}) },
      { yol: '/api/auth/logout', cevap: () => yanit(204, null) },
      { yol: '/api/poi', cevap: () => yanit(401, {}) },
    ])

    await expect(authFetch('/api/poi', {}, onUnauthorized)).rejects.toThrow(/Oturum/)

    expect(onUnauthorized).toHaveBeenCalledOnce()
    expect(getToken()).toBeNull()
  })

  it('taze token da 401 alırsa SONSUZ DÖNGÜYE girmiyor', async () => {
    oturumKur()

    let veriCagrisi = 0
    fetchKur([
      {
        yol: '/api/auth/refresh',
        cevap: () => yanit(200, {
          token: 'erisim-2',
          expiresAt: an(10 * DAKIKA),
          username: 'azra',
          refreshToken: 'yenileme-2',
          refreshTokenExpiresAt: an(7 * 24 * 60 * DAKIKA),
        }),
      },
      { yol: '/api/auth/logout', cevap: () => yanit(204, null) },
      { yol: '/api/poi', cevap: () => { veriCagrisi += 1; return yanit(401, {}) } },
    ])

    await expect(authFetch('/api/poi')).rejects.toThrow(/Oturum/)

    // İkinci 401 "token eskiydi" ile açıklanamaz; tekrar denemek sonsuz
    // döngü olurdu.
    expect(veriCagrisi).toBe(2)
  })

  it('yenileme anahtarı YOKSA hiç denemeden çıkış yapıyor', async () => {
    saveSession({ token: 'erisim-1', expiresAt: an(DAKIKA), username: 'azra' })

    const sahte = fetchKur([{ yol: '/api/poi', cevap: () => yanit(401, {}) }])

    await expect(authFetch('/api/poi')).rejects.toThrow(/Oturum/)

    // /api/auth/refresh'e hiç gidilmemeli: elde anahtar yokken yenileme
    // istemek boşuna bir tur ve boşuna bir hata kaydı olurdu.
    expect(sahte.mock.calls.every(([url]) => !url.includes('refresh'))).toBe(true)
  })
})

describe('tek uçuş (single flight) — eşzamanlı 401\'ler', () => {
  it('aynı anda gelen ÜÇ 401 için sunucuya TEK yenileme isteği gidiyor', async () => {
    oturumKur()

    let yenilemeSayisi = 0
    const ilkTur = new Set()

    fetchKur([
      {
        yol: '/api/auth/refresh',
        cevap: async () => {
          yenilemeSayisi += 1
          // Gerçek ağ gecikmesini taklit ediyoruz: gecikme olmasaydı ilk
          // çağrı diğerleri başlamadan biter ve yarış hiç oluşmazdı —
          // test de yanlışlıkla geçerdi.
          await new Promise((c) => setTimeout(c, 20))
          return yanit(200, {
            token: 'erisim-2',
            expiresAt: an(10 * DAKIKA),
            username: 'azra',
            refreshToken: 'yenileme-2',
            refreshTokenExpiresAt: an(7 * 24 * 60 * DAKIKA),
          })
        },
      },
      {
        yol: '/api/',
        cevap: () => {
          // Her uç ilk çağrısında 401, sonrasında 200.
          const anahtar = Math.random()
          if (ilkTur.size < 3) { ilkTur.add(anahtar); return yanit(401, {}) }
          return yanit(200, [])
        },
      },
    ])

    const sonuclar = await Promise.all([
      authFetch('/api/poi'),
      authFetch('/api/point'),
      authFetch('/api/polygon'),
    ])

    sonuclar.forEach((r) => expect(r.status).toBe(200))

    // ASIL İDDİA. Üç yenileme gitseydi ikisi ölmüş anahtarı sunar, sunucu
    // hırsızlık sayar ve kullanıcının bütün oturumları kapanırdı.
    expect(yenilemeSayisi).toBe(1)
  })
})

describe('çıkış', () => {
  it('sunucudaki oturumu da kapatıyor', async () => {
    oturumKur()
    const sahte = fetchKur([{ yol: '/api/auth/logout', cevap: () => yanit(204, null) }])

    clearSession()

    // Yalnızca yerel anahtarı silmek YETMEZ: silinen kopya, birinin daha
    // önce ele geçirdiği kopyayı geçersiz kılmaz.
    const cikis = sahte.mock.calls.find(([url]) => url.includes('logout'))
    expect(cikis).toBeDefined()
    expect(JSON.parse(cikis[1].body)).toEqual({ refreshToken: 'yenileme-1' })
    expect(cikis[1].keepalive).toBe(true)

    expect(getRefreshToken()).toBeNull()
  })

  it('sunucuya ulaşılamasa bile YEREL çıkış yapılıyor', async () => {
    oturumKur()
    vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new Error('ağ yok'))))

    clearSession()

    // Kullanıcı çıkmak istedi; sunucu erişilemiyor diye ekranda kalmamalı.
    expect(getToken()).toBeNull()
    expect(isAuthenticated()).toBe(false)
  })
})

describe('kalan süre metni', () => {
  // Süreler bilerek sınırın ORTASINDA seçildi.
  //
  // İlk yazılışta "tam 7 gün" kullanılmıştı ve test kararsızdı: 10080 dakika
  // ile saatin okunması arasında BİR MİLİSANİYE geçse dakika 10079'a düşüyor,
  // 10079/1440 = 6 oluyor ve beklenen '7 gün' yerine '6 gün' çıkıyordu.
  // Sınıra oturan bir test, kodu değil makinenin o anki hızını ölçer.
  it('bir saatin altında m:ss', () => {
    oturumKur({ yenilemeKalan: 5 * DAKIKA + 30_000 })
    expect(kalanOturumMetni()).toMatch(/^5:[0-3]\d$/)
  })

  it('saatlerde "sa"', () => {
    oturumKur({ yenilemeKalan: 5 * 60 * DAKIKA + 30 * DAKIKA })
    expect(kalanOturumMetni()).toBe('5 sa')
  })

  it('günlerde "gün" — 10079:59 gibi bir sayı YAZMIYOR', () => {
    oturumKur({ yenilemeKalan: 7 * 24 * 60 * DAKIKA - 12 * 60 * DAKIKA })
    expect(kalanOturumMetni()).toBe('6 gün')
  })

  it('süre bittiyse 0:00', () => {
    oturumKur({ yenilemeKalan: -DAKIKA })
    expect(kalanOturumMetni()).toBe('0:00')
  })
})
