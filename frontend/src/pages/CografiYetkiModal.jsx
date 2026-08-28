import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
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
import {
  cografiYetkileriListele, cografiYetkiEkle, cografiYetkiSil,
  ilSinirlariGetir, bolgeleriGetir, secilebilirAlanlariGetir,
} from '../adminApi'
import { SilIkonu } from '../icons'

// ============================================================================
//  COĞRAFİ YETKİ TANIMLAMA (Ödev 7 / Madde 2 · Ödev 10)
//
//  Kullanıcı veya rol satırındaki düğmeden açılır; Türkiye'ye zoomlanmış bir
//  harita gösterir ve o sahibe izinli alan tanımlatır.
//
//  DÖRT TANIMLAMA YOLU (Ödev 10 · Ödev 11):
//    • Elle çiz     — haritaya serbest poligon (Ödev 7'den beri var)
//    • İl seç       — haritadan tıklayarak bir veya birden çok il
//    • Bölge seç    — bir veya birden çok coğrafi bölge
//    • Kayıtlı alan — daha önce çizilmiş envanter poligonlarından seçim
//
//  Dördü de sonuçta AYNI şeye dönüşüyor: bir geometri. Birleştirme işi
//  sunucuda yapılıyor; arayüz yalnızca "hangi iller / hangi bölgeler /
//  hangi alanlar" bilgisini gönderiyor. Böylece kaydedilen sınır ile
//  gerçek sınır arasında fark oluşmuyor.
//
//  NEDEN AYRI BİLEŞEN? Aynı ekran hem Kullanıcı Listesi'nden hem Rol
//  Listesi'nden açılıyor; tek fark sahibin kim olduğu.
// ============================================================================

/** Türkiye'nin yaklaşık merkezi (boylam, enlem) ve tüm ülkeyi gösteren zoom. */
const TURKIYE_MERKEZ = [35.24, 39.0]
const TURKIYE_ZOOM = 5.6

const MODLAR = [
  { anahtar: 'cizim', etiket: 'Elle çiz', ipucu: 'Haritada köşeleri tıklayın, bitirmek için çift tıklayın.' },
  { anahtar: 'il', etiket: 'İl seç', ipucu: 'Haritadan il tıklayın. Birden fazla il seçebilirsiniz.' },
  { anahtar: 'bolge', etiket: 'Bölge seç', ipucu: 'Bir veya birden çok bölge seçin; haritadan il tıklamak da o bölgeyi seçer.' },
  { anahtar: 'poligon', etiket: 'Kayıtlı alan', ipucu: 'Daha önce çizilmiş alanlardan seçin — sınırı yeniden çizmeye gerek yok.' },
]

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

/** Seçilmemiş il: ince gri kenar. Dolgu SAYDAM DEĞİL 'şeffaf renk' —
 *  tamamen dolgusuz bırakılsaydı ilin içine tıklamak işe yaramazdı,
 *  OpenLayers yalnızca çizilen pikselleri isabet sayıyor. */
const IL_STILI = new Style({
  fill: new Fill({ color: 'rgba(255, 255, 255, 0.01)' }),
  stroke: new Stroke({ color: 'rgba(90, 107, 123, 0.55)', width: 1 }),
})

/** Seçili il: turuncu — taslak rengiyle aynı aile, "henüz kaydedilmedi" demek. */
const SECILI_IL_STILI = new Style({
  fill: new Fill({ color: 'rgba(232, 161, 60, 0.35)' }),
  stroke: new Stroke({ color: '#d9822b', width: 1.8 }),
})

