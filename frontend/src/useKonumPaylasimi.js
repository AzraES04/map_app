import { useCallback, useEffect, useRef, useState } from 'react'

import { konumBildir, konumYayininiDurdur } from './turApi'

// ============================================================================
//  REHBERİN CANLI KONUM YAYINI
//
//  ---- NEDEN YALNIZCA REHBER? ----
//  "Herkesin konumu" başka bir üründür: her katılımcıdan sürekli GPS toplamak
//  açık rıza, saklama süresi ve silme hakkı gerektirir. Rehberin konumu ise
//  turun KENDİSİNİN konumu — grup zaten onu takip ediyor ve yayını başlatan
//  da o.
//
//  ---- NEDEN AÇIK BİR DÜĞMEYE BAĞLI? ----
//  Tarayıcı zaten izin soruyor ama izin bir kez verilince kalıcı olabiliyor.
//  Kendi düğmemiz olmadan rehber, konumunun hâlâ yayınlandığını fark etmeden
//  turdan çıkabilirdi. Düğme kapatıldığında konum sunucudan da SİLİNİYOR.
//
//  ---- NEDEN HER OKUMADA GÖNDERİLMİYOR? ----
//  watchPosition şehir içinde saniyede birkaç kez tetiklenebiliyor. Her
//  tetiklemede istek atmak hem pilin hem sunucunun israfı; misafir sayfası
//  zaten 15 saniyede bir okuyor. GÖNDERİM ARALIĞI o yüzden 10 saniye —
//  yayının okunma sıklığından kısa, ama gereksiz sık değil.
// ============================================================================

/** En sık gönderim aralığı (ms). */
const GONDERIM_ARALIGI_MS = 10_000

export function useKonumPaylasimi(oturumId, onUnauthorized) {
  const [paylasiliyor, setPaylasiliyor] = useState(false)
  const [hata, setHata] = useState(null)
  const [sonKonum, setSonKonum] = useState(null)

  const izlemeRef = useRef(null)
  const sonGonderimRef = useRef(0)

  const durdur = useCallback(async () => {
    if (izlemeRef.current !== null && typeof navigator !== 'undefined') {
      navigator.geolocation.clearWatch(izlemeRef.current)
      izlemeRef.current = null
    }

    setPaylasiliyor(false)
    setSonKonum(null)

    // Sunucudaki konumu da siliyoruz: yalnızca izlemeyi bıraksaydık son
    // konum kayıtta kalır ve misafirler onu "şu an oradalar" diye okurdu.
    if (oturumId) {
      try {
        await konumYayininiDurdur(oturumId, onUnauthorized)
      } catch {
        /* yayın zaten kapalıysa sorun değil */
      }
    }
  }, [oturumId, onUnauthorized])

  const baslat = useCallback(() => {
    if (typeof navigator === 'undefined' || !navigator.geolocation) {
      setHata('Bu tarayıcı konum paylaşımını desteklemiyor.')
      return
    }

    setHata(null)
    setPaylasiliyor(true)
    sonGonderimRef.current = 0

    izlemeRef.current = navigator.geolocation.watchPosition(
      (konum) => {
        const { latitude, longitude, accuracy } = konum.coords
        setSonKonum({ lat: latitude, lon: longitude, dogruluk: accuracy })

        const simdi = Date.now()
        if (simdi - sonGonderimRef.current < GONDERIM_ARALIGI_MS) return
        sonGonderimRef.current = simdi

        konumBildir(
          oturumId,
          { lat: latitude, lon: longitude, dogrulukMetre: accuracy },
          onUnauthorized,
        ).catch((err) => setHata(err.message || 'Konum gönderilemedi.'))
      },
      (err) => {
        // İZİN REDDİ ile SİNYAL YOKLUĞU ayrı mesajlar: ilkinde kullanıcının
        // yapacağı bir şey var (tarayıcı ayarı), ikincisinde yok.
        setHata(err.code === err.PERMISSION_DENIED
          ? 'Konum izni verilmedi.'
          : 'Konum alınamıyor (sinyal yok).')
        setPaylasiliyor(false)
      },
      { enableHighAccuracy: true, maximumAge: 5_000, timeout: 20_000 },
    )
  }, [oturumId, onUnauthorized])

  // Bileşen kapanınca izlemeyi bırak. Sunucudaki konumu SİLMİYORUZ: sayfayı
  // yenileyen rehberin yayını kesilmemeli — bilinçli kapatma ayrı bir eylem.
  useEffect(() => () => {
    if (izlemeRef.current !== null && typeof navigator !== 'undefined') {
      navigator.geolocation.clearWatch(izlemeRef.current)
    }
  }, [])

  return { paylasiliyor, sonKonum, hata, baslat, durdur }
}
