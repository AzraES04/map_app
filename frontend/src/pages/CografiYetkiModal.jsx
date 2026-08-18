import { useCallback, useEffect, useRef, useState } from 'react'
import Map from 'ol/Map'
import View from 'ol/View'
import TileLayer from 'ol/layer/Tile'
import OSM from 'ol/source/OSM'
import VectorLayer from 'ol/layer/Vector'
import VectorSource from 'ol/source/Vector'
import Draw from 'ol/interaction/Draw'
import { Style, Fill, Stroke } from 'ol/style'
import { fromLonLat } from 'ol/proj'
import { createEmpty, extend as extentGenislet, isEmpty as extentBosMu } from 'ol/extent'
import 'ol/ol.css'

import { geometryToWkt, wktToFeature } from '../geo'
import { cografiYetkileriListele, cografiYetkiEkle, cografiYetkiSil } from '../adminApi'
import { SilIkonu } from '../icons'

// ============================================================================
//  COĞRAFİ YETKİ TANIMLAMA (Ödev 7 / Madde 2)
//
//  Kullanıcı veya rol satırındaki düğmeden açılır; Türkiye'ye zoomlanmış bir
//  harita gösterir ve o sahibe izinli alan çizdirir.
//
//  NEDEN AYRI BİLEŞEN? Aynı ekran hem Kullanıcı Listesi'nden hem Rol
//  Listesi'nden açılıyor. Tek fark sahibin kim olduğu — o da prop olarak
//  geliyor. İki kopya yazsaydık, haritayla ilgili her düzeltmeyi iki yerde
//  yapmak gerekirdi.
//
//  Harita kurulumu MapPage'inkinden bilinçli olarak SADE: burada katman
//  yönetimi, arama, analiz yok; tek iş bir poligon çizdirmek.
// ============================================================================

/** Türkiye'nin yaklaşık merkezi (boylam, enlem) ve tüm ülkeyi gösteren zoom. */
const TURKIYE_MERKEZ = [35.24, 39.0]
const TURKIYE_ZOOM = 5.6

/** Kaydedilmiş izinli alanların stili — marka mavisi, yarı saydam dolgu. */
const ALAN_STILI = new Style({
  fill: new Fill({ color: 'rgba(82, 179, 199, 0.18)' }),
  stroke: new Stroke({ color: '#52b3c7', width: 2 }),
})

/** Henüz kaydedilmemiş taslak — kesikli çizgi "geçici" demektir. */
const TASLAK_STILI = new Style({
  fill: new Fill({ color: 'rgba(232, 161, 60, 0.20)' }),
  stroke: new Stroke({ color: '#e8a13c', width: 2, lineDash: [6, 4] }),
})

/**
 * @param {{
 *   sahip: { tur: 'kullanici' | 'rol', id: number, ad: string },
 *   onKapat: Function,
 *   onDegisti?: Function,
 *   onOturumBitti: Function,
 * }} props
 */
