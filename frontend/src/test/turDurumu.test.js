import { describe, it, expect } from 'vitest'

import {
  EYLEM,
  ILK_DURUM,
  OTURUM_DURUMU,
  TUR_ROLU,
  bagliKatilimcilar,
  izlenenOturum,
  mekanTipi,
  oturumAcikMi,
  rehberMi,
  turListesi,
  turReducer,
  turunCanliOturumu,
} from '../turDurumu'

// ============================================================================
//  Tur modülü — durum makinesinin sınavı
//
//  Buradaki testlerin çoğu, indirgeyicinin GÖZLE FARK EDİLMEYEN kararlarını
//  koruyor: geç gelen yayın mesajının yok sayılması, katılımcı listesinin
//  yayın mesajıyla silinmemesi, sıra numaralarının 1..N sıkıştırılması.
//  Hepsi doğru çalıştığında ekranda hiçbir şey olmaz; biri bozulduğunda
//  "araç geri atladı", "katılımcılar kayboldu" gibi rastgele görünen
//  hatalar çıkar. O yüzden testleri var.
// ============================================================================

const TUR = {
  id: 1,
  name: 'Ankara Kale Turu',
  color: '#7b5cd6',
  waypoints: [],
  routeUpToDate: true,
  liveSessionId: null,
  isActive: true,
}

const durak = (id, order, tourId = 1) => ({
  id,
  tourId,
  order,
  placeId: `osm:node/${id}`,
  poiId: null,
  name: `Durak ${id}`,
  venueType: 'Museum',
  dwellMinutes: 30,
  wkt: `POINT (32.8${id} 39.9${id})`,
  note: null,
  isActive: true,
})

/** Eylemleri sırayla uygular — okunması `reduce` zincirinden kolay. */
const uygula = (durum, ...eylemler) => eylemler.reduce(turReducer, durum)

const turluDurum = (duraklar = []) =>
  uygula(ILK_DURUM, {
    tur: EYLEM.TURLAR_GELDI,
    turlar: [{ ...TUR, waypoints: duraklar }],
  })

describe('sabitler ve yardımcılar', () => {
  it('açık oturum durumlarını tanır, kapananları tanımaz', () => {
    expect(oturumAcikMi(OTURUM_DURUMU.PLANLANDI)).toBe(true)
    expect(oturumAcikMi(OTURUM_DURUMU.YAYINDA)).toBe(true)
    expect(oturumAcikMi(OTURUM_DURUMU.DURAKLATILDI)).toBe(true)
    expect(oturumAcikMi(OTURUM_DURUMU.TAMAMLANDI)).toBe(false)
    expect(oturumAcikMi(OTURUM_DURUMU.IPTAL)).toBe(false)
  })

  it('tanınmayan mekan tipini "Diğer" sayar', () => {
    // Sunucu yeni bir VenueType eklerse arayüz çökmemeli.
    expect(mekanTipi('Museum').etiket).toBe('Müze')
    expect(mekanTipi('Aquarium').deger).toBe('Other')
  })
})

describe('turlar', () => {
  it('gelen listeyi sözlüğe çevirir ve sırayı korur', () => {
    const durum = uygula(ILK_DURUM, {
      tur: EYLEM.TURLAR_GELDI,
      turlar: [{ ...TUR, id: 5 }, { ...TUR, id: 2 }],
    })

    expect(durum.turSirasi).toEqual([5, 2])
    expect(turListesi(durum).map((t) => t.id)).toEqual([5, 2])
  })

  it('yeni turu listenin başına koyar, var olanı yerinde günceller', () => {
    const durum = uygula(
      turluDurum(),
      { tur: EYLEM.TUR_KAYDEDILDI, turu: { ...TUR, id: 9 } },
      { tur: EYLEM.TUR_KAYDEDILDI, turu: { ...TUR, id: 1, name: 'Yeni ad' } },
    )

    expect(durum.turSirasi).toEqual([9, 1])
    expect(durum.turlar[1].name).toBe('Yeni ad')
  })

  it('silinen tur seçiliyse seçimi de kaldırır', () => {
    const durum = uygula(
      turluDurum(),
      { tur: EYLEM.TUR_SECILDI, turId: 1 },
      { tur: EYLEM.TUR_SILINDI, turId: 1 },
    )

    expect(durum.turlar[1]).toBeUndefined()
    expect(durum.seciliTurId).toBeNull()
  })
})

