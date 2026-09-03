// ============================================================================
//  TUR MODÜLÜ — İSTEMCİ TARAFI DURUM (state) YAPISI
//
//  Bu dosya bir "store" değil, SAF bir durum makinesi: tek bir başlangıç
//  nesnesi (`ILK_DURUM`) ve tek bir indirgeyici (`turReducer`). React tarafı
//  `useReducer(turReducer, ILK_DURUM)` ile bağlanıyor.
//
//  NEDEN AYRI DOSYA VE NEDEN useState DEĞİL?
//  Turun canlı durumu ÜÇ AYRI KAYNAKTAN besleniyor:
//      1. REST cevapları        (tur listesi, oturum ayrıntısı)
//      2. SignalR yayını        (konum, durak ilerledi, katılımcı girdi/çıktı)
//      3. Kullanıcının kendisi  (seçim, panel açma, taslak durak)
//  Bunları ayrı ayrı useState'lerde tutsaydık iki kaynak aynı anda geldiğinde
//  hangisinin kazandığı çağrı sırasına kalırdı — yayın mesajı ile REST cevabı
//  yarıştığında ekranda eski konumun kalması tam olarak bu cinsten bir hata.
//  Tek indirgeyicide her güncelleme AÇIKÇA bir eyleme bağlı ve sıra bellidir.
//
//  DURUM AĞACI (normalleştirilmiş):
//
//    {
//      turlar:        { [id]: Tur },        // sözlük, dizi değil
//      turSirasi:     [id, ...],            // görüntüleme sırası
//      oturumlar:     { [id]: Oturum },
//      seciliTurId:   number | null,
//      izlenenOturumId: number | null,
//      benimKullaniciId: number | null,
//      yukleniyor:    boolean,
//      hata:          string | null,
//      taslakDurak:   TaslakDurak | null,
//    }
//
//  NEDEN SÖZLÜK + SIRA DİZİSİ?
//  Yayın saniyede birkaç kez "şu oturumun konumu değişti" diyor. Dizi tutsaydık
//  her mesajda listeyi baştan sona tarayıp yeni bir dizi üretmek gerekirdi.
//  Sözlükte güncelleme doğrudan id ile yapılıyor; sıra ise ayrı bir dizide
//  duruyor çünkü nesne anahtarlarının sırası bir GARANTİ değildir.
//
//  ROLLER: rol OTURUMA aittir (bkz. backend Entities/Tour.cs → TourRole).
//  İstemci rolü kendisi HESAPLAMIYOR, sunucunun gönderdiği `myRole` alanını
//  okuyor: rehberliği "guideUserId === benimId" diye hesaplasaydık, rehberlik
//  ileride devredildiğinde arayüz yanlış cevap verirdi.
//
//  DİKKAT: buradaki rol ve durum bilgisi yalnızca GÖRÜNÜRLÜK içindir. Asıl
//  kontrol sunucuda (yetkiler.js dosyasının başındaki uyarının aynısı).
// ============================================================================

// ---------------------------------------------------------------------------
//  Sabitler — sunucudaki enum'larla BİREBİR aynı olmalı
//  (backend/StajProject.Entities/Tour.cs)
// ---------------------------------------------------------------------------

/** Oturum içi rol. Guide yönetir, Participant izler. */
export const TUR_ROLU = {
  REHBER: 'Guide',
  KATILIMCI: 'Participant',
}

/** Oturumun yaşam döngüsü. */
export const OTURUM_DURUMU = {
  PLANLANDI: 'Planned',
  YAYINDA: 'Live',
  DURAKLATILDI: 'Paused',
  TAMAMLANDI: 'Completed',
  IPTAL: 'Cancelled',
}

/** Oturum hâlâ açık mı? (kod geçerli, katılım mümkün) */
export const oturumAcikMi = (durum) =>
  durum === OTURUM_DURUMU.PLANLANDI ||
  durum === OTURUM_DURUMU.YAYINDA ||
  durum === OTURUM_DURUMU.DURAKLATILDI

/**
 * Mekan tipleri: sunucudaki VenueType enum'ının karşılığı + arayüz bilgileri.
 *
 * Etiket ve varsayılan kalış süresi BURADA çünkü ikisi de sunum kararı:
 * sunucu "Museum" diyor, ekranda "Müze" yazması ve yeni eklenen bir müze
 * durağına 45 dakika önerilmesi arayüzün işi. Sunucuya taşısaydık her dil
 * değişikliği bir sürüm isterdi.
 */