/** Seçilebilir kayıtlı alan (Ödev 11) — yeşil, envanter poligon rengiyle aynı. */
const KAYITLI_ALAN_STILI = new Style({
  fill: new Fill({ color: 'rgba(87, 160, 90, 0.14)' }),
  stroke: new Stroke({ color: 'rgba(87, 160, 90, 0.85)', width: 1.4 }),
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
  const taslakKaynagiRef = useRef(null)   // elle çizilen alan
  const ilKaynagiRef = useRef(null)       // il sınırları (seçim katmanı)
  const ilKatmaniRef = useRef(null)
  const poligonKaynagiRef = useRef(null)  // kayıtlı alanlar (Ödev 11)
  const poligonKatmaniRef = useRef(null)
  const drawRef = useRef(null)

  const [alanlar, setAlanlar] = useState([])
  const [yukleniyor, setYukleniyor] = useState(true)
  const [hata, setHata] = useState(null)
  const [kaydediliyor, setKaydediliyor] = useState(false)
  const [ad, setAd] = useState('')

  // ---- Ödev 10: tanımlama yolu ----
  const [mod, setMod] = useState('cizim')
  const [taslakWkt, setTaslakWkt] = useState(null)
  const [seciliPlakalar, setSeciliPlakalar] = useState([])
  // Ödev 11: bölge seçimi artık ÇOKLU
  const [seciliBolgeler, setSeciliBolgeler] = useState([])
  const [seciliPoligonlar, setSeciliPoligonlar] = useState([])
  const [bolgeler, setBolgeler] = useState([])
  const [kayitliAlanlar, setKayitliAlanlar] = useState([])
  const [illerYukleniyor, setIllerYukleniyor] = useState(false)

  // Seçili il adları — hem yan panelde hem otomatik ad önerisinde kullanılıyor.
  const [ilAdlari, setIlAdlari] = useState({})   // { plaka: ad }

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

  // Bölge listesi bir kez — modal açılır açılmaz, sekmeye basılmadan.
  // Küçük bir istek; sekmeye basınca beklemek gereksiz gecikme olurdu.
  useEffect(() => {
    bolgeleriGetir(onOturumBitti)
      .then(setBolgeler)
      .catch(() => { /* bölge listesi gelmezse o sekme boş kalır */ })
  }, [onOturumBitti])

  // ------------------------------------------------------------------
  //  Harita — bir kez kurulur
  // ------------------------------------------------------------------

  useEffect(() => {
    if (!haritaElement.current) return undefined

    const alanKaynagi = new VectorSource()
    const taslakKaynagi = new VectorSource()
    const ilKaynagi = new VectorSource()
    const poligonKaynagi = new VectorSource()
    alanKaynagiRef.current = alanKaynagi
    taslakKaynagiRef.current = taslakKaynagi
    ilKaynagiRef.current = ilKaynagi
    poligonKaynagiRef.current = poligonKaynagi

    // İl katmanı: stil FONKSİYON, sabit stil değil — seçili olup olmadığına
    // göre değişmesi gerekiyor ve feature'ın kendi özelliğinden okuyor.
    const ilKatmani = new VectorLayer({
      source: ilKaynagi,
      visible: false,
      style: (feature) => (feature.get('secili') ? SECILI_IL_STILI : IL_STILI),
    })
    ilKatmaniRef.current = ilKatmani

    // Kayıtlı alanlar da aynı desende: seçili olan turuncu, olmayan yeşil.
    const poligonKatmani = new VectorLayer({
      source: poligonKaynagi,
      visible: false,
      style: (feature) => (feature.get('secili') ? SECILI_IL_STILI : KAYITLI_ALAN_STILI),
    })
    poligonKatmaniRef.current = poligonKatmani

    const harita = new Map({
      target: haritaElement.current,
      layers: [
        new TileLayer({ source: new OSM() }),
        ilKatmani,
        poligonKatmani,
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

    haritaRef.current = harita

    return () => {
      harita.setTarget(undefined)
      haritaRef.current = null
    }
  }, [])

  // ------------------------------------------------------------------
  //  Mod değişince: çizim aracını ve il katmanını aç/kapat
  // ------------------------------------------------------------------

  useEffect(() => {
    const harita = haritaRef.current
    if (!harita) return undefined

    ilKatmaniRef.current?.setVisible(mod === 'il' || mod === 'bolge')
    poligonKatmaniRef.current?.setVisible(mod === 'poligon')

    if (mod !== 'cizim') return undefined

    // Çizim aracı YALNIZCA "elle çiz" modunda ekleniyor. Sürekli açık
    // bıraksaydık il seçmek için yapılan tıklamalar poligon çizmeye başlardı.
    const draw = new Draw({ source: taslakKaynagiRef.current, type: 'Polygon' })

    draw.on('drawstart', () => {
      // Aynı anda tek taslak: yenisi başlayınca eskisi silinsin, yoksa
      // hangi alanın kaydedileceği belirsiz olurdu.
      taslakKaynagiRef.current.clear()
    })

    draw.on('drawend', (olay) => {
      setTaslakWkt(geometryToWkt(olay.feature.getGeometry()))
    })

    harita.addInteraction(draw)
    drawRef.current = draw

    return () => {
      harita.removeInteraction(draw)
      drawRef.current = null
    }
  }, [mod])

  // İl sınırlarını ilk ihtiyaç duyulduğunda indir (~250 KB).
  // adminApi tarafında modül düzeyinde saklandığı için modal ikinci kez
  // açıldığında ağa hiç gidilmiyor.
  useEffect(() => {
    if (mod !== 'il' && mod !== 'bolge') return
    if (ilKaynagiRef.current?.getFeatures().length) return

    setIllerYukleniyor(true)
    ilSinirlariGetir(onOturumBitti)
      .then((iller) => {
        const kaynak = ilKaynagiRef.current
        if (!kaynak) return

        const adlar = {}
        iller.forEach((il) => {
          const feature = wktToFeature(il.wkt)
          feature.set('plaka', il.plaka)
          feature.set('ad', il.ad)
          feature.set('bolge', il.bolge)
          feature.set('secili', false)
          kaynak.addFeature(feature)
          adlar[il.plaka] = il.ad
        })
        setIlAdlari(adlar)
      })
      .catch((err) => {
        if (err.message !== 'Oturum süresi doldu') setHata(err.message)
      })
      .finally(() => setIllerYukleniyor(false))
  }, [mod, onOturumBitti])

  // Kayıtlı alanları ilk ihtiyaç duyulduğunda getir (Ödev 11).
  useEffect(() => {
    if (mod !== 'poligon') return
    if (poligonKaynagiRef.current?.getFeatures().length) return

    secilebilirAlanlariGetir(onOturumBitti)
      .then((alanlar) => {
        const kaynak = poligonKaynagiRef.current
        if (!kaynak) return

        alanlar.forEach((alan) => {
          const feature = wktToFeature(alan.wkt)
          feature.set('poligonId', alan.id)
          feature.set('ad', alan.name)
          feature.set('secili', false)
          kaynak.addFeature(feature)
        })
        setKayitliAlanlar(alanlar)
      })
      .catch((err) => {
        if (err.message !== 'Oturum süresi doldu') setHata(err.message)
      })
  }, [mod, onOturumBitti])

  // ------------------------------------------------------------------
  //  Haritadan tıklama ile seçim
  // ------------------------------------------------------------------

  useEffect(() => {
    const harita = haritaRef.current
    if (!harita || mod === 'cizim') return undefined

    const hedefKatman = mod === 'poligon' ? poligonKatmaniRef.current : ilKatmaniRef.current

    const tiklama = (olay) => {
      const feature = harita.forEachFeatureAtPixel(
        olay.pixel,
        (f) => f,
        { layerFilter: (l) => l === hedefKatman },
      )
      if (!feature) return

      // Üç modda da AÇ/KAPA davranışı: seçiliyse çıkar, değilse ekle.
      // Tek seçime zorlamak, "iki il birden" gibi en sık isteği imkânsız kılardı.
      if (mod === 'il') {
        const plaka = feature.get('plaka')
        setSeciliPlakalar((o) => (o.includes(plaka) ? o.filter((x) => x !== plaka) : [...o, plaka]))
      } else if (mod === 'bolge') {
        // Bölge modunda ilin kendisi değil, BÖLGESİ seçiliyor.
        const bolge = feature.get('bolge')
        setSeciliBolgeler((o) => (o.includes(bolge) ? o.filter((x) => x !== bolge) : [...o, bolge]))
      } else {
        const id = feature.get('poligonId')
        setSeciliPoligonlar((o) => (o.includes(id) ? o.filter((x) => x !== id) : [...o, id]))
      }
    }

    harita.on('singleclick', tiklama)
    return () => harita.un('singleclick', tiklama)
  }, [mod])

  // Seçim değişince il katmanının stilini tazele.
  // Feature'ın kendi özelliğine yazıyoruz çünkü stil fonksiyonu React
  // state'ini göremez — her stil çağrısında en güncel değeri okuması lazım.
  useEffect(() => {
    const kaynak = ilKaynagiRef.current
    if (!kaynak) return

    kaynak.getFeatures().forEach((f) => {
      const secili = mod === 'il'
        ? seciliPlakalar.includes(f.get('plaka'))
        : mod === 'bolge' && seciliBolgeler.includes(f.get('bolge'))
      f.set('secili', secili, true)   // true = değişikliği yayınlama, tek seferde çizeceğiz
    })
    ilKatmaniRef.current?.changed()

    poligonKaynagiRef.current?.getFeatures().forEach((f) => {
      f.set('secili', seciliPoligonlar.includes(f.get('poligonId')), true)
    })
    poligonKatmaniRef.current?.changed()
  }, [mod, seciliPlakalar, seciliBolgeler, seciliPoligonlar])

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
  //  Seçimin özeti ve önerilen ad
  // ------------------------------------------------------------------

  const secimVar = mod === 'cizim' ? !!taslakWkt
    : mod === 'il' ? seciliPlakalar.length > 0
      : mod === 'bolge' ? seciliBolgeler.length > 0
        : seciliPoligonlar.length > 0

  /**
   * Seçim listesinden okunabilir bir ad üretir.
   * Uzun listeyi ada sığdırmak yerine sayıya çeviriyoruz: alan adı kolonu
   * 200 karakter ve tanımlı alanlar listesinde okunaklı kalmalı.
   */
  const adOner = (adlar, birim) => {
    const temiz = adlar.filter(Boolean)
    if (temiz.length === 0) return ''
    return temiz.length <= 3 ? temiz.join(' + ') : `${temiz.length} ${birim} seçimi`
  }

  const onerilenAd = useMemo(() => {
    if (mod === 'bolge') {
      return seciliBolgeler.length === 1
        ? `${seciliBolgeler[0]} Bölgesi`
        : adOner(seciliBolgeler, 'bölge')
    }

    if (mod === 'il') return adOner(seciliPlakalar.map((p) => ilAdlari[p]), 'il')

    if (mod === 'poligon') {
      const adlar = seciliPoligonlar
        .map((id) => kayitliAlanlar.find((a) => a.id === id)?.name)
      return adOner(adlar, 'alan')
    }

    return ''
  }, [mod, seciliBolgeler, seciliPlakalar, seciliPoligonlar, ilAdlari, kayitliAlanlar])

  // Kullanıcı kendi adını yazdıysa ona dokunmuyoruz; yazmadıysa öneriyi
  // kutuya koyuyoruz. "Ad ver" adımını çoğu durumda tek tıka indiriyor.
  const [adElleGirildi, setAdElleGirildi] = useState(false)
  useEffect(() => {
    if (!adElleGirildi) setAd(onerilenAd)
  }, [onerilenAd, adElleGirildi])

  // ------------------------------------------------------------------
  //  İşlemler
  // ------------------------------------------------------------------

  const secimiTemizle = useCallback(() => {
    taslakKaynagiRef.current?.clear()
    setTaslakWkt(null)
    setSeciliPlakalar([])
    setSeciliBolgeler([])
    setSeciliPoligonlar([])
    setAdElleGirildi(false)
    setAd('')
  }, [])

  const modDegistir = (yeni) => {
    if (yeni === mod) return
    // Mod değişince seçim sıfırlanıyor: "il seçtim, sonra çizime geçtim"
    // durumunda hangisinin kaydedileceği belirsiz olurdu. Sunucu da zaten
    // iki yolun birden gelmesini reddediyor.
    secimiTemizle()
    setMod(yeni)
  }

  const kaydet = async (e) => {
    e.preventDefault()
    if (!secimVar) return

    setKaydediliyor(true)
    setHata(null)
    try {
      const sahiplik = sahip.tur === 'kullanici' ? { userId: sahip.id } : { roleId: sahip.id }

      // Yalnızca seçilen yola ait alan gönderiliyor; sunucu "tek yol" kuralını
      // ayrıca doğruluyor.
      const alanTanimi = mod === 'cizim' ? { wkt: taslakWkt }
        : mod === 'il' ? { ilPlakalari: seciliPlakalar }
          : mod === 'bolge' ? { bolgeler: seciliBolgeler }
            : { poligonIdleri: seciliPoligonlar }

      await cografiYetkiEkle({ name: ad.trim(), ...sahiplik, ...alanTanimi }, onOturumBitti)
      secimiTemizle()
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
  const aktifMod = MODLAR.find((m) => m.anahtar === mod)

  return (
    // Perdeye tıklayınca kapan; kutuya tıklayınca kapanmasın (stopPropagation).
    <div className="modal-perde" onClick={onKapat}>
      <div className="modal-kutu" onClick={(e) => e.stopPropagation()} role="dialog" aria-modal="true">
        <header className="modal-baslik">
          <div>
            <h2>Coğrafi Yetki — {sahip.ad} {sahipEtiketi}</h2>
            <p className="muted">
              {sahip.tur === 'rol'
                ? 'Bu roldeki kullanıcılar yalnızca tanımlı alanların içine çizim yapabilir.'
                : 'Bu kullanıcı yalnızca tanımlı alanların içine çizim yapabilir.'}
            </p>
          </div>
          <button type="button" className="modal-kapat" onClick={onKapat} aria-label="Kapat">×</button>
        </header>

        {hata && <p className="error-banner">{hata}</p>}

        <div className="modal-govde">
          <div className="geo-harita" ref={haritaElement} />

          <aside className="geo-yan">
            {/* ---- Ödev 10: tanımlama yolu ---- */}
            <div className="mod-secici" role="tablist" aria-label="Alan tanımlama yolu">
              {MODLAR.map((m) => (
                <button
                  key={m.anahtar}
                  type="button"
                  role="tab"
                  aria-selected={mod === m.anahtar}
                  className={`mod-btn${mod === m.anahtar ? ' active' : ''}`}
                  onClick={() => modDegistir(m.anahtar)}
                >
                  {m.etiket}
                </button>
              ))}
            </div>

            <form className="geo-form" onSubmit={kaydet}>
              {mod === 'bolge' && (
                <div className="bolge-listesi">
                  {bolgeler.map((b) => {
                    const secili = seciliBolgeler.includes(b.ad)
                    return (
                      <button
                        key={b.ad}
                        type="button"
                        className={`bolge-btn${secili ? ' active' : ''}`}
                        // Ödev 11: çoklu seçim — aynı düğme hem seçiyor hem kaldırıyor.
                        onClick={() => setSeciliBolgeler((o) => (secili
                          ? o.filter((x) => x !== b.ad)
                          : [...o, b.ad]))}
                        aria-pressed={secili}
                      >
                        {b.ad}
                        <small>{b.ilSayisi} il</small>
                      </button>
                    )
                  })}
                </div>
              )}

              {mod === 'poligon' && (
                kayitliAlanlar.length === 0 ? (
                  <p className="muted">Seçilebilecek kayıtlı alan yok.</p>
                ) : (
                  <div className="kayitli-alan-listesi">
                    {kayitliAlanlar.map((alan) => {
                      const secili = seciliPoligonlar.includes(alan.id)
                      return (
                        <button
                          key={alan.id}
                          type="button"
                          className={`bolge-btn${secili ? ' active' : ''}`}
                          onClick={() => setSeciliPoligonlar((o) => (secili
                            ? o.filter((x) => x !== alan.id)
                            : [...o, alan.id]))}
                          aria-pressed={secili}
                        >
                          {alan.name}
                          <small>#{alan.id}</small>
                        </button>
                      )
                    })}
                  </div>
                )
              )}

              {mod === 'il' && seciliPlakalar.length > 0 && (
                <ul className="secili-iller">
                  {seciliPlakalar.map((p) => (
                    <li key={p}>
                      {ilAdlari[p] ?? p}
                      <button
                        type="button"
                        onClick={() => setSeciliPlakalar((o) => o.filter((x) => x !== p))}
                        aria-label={`${ilAdlari[p] ?? p} seçimini kaldır`}
                      >
                        ×
                      </button>
                    </li>
                  ))}
                </ul>
              )}

              <label htmlFor="alan-adi">Alan adı</label>
              <input
                id="alan-adi"
                value={ad}
                onChange={(e) => { setAd(e.target.value); setAdElleGirildi(true) }}
                placeholder="Örn. Ankara ve çevresi"
                maxLength={200}
                required
              />

              <p className={`geo-durum${secimVar ? ' hazir' : ''}`}>
                {illerYukleniyor
                  ? 'İl sınırları yükleniyor…'
                  : secimVar
                    ? mod === 'cizim' ? 'Alan çizildi. Ad verip kaydedin.'
                      : mod === 'il' ? `${seciliPlakalar.length} il seçildi. Ad verip kaydedin.`
                        : mod === 'bolge' ? `${seciliBolgeler.length} bölge seçildi. Ad verip kaydedin.`
                          : `${seciliPoligonlar.length} kayıtlı alan seçildi. Ad verip kaydedin.`
                    : aktifMod.ipucu}
              </p>

              <div className="admin-eylemler">
                <button type="submit" className="btn-primary" disabled={!secimVar || kaydediliyor}>
                  {kaydediliyor ? 'Kaydediliyor…' : 'Alanı kaydet'}
                </button>
                <button type="button" className="btn-ghost" onClick={secimiTemizle} disabled={!secimVar}>
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
                    <button type="button" className="btn-ghost sil" onClick={() => sil(alan)} title="Sil">
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
