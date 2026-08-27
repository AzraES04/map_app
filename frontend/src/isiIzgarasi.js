// ============================================================================
//  Ödev 14 — ağırlıklı uygunluk ızgarasını HARİTA KATMANINA çevirme
//
//  Sunucu bir sayı dizisi gönderiyor: seçilen alanın her hücresi için 0–1
//  arası bir uygunluk puanı (alan dışı hücreler -1). Bu dosya o diziyi
//  renklendirip haritaya konabilecek bir görüntü katmanına dönüştürüyor.
//
//  NEDEN VEKTÖR DEĞİL DE GÖRÜNTÜ?
//  96×96'lık bir ızgara 9216 hücre. Her hücreyi ayrı bir OpenLayers
//  feature'ı yapsaydık her karede 9216 poligon çizilirdi — harita kaydırırken
//  gözle görülür şekilde takılırdı. Tek bir PNG ise GPU'nun tek işlemi;
//  üstelik tarayıcı onu büyütürken yumuşatıyor (image smoothing), yani
//  kareli bir battaniye yerine akışkan bir ısı yüzeyi çıkıyor.
//
//  NEDEN GeoServer'ın ısı haritası (Ödev 9) KULLANILMIYOR?
//  O katman TEK bir yoğunluğu çiziyor ve hesabı sunucuda, sabit bir SLD ile
//  yapıyor. Buradaki yüzey ise kullanıcının panelde verdiği ağırlıklara göre
//  her istekte yeniden hesaplanıyor; ağırlık her değiştiğinde GeoServer'a
//  yeni bir stil yazmak gerekirdi. İki araç yan yana duruyor ve bilerek
//  FARKLI renk rampaları kullanıyorlar (aşağıya bakınız).
// ============================================================================

import ImageLayer from 'ol/layer/Image'
import Static from 'ol/source/ImageStatic'
import { fromLonLat, toLonLat } from 'ol/proj'

/**
 * UYGUNLUK RAMPASI — mor → lacivert → turkuaz → yeşil → sarı ("viridis").
 *
 * Ödev 9'un ısı haritası kehribar-erik tonlarında (bkz. wms.js ISI_RAMPASI).
 * İkisi aynı olsaydı haritada aynı anda açık iki katmanı gözle ayırmak
 * imkânsızdı: "bu renk yoğunluk mu, uygunluk mu?" Bilinçli olarak farklı bir
 * aile seçildi.
 *
 * Viridis'in ikinci gerekçesi ALGISAL DÜZGÜNLÜK: eşit puan farkları ekranda
 * eşit renk farkı olarak görünüyor ve gri tonlamaya indirgendiğinde bile
 * sıralama korunuyor (renk körlüğü ve siyah-beyaz çıktı için önemli).
 * Gökkuşağı rampası bu iki özelliğin ikisini de sağlamıyor.
 */
export const UYGUNLUK_RAMPASI = [
  { deger: 0.00, renk: '#440154' },
  { deger: 0.25, renk: '#3b528b' },
  { deger: 0.50, renk: '#21918c' },
  { deger: 0.75, renk: '#5ec962' },
  { deger: 1.00, renk: '#fde725' },
]

/** Lejant çubuğu için CSS gradyanı. */
export const UYGUNLUK_GRADYANI = `linear-gradient(90deg, ${
  UYGUNLUK_RAMPASI.map((d) => `${d.renk} ${Math.round(d.deger * 100)}%`).join(', ')
})`

/** "#rrggbb" → [r, g, b]. Rampayı bir kez sayıya çevirmek için. */
function renktenRgb(hex) {
  return [
    parseInt(hex.slice(1, 3), 16),
    parseInt(hex.slice(3, 5), 16),
    parseInt(hex.slice(5, 7), 16),
  ]
}

const RAMPA_RGB = UYGUNLUK_RAMPASI.map((d) => ({ deger: d.deger, rgb: renktenRgb(d.renk) }))

/**
 * 0–1 arası bir orana karşılık gelen renk (iki durak arasında doğrusal geçiş).
 *
 * Rampayı beş durakla tanımlayıp aradaki tonları hesaplıyoruz; 256 rengi elle
 * yazmak yerine. Ara renkler doğrusal karışımla üretiliyor — viridis'in
 * durakları zaten yeterince sık olduğu için gözle fark edilmiyor.
 */
export function uygunlukRengi(oran) {
  const t = Math.min(1, Math.max(0, oran))

  for (let i = 1; i < RAMPA_RGB.length; i += 1) {
    const onceki = RAMPA_RGB[i - 1]
    const simdiki = RAMPA_RGB[i]
    if (t > simdiki.deger) continue

    const aralik = simdiki.deger - onceki.deger
    const pay = aralik === 0 ? 0 : (t - onceki.deger) / aralik

    return [
      Math.round(onceki.rgb[0] + (simdiki.rgb[0] - onceki.rgb[0]) * pay),
      Math.round(onceki.rgb[1] + (simdiki.rgb[1] - onceki.rgb[1]) * pay),
      Math.round(onceki.rgb[2] + (simdiki.rgb[2] - onceki.rgb[2]) * pay),
    ]
  }

  return RAMPA_RGB[RAMPA_RGB.length - 1].rgb
}

/** CSS'te kullanılabilir hâli — kriter rozetleri ve aday işaretçileri için. */
export const uygunlukRengiCss = (oran) => `rgb(${uygunlukRengi(oran).join(', ')})`

// Saydamlık, düşük puanlı bölgede altındaki haritayı göstersin diye puanla
// birlikte artıyor. Tam opak olsaydı analiz açıkken şehir ve yol adları
// kaybolur, "burası neresi?" sorusu cevapsız kalırdı.
const EN_AZ_SAYDAMSIZLIK = 0.45
const EN_COK_SAYDAMSIZLIK = 0.88

