import { useCallback, useEffect, useMemo, useRef, useState } from 'react'

import {
  GOSTERIM_SURESI_MS,
  simulasyonDurumu,
  simulasyonHazirla,
} from './turSimulasyonu'

// ============================================================================
//  TUR SİMÜLASYONUNU OYNATAN KANCA
//
//  Hesap saf modülde (turSimulasyonu.js); burada yalnızca ZAMAN yönetiliyor:
//  başlat, ilerlet, durdur, bileşen kapanınca temizle.
//
//  ---- NEDEN setInterval, requestAnimationFrame DEĞİL? ----
//  rAF daha akıcı olurdu ama iki dezavantajı var ve ikisi de bu iş için
//  önemli:
//
//    1. Sekme arka plana düşünce rAF DURUYOR. Sunum sırasında haritayı açık
//       bırakıp başka sekmeye geçen biri geri döndüğünde simülasyonu donmuş
//       bulurdu.
//    2. Test edilmesi zor: sahte zamanlayıcılarla rAF'ı ilerletmek ayrı bir
//       kurulum istiyor.
//
//  25 kare/sn (40 ms), şehir ölçeğinde ilerleyen bir simge için fazlasıyla
//  akıcı — hareket zaten saniyede birkaç piksel.
//
//  ---- SİMGE NEREDE DURUYOR? ----
//  İlk sürümde araç yalnızca oynatılırken görünüyor, oynatma da rotanın
//  BAŞINDAN başlıyordu. Kullanıcı geri bildirimi: "simülasyon rastgele bir
//  şekilde değil de şu anda bulunan tur noktasında görünecek şekilde olsun
//  istemiştim."
//
//  Haklı bir itirazdı: canlı bir turda haritadaki simgenin anlamı "grup
//  nerede" olmalı, "bir gösterim nereden başlıyor" değil. Artık:
//
//    CANLI TURDA   → simge, rehberin ilerlettiği MEVCUT DURAKTA duruyor.
//                    Oynat, oradan SIRADAKİ durağa olan bacağı canlandırıyor;
//                    bitince yeni durakta park ediyor.
//    ÖNERİDE       → henüz bir "şu an" yok (tur başlamadı), bu yüzden eski
//                    davranış korunuyor: baştan sona önizleme.
// ============================================================================

/** Bir kare aralığı (ms) — 25 kare/saniye. */
const KARE_MS = 40

/**
 * @param {object|null} tur Rotası ve durakları olan tur (öneri ya da kayıtlı).
 * @param {object|null} oturum Canlı oturum; verilirse simge MEVCUT durakta
 *   duruyor ve oynatma yalnızca sıradaki bacağı canlandırıyor.
 * @param {number} sureMs Tam turun gösterim süresi; gerçek seyahat süresi DEĞİL.
 * @returns {{oynatilabilir: boolean, oynuyor: boolean, canli: boolean,
 *            durum: object|null, oynat: Function, durdur: Function}}
 */
export function useTurSimulasyonu(tur, oturum = null, sureMs = GOSTERIM_SURESI_MS) {
  // Rota değişmedikçe tablo yeniden kurulmuyor: uzun bir rotada durak
  // izdüşümleri her karede değil, bir kez hesaplanıyor.
  const sim = useMemo(() => simulasyonHazirla(tur), [tur])

  // Grubun bulunduğu durağın rota üzerindeki oranı.
  //
  // Oturum yoksa (öneri önizlemesi) null: gösterilecek bir "şu an" yok.
  const mevcutOran = useMemo(() => {
    if (!sim || !oturum) return null

    const sira = oturum.currentWaypointOrder
    if (!sira) return 0                      // tur başladı ama ilk durağa varılmadı

    return sim.oranlar.find((o) => o.order === sira)?.oran ?? 0
  }, [sim, oturum])

  /** Sıradaki durağın oranı — oynatmanın bittiği yer. Son duraktaysa null. */
  const hedefOran = useMemo(() => {
    if (!sim || mevcutOran === null) return null

    const sonraki = sim.oranlar.find((o) => o.oran > mevcutOran + 1e-9)
    return sonraki?.oran ?? null
  }, [sim, mevcutOran])

  const [durum, setDurum] = useState(null)
  const [oynuyor, setOynuyor] = useState(false)
  const sayacRef = useRef(null)

  const temizle = useCallback(() => {
    if (sayacRef.current) {
      clearInterval(sayacRef.current)
      sayacRef.current = null
    }
  }, [])

  /**
   * DURAKTA PARK ETMİŞ simge.
   *
   * Oynatma bitince ya da hiç oynatılmamışken haritada görünen şey bu.
   * Canlı tur yoksa null: önizlemede simge ancak oynatılırken çıkıyor.
   */
  const parkDurumu = useCallback(() => {
    if (!sim || mevcutOran === null) return null
    return simulasyonDurumu(sim.tablo, sim.oranlar, mevcutOran)
  }, [sim, mevcutOran])

  const durdur = useCallback(() => {
    temizle()
    setOynuyor(false)
    // Canlı turda simge KALIYOR — yalnızca grubun bulunduğu yere dönüyor.
    // Sıfırlayıp yok etseydik "durdur" tuşu, turun nerede olduğunu da
    // ekrandan silerdi.
    setDurum(parkDurumu())
  }, [temizle, parkDurumu])

  const oynat = useCallback(() => {
    if (!sim) return

    temizle()
    setOynuyor(true)

    // İki kip: canlı turda YALNIZCA sıradaki bacak, önizlemede tam tur.
    const baslangicOrani = mevcutOran ?? 0
    const bitisOrani = mevcutOran === null ? 1 : (hedefOran ?? 1)
    const araOran = Math.max(0, bitisOrani - baslangicOrani)

    // Süre, kat edilen orana göre kısalıyor: tek bir bacak da 45 saniye
    // sürseydi kısa bir yürüyüş, uzun bir yolculuk kadar zaman alırdı.
    const buSure = Math.max(1_200, sureMs * (araOran || 1))

    // Zaman DUVAR SAATİNDEN okunuyor, kare sayısından değil: bir kare
    // gecikirse (tarayıcı meşgul) simülasyon yavaşlamıyor, atlıyor.
    const baslangic = Date.now()

    sayacRef.current = setInterval(() => {
      const t = Math.min(1, (Date.now() - baslangic) / buSure)
      const oran = baslangicOrani + araOran * t

      setDurum(simulasyonDurumu(sim.tablo, sim.oranlar, oran))

      if (t >= 1) {
        temizle()
        setOynuyor(false)
      }
    }, KARE_MS)
  }, [sim, mevcutOran, hedefOran, sureMs, temizle])

  // Tur ya da grubun konumu değişince simge yeniden konumlanıyor: rehber
  // sonraki durağa geçtiğinde harita kendiliğinden güncelleniyor.
  useEffect(() => {
    temizle()
    setOynuyor(false)
    setDurum(parkDurumu())
    return temizle
  }, [parkDurumu, temizle])

  return {
    oynatilabilir: sim !== null,
    // Canlı bir turu mu izliyoruz? Arayüz düğme metnini buna göre seçiyor.
    canli: mevcutOran !== null,
    oynuyor,
    durum,
    oynat,
    durdur,
  }
}
