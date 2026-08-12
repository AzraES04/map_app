import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import Map from 'ol/Map'
import View from 'ol/View'
import TileLayer from 'ol/layer/Tile'
import OSM from 'ol/source/OSM'
import VectorLayer from 'ol/layer/Vector'
import VectorSource from 'ol/source/Vector'
import Draw from 'ol/interaction/Draw'
import { Style, Circle, Fill, Stroke } from 'ol/style'
import { fromLonLat } from 'ol/proj'
import 'ol/ol.css'

import { clearSession, getUsername, getExpiresAt, scheduleAutoLogout } from '../auth'
import { DRAW_TYPES, DRAW_TYPE_KEYS, geometryToWkt, wktToFeature, describeGeometry } from '../geo'
import { listele, kaydet, sil } from '../api'

// Türkiye'nin yaklaşık merkezi (boylam, enlem) — 4326 cinsinden yazıp
// fromLonLat ile haritanın diline (3857) çeviriyoruz.
const TURKEY_CENTER = [35.24, 39.0]
const TURKEY_ZOOM = 6.4

// --------------------------------------------------------------------------
//  Katman stilleri
// --------------------------------------------------------------------------

/** Kaydedilmiş geometrilerin görünümü — her tip kendi rengiyle. */
function kayitStili(type) {
  const renk = DRAW_TYPES[type].color
  return new Style({
    image: new Circle({
      radius: 7,
      fill: new Fill({ color: renk }),
      stroke: new Stroke({ color: '#ffffff', width: 2 }),
    }),
    stroke: new Stroke({ color: renk, width: 3 }),
    fill: new Fill({ color: `${renk}33` }),   // sondaki 33 = %20 saydamlık (hex alfa)
  })
}

/** Henüz kaydedilmemiş çizim: kesikli turuncu — "bu geçici" mesajını verir. */
const taslakStili = new Style({
  image: new Circle({
    radius: 7,
    fill: new Fill({ color: '#e08b2f' }),
    stroke: new Stroke({ color: '#ffffff', width: 2 }),
  }),
  stroke: new Stroke({ color: '#e08b2f', width: 3, lineDash: [8, 6] }),
  fill: new Fill({ color: 'rgba(224, 139, 47, 0.20)' }),
})

/** Listede fareyle üzerine gelinen kaydın haritadaki vurgusu. */
const vurguStili = new Style({
  image: new Circle({
    radius: 11,
    fill: new Fill({ color: 'rgba(224, 139, 47, 0.9)' }),
    stroke: new Stroke({ color: '#ffffff', width: 3 }),
  }),
  stroke: new Stroke({ color: '#e08b2f', width: 6 }),
  fill: new Fill({ color: 'rgba(224, 139, 47, 0.35)' }),
})

const BOS_KAYITLAR = { Point: [], LineString: [], Polygon: [] }

