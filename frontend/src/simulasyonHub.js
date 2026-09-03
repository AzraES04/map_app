// ============================================================================
//  SignalR bağlantısı — canlı araç konumları (Ödev 19 / Madde 2)
//
//  BU DOSYA NEDEN VAR?
//  Bağlantı UYGULAMA ÖMÜRLÜDÜR, bileşen ömürlü değil: kullanıcı bir hattı
//  takip ederken haritada gezinip popup açıp kapatabilir, hatta React
//  bileşeni yeniden çizebilir — bağlantı bunlardan etkilenmemeli. Bu yüzden
//  bağlantı modül düzeyinde tek bir nesne olarak tutuluyor (tekil / singleton)
//  ve bileşenler ona yalnızca abone oluyor.
//
//  TOKEN NEREDEN GELİYOR?
//  `accessTokenFactory` her (yeniden) bağlanmada çağrılıyor ve o anki
//  token'ı okuyor. Sabit bir değer geçseydik, oturum yenilendikten sonraki
//  ilk yeniden bağlanma eski token'la denenir ve 401 alırdı.
//
//  Tarayıcı WebSocket el sıkışmasına özel başlık ekleyemediği için SignalR
//  token'ı adres satırına koyuyor (?access_token=...). Sunucu tarafında bu
//  YALNIZCA /hubs yolları için okunuyor (bkz. Program.cs → OnMessageReceived).
// ============================================================================

import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { getToken } from './auth'

/** Sunucudaki hub yolu — Program.cs'teki MapHub ile aynı olmalı. */
const HUB_YOLU = '/hubs/simulasyon'

/** Sunucunun gönderdiği olayların adları — SimulasyonYayinci ile aynı olmalı. */
const KONUM_OLAYI = 'KonumGuncellendi'
const BASLADI_OLAYI = 'SimulasyonBasladi'
const BITTI_OLAYI = 'SimulasyonBitti'

let baglanti = null

/** Aynı anda birden çok "bağlan" çağrısı gelirse tek sözü paylaşsınlar. */
let baglanmaSozu = null

/** Konum güncellemesini dinleyen bileşenler. */
const dinleyiciler = new Set()

/**
 * Sefer BAŞLADI / BİTTİ duyurularını dinleyenler.
 *
 * Konum dinleyicilerinden AYRI: bu iki olay herkese gidiyor ve amacı
 * düğmeleri tazelemek ("Takip Et" belirsin/kalksın). Konum mesajları ise
 * yalnızca takip edenlere gidiyor ve haritadaki aracı oynatıyor.
 */
const degisimDinleyicileri = new Set()

/**
 * Şu an katıldığımız gruplar.
 *
 * Neden saklıyoruz? Bağlantı koptuğunda SignalR yeniden bağlanıyor ama
 * GRUP ÜYELİKLERİ SUNUCUDA KALMIYOR — yeni bağlantı yeni bir kimlik. Bunu
 * bilmeyen uygulamalar "bir süre sonra güncellemeler durdu" hatasını yaşar;
 * biz yeniden bağlanınca grupları tekrar kuruyoruz.
 */
const gruplar = new Set()

/**
 * Bağlantıyı kurar (zaten kuruluysa onu döner).
 * Hata durumunda `null` döner: canlı takip bir KOLAYLIK, uygulamanın geri
 * kalanı SignalR olmadan da çalışmaya devam etmeli.
 */