export const MEKAN_TIPLERI = [
  { deger: 'Museum', etiket: 'Müze', varsayilanDakika: 45 },
  { deger: 'Monument', etiket: 'Anıt', varsayilanDakika: 20 },
  { deger: 'ReligiousSite', etiket: 'İbadet Yeri', varsayilanDakika: 20 },
  { deger: 'Park', etiket: 'Park', varsayilanDakika: 30 },
  { deger: 'Viewpoint', etiket: 'Seyir Noktası', varsayilanDakika: 15 },
  { deger: 'Restaurant', etiket: 'Restoran', varsayilanDakika: 60 },
  { deger: 'Cafe', etiket: 'Kafe', varsayilanDakika: 30 },
  { deger: 'Shopping', etiket: 'Alışveriş', varsayilanDakika: 45 },
  { deger: 'Hotel', etiket: 'Otel', varsayilanDakika: 10 },
  { deger: 'TransportHub', etiket: 'Ulaşım Noktası', varsayilanDakika: 10 },
  { deger: 'Other', etiket: 'Diğer', varsayilanDakika: 15 },
]

/** Tanınmayan tip sessizce "Diğer" sayılır — sunucu yeni bir tip eklerse arayüz çökmesin. */
export const mekanTipi = (deger) =>
  MEKAN_TIPLERI.find((t) => t.deger === deger) ??
  MEKAN_TIPLERI[MEKAN_TIPLERI.length - 1]

// ---------------------------------------------------------------------------
//  Tip tanımları (JSDoc)
//
//  Alan adları sunucudaki DTO'larla birebir aynı (TourDtos.cs): gelen JSON
//  yeniden adlandırılmadan duruma giriyor. Adları çevirseydik her yeni alanda
//  iki tarafı elle eşlemek gerekirdi ve unutulan bir alan sessizce undefined
//  olurdu.
// ---------------------------------------------------------------------------

/**
 * @typedef {Object} Durak            Sunucudaki WaypointDto
 * @property {number} id
 * @property {number} tourId
 * @property {number} order           Tur içindeki sıra (1'den başlar)
 * @property {string} placeId         Dış sağlayıcı kimliği — "osm:node/123"
 * @property {number|null} poiId      Sistemdeki POI karşılığı; yoksa null
 * @property {string} name
 * @property {string} venueType       MEKAN_TIPLERI değerlerinden biri
 * @property {number} dwellMinutes    Planlanan kalış süresi (dakika)
 * @property {string} wkt             "POINT (32.85 39.93)" — EPSG:4326
 * @property {string|null} note
 * @property {boolean} isActive
 */

/**
 * @typedef {Object} Tur              Sunucudaki TourDto
 * @property {number} id
 * @property {string} name
 * @property {string|null} description
 * @property {string} color           "#rrggbb"
 * @property {string|null} scheduledStartUtc
 * @property {number|null} guideUserId
 * @property {string|null} guideUserName
 * @property {Durak[]} waypoints      SIRAYLA
 * @property {string|null} routeWkt   Yollara oturmuş rota; hesaplanmadıysa null
 * @property {number|null} routeDistanceMeters
 * @property {number|null} routeDurationSeconds
 * @property {boolean} routeUpToDate  false ise arayüz "rota güncel değil" diyor
 * @property {number} totalDwellMinutes
 * @property {number} estimatedTotalMinutes
 * @property {number|null} liveSessionId
 * @property {boolean} isActive
 */

/**
 * @typedef {Object} Katilimci        Sunucudaki TourParticipantDto
 * @property {number} userId
 * @property {string} userName
 * @property {string} role            TUR_ROLU değerlerinden biri
 * @property {string} joinedUtc
 * @property {string|null} leftUtc
 */

/**
 * @typedef {Object} Oturum           Sunucudaki TourSessionDto
 * @property {number} id
 * @property {number} tourId
 * @property {string} tourName
 * @property {string} color
 * @property {string} status          OTURUM_DURUMU değerlerinden biri
 * @property {string|null} joinCode   YALNIZCA rehbere dolu gelir
 * @property {number} guideUserId
 * @property {string} guideUserName
 * @property {string|null} startedUtc
 * @property {string|null} endedUtc
 * @property {number|null} currentWaypointId
 * @property {number|null} currentWaypointOrder
 * @property {string|null} currentWaypointName
 * @property {number|null} nextWaypointOrder
 * @property {string|null} nextWaypointName
 * @property {number} progressPercent 0-100
 * @property {number|null} lastLon
 * @property {number|null} lastLat
 * @property {string|null} lastPositionUtc
 * @property {number} participantCount
 * @property {string|null} myRole     'Guide' | 'Participant' | null
 * @property {Katilimci[]} participants
 */