/**
 * Izgarayı bir PNG'ye boyayıp OpenLayers görüntü kaynağı üretir.
 *
 * ÖNEMLİ AYRINTI — MERCATOR DÜZELTMESİ.
 * Izgara DERECE düzleminde eşit aralıklı: her satır aynı enlem farkını
 * kaplıyor. Harita ise Web Mercator (EPSG:3857) ve orada enlem doğrusal
 * DEĞİL — kuzeye gidildikçe aynı derece farkı daha çok piksel kaplıyor.
 * Görüntüyü olduğu gibi extent'e yaymak, yüzeyi kuzey-güney yönünde
 * kaydırırdı: Türkiye ölçeğinde birkaç kilometrelik sistematik hata.
 *
 * Çözüm: çıktı satırlarını MERCATOR'da eşit aralıklı üretip her satırın
 * enlemini geri hesaplıyoruz, sonra ızgaradan o enleme düşen değeri
 * (komşu iki satır arasında ara değerle) okuyoruz.
 *
 * @param {{extent: number[], sutun: number, satir: number,
 *          degerler: number[], enYuksekSkor: number}} izgara Sunucudan gelen ızgara
 * @returns {import('ol/source/ImageStatic').default | null}
 */
export function izgaraKaynagiOlustur(izgara) {
  if (!izgara?.degerler?.length || !izgara.sutun || !izgara.satir) return null

  const [minBoylam, minEnlem, maxBoylam, maxEnlem] = izgara.extent
  const { sutun, satir, degerler } = izgara

  // Renk ölçeği en yüksek skora göre yayılıyor (sebep: IsiIzgarasiDto.EnYuksekSkor).
  const enBuyuk = izgara.enYuksekSkor > 0 ? izgara.enYuksekSkor : 1

  const [minX, minY] = fromLonLat([minBoylam, minEnlem])
  const [maxX, maxY] = fromLonLat([maxBoylam, maxEnlem])

  const tuval = document.createElement('canvas')
  tuval.width = sutun
  tuval.height = satir

  const ctx = tuval.getContext('2d')
  const goruntu = ctx.createImageData(sutun, satir)

  const hucreDerY = (maxEnlem - minEnlem) / satir

  /** Izgara hücresinin değeri; satır taşarsa en yakın kenara sabitlenir. */
  const hucre = (r, c) => degerler[Math.min(satir - 1, Math.max(0, r)) * sutun + c]

  for (let y = 0; y < satir; y += 1) {
    // Bu çıktı satırının Mercator'daki orta noktası → gerçek enlemi.
    const mercY = maxY - ((y + 0.5) / satir) * (maxY - minY)
    const enlem = toLonLat([0, mercY])[1]

    // Enlemin ızgaradaki (kesirli) satır karşılığı.
    const kaynakSatir = (maxEnlem - enlem) / hucreDerY - 0.5
    const ust = Math.floor(kaynakSatir)
    const pay = kaynakSatir - ust

    for (let x = 0; x < sutun; x += 1) {
      const a = hucre(ust, x)
      const b = hucre(ust + 1, x)

      // -1 = "alan dışı". İki komşudan YAKIN olanı dışarıdaysa piksel de
      // dışarıda sayılıyor: ara değer hesaplamak, alanın kenarında olmayan
      // bir puan uydurmak olurdu.
      const yakin = pay < 0.5 ? a : b
      const indeks = (y * sutun + x) * 4

      if (yakin < 0) {
        goruntu.data[indeks + 3] = 0     // tamamen saydam
        continue
      }

      // İki komşu da geçerliyse yumuşak geçiş; biri dışarıdaysa geçerli olan.
      const deger = a < 0 ? b : b < 0 ? a : a + (b - a) * pay
      const oran = Math.min(1, deger / enBuyuk)
      const [r, g, bMavi] = uygunlukRengi(oran)

      goruntu.data[indeks] = r
      goruntu.data[indeks + 1] = g
      goruntu.data[indeks + 2] = bMavi
      goruntu.data[indeks + 3] = Math.round(
        255 * (EN_AZ_SAYDAMSIZLIK + (EN_COK_SAYDAMSIZLIK - EN_AZ_SAYDAMSIZLIK) * oran),
      )
    }
  }

  ctx.putImageData(goruntu, 0, 0)

  return new Static({
    url: tuval.toDataURL(),
    // Görüntü METRE cinsinden bir kutuya oturuyor; yukarıdaki döngü zaten
    // satırları Mercator'a göre ürettiği için gerdirme doğru.
    imageExtent: [minX, minY, maxX, maxY],
    projection: 'EPSG:3857',
  })
}

/**
 * Uygunluk yüzeyinin haritadaki katmanı.
 *
 * Kaynağı BOŞ başlıyor: katman harita kurulurken bir kez ekleniyor, analiz
 * sonucu geldiğinde yalnızca kaynağı değişiyor. Her sonuçta katman ekleyip
 * çıkarsaydık katman sırası (zIndex) her seferinde yeniden pazarlık konusu
 * olurdu — Ödev 9'da tam olarak bu yüzden bir hata yaşanmıştı (ısı haritası
 * maskenin üstüne çıkıyordu).
 */
export function uygunlukKatmaniOlustur() {
  return new ImageLayer({
    source: null,
    visible: false,
    // POI simgelerinin (300) ÜSTÜNDE ki yüzey okunabilsin, ısı haritasının
    // (500) ve maskenin (900) ALTINDA ki o iki kural bozulmasın.
    zIndex: 450,
  })
}
