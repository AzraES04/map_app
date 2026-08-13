import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import Map from 'ol/Map'
import View from 'ol/View'
import TileLayer from 'ol/layer/Tile'
import OSM from 'ol/source/OSM'
import VectorLayer from 'ol/layer/Vector'
import VectorSource from 'ol/source/Vector'
import Draw from 'ol/interaction/Draw'
import Overlay from 'ol/Overlay'
import { Style, Circle, Fill, Stroke, Text } from 'ol/style'
import { fromLonLat } from 'ol/proj'
import { defaults as varsayilanKontroller } from 'ol/control/defaults'
import ScaleLine from 'ol/control/ScaleLine'
import MousePosition from 'ol/control/MousePosition'
import { easeOut } from 'ol/easing'
import { createEmpty, extend as extentGenislet, isEmpty as extentBosMu } from 'ol/extent'
import 'ol/ol.css'

import { clearSession, getUsername, getExpiresAt, scheduleAutoLogout } from '../auth'
import { DRAW_TYPES, DRAW_TYPE_KEYS, geometryToWkt, wktToFeature, describeGeometry } from '../geo'
import { listele, kaydet, sil, geriAl } from '../api'

// Türkiye'nin yaklaşık merkezi (boylam, enlem) — 4326 cinsinden yazıp
// fromLonLat ile haritanın diline (3857) çeviriyoruz.
const TURKEY_CENTER = [35.24, 39.0]
const TURKEY_ZOOM = 6.4

// --- Açılış sahnesi: "uzaydan Türkiye'ye iniş" ---
//
// OpenLayers gerçek bir 3B küre çizemez (o Cesium'un işi). Bunun yerine:
//   1. Haritayı DAİREYE kırpıyoruz              → gezegen silueti
//   2. Dışını derin uzay gradyanıyla kapatıyoruz → derinlik
//   3. Üstüne küresel gölge + atmosfer halkası   → hacim hissi
//   4. Daireyi büyütüp Türkiye'ye zoomluyoruz    → atmosfere iniş
//
// Kaydırma/dönme YOK: sahne boyunca merkez sabit, sadece zoom değişiyor.
// Bu, hareketi tek bir eksene indirdiği için daha sakin ve kontrollü duruyor.
const UZAY_ZOOM = 2.4             // Bu değerin altında OpenLayers dünyayı ekrana sabitler
const KURE_BEKLEME = 1200         // ms — küre sahnede dursun, sonra iniş başlasın
const INIS_SURESI = 2600          // ms — Türkiye'ye iniş
const GIRIS_ANAHTARI = 'staj_giris_animasyonu'  // sessionStorage bayrağı

// Etiketleri decluttter ederken üç katmanı da AYNI gruba koyuyoruz; böylece
// nokta etiketi ile poligon etiketi de birbiriyle çakışmıyor.
const DECLUTTER_GRUBU = 'geometri-etiketleri'

// --------------------------------------------------------------------------
//  Katman stilleri
// --------------------------------------------------------------------------

/**
 * Kaydedilmiş geometrilerin görünümü — her tip kendi rengiyle, adı etiketli.
 *
 * Sabit bir Style yerine STİL FONKSİYONU döndürüyoruz: OpenLayers bunu her
 * feature için ayrı çağırır, böylece etiket metnini feature'dan okuyabiliyoruz.
 */