/**
 * @typedef {Object} TaslakDurak      Haritaya konmuş ama HENÜZ KAYDEDİLMEMİŞ durak
 * @property {string} wkt
 * @property {string} name
 * @property {string} placeId
 * @property {number|null} poiId
 * @property {string} venueType
 * @property {number} dwellMinutes
 * @property {string} note
 */

/**
 * @typedef {Object} TurDurumu
 * @property {Object<number, Tur>} turlar
 * @property {number[]} turSirasi
 * @property {Object<number, Oturum>} oturumlar
 * @property {number|null} seciliTurId
 * @property {number|null} izlenenOturumId
 * @property {number|null} benimKullaniciId
 * @property {boolean} yukleniyor
 * @property {string|null} hata
 * @property {TaslakDurak|null} taslakDurak
 */

/** @type {TurDurumu} */
export const ILK_DURUM = {
  turlar: {},
  turSirasi: [],
  oturumlar: {},

  /** Yönetim panelinde/haritada seçili tur. */
  seciliTurId: null,

  /**
   * ŞU AN TAKİP EDİLEN oturum — aynı anda yalnızca BİR tane.
   *
   * Neden çoğul değil? Katılımcı bir turdadır; iki turu aynı anda izlemek
   * haritada iki grubu takip etmek demek olurdu ve "sıradaki durak" gibi
   * bilgilerin hangisine ait olduğu belirsizleşirdi. Oturum SÖZLÜĞÜ ise
   * çoğul: liste ekranı bütün canlı turları rozetle gösteriyor.
   */
  izlenenOturumId: null,

  /** Giriş yapan kullanıcının id'si — "bu tur benim mi?" sorusu için. */
  benimKullaniciId: null,

  yukleniyor: false,
  hata: null,

  /** Haritaya konmuş, henüz kaydedilmemiş durak (form açıkken dolu). */
  taslakDurak: null,
}

// ---------------------------------------------------------------------------
//  Eylemler
// ---------------------------------------------------------------------------

export const EYLEM = {
  YUKLENIYOR: 'yukleniyor',
  HATA: 'hata',

  TURLAR_GELDI: 'turlarGeldi',
  TUR_KAYDEDILDI: 'turKaydedildi',
  TUR_SILINDI: 'turSilindi',
  TUR_SECILDI: 'turSecildi',

  DURAK_KAYDEDILDI: 'durakKaydedildi',
  DURAK_SILINDI: 'durakSilindi',
  DURAKLAR_SIRALANDI: 'duraklarSiralandi',

  TASLAK_ACILDI: 'taslakAcildi',
  TASLAK_GUNCELLENDI: 'taslakGuncellendi',
  TASLAK_KAPANDI: 'taslakKapandi',

  OTURUMLAR_GELDI: 'oturumlarGeldi',
  OTURUM_GUNCELLENDI: 'oturumGuncellendi',
  OTURUM_BITTI: 'oturumBitti',
  OTURUM_IZLENIYOR: 'oturumIzleniyor',
  IZLEME_BIRAKILDI: 'izlemeBirakildi',

  KULLANICI_TANITILDI: 'kullaniciTanitildi',
}

// ---------------------------------------------------------------------------
//  Yardımcılar — hepsi saf; durumu yerinde DEĞİŞTİRMİYOR, yenisini üretiyor
// ---------------------------------------------------------------------------

/** Dizi → { [id]: nesne } sözlüğü. */
const sozlugeCevir = (liste) =>
  liste.reduce((toplam, kayit) => {
    toplam[kayit.id] = kayit
    return toplam
  }, {})

/** Bir turun duraklarını sıra numarasına göre 1..N olarak yeniden yazar. */
const siralariSikistir = (duraklar) =>
  [...duraklar]
    .sort((a, b) => a.order - b.order)
    .map((durak, i) => ({ ...durak, order: i + 1 }))

/**
 * Bir turu, duraklarına dokunan bir işlevden geçirir.
 * Tur bulunamazsa durum AYNEN döner (kimliği bilinmeyen bir güncelleme
 * sessizce yeni bir tur uydurmamalı).
 */