export default function CografiYetkiModal({ sahip, onKapat, onDegisti, onOturumBitti }) {
  const haritaElement = useRef(null)
  const haritaRef = useRef(null)
  const alanKaynagiRef = useRef(null)     // kaydedilmiş alanlar
  const taslakKaynagiRef = useRef(null)   // yeni çizilen alan

  const [alanlar, setAlanlar] = useState([])
  const [yukleniyor, setYukleniyor] = useState(true)
  const [hata, setHata] = useState(null)
  const [kaydediliyor, setKaydediliyor] = useState(false)
  const [taslakWkt, setTaslakWkt] = useState(null)
  const [ad, setAd] = useState('')

  // ------------------------------------------------------------------
  //  Veri
  // ------------------------------------------------------------------

  const yukle = useCallback(async () => {
    setYukleniyor(true)
    setHata(null)
    try {
      const sorgu = sahip.tur === 'kullanici' ? { userId: sahip.id } : { roleId: sahip.id }
      setAlanlar(await cografiYetkileriListele(sorgu, onOturumBitti))
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    } finally {
      setYukleniyor(false)
    }
  }, [sahip.tur, sahip.id, onOturumBitti])

  useEffect(() => { yukle() }, [yukle])

  // ------------------------------------------------------------------
  //  Harita — bir kez kurulur
  // ------------------------------------------------------------------

  useEffect(() => {
    if (!haritaElement.current) return undefined

    const alanKaynagi = new VectorSource()
    const taslakKaynagi = new VectorSource()
    alanKaynagiRef.current = alanKaynagi
    taslakKaynagiRef.current = taslakKaynagi

    const harita = new Map({
      target: haritaElement.current,
      layers: [
        new TileLayer({ source: new OSM() }),
        new VectorLayer({ source: alanKaynagi, style: ALAN_STILI }),
        new VectorLayer({ source: taslakKaynagi, style: TASLAK_STILI }),
      ],
      view: new View({
        // Ödevin şartı: harita TÜRKİYE SINIRLARINA zoomlanmış açılsın.
        center: fromLonLat(TURKIYE_MERKEZ),
        zoom: TURKIYE_ZOOM,
      }),
      controls: [],   // modal içinde sade dursun
    })

    // Poligon çizim aracı sürekli açık: modalın tek işi bu. "Önce araca bas,
    // sonra çiz" adımını kaldırmak akışı bir tık kısaltıyor.
    const draw = new Draw({ source: taslakKaynagi, type: 'Polygon' })

    draw.on('drawstart', () => {
      // Aynı anda tek taslak: yenisi başlayınca eskisi silinsin, yoksa
      // hangi alanın kaydedileceği belirsiz olurdu.
      taslakKaynagi.clear()
    })

    draw.on('drawend', (olay) => {
      setTaslakWkt(geometryToWkt(olay.feature.getGeometry()))
    })

    harita.addInteraction(draw)
    haritaRef.current = harita

    return () => {
      harita.setTarget(undefined)
      haritaRef.current = null
    }
  }, [])

  // Kaydedilmiş alanlar değiştikçe haritayı tazele
  useEffect(() => {
    const kaynak = alanKaynagiRef.current
    const harita = haritaRef.current
    if (!kaynak || !harita) return

    kaynak.clear()
    alanlar.forEach((alan) => kaynak.addFeature(wktToFeature(alan.wkt)))

    // Tanımlı alan varsa oraya yaklaş; yoksa Türkiye görünümünde kal.
    if (alanlar.length === 0) return

    const kapsam = createEmpty()
    kaynak.getFeatures().forEach((f) => extentGenislet(kapsam, f.getGeometry().getExtent()))
    if (!extentBosMu(kapsam)) {
      harita.getView().fit(kapsam, { padding: [40, 40, 40, 40], maxZoom: 11 })
    }
  }, [alanlar])

  // ------------------------------------------------------------------
  //  İşlemler
  // ------------------------------------------------------------------

  const taslagiTemizle = () => {
    taslakKaynagiRef.current?.clear()
    setTaslakWkt(null)
    setAd('')
  }

  const kaydet = async (e) => {
    e.preventDefault()
    if (!taslakWkt) return

    setKaydediliyor(true)
    setHata(null)
    try {
      const sahiplik = sahip.tur === 'kullanici' ? { userId: sahip.id } : { roleId: sahip.id }
      await cografiYetkiEkle({ name: ad.trim(), ...sahiplik, wkt: taslakWkt }, onOturumBitti)
      taslagiTemizle()
      await yukle()
      onDegisti?.()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    } finally {
      setKaydediliyor(false)
    }
  }

  const sil = async (alan) => {
    if (!window.confirm(`"${alan.name}" alan tanımı kaldırılsın mı?`)) return

    setHata(null)
    try {
      await cografiYetkiSil(alan.id, onOturumBitti)
      await yukle()
      onDegisti?.()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    }
  }

  // ------------------------------------------------------------------

  const sahipEtiketi = sahip.tur === 'kullanici' ? 'kullanıcısı' : 'rolü'

  return (
    // Perdeye tıklayınca kapan; kutuya tıklayınca kapanmasın (stopPropagation).
    <div className="modal-perde" onClick={onKapat}>
      <div className="modal-kutu" onClick={(e) => e.stopPropagation()} role="dialog" aria-modal="true">
        <header className="modal-baslik">
          <div>
            <h2>Coğrafi Yetki — {sahip.ad} {sahipEtiketi}</h2>
            <p className="muted">
              Haritada bir alan çizin. {sahip.tur === 'rol'
                ? 'Bu roldeki kullanıcılar yalnızca bu alanın içine çizim yapabilir.'
                : 'Bu kullanıcı yalnızca bu alanın içine çizim yapabilir.'}
            </p>
          </div>
          <button type="button" className="modal-kapat" onClick={onKapat} aria-label="Kapat">×</button>
        </header>

        {hata && <p className="error-banner">{hata}</p>}

        <div className="modal-govde">
          <div className="geo-harita" ref={haritaElement} />

          <aside className="geo-yan">
            <form className="geo-form" onSubmit={kaydet}>
              <label htmlFor="alan-adi">Alan adı</label>
              <input
                id="alan-adi"
                value={ad}
                onChange={(e) => setAd(e.target.value)}
                placeholder="Örn. Ankara ve çevresi"
                maxLength={200}
                required
              />

              <p className={`geo-durum${taslakWkt ? ' hazir' : ''}`}>
                {taslakWkt
                  ? 'Alan çizildi. Ad verip kaydedin.'
                  : 'Haritada köşeleri tıklayın, bitirmek için çift tıklayın.'}
              </p>

              <div className="admin-eylemler">
                <button type="submit" className="btn-primary" disabled={!taslakWkt || kaydediliyor}>
                  {kaydediliyor ? 'Kaydediliyor…' : 'Alanı kaydet'}
                </button>
                <button type="button" className="btn-ghost" onClick={taslagiTemizle} disabled={!taslakWkt}>
                  Temizle
                </button>
              </div>
            </form>

            <h3>Tanımlı alanlar</h3>
            {yukleniyor ? (
              <p className="muted">Yükleniyor…</p>
            ) : alanlar.length === 0 ? (
              <p className="muted">
                Henüz alan tanımlanmamış — kısıt yok, haritanın her yerine çizim yapılabilir.
              </p>
            ) : (
              <ul className="geo-liste">
                {alanlar.map((alan) => (
                  <li key={alan.id}>
                    <span className="geo-liste-bilgi">
                      <strong>{alan.name}</strong>
                      <small>{new Date(alan.insertedDate).toLocaleDateString('tr-TR')}</small>
                    </span>
                    <button type="button" className="btn-ghost sil" onClick={() => sil(alan)} title="Kaldır">
                      <SilIkonu />
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </aside>
        </div>
      </div>
    </div>
  )
}