function kayitStili(type) {
  const renk = DRAW_TYPES[type].color
  const cizgiMi = type === 'LineString'

  const gorunum = {
    image: new Circle({
      radius: 7,
      fill: new Fill({ color: renk }),
      stroke: new Stroke({ color: '#ffffff', width: 2 }),
      // 'obstacle': işaretçinin KENDİSİ declutter yüzünden gizlenmez, ama
      // etiketler onun üstüne binmemek için etrafından dolaşır.
      // Bu olmasaydı üst üste gelen iki nokta birbirini yok ederdi.
      declutterMode: 'obstacle',
    }),
    stroke: new Stroke({ color: renk, width: 3 }),
    fill: new Fill({ color: `${renk}33` }),   // sondaki 33 = %20 saydamlık (hex alfa)
  }

  const sadeStil = new Style(gorunum)

  const etiketliStil = new Style({
    ...gorunum,
    text: new Text({
      font: '600 12px system-ui, -apple-system, "Segoe UI", sans-serif',
      fill: new Fill({ color: '#1a2733' }),
      // Beyaz "halo" — etiketin OSM'in yeşil/gri alanları üzerinde de okunmasını sağlar.
      stroke: new Stroke({ color: 'rgba(255,255,255,0.92)', width: 3.5 }),
      // Çizgide etiket çizginin eğrisini takip eder; nokta/poligonda düz yazılır.
      placement: cizgiMi ? 'line' : 'point',
      textBaseline: cizgiMi ? 'bottom' : 'middle',
      offsetY: type === 'Point' ? -17 : 0,      // nokta işaretçisinin üstünde dursun
      overflow: false,                          // sığmıyorsa yazma (declutter mantığı)
    }),
  })

  return (feature) => {
    const ad = feature.get('ad')
    if (!ad) return sadeStil
    etiketliStil.getText().setText(ad)
    return etiketliStil
  }
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
  const aracGrubuRef = useRef(null)    // kaydettikten sonra odağı geri vermek için
  const popupElement = useRef(null)    // popup'ın DOM kökü (OpenLayers konumlandırıyor)
  const popupOverlayRef = useRef(null)
  const toastZamanlayiciRef = useRef(null)

  // --- Ekran durumu ---------------------------------------------------------
  const [activeTool, setActiveTool] = useState(null)      // null | 'Point' | 'LineString' | 'Polygon'
  const [pending, setPending] = useState(null)            // çizildi, henüz kaydedilmedi
  const [form, setForm] = useState({ name: '', description: '', imageUrl: '' })
  const [records, setRecords] = useState(BOS_KAYITLAR)
  const [visible, setVisible] = useState({ Point: true, LineString: true, Polygon: true })
  const [activeTab, setActiveTab] = useState('Point')
  const [toast, setToast] = useState(null)                // { tur: 'ok' | 'hata', mesaj }
  const [saving, setSaving] = useState(false)
  const [remaining, setRemaining] = useState('')
  // Açılış sahnesinin evresi: null (kapalı) | 'kure' (gezegen sahnede) | 'inis' (Türkiye'ye zoom)
  const [uzaySahnesi, setUzaySahnesi] = useState(null)
  // Haritada tıklanan geometrinin popup içeriği: { dto, tip, ozet } | null
  const [secili, setSecili] = useState(null)

  const goLogin = useCallback(
    () => navigate('/login', { replace: true, state: { expired: true } }),
    [navigate],
  )

  /**
   * Alt ortada bildirim gösterir.
   * @param {'ok'|'hata'} tur
   * @param {string} mesaj
   * @param {{etiket: string, calistir: Function}} [eylem] Bildirime düğme ekler (örn. "Geri al")
   */
  const bildir = useCallback((tur, mesaj, eylem) => {
    // Önceki zamanlayıcıyı iptal et: yoksa eski bildirimin sayacı yeni bildirimi kapatır.
    if (toastZamanlayiciRef.current) clearTimeout(toastZamanlayiciRef.current)

    setToast({ tur, mesaj, eylem })
    // Düğmeli bildirimde kullanıcıya karar verecek zaman tanı.
    toastZamanlayiciRef.current = setTimeout(() => setToast(null), eylem ? 7000 : 3500)
  }, [])

  const toastKapat = useCallback(() => {
    if (toastZamanlayiciRef.current) clearTimeout(toastZamanlayiciRef.current)
    setToast(null)
  }, [])

  // ------------------------------------------------------------------------
  //  Harita yardımcıları
  //  DİKKAT: Bunlar aşağıdaki useEffect'lerin bağımlılık listesinde geçtiği için
  //  onlardan ÖNCE tanımlanmak zorunda. const bildirimleri "temporal dead zone"
  //  içindedir: tanımlanmadan önce erişilirse ReferenceError fırlatır.
  // ------------------------------------------------------------------------

  /**
   * Haritayı verilen geometriye yaklaştır.
   * Hem sağ paneldeki listeden hem de haritadaki şekle tıklamadan çağrılıyor —
   * tek fonksiyon olduğu için iki yol da birebir aynı davranıyor.
   */
  const odaklanFeature = useCallback((feature) => {
    if (!feature || !mapRef.current) return
    mapRef.current.getView().fit(feature.getGeometry().getExtent(), {
      padding: [90, 90, 90, 90],
      maxZoom: 15,        // tek nokta için sonsuza kadar yakınlaşmasın
      duration: 500,      // yumuşak geçiş
      easing: easeOut,
    })
  }, [])

  /** Bir feature'ı vurgu katmanına koy (klon — orijinali iki katmana birden koyamayız). */
  const vurgulaFeature = useCallback((feature) => {
    const kaynak = highlightSourceRef.current
    if (!kaynak) return
    kaynak.clear()
    if (feature) kaynak.addFeature(feature.clone())
  }, [])

  const featureBul = useCallback(
    (type, dto) => sourcesRef.current[type]?.getFeatureById(`${type}-${dto.id}`),
    [],
  )

  /**
   * Popup'ın hangi koordinata tutunacağını belirler.
   * Nokta → kendisi; çizgi → orta noktası; poligon → iç merkezi.
   * Sınırlayıcı kutunun merkezini kullanmıyoruz: "C" gibi içbükey bir poligonda
   * o merkez şeklin DIŞINA düşebilir, popup boşlukta asılı kalırdı.
   */
  const popupKonumu = useCallback((geometry) => {
    switch (geometry.getType()) {
      case 'Point':
        return geometry.getCoordinates()
      case 'LineString':
        return geometry.getCoordinateAt(0.5)          // %50'si — çizginin ortası
      case 'Polygon':
        return geometry.getInteriorPoint().getCoordinates().slice(0, 2)
      default:
        return geometry.getExtent().slice(0, 2)
    }
  }, [])

  const popupKapat = useCallback(() => {
    setSecili(null)
    popupOverlayRef.current?.setPosition(undefined)   // undefined = popup'ı gizle
  }, [])

  /** Haritadaki bir geometriye tıklanınca popup'ı aç. */
  const popupAc = useCallback((feature) => {
    const dto = feature.get('dto')
    const tip = feature.get('tip')
    if (!dto) return

    setSecili({ dto, tip, ozet: describeGeometry(feature.getGeometry()) })
    popupOverlayRef.current?.setPosition(popupKonumu(feature.getGeometry()))
  }, [popupKonumu])

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
          feature.set('ad', dto.name)         // stil fonksiyonu etiketi buradan okuyor
          // Kaydın tamamını feature'a iliştiriyoruz: haritada tıklandığında
          // popup içeriğini state'te aramaya gerek kalmıyor, doğrudan burada.
          // Bu aynı zamanda "eski state'e takılma" (stale closure) riskini de kaldırıyor.
          feature.set('dto', dto)
          feature.set('tip', key)
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
      layers[key] = new VectorLayer({
        source: sources[key],
        style: kayitStili(key),
        // Etiket çakışma yönetimi. Üç katman da aynı grup adını kullandığı için
        // OpenLayers her karede tüm etiketlerin kutularını karşılaştırıp
        // çakışanları gizliyor — "çakışma yoksa ismi yaz" davranışı tam olarak bu.
        declutter: DECLUTTER_GRUBU,
      })
    })
    sourcesRef.current = sources
    layersRef.current = layers

    const drawSource = new VectorSource()
    drawSourceRef.current = drawSource

    const highlightSource = new VectorSource()
    highlightSourceRef.current = highlightSource

    // --- Açılış animasyonu oynatılsın mı? ---
    // İki koşul: (1) kullanıcı bu oturumda daha önce görmediyse — her sayfa
    // yenilemesinde tekrar oynarsa sinir bozucu olur; (2) işletim sisteminde
    // "hareketi azalt" ayarı kapalıysa (erişilebilirlik).
    const hareketAzalt = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    const girisOynat =
      !hareketAzalt &&
      !sessionStorage.getItem(GIRIS_ANAHTARI) &&
      // Sayfa arka plan sekmesinde açıldıysa tarayıcı animasyon karesi üretmez;
      // animasyon görülmeden "oynatıldı" sayılmasın diye hiç başlatmıyoruz.
      document.visibilityState === 'visible'

    const view = new View({
      // Merkez her iki durumda da Türkiye: sahne boyunca kaydırma yok, sadece zoom.
      center: fromLonLat(TURKEY_CENTER),                  // 4326 → 3857
      zoom: girisOynat ? UZAY_ZOOM : TURKEY_ZOOM,
    })

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
      view,
      controls: varsayilanKontroller().extend([
        // Ölçek çubuğu — haritanın gerçek mesafeyle ilişkisini gösterir.
        new ScaleLine({ units: 'metric' }),
        // İmlecin altındaki koordinatı CANLI gösterir.
        // projection: 'EPSG:4326' → harita 3857'de çalışsa bile burada
        // dönüştürülmüş hâlini, yani veritabanına yazılacak değeri görüyoruz.
        new MousePosition({
          projection: 'EPSG:4326',
          className: 'koordinat-gostergesi',
          placeholder: 'İmleci haritaya getirin',
          coordinateFormat: (koordinat) =>
            koordinat
              ? `B ${koordinat[0].toFixed(5)}°  ·  E ${koordinat[1].toFixed(5)}°`
              : '',
        }),
      ]),
    })
    mapRef.current = map

    // Popup: React'in yönettiği bir div'i OpenLayers harita koordinatına bağlıyoruz.
    // OL elemanı kendi kapsayıcısına taşır ve konumunu yönetir; içeriği React çizer.
    const popup = new Overlay({
      element: popupElement.current,
      positioning: 'bottom-center',   // popup'ın ALTI, verilen koordinata oturur
      offset: [0, -16],               // işaretçinin biraz üstünde dursun
      // autoPan: popup ekranın dışına taşarsa harita kendiliğinden kayıp onu içeri alır
      autoPan: { animation: { duration: 300 }, margin: 28 },
    })
    map.addOverlay(popup)
    popupOverlayRef.current = popup

    // --- Uzaydan Türkiye'ye iniş ---
    let atlaDinleyici = null
    let inisZamanlayici = null
    if (girisOynat) {
      setUzaySahnesi('kure')   // gezegen diski sahneye girer

      /** Sahneyi kapat ve haritayı Türkiye'ye sabitle (hem normal bitiş hem "atla" için). */
      const sahneyiBitir = (tamamlandi) => {
        if (tamamlandi) sessionStorage.setItem(GIRIS_ANAHTARI, '1')
        setUzaySahnesi(null)
      }

      // Küre önce kısa bir süre sahnede dursun — göz onu "gezegen" olarak
      // okumaya fırsat bulsun. Hemen zoomlarsak sadece bir geçiş efekti gibi durur.
      inisZamanlayici = setTimeout(() => {
        setUzaySahnesi('inis')   // CSS: daire büyümeye ve sahne solmaya başlar

        // Aynı anda OpenLayers zoom animasyonu. İkisi bağımsız çalışır ama
        // eşzamanlı başladıkları için tek bir hareket gibi algılanır.
        view.animate(
          {
            zoom: TURKEY_ZOOM,
            duration: INIS_SURESI,
            easing: easeOut,   // hızlı başlar, sona doğru yavaşlar: "yerine oturma"
          },
          sahneyiBitir,
        )
      }, KURE_BEKLEME)

      // Kullanıcı beklemek istemiyorsa ilk dokunuşta sahneyi kes ve Türkiye'ye atla.
      atlaDinleyici = () => {
        clearTimeout(inisZamanlayici)
        view.cancelAnimations()
        view.setZoom(TURKEY_ZOOM)
        setUzaySahnesi(null)
      }
      map.getViewport().addEventListener('pointerdown', atlaDinleyici, { once: true })
      map.getViewport().addEventListener('wheel', atlaDinleyici, { once: true, passive: true })
    }

    yukle()

    // Temizlik: bileşen kaldırılınca haritayı DOM'dan ayır.
    // React StrictMode geliştirmede effect'i iki kez çalıştırır; bu satır
    // olmasaydı sayfada iki harita üst üste binerdi.
    return () => {
      if (inisZamanlayici) clearTimeout(inisZamanlayici)
      if (atlaDinleyici) {
        map.getViewport().removeEventListener('pointerdown', atlaDinleyici)
        map.getViewport().removeEventListener('wheel', atlaDinleyici)
      }
      view.cancelAnimations()   // bileşen kalkarken devam eden animasyon kalmasın
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
      // Çizim SIRASINDAKİ görünüm (henüz tamamlanmamış "sketch").
      // Bu satır olmasaydı OpenLayers kendi varsayılan parlak mavi stilini
      // kullanırdı ve yarım çizim, tamamlanmış taslaktan farklı görünürdü.
      style: taslakStili,
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
      setForm({ name: '', description: '', imageUrl: '' })
    })

    map.addInteraction(draw)
    drawRef.current = draw

    // ⚠️ Bu temizlik olmadan: araç değiştirdiğinde eski interaction haritada
    // kalır, tek tıklamayla iki geometri birden çizilir. En sık yapılan hata.
    return () => {
      draw.abortDrawing()          // yarım kalmış sketch varsa temizle
      map.removeInteraction(draw)
      drawRef.current = null
    }
  }, [activeTool])

  // ------------------------------------------------------------------------
  //  Harita üzerinde etkileşim (çizim aracı KAPALIYKEN)
  //
  //  - Şeklin üzerine gelince: imleç el işaretine döner, şekil vurgulanır
  //  - Şekle tıklayınca: haritayı o şekle yaklaştırır (listeden seçmeye gerek yok)
  //  - Boş alana tıklayınca: haritayı oraya yumuşakça kaydırır
  //
  //  activeTool bağımlılıkta: çizim modundayken bu davranışlar devre dışı kalmalı,
  //  yoksa çizim yapmak isterken harita kayardı.
  // ------------------------------------------------------------------------
  useEffect(() => {
    const map = mapRef.current
    if (!map) return undefined

    const kayitKatmanlari = Object.values(layersRef.current)

    /** Verilen pikselin altında kayıtlı bir geometri var mı? */
    const pikseldekiFeature = (pixel) =>
      map.forEachFeatureAtPixel(pixel, (feature) => feature, {
        // Sadece kayıtlı geometriler; taslak ve vurgu katmanları hesaba katılmasın.
        layerFilter: (layer) => kayitKatmanlari.includes(layer),
        // İnce çizgiyi/küçük noktayı tam piksel isabetiyle yakalamak zor;
        // 8 piksellik tolerans tıklamayı çok daha kolay hale getiriyor.
        hitTolerance: 8,
      })

    // Son vurgulanan feature'ı hatırlıyoruz: her fare hareketinde katmanı
    // gereksiz yere temizleyip yeniden doldurmayalım (her seferinde yeniden çizim demek).
    let sonVurguId = null

    const fareHareketi = (evt) => {
      if (evt.dragging || activeTool) return
      const feature = pikseldekiFeature(evt.pixel)
      const yeniId = feature ? feature.getId() : null
      if (yeniId === sonVurguId) return

      sonVurguId = yeniId
      map.getViewport().style.cursor = feature ? 'pointer' : ''
      vurgulaFeature(feature)
    }

    // 'click' değil 'singleclick': OpenLayers çift tıklamayı ayırt edebilmek için
    // ~250 ms bekler. 'click' kullansaydık çift tıklayarak zoom yaparken
    // aşağıdaki kod da iki kez tetiklenir, harita zıplardı.
    const tekTiklama = (evt) => {
      if (activeTool) return          // çizim modunda tıklama çizime aittir
      const feature = pikseldekiFeature(evt.pixel)

      if (feature) {
        // Şekle tıklandı → bilgi kartını aç.
        // Otomatik zoom YAPMIYORUZ: popup açılırken harita da hareket etseydi
        // kart ekranda kayar, okumak zorlaşırdı. Yaklaşmak isteyen kartın
        // içindeki "Yakınlaş" düğmesini kullanıyor.
        popupAc(feature)
      } else {
        // Boş alana tıklandı → popup'ı kapat ve oraya git (zoom korunur)
        popupKapat()
        map.getView().animate({ center: evt.coordinate, duration: 450, easing: easeOut })
      }
    }

    map.on('pointermove', fareHareketi)
    map.on('singleclick', tekTiklama)

    return () => {
      map.un('pointermove', fareHareketi)
      map.un('singleclick', tekTiklama)
      map.getViewport().style.cursor = ''
    }
  }, [activeTool, popupAc, popupKapat, vurgulaFeature])

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
        {
          name: form.name.trim(),
          // Boş metin yerine null: veritabanında "değer yok"un doğru karşılığı NULL'dur
          description: form.description.trim() || null,
          imageUrl: form.imageUrl.trim() || null,
          wkt: pending.wkt,
        },
        goLogin,
      )
      drawSourceRef.current.clear()
      setPending(null)
      setActiveTab(pending.type)          // kaydedilen tipin sekmesine geç
      await yukle()
      bildir('ok', `${DRAW_TYPES[pending.type].label} kaydedildi.`)

      // Odağı aktif araç düğmesine geri ver: form kapanınca odak boşlukta kalmasın,
      // klavye kullanıcısı arka arkaya çizim yapabilsin.
      aracGrubuRef.current?.querySelector('.tool-btn.active')?.focus()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    } finally {
      setSaving(false)
    }
  }

  /**
   * Silme — onay sormadan.
   *
   * Klasik "Emin misiniz?" kutusu yerine "sil + geri al" desenini kullanıyoruz.
   * Gerekçe: onay kutusu HER silmede kullanıcıyı durdurur, oysa hata nadirdir;
   * üstelik insanlar bir süre sonra okumadan onaylar, yani koruma da sağlamaz.
   * Geri alma ise sadece hata yapıldığında devreye girer ve gerçekten kurtarır.
   *
   * Bu deseni kullanabilmemizin tek sebebi SOFT DELETE: kayıt veritabanında
   * duruyor, geri getirmek tek UPDATE. Fiziksel silme olsaydı geri alınamazdı.
   */
  const handleDelete = async (type, dto) => {
    try {
      await sil(DRAW_TYPES[type].endpoint, dto.id, goLogin)
      temizleVurgu()
      popupKapat()
      await yukle()

      bildir('ok', `"${dto.name}" silindi.`, {
        etiket: 'Geri al',
        calistir: async () => {
          try {
            await geriAl(DRAW_TYPES[type].endpoint, dto.id, goLogin)
            await yukle()
            bildir('ok', `"${dto.name}" geri alındı.`)
          } catch (err) {
            if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
          }
        },
      })
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    }
  }

  /** Listeden bir kayda tıklayınca haritayı oraya götür. */
  const odaklan = (type, dto) => odaklanFeature(featureBul(type, dto))

  /** Fareyle üzerine gelinen kaydı haritada vurgula. */
  const vurgula = (type, dto) => vurgulaFeature(featureBul(type, dto))

  const temizleVurgu = () => highlightSourceRef.current?.clear()

  /** Haritayı açılıştaki Türkiye görünümüne döndür. */
  const turkiyeyeDon = () => {
    mapRef.current?.getView().animate({
      center: fromLonLat(TURKEY_CENTER),
      zoom: TURKEY_ZOOM,
      duration: 700,
      easing: easeOut,
    })
  }

  /** Tüm kayıtları ekrana sığdır (hepsinin birleşik sınırlayıcı kutusuna fit). */
  const tumunuGoster = () => {
    const map = mapRef.current
    if (!map) return

    // createEmpty() sonsuzlarla dolu bir "boş extent" verir; her katmanınkiyle genişletiyoruz.
    const kapsam = createEmpty()
    DRAW_TYPE_KEYS.forEach((key) => {
      const kaynak = sourcesRef.current[key]
      if (kaynak && kaynak.getFeatures().length) extentGenislet(kapsam, kaynak.getExtent())
    })

    if (extentBosMu(kapsam)) return turkiyeyeDon()   // hiç kayıt yoksa Türkiye'ye dön

    map.getView().fit(kapsam, {
      padding: [90, 90, 90, 90],
      maxZoom: 14,
      duration: 600,
      easing: easeOut,
    })
  }

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
        {/* Harita ve üzerine binen düğmeler ayrı bir sarmalayıcıda:
            harita div'inin çocuklarını OpenLayers yönetiyor, React'in oraya
            eleman eklemesi çakışma yaratırdı. */}
        <div className="map-alan">
          {/* uzay-renk: açılış sahnesi boyunca haritaya renk derecelendirmesi uygulanır
              (denizler derin maviye, karalar doygun ve koyu). Sahne kapanınca sınıf
              kalkar ve CSS geçişiyle normal harita renklerine yumuşakça döner. */}
          <div
            ref={mapElement}
            className={
              `map-container${activeTool ? ' cizim-modu' : ''}${uzaySahnesi ? ' uzay-renk' : ''}`
            }
          />

          {/* Açılış sahnesi. Haritanın ÜSTÜNE biner ama pointer-events: none olduğu
              için tıklamalar haritaya geçer — böylece "atla" davranışı çalışır. */}
          {uzaySahnesi && (
            <div className={`uzay-sahnesi ${uzaySahnesi}`} aria-hidden="true">
              {/* Uzay: tüm alanı kaplar, ortasındaki dairesel delikten harita görünür */}
              <div className="uzay-katmani" />
              {/* Küresel hacim: sol üstten ışık, sağ altta gölge + atmosfer halkası */}
              <div className="kure-isik" />
            </div>
          )}

          {/* POPUP — OpenLayers bu div'i alıp harita koordinatına konumlandırır.
              DOM'da hep duruyor; içeriği yalnızca bir kayıt seçiliyken doluyor. */}
          <div ref={popupElement} className="harita-popup">
            {secili && (
              <>
                <div className="popup-baslik">
                  <span className="dot" style={{ background: DRAW_TYPES[secili.tip].color }} />
                  <strong>{secili.dto.name}</strong>
                  <button type="button" className="popup-kapat" onClick={popupKapat}
                          aria-label="Kapat">×</button>
                </div>

                {secili.dto.imageUrl && (
                  <img
                    className="popup-gorsel"
                    src={secili.dto.imageUrl}
                    alt={secili.dto.name}
                    loading="lazy"
                    // Adres kırıksa boş çerçeve yerine görseli tamamen gizle
                    onError={(e) => { e.currentTarget.style.display = 'none' }}
                  />
                )}

                {secili.dto.description && (
                  <p className="popup-aciklama">{secili.dto.description}</p>
                )}

                <dl className="popup-bilgi">
                  <dt>Tip</dt>
                  <dd>{DRAW_TYPES[secili.tip].label}</dd>
                  <dt>Konum</dt>
                  <dd>{secili.ozet}</dd>
                  <dt>Eklendi</dt>
                  <dd>{new Date(secili.dto.createdAt).toLocaleString('tr-TR')}</dd>
                </dl>

                <div className="popup-eylemler">
                  <button
                    type="button"
                    className="btn-primary"
                    onClick={() => odaklan(secili.tip, secili.dto)}
                  >
                    Yakınlaş
                  </button>
                  <button
                    type="button"
                    className="btn-ghost sil"
                    onClick={() => handleDelete(secili.tip, secili.dto)}
                  >
                    Sil
                  </button>
                </div>
              </>
            )}
          </div>

          <div className="harita-araclari">
            <button type="button" className="harita-btn" onClick={turkiyeyeDon}
                    title="Türkiye görünümüne dön">
              <svg viewBox="0 0 20 20" aria-hidden="true">
                <path d="M10 2.5 2.5 9h2v8h4v-5h3v5h4V9h2L10 2.5Z" />
              </svg>
              <span>Türkiye</span>
            </button>

            <button type="button" className="harita-btn" onClick={tumunuGoster}
                    title="Tüm kayıtları ekrana sığdır">
              <svg viewBox="0 0 20 20" aria-hidden="true">
                <path d="M3 7V3h4M17 7V3h-4M3 13v4h4M17 13v4h-4" fill="none"
                      stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
              </svg>
              <span>Tümü</span>
            </button>
          </div>
        </div>


        <aside className="side-panel">
          {/* ---------- ① ÇİZİM ARAÇLARI ---------- */}
          <section className="panel-section">
            <h2>Çizim Araçları</h2>

            <div className="tool-group" ref={aracGrubuRef}>
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

              <label htmlFor="gorsel">Görsel adresi <small>(isteğe bağlı)</small></label>
              <input
                id="gorsel"
                type="url"
                value={form.imageUrl}
                onChange={(e) => setForm({ ...form, imageUrl: e.target.value })}
                placeholder="https://..."
                maxLength={500}
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

      {/* role="status" + aria-live="polite": ekran okuyucu, kullanıcının işini
          bölmeden bildirimi seslendirir. Görsel toast'ın işitsel karşılığı. */}
      {toast && (
        <div className={`toast toast-${toast.tur}`} role="status" aria-live="polite">
          <span>{toast.mesaj}</span>
          {toast.eylem && (
            <button
              type="button"
              className="toast-eylem"
              onClick={() => { toastKapat(); toast.eylem.calistir() }}
            >
              {toast.eylem.etiket}
            </button>
          )}
        </div>
      )}
    </div>
  )
}