describe('duraklar', () => {
  it('yeni durağı ekler ve sırayı 1..N sıkıştırır', () => {
    const durum = uygula(turluDurum([durak(10, 1)]), {
      tur: EYLEM.DURAK_KAYDEDILDI,
      durak: durak(11, 7),        // sunucu 7 demiş olsa da liste 1..N olmalı
    })

    expect(durum.turlar[1].waypoints.map((d) => [d.id, d.order]))
      .toEqual([[10, 1], [11, 2]])
  })

  it('var olan durağı ikinci kez eklemez, günceller', () => {
    const durum = uygula(turluDurum([durak(10, 1)]), {
      tur: EYLEM.DURAK_KAYDEDILDI,
      durak: { ...durak(10, 1), dwellMinutes: 90 },
    })

    expect(durum.turlar[1].waypoints).toHaveLength(1)
    expect(durum.turlar[1].waypoints[0].dwellMinutes).toBe(90)
  })

  it('durak silinince kalanların sırası boşluksuz kalır', () => {
    const durum = uygula(turluDurum([durak(10, 1), durak(11, 2), durak(12, 3)]), {
      tur: EYLEM.DURAK_SILINDI,
      turId: 1,
      durakId: 11,
    })

    expect(durum.turlar[1].waypoints.map((d) => d.order)).toEqual([1, 2])
  })

  it('sürükle-bırak sıralamasını verilen id sırasına göre uygular', () => {
    const durum = uygula(turluDurum([durak(10, 1), durak(11, 2), durak(12, 3)]), {
      tur: EYLEM.DURAKLAR_SIRALANDI,
      turId: 1,
      durakIdleri: [12, 10, 11],
    })

    expect(durum.turlar[1].waypoints.map((d) => d.id)).toEqual([12, 10, 11])
    expect(durum.turlar[1].waypoints.map((d) => d.order)).toEqual([1, 2, 3])
  })

  it('bilinmeyen tura gelen durak güncellemesi durumu değiştirmez', () => {
    const onceki = turluDurum([durak(10, 1)])
    const sonraki = turReducer(onceki, {
      tur: EYLEM.DURAK_KAYDEDILDI,
      durak: durak(20, 1, 99),
    })

    // Aynı NESNE dönmeli: gereksiz yeniden çizim de olmasın.
    expect(sonraki).toBe(onceki)
  })
})

describe('taslak durak', () => {
  it('taslak kapalıyken gelen güncellemeyi yok sayar', () => {
    const onceki = turluDurum()
    expect(turReducer(onceki, { tur: EYLEM.TASLAK_GUNCELLENDI, alanlar: { name: 'X' } }))
      .toBe(onceki)
  })

  it('açık taslağın yalnızca verilen alanlarını değiştirir', () => {
    const durum = uygula(
      turluDurum(),
      { tur: EYLEM.TASLAK_ACILDI, taslak: { wkt: 'POINT (1 2)', name: '', dwellMinutes: 15 } },
      { tur: EYLEM.TASLAK_GUNCELLENDI, alanlar: { name: 'Anıtkabir' } },
    )

    expect(durum.taslakDurak).toEqual({ wkt: 'POINT (1 2)', name: 'Anıtkabir', dwellMinutes: 15 })
  })
})