const durakGuncelle = (durum, turId, islev) => {
  const tur = durum.turlar[turId]
  if (!tur) return durum

  return {
    ...durum,
    turlar: {
      ...durum.turlar,
      [turId]: { ...tur, waypoints: siralariSikistir(islev(tur.waypoints)) },
    },
  }
}

// ---------------------------------------------------------------------------
//  İndirgeyici
// ---------------------------------------------------------------------------

/**
 * @param {TurDurumu} durum
 * @param {{tur: string, [k: string]: any}} eylem
 * @returns {TurDurumu}
 */
export function turReducer(durum = ILK_DURUM, eylem) {
  switch (eylem.tur) {
    case EYLEM.YUKLENIYOR:
      return { ...durum, yukleniyor: eylem.deger !== false, hata: null }

    case EYLEM.HATA:
      return { ...durum, yukleniyor: false, hata: eylem.mesaj ?? 'Bilinmeyen hata' }

    case EYLEM.KULLANICI_TANITILDI:
      return { ...durum, benimKullaniciId: eylem.kullaniciId ?? null }

    // ---------- Turlar ----------

    case EYLEM.TURLAR_GELDI:
      return {
        ...durum,
        yukleniyor: false,
        hata: null,
        turlar: sozlugeCevir(eylem.turlar),
        turSirasi: eylem.turlar.map((t) => t.id),
      }

    case EYLEM.TUR_KAYDEDILDI: {
      const tur = eylem.turu
      const yeniMi = !durum.turlar[tur.id]
      return {
        ...durum,
        turlar: { ...durum.turlar, [tur.id]: tur },
        // Yeni tur listenin BAŞINA: kullanıcı az önce eklediğini aramamalı.
        turSirasi: yeniMi ? [tur.id, ...durum.turSirasi] : durum.turSirasi,
      }
    }

    case EYLEM.TUR_SILINDI: {
      const { [eylem.turId]: _silinen, ...kalanlar } = durum.turlar
      return {
        ...durum,
        turlar: kalanlar,
        turSirasi: durum.turSirasi.filter((id) => id !== eylem.turId),
        // Silinen tur seçiliyse seçim de kalkmalı; yoksa panel olmayan bir
        // turu göstermeye çalışırdı.
        seciliTurId: durum.seciliTurId === eylem.turId ? null : durum.seciliTurId,
      }
    }

    case EYLEM.TUR_SECILDI:
      return { ...durum, seciliTurId: eylem.turId ?? null }

    // ---------- Duraklar ----------

    case EYLEM.DURAK_KAYDEDILDI: {
      const durak = eylem.durak
      return durakGuncelle(durum, durak.tourId, (duraklar) => {
        const varMi = duraklar.some((d) => d.id === durak.id)
        return varMi
          ? duraklar.map((d) => (d.id === durak.id ? durak : d))
          : [...duraklar, durak]
      })
    }

    case EYLEM.DURAK_SILINDI:
      return durakGuncelle(durum, eylem.turId, (duraklar) =>
        duraklar.filter((d) => d.id !== eylem.durakId),
      )

    case EYLEM.DURAKLAR_SIRALANDI:
      // Sürükle-bırak sonucu ANINDA uygulanıyor (iyimser güncelleme); sunucu
      // reddederse çağıran taraf TURLAR_GELDI ile listeyi tazeliyor.
      return durakGuncelle(durum, eylem.turId, (duraklar) =>
        eylem.durakIdleri
          .map((id, i) => {
            const durak = duraklar.find((d) => d.id === id)
            return durak ? { ...durak, order: i + 1 } : null
          })
          .filter(Boolean),
      )

    // ---------- Taslak durak ----------

    case EYLEM.TASLAK_ACILDI:
      return { ...durum, taslakDurak: eylem.taslak }

    case EYLEM.TASLAK_GUNCELLENDI:
      // Taslak kapalıyken gelen düzenleme yok sayılıyor: form kapandıktan
      // sonra gelen geç bir olay yeni bir taslak AÇMAMALI.
      return durum.taslakDurak
        ? { ...durum, taslakDurak: { ...durum.taslakDurak, ...eylem.alanlar } }
        : durum

    case EYLEM.TASLAK_KAPANDI:
      return { ...durum, taslakDurak: null }

    // ---------- Oturumlar ----------

    case EYLEM.OTURUMLAR_GELDI:
      return { ...durum, oturumlar: sozlugeCevir(eylem.oturumlar) }

    case EYLEM.OTURUM_GUNCELLENDI: {
      const gelen = eylem.oturum
      const eski = durum.oturumlar[gelen.id]

      // GEÇ GELEN MESAJI YOK SAY.
      //
      // SignalR mesajları ağ üzerinde sıralarını kaybedebilir; REST cevabı da
      // yayın mesajıyla yarışabilir. Zaman damgası eskiyse aracı geriye
      // atlatmamak için mesajı düşürüyoruz.
      if (eski && gelen.lastPositionUtc && eski.lastPositionUtc &&
          new Date(gelen.lastPositionUtc) < new Date(eski.lastPositionUtc)) {
        return durum
      }

      // Yayın mesajı katılımcı listesini TAŞIMIYOR (her tikte değişmeyen
      // veriyi tekrarlamamak için). Boş gelen listeyi yazsaydık ekrandaki
      // katılımcılar bir anda kaybolurdu — o yüzden eskisini koruyoruz.
      const katilimcilar = gelen.participants?.length
        ? gelen.participants
        : eski?.participants ?? []

      // Katılım kodu da yalnızca rehbere ve yalnızca ayrıntı cevabında dolu
      // geliyor; aynı gerekçeyle koruyoruz.
      const kod = gelen.joinCode ?? eski?.joinCode ?? null

      return {
        ...durum,
        oturumlar: {
          ...durum.oturumlar,
          [gelen.id]: { ...eski, ...gelen, participants: katilimcilar, joinCode: kod },
        },
        // Tur listesindeki "canlı" rozeti bu alandan besleniyor.
        turlar: durum.turlar[gelen.tourId]
          ? {
              ...durum.turlar,
              [gelen.tourId]: {
                ...durum.turlar[gelen.tourId],
                liveSessionId: oturumAcikMi(gelen.status) ? gelen.id : null,
              },
            }
          : durum.turlar,
      }
    }

    case EYLEM.OTURUM_BITTI: {
      const { [eylem.oturumId]: biten, ...kalanlar } = durum.oturumlar
      return {
        ...durum,
        oturumlar: kalanlar,
        izlenenOturumId:
          durum.izlenenOturumId === eylem.oturumId ? null : durum.izlenenOturumId,
        turlar: biten && durum.turlar[biten.tourId]
          ? {
              ...durum.turlar,
              [biten.tourId]: { ...durum.turlar[biten.tourId], liveSessionId: null },
            }
          : durum.turlar,
      }
    }

    case EYLEM.OTURUM_IZLENIYOR:
      return { ...durum, izlenenOturumId: eylem.oturumId }

    case EYLEM.IZLEME_BIRAKILDI:
      return { ...durum, izlenenOturumId: null }

    default:
      return durum
  }
}