export default function MapPage() {
  const navigate = useNavigate()

  // --- DOM ve OpenLayers nesneleri için ref'ler -----------------------------
  // Neden state değil ref? Bu nesneler değiştiğinde React'in yeniden render
  // etmesine gerek yok; üstelik render'lar arasında AYNI nesnenin kalması şart.
  const mapElement = useRef(null)
  const mapRef = useRef(null)
  const sourcesRef = useRef({})        // { Point: VectorSource, LineString: ..., Polygon: ... }
  const layersRef = useRef({})
  const drawSourceRef = useRef(null)   // geçici çizim katmanı
  const drawRef = useRef(null)         // aktif Draw interaction
  const highlightSourceRef = useRef(null)

  // --- Ekran durumu ---------------------------------------------------------
  const [activeTool, setActiveTool] = useState(null)      // null | 'Point' | 'LineString' | 'Polygon'
  const [pending, setPending] = useState(null)            // çizildi, henüz kaydedilmedi
  const [form, setForm] = useState({ name: '', description: '' })
  const [records, setRecords] = useState(BOS_KAYITLAR)
  const [visible, setVisible] = useState({ Point: true, LineString: true, Polygon: true })
  const [activeTab, setActiveTab] = useState('Point')
  const [toast, setToast] = useState(null)                // { tur: 'ok' | 'hata', mesaj }
  const [saving, setSaving] = useState(false)
  const [remaining, setRemaining] = useState('')

  const goLogin = useCallback(
    () => navigate('/login', { replace: true, state: { expired: true } }),
    [navigate],
  )

  const bildir = useCallback((tur, mesaj) => {
    setToast({ tur, mesaj })
    setTimeout(() => setToast(null), 3500)
  }, [])

  // ------------------------------------------------------------------------
  //  Veritabanından kayıtları çek ve haritaya bas
  // ------------------------------------------------------------------------
  const yukle = useCallback(async () => {
    try {
      // Üç isteği paralel atıyoruz; sırayla beklemenin anlamı yok.
      const [points, lines, polygons] = await Promise.all(
        DRAW_TYPE_KEYS.map((key) => listele(DRAW_TYPES[key].endpoint, goLogin)),
      )
      const gelen = { Point: points, LineString: lines, Polygon: polygons }

      DRAW_TYPE_KEYS.forEach((key) => {
        const source = sourcesRef.current[key]
        if (!source) return
        source.clear()
        gelen[key].forEach((dto) => {
          // WKT (4326) → feature (3857). Dönüşüm geo.js'in içinde.
          const feature = wktToFeature(dto.wkt)
          feature.setId(`${key}-${dto.id}`)   // sonradan bulabilmek için kimlik
          source.addFeature(feature)
        })
      })

      setRecords(gelen)
    } catch (err) {
      // 401 ise authFetch zaten login'e yönlendirdi; diğer hataları gösteriyoruz.
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    }
  }, [goLogin, bildir])

  // ------------------------------------------------------------------------
  //  Harita kurulumu — sadece bir kez
  // ------------------------------------------------------------------------
  useEffect(() => {
    const sources = {}
    const layers = {}

    DRAW_TYPE_KEYS.forEach((key) => {
      sources[key] = new VectorSource()
      layers[key] = new VectorLayer({ source: sources[key], style: kayitStili(key) })
    })
    sourcesRef.current = sources
    layersRef.current = layers

    const drawSource = new VectorSource()
    drawSourceRef.current = drawSource

    const highlightSource = new VectorSource()
    highlightSourceRef.current = highlightSource

    const map = new Map({
      target: mapElement.current,
      layers: [
        new TileLayer({ source: new OSM() }),
        // Sıra önemli: poligon en altta, çizgi ortada, nokta en üstte dursun ki
        // küçük noktalar büyük alanların altında kaybolmasın.
        layers.Polygon,
        layers.LineString,
        layers.Point,
        new VectorLayer({ source: drawSource, style: taslakStili }),
        new VectorLayer({ source: highlightSource, style: vurguStili }),
      ],
      view: new View({
        center: fromLonLat(TURKEY_CENTER),   // 4326 → 3857 dönüşümü
        zoom: TURKEY_ZOOM,
      }),
    })
    mapRef.current = map

    yukle()

    // Temizlik: bileşen kaldırılınca haritayı DOM'dan ayır.
    // React StrictMode geliştirmede effect'i iki kez çalıştırır; bu satır
    // olmasaydı sayfada iki harita üst üste binerdi.
    return () => {
      map.setTarget(null)
      mapRef.current = null
    }
  }, [yukle])

  // ------------------------------------------------------------------------
  //  Oturum: otomatik çıkış + kalan süre sayacı
  // ------------------------------------------------------------------------
  useEffect(() => {
    scheduleAutoLogout(goLogin)

    const interval = setInterval(() => {
      const expiresAt = getExpiresAt()
      if (!expiresAt) return
      const ms = expiresAt.getTime() - Date.now()
      if (ms <= 0) { setRemaining('0:00'); return }
      const m = Math.floor(ms / 60000)
      const s = Math.floor((ms % 60000) / 1000)
      setRemaining(`${m}:${s.toString().padStart(2, '0')}`)
    }, 1000)

    return () => clearInterval(interval)
  }, [goLogin])

  // ------------------------------------------------------------------------
  //  Çizim etkileşimi — activeTool her değiştiğinde yeniden kurulur
  // ------------------------------------------------------------------------
  useEffect(() => {
    const map = mapRef.current
    if (!map || !activeTool) return undefined

    const draw = new Draw({
      source: drawSourceRef.current,
      type: activeTool,              // 'Point' | 'LineString' | 'Polygon'
    })

    // Yeni çizime başlanınca önceki taslağı temizle (aynı anda tek taslak).
    draw.on('drawstart', () => {
      drawSourceRef.current.clear()
      setPending(null)
    })

    // Çizim bitti: geometriyi WKT'ye çevir ve kayıt formunu aç.
    draw.on('drawend', (evt) => {
      const geometry = evt.feature.getGeometry()
      setPending({
        type: activeTool,
        wkt: geometryToWkt(geometry),        // 3857 → 4326 dönüşümü burada
        ozet: describeGeometry(geometry),
      })
      setForm({ name: '', description: '' })
    })

    map.addInteraction(draw)
    drawRef.current = draw

    // ⚠️ Bu temizlik olmadan: araç değiştirdiğinde eski interaction haritada
    // kalır, tek tıklamayla iki geometri birden çizilir. En sık yapılan hata.
    return () => {
      map.removeInteraction(draw)
      drawRef.current = null
    }
  }, [activeTool])

  // ------------------------------------------------------------------------
  //  Klavye kısayolları
  // ------------------------------------------------------------------------
  useEffect(() => {
    const onKeyDown = (e) => {
      // Kullanıcı forma yazı yazıyorsa kısayolları çalıştırma!
      // Bu kontrol olmadan ad alanında Backspace'e basmak çizimin son
      // noktasını silerdi.
      const tag = e.target.tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA') return

      if (e.key === 'Escape') {
        drawRef.current?.abortDrawing()      // yarım çizimi iptal et
        drawSourceRef.current?.clear()
        setPending(null)
      }

      if (e.key === 'Backspace' && drawRef.current) {
        e.preventDefault()                   // tarayıcı "geri" gitmesin
        drawRef.current.removeLastPoint()
      }
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [])

  // ------------------------------------------------------------------------
  //  Katman görünürlüğü
  // ------------------------------------------------------------------------
  useEffect(() => {
    DRAW_TYPE_KEYS.forEach((key) => layersRef.current[key]?.setVisible(visible[key]))
  }, [visible])

  // ------------------------------------------------------------------------
  //  Eylemler
  // ------------------------------------------------------------------------

  const aracSec = (key) => {
    // Aynı butona tekrar basmak aracı kapatır (toggle davranışı).
    setActiveTool((onceki) => (onceki === key ? null : key))
    drawSourceRef.current?.clear()
    setPending(null)
  }

  const vazgec = () => {
    drawSourceRef.current?.clear()
    setPending(null)
  }

  const handleSave = async (e) => {
    e.preventDefault()
    if (!pending) return

    setSaving(true)
    try {
      await kaydet(
        DRAW_TYPES[pending.type].endpoint,
        { name: form.name.trim(), description: form.description.trim() || null, wkt: pending.wkt },
        goLogin,
      )
      drawSourceRef.current.clear()
      setPending(null)
      setActiveTab(pending.type)          // kaydedilen tipin sekmesine geç
      await yukle()
      bildir('ok', `${DRAW_TYPES[pending.type].label} kaydedildi.`)
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async (type, dto) => {
    if (!window.confirm(`"${dto.name}" kaydı silinsin mi?`)) return
    try {
      await sil(DRAW_TYPES[type].endpoint, dto.id, goLogin)
      temizleVurgu()
      await yukle()
      bildir('ok', 'Kayıt silindi.')
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    }
  }

  /** Listeden bir kayda tıklayınca haritayı oraya götür. */
  const odaklan = (type, dto) => {
    const feature = sourcesRef.current[type]?.getFeatureById(`${type}-${dto.id}`)
    if (!feature || !mapRef.current) return

    mapRef.current.getView().fit(feature.getGeometry().getExtent(), {
      padding: [80, 80, 80, 80],
      maxZoom: 16,        // tek nokta için sonsuza kadar yakınlaşmasın
      duration: 400,      // yumuşak geçiş
    })
  }

  /** Fareyle üzerine gelinen kaydı haritada vurgula. */
  const vurgula = (type, dto) => {
    const feature = sourcesRef.current[type]?.getFeatureById(`${type}-${dto.id}`)
    if (!feature) return
    highlightSourceRef.current.clear()
    highlightSourceRef.current.addFeature(feature.clone())
  }

  const temizleVurgu = () => highlightSourceRef.current?.clear()

  const handleLogout = () => {
    clearSession()
    navigate('/login', { replace: true })
  }

  const toplamKayit = DRAW_TYPE_KEYS.reduce((t, k) => t + records[k].length, 0)
  const sureAzaldi = remaining !== '' && Number(remaining.split(':')[0]) < 1

  // ------------------------------------------------------------------------
  //  Arayüz
  // ------------------------------------------------------------------------
  return (
    <div className="map-layout">
      <header className="topbar">
        <span className="topbar-title">🗺️ Harita Uygulaması</span>
        <span className="topbar-right">
          <span className={`badge${sureAzaldi ? ' badge-uyari' : ''}`}>⏱ {remaining}</span>
          <span className="badge user">👤 {getUsername()}</span>
          <button className="logout-btn" onClick={handleLogout}>Çıkış</button>
        </span>
      </header>

      <div className="map-content">
        <div ref={mapElement} className={`map-container${activeTool ? ' cizim-modu' : ''}`} />

        <aside className="side-panel">
          {/* ---------- ① ÇİZİM ARAÇLARI ---------- */}
          <section className="panel-section">
            <h2>Çizim Araçları</h2>

            <div className="tool-group">
              {DRAW_TYPE_KEYS.map((key) => (
                <button
                  key={key}
                  type="button"
                  className={`tool-btn${activeTool === key ? ' active' : ''}`}
                  onClick={() => aracSec(key)}
                  aria-pressed={activeTool === key}
                  title={`${DRAW_TYPES[key].label} çiz`}
                >
                  <span className="tool-icon">{DRAW_TYPES[key].icon}</span>
                  {DRAW_TYPES[key].label}
                </button>
              ))}
            </div>

            {activeTool ? (
              <p className="tool-hint">
                {DRAW_TYPES[activeTool].hint}
                <br />
                <kbd>Esc</kbd> iptal · <kbd>⌫</kbd> son noktayı sil
              </p>
            ) : (
              <p className="tool-hint muted">Çizime başlamak için bir araç seçin.</p>
            )}
          </section>

          {/* ---------- ② KAYIT FORMU (çizim bitince belirir) ---------- */}
          {pending && (
            <form className="panel-section draw-form" onSubmit={handleSave}>
              <h2>
                <span className="dot" style={{ background: DRAW_TYPES[pending.type].color }} />
                Yeni {DRAW_TYPES[pending.type].label}
              </h2>

              <p className="ozet">{pending.ozet}</p>

              <label htmlFor="ad">Ad *</label>
              <input
                id="ad"
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                placeholder="Örn: Anıtkabir"
                maxLength={200}
                required
                autoFocus
              />

              <label htmlFor="aciklama">Açıklama</label>
              <input
                id="aciklama"
                value={form.description}
                onChange={(e) => setForm({ ...form, description: e.target.value })}
                placeholder="İsteğe bağlı"
                maxLength={1000}
              />

              <label htmlFor="wkt">WKT <small>(EPSG:4326)</small></label>
              <textarea id="wkt" className="wkt-box" value={pending.wkt} readOnly rows={3} />

              <div className="form-actions">
                <button type="submit" className="btn-primary" disabled={saving || !form.name.trim()}>
                  {saving ? 'Kaydediliyor…' : 'Kaydet'}
                </button>
                <button type="button" className="btn-ghost" onClick={vazgec} disabled={saving}>
                  Vazgeç
                </button>
              </div>
            </form>
          )}

          {/* ---------- ③ KATMANLAR ---------- */}
          <section className="panel-section">
            <h2>Katmanlar</h2>
            {DRAW_TYPE_KEYS.map((key) => (
              <label key={key} className="layer-toggle">
                <input
                  type="checkbox"
                  checked={visible[key]}
                  onChange={(e) => setVisible({ ...visible, [key]: e.target.checked })}
                />
                <span className="dot" style={{ background: DRAW_TYPES[key].color }} />
                {DRAW_TYPES[key].label}
                <span className="sayi">{records[key].length}</span>
              </label>
            ))}
          </section>

          {/* ---------- ④ KAYITLI GEOMETRİLER ---------- */}
          <section className="panel-section grow">
            <h2>Kayıtlı Geometriler <span className="sayi">{toplamKayit}</span></h2>

            <div className="geom-tabs" role="tablist">
              {DRAW_TYPE_KEYS.map((key) => (
                <button
                  key={key}
                  type="button"
                  role="tab"
                  aria-selected={activeTab === key}
                  className={`tab${activeTab === key ? ' active' : ''}`}
                  onClick={() => setActiveTab(key)}
                >
                  {DRAW_TYPES[key].icon} {records[key].length}
                </button>
              ))}
            </div>

            {records[activeTab].length === 0 ? (
              <p className="bos-durum">
                <span className="bos-ikon">{DRAW_TYPES[activeTab].icon}</span>
                Henüz {DRAW_TYPES[activeTab].label.toLowerCase()} kaydı yok.
              </p>
            ) : (
              <ul className="geom-list" onMouseLeave={temizleVurgu}>
                {records[activeTab].map((dto) => (
                  <li
                    key={dto.id}
                    onMouseEnter={() => vurgula(activeTab, dto)}
                    onClick={() => odaklan(activeTab, dto)}
                  >
                    <div className="geom-bilgi">
                      <strong>{dto.name}</strong>
                      {dto.description && <span className="muted"> — {dto.description}</span>}
                      <code className="wkt-onizleme">{dto.wkt}</code>
                    </div>
                    <button
                      type="button"
                      className="sil-btn"
                      title="Sil"
                      onClick={(e) => { e.stopPropagation(); handleDelete(activeTab, dto) }}
                    >
                      🗑
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </aside>
      </div>

      {toast && <div className={`toast toast-${toast.tur}`}>{toast.mesaj}</div>}
    </div>
  )
}