describe('oturumlar', () => {
  const oturum = (ek = {}) => ({
    id: 7,
    tourId: 1,
    tourName: TUR.name,
    color: TUR.color,
    status: OTURUM_DURUMU.YAYINDA,
    joinCode: null,
    guideUserId: 3,
    guideUserName: 'rehber',
    currentWaypointId: null,
    progressPercent: 0,
    lastLon: 32.8,
    lastLat: 39.9,
    lastPositionUtc: '2026-09-01T10:00:00Z',
    participantCount: 2,
    myRole: TUR_ROLU.KATILIMCI,
    participants: [],
    ...ek,
  })

  it('canlı oturum turun rozetini işaretler, biten oturum kaldırır', () => {
    const canli = uygula(turluDurum(), { tur: EYLEM.OTURUM_GUNCELLENDI, oturum: oturum() })
    expect(canli.turlar[1].liveSessionId).toBe(7)
    expect(turunCanliOturumu(canli, 1).id).toBe(7)

    const biten = turReducer(canli, { tur: EYLEM.OTURUM_BITTI, oturumId: 7 })
    expect(biten.turlar[1].liveSessionId).toBeNull()
    expect(biten.oturumlar[7]).toBeUndefined()
  })

  it('kapanan oturum izleniyorsa izlemeyi de bırakır', () => {
    const durum = uygula(
      turluDurum(),
      { tur: EYLEM.OTURUM_GUNCELLENDI, oturum: oturum() },
      { tur: EYLEM.OTURUM_IZLENIYOR, oturumId: 7 },
      { tur: EYLEM.OTURUM_BITTI, oturumId: 7 },
    )

    expect(durum.izlenenOturumId).toBeNull()
    expect(izlenenOturum(durum)).toBeNull()
  })

  it('GEÇ gelen yayın mesajını yok sayar (araç geri atlamasın)', () => {
    const durum = uygula(
      turluDurum(),
      { tur: EYLEM.OTURUM_GUNCELLENDI, oturum: oturum({ lastLon: 33, lastPositionUtc: '2026-09-01T10:00:10Z' }) },
      { tur: EYLEM.OTURUM_GUNCELLENDI, oturum: oturum({ lastLon: 32, lastPositionUtc: '2026-09-01T10:00:05Z' }) },
    )

    expect(durum.oturumlar[7].lastLon).toBe(33)
  })

  it('katılımcı listesini ve katılım kodunu yayın mesajıyla silmez', () => {
    const katilimcilar = [
      { userId: 3, userName: 'rehber', role: TUR_ROLU.REHBER, joinedUtc: 'x', leftUtc: null },
      { userId: 4, userName: 'gezgin', role: TUR_ROLU.KATILIMCI, joinedUtc: 'x', leftUtc: null },
      { userId: 5, userName: 'ayrilan', role: TUR_ROLU.KATILIMCI, joinedUtc: 'x', leftUtc: 'y' },
    ]

    const durum = uygula(
      turluDurum(),
      // Ayrıntı cevabı: liste ve kod dolu
      { tur: EYLEM.OTURUM_GUNCELLENDI, oturum: oturum({ joinCode: 'K7QF2M', myRole: TUR_ROLU.REHBER, participants: katilimcilar }) },
      // Yayın mesajı: ikisi de boş — eskisi korunmalı
      { tur: EYLEM.OTURUM_GUNCELLENDI, oturum: oturum({ progressPercent: 42, lastPositionUtc: '2026-09-01T10:00:20Z' }) },
    )

    const guncel = durum.oturumlar[7]
    expect(guncel.progressPercent).toBe(42)
    expect(guncel.joinCode).toBe('K7QF2M')
    expect(guncel.participants).toHaveLength(3)
    expect(bagliKatilimcilar(guncel).map((k) => k.userId)).toEqual([3, 4])
  })

  it('rehberliği sunucunun myRole alanından okur', () => {
    expect(rehberMi(oturum({ myRole: TUR_ROLU.REHBER }))).toBe(true)
    // guideUserId aynı olsa bile rol alanı katılımcı diyorsa katılımcıdır.
    expect(rehberMi(oturum({ myRole: TUR_ROLU.KATILIMCI }))).toBe(false)
    expect(rehberMi(null)).toBe(false)
  })
})