// ---------------------------------------------------------------------------
//  Seçiciler (selector) — bileşenler durumu doğrudan kurcalamasın diye
// ---------------------------------------------------------------------------

/** Turlar, görüntüleme sırasıyla. */
export const turListesi = (durum) =>
  durum.turSirasi.map((id) => durum.turlar[id]).filter(Boolean)

/** Seçili tur; yoksa null. */
export const seciliTur = (durum) =>
  durum.seciliTurId ? durum.turlar[durum.seciliTurId] ?? null : null

/** Takip edilen oturum; yoksa null. */
export const izlenenOturum = (durum) =>
  durum.izlenenOturumId ? durum.oturumlar[durum.izlenenOturumId] ?? null : null

/**
 * Verilen oturumda REHBER miyiz?
 *
 * Sunucunun gönderdiği `myRole` okunuyor — kendi id'mizi guideUserId ile
 * karşılaştırmıyoruz (dosya başındaki gerekçe).
 */
export const rehberMi = (oturum) => oturum?.myRole === TUR_ROLU.REHBER

/** Oturumda bağlı olan katılımcılar (ayrılanlar hariç). */
export const bagliKatilimcilar = (oturum) =>
  (oturum?.participants ?? []).filter((k) => !k.leftUtc)

/** Turun canlı oturumu varsa onu döner. */
export const turunCanliOturumu = (durum, turId) => {
  const tur = durum.turlar[turId]
  return tur?.liveSessionId ? durum.oturumlar[tur.liveSessionId] ?? null : null
}
