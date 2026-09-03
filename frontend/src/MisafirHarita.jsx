import { useEffect, useRef } from 'react'
import Map from 'ol/Map'
import View from 'ol/View'
import TileLayer from 'ol/layer/Tile'
import VectorLayer from 'ol/layer/Vector'
import VectorSource from 'ol/source/Vector'
import OSM from 'ol/source/OSM'
import Feature from 'ol/Feature'
import { LineString, Point } from 'ol/geom'
import { fromLonLat } from 'ol/proj'
import { Circle as CircleStyle, Fill, Stroke, Style, Text } from 'ol/style'

import { cizgiCoz } from './turSimulasyonu'
import { noktaCoz } from './turIlerleme'

// ============================================================================
//  MİSAFİR HARİTASI — rota, duraklar ve rehberin CANLI KONUMU
//
//  ---- ÖNCE "HARİTA YOK" DENMİŞTİ, NEDEN DEĞİŞTİ? ----
//  İlk sürümde misafir sayfasına harita koymadım; gerekçem paketin ağırlığı
//  ve telefonda yol tarifinin zaten cihazın kendi uygulamasında daha iyi
//  olmasıydı. İkisi de hâlâ doğru ama soruyu yanlış cevaplıyordu:
//  kullanıcının istediği yol tarifi değil, "ŞU AN NEREDEYİZ".
//
//  Ağırlık gerekçesi de zayıftı: OpenLayers zaten aynı paketin içinde,
//  misafir onu harita olsa da olmasa da indiriyor.
//
//  ---- NEDEN KENDİ BİLEŞENİ? ----
//  MapPage'deki harita çizim araçları, katman anahtarları, WMS ve yetki
//  mantığıyla iç içe. Misafire gereken üç şey var: çizgi, duraklar, canlı
//  nokta. Ayrı ve küçük bir bileşen, o üçünü misafirin görmemesi gereken
//  hiçbir şeyi taşımadan veriyor.
// ============================================================================

/** Rehberin canlı konumu — nabız gibi atan nokta. */
function konumStili() {
  return new Style({
    image: new CircleStyle({
      radius: 7,
      fill: new Fill({ color: '#2fbf71' }),
      stroke: new Stroke({ color: '#ffffff', width: 2.5 }),
    }),
  })
}

function durakStili(mevcutSira) {
  return (feature) => {
    const sira = feature.get('sira')
    const simdi = sira === mevcutSira

    return new Style({
      image: new CircleStyle({
        radius: simdi ? 11 : 8,
        fill: new Fill({ color: simdi ? '#7b5cd6' : '#43606e' }),
        stroke: new Stroke({ color: '#ffffff', width: simdi ? 2.5 : 1.5 }),
      }),
      text: new Text({
        text: String(sira),
        fill: new Fill({ color: '#ffffff' }),
        font: `${simdi ? 700 : 600} 11px system-ui, sans-serif`,
      }),
    })
  }
}

export default function MisafirHarita({ tur }) {
  const kapsayiciRef = useRef(null)
  const haritaRef = useRef(null)
  const kaynakRef = useRef(null)
  const konumKaynagiRef = useRef(null)

  // Harita BİR KEZ kuruluyor; sonraki güncellemeler yalnızca katman
  // kaynaklarına yazıyor. Her yoklamada haritayı yeniden kursaydık ekran
  // 15 saniyede bir sıfırlanır, misafirin yaptığı yakınlaştırma kaybolurdu.
  useEffect(() => {
    if (!kapsayiciRef.current || haritaRef.current) return undefined

    const kaynak = new VectorSource()
    const konumKaynagi = new VectorSource()
    kaynakRef.current = kaynak
    konumKaynagiRef.current = konumKaynagi

    haritaRef.current = new Map({
      target: kapsayiciRef.current,
      layers: [
        new TileLayer({ source: new OSM() }),
        new VectorLayer({ source: kaynak }),
        // Canlı konum EN ÜSTTE: durak numaralarının arkasında kalırsa
        // "grup nerede" sorusu cevapsız kalır.
        new VectorLayer({ source: konumKaynagi, style: konumStili(), zIndex: 10 }),
      ],
      view: new View({ center: fromLonLat([32.85, 39.93]), zoom: 12 }),
      controls: [],
    })

    return () => {
      haritaRef.current?.setTarget(undefined)
      haritaRef.current = null
    }
  }, [])

  // Rota + duraklar
  useEffect(() => {
    const kaynak = kaynakRef.current
    if (!kaynak || !tur) return

    kaynak.clear()

    const cizgi = cizgiCoz(tur.routeWkt)
    if (cizgi.length > 1) {
      kaynak.addFeature(new Feature({
        geometry: new LineString(cizgi.map((n) => fromLonLat([n.lon, n.lat]))),
      }))
    }

    for (const durak of tur.waypoints ?? []) {
      const nokta = noktaCoz(durak.wkt)
      if (!nokta) continue

      const f = new Feature({ geometry: new Point(fromLonLat([nokta.lon, nokta.lat])) })
      f.set('sira', durak.order)
      f.setStyle(durakStili(tur.currentWaypointOrder)(f))
      kaynak.addFeature(f)
    }

    // İLK YÜKLEMEDE rotaya sığdır, sonrakilerde DOKUNMA: her yoklamada
    // yeniden sığdırsaydık misafir haritayı kaydırdığı anda geri zıplardı.
    if (!haritaRef.current?.get('sigdirildi') && kaynak.getFeatures().length > 0) {
      haritaRef.current?.getView().fit(kaynak.getExtent(), { padding: [30, 30, 30, 30] })
      haritaRef.current?.set('sigdirildi', true)
    }
  }, [tur])

  // Rehberin canlı konumu
  useEffect(() => {
    const kaynak = konumKaynagiRef.current
    if (!kaynak) return

    kaynak.clear()

    if (tur?.guideLat == null || tur?.guideLon == null) return

    kaynak.addFeature(new Feature({
      geometry: new Point(fromLonLat([tur.guideLon, tur.guideLat])),
    }))
  }, [tur?.guideLat, tur?.guideLon])

  // Çizginin stili katman düzeyinde verilemiyor (durak stilleri feature
  // başına), o yüzden çizgiye kendi stilini burada veriyoruz.
  useEffect(() => {
    const cizgiler = kaynakRef.current?.getFeatures()
      .filter((f) => f.getGeometry() instanceof LineString) ?? []

    for (const f of cizgiler) {
      f.setStyle(new Style({
        stroke: new Stroke({ color: tur?.color || '#7b5cd6', width: 4 }),
      }))
    }
  }, [tur])

  return <div className="misafir-harita" ref={kapsayiciRef} />
}
