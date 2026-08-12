import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import Map from 'ol/Map'
import View from 'ol/View'
import TileLayer from 'ol/layer/Tile'
import OSM from 'ol/source/OSM'
import VectorLayer from 'ol/layer/Vector'
import VectorSource from 'ol/source/Vector'
import Feature from 'ol/Feature'
import Point from 'ol/geom/Point'
import { fromLonLat } from 'ol/proj'
import { Style, Circle, Fill, Stroke } from 'ol/style'
import 'ol/ol.css'
import { authFetch, clearSession, getUsername, getExpiresAt, scheduleAutoLogout } from '../auth'

// Türkiye'nin yaklaşık merkezi (boylam, enlem)
const TURKEY_CENTER = [35.24, 39.0]
const TURKEY_ZOOM = 6.4

export default function MapPage() {
  const mapElement = useRef(null)
  const vectorSourceRef = useRef(null)
  const navigate = useNavigate()
  const [locations, setLocations] = useState([])
  const [remaining, setRemaining] = useState('')

  const goLogin = () => navigate('/login', { replace: true, state: { expired: true } })

  // Kayıtlı konumları API'den çek ve haritaya nokta olarak bas
  const loadLocations = async () => {
    try {
      const res = await authFetch('/api/locations', {}, goLogin)
      const data = await res.json()
      setLocations(data)
      const source = vectorSourceRef.current
      if (source) {
        source.clear()
        data.forEach((loc) => {
          source.addFeature(
            new Feature({ geometry: new Point(fromLonLat([loc.longitude, loc.latitude])) })
          )
        })
      }
    } catch {
      /* 401 durumunda authFetch zaten login'e yönlendirdi */
    }
  }

  useEffect(() => {
    // Token süresi dolunca otomatik çıkış → login ekranı
    scheduleAutoLogout(goLogin)

    // Kalan süre sayacı (üst barda gösterilir)
    const interval = setInterval(() => {
      const expiresAt = getExpiresAt()
      if (!expiresAt) return
      const ms = expiresAt.getTime() - Date.now()
      if (ms <= 0) { setRemaining('0:00'); return }
      const m = Math.floor(ms / 60000)
      const s = Math.floor((ms % 60000) / 1000)
      setRemaining(`${m}:${s.toString().padStart(2, '0')}`)
    }, 1000)

    // OpenLayers haritası: OSM altlık + konum noktaları katmanı
    const vectorSource = new VectorSource()
    vectorSourceRef.current = vectorSource

    const map = new Map({
      target: mapElement.current,
      layers: [
        new TileLayer({ source: new OSM() }),
        new VectorLayer({
          source: vectorSource,
          style: new Style({
            image: new Circle({
              radius: 7,
              fill: new Fill({ color: '#23606e' }),
              stroke: new Stroke({ color: '#ffffff', width: 2 }),
            }),
          }),
        }),
      ],
      view: new View({
        center: fromLonLat(TURKEY_CENTER), // Türkiye'ye zoomlu açılış
        zoom: TURKEY_ZOOM,
      }),
    })

    loadLocations()

    return () => {
      clearInterval(interval)
      map.setTarget(null)
    }
  }, [])

  const handleLogout = () => {
    clearSession()
    navigate('/login', { replace: true })
  }

  return (
    <div className="map-layout">
      <header className="topbar">
        <span className="topbar-title">🗺️ Harita Uygulaması</span>
        <span className="topbar-right">
          <span className="badge">⏱ {remaining}</span>
          <span className="badge user">👤 {getUsername()}</span>
          <button className="logout-btn" onClick={handleLogout}>Çıkış</button>
        </span>
      </header>

      <div className="map-content">
        <div ref={mapElement} className="map-container" />
        <aside className="side-panel">
          <h2>Kayıtlı Konumlar ({locations.length})</h2>
          {locations.length === 0 ? (
            <p className="muted">Henüz kayıt yok.</p>
          ) : (
            <ul>
              {locations.map((l) => (
                <li key={l.id}>
                  <strong>{l.name}</strong>
                  <span className="muted"> ({l.longitude.toFixed(4)}, {l.latitude.toFixed(4)})</span>
                </li>
              ))}
            </ul>
          )}
        </aside>
      </div>
    </div>
  )
}