export async function hubaBaglan() {
  if (baglanti?.state === HubConnectionState.Connected) return baglanti
  if (baglanmaSozu) return baglanmaSozu

  baglanmaSozu = (async () => {
    try {
      if (!baglanti) {
        baglanti = new HubConnectionBuilder()
          .withUrl(HUB_YOLU, { accessTokenFactory: () => getToken() ?? '' })
          // Kopan bağlantı kendiliğinden toparlansın: varsayılan deneme
          // aralıkları 0, 2, 10, 30 saniye. Elle yazsaydık aynı sayıları
          // tekrarlamış olurduk.
          .withAutomaticReconnect()
          // Konsolu boğmasın: uyarı ve hatalar yeter.
          .configureLogging(LogLevel.Warning)
          .build()

        // ⚠ Bu geri çağrımlar DEĞER DÖNDÜRMEMELİ. SignalR 7+ istemcisi,
        // değer dönen bir istemci metodunu "sunucuya sonuç gönderiyorum"
        // sanıyor ve sunucu sonuç beklemediği için her mesajda konsola
        // hata basıyor. `forEach` undefined döndürdüğü için güvendeyiz;
        // gövdeyi süslü parantezsiz tek satıra indirmek bunu bozardı.
        baglanti.on(KONUM_OLAYI, (durum) => {
          dinleyiciler.forEach((dinleyici) => dinleyici(durum))
        })

        baglanti.on(BASLADI_OLAYI, (durum) => {
          degisimDinleyicileri.forEach((d) => d({ tur: 'basladi', durum }))
        })

        baglanti.on(BITTI_OLAYI, (guzergahId) => {
          degisimDinleyicileri.forEach((d) => d({ tur: 'bitti', guzergahId }))
        })

        // Yeniden bağlanınca grup üyelikleri sıfırlanmış olur; geri kuruyoruz.
        baglanti.onreconnected(() => {
          gruplar.forEach((id) => {
            baglanti.invoke('Katil', id).catch(() => { /* bir sonraki denemede */ })
          })
        })
      }

      if (baglanti.state === HubConnectionState.Disconnected) {
        await baglanti.start()
      }

      return baglanti
    } catch {
      // Sunucu kapalı ya da hub yolu vekillenmemiş olabilir. Sessizce
      // null dönüyoruz; çağıran taraf kullanıcıya kendi mesajını veriyor.
      return null
    } finally {
      baglanmaSozu = null
    }
  })()

  return baglanmaSozu
}

/**
 * Konum güncellemelerini dinle.
 * @param {(durum: object) => void} geriCagir
 * @returns {() => void} aboneliği bırakan fonksiyon
 */
export function konumDinle(geriCagir) {
  dinleyiciler.add(geriCagir)
  return () => dinleyiciler.delete(geriCagir)
}

/**
 * Sefer başladı/bitti duyurularını dinle.
 *
 * Gruba katılmaya GEREK YOK: bu iki olay bağlı olan herkese gidiyor, çünkü
 * amacı "hangi hatta sefer var?" sorusunu cevaplamak — takip etmeden önce
 * sorulan soru bu.
 *
 * @param {(olay: {tur: 'basladi'|'bitti', durum?: object, guzergahId?: number}) => void} geriCagir
 * @returns {() => void} aboneliği bırakan fonksiyon
 */
export function simulasyonDegisimiDinle(geriCagir) {
  degisimDinleyicileri.add(geriCagir)
  return () => degisimDinleyicileri.delete(geriCagir)
}

/** "Takip Et" — hattın yayın grubuna katıl. */
export async function guzergahiTakipEt(guzergahId) {
  const b = await hubaBaglan()
  if (!b) return false

  await b.invoke('Katil', guzergahId)
  gruplar.add(guzergahId)
  return true
}

/** "Takibi Bırak" — gruptan çık. */
export async function takibiBirak(guzergahId) {
  gruplar.delete(guzergahId)
  if (baglanti?.state !== HubConnectionState.Connected) return

  try {
    await baglanti.invoke('Ayril', guzergahId)
  } catch {
    /* bağlantı bu arada koptuysa zaten gruptan düşmüşüz demektir */
  }
}

/**
 * Bağlantıyı tamamen kapat (çıkış yaparken).
 *
 * Kapatmasaydık, çıkış yapan kullanıcının açık bir kanalı kalırdı ve
 * sunucu ona veri göndermeye devam ederdi.
 */
export async function baglantiyiKapat() {
  gruplar.clear()
  dinleyiciler.clear()
  degisimDinleyicileri.clear()

  if (!baglanti) return
  try {
    await baglanti.stop()
  } catch {
    /* kapanırken hata önemli değil */
  }
  baglanti = null
}

/** Testler ve tanılama için: şu an takip edilen güzergah id'leri. */
export const takipEdilenGruplar = () => [...gruplar]
