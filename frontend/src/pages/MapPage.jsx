import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import Map from 'ol/Map'
import View from 'ol/View'
import TileLayer from 'ol/layer/Tile'
import OSM from 'ol/source/OSM'
import VectorLayer from 'ol/layer/Vector'
import VectorSource from 'ol/source/Vector'
import Draw from 'ol/interaction/Draw'
import Modify from 'ol/interaction/Modify'
import Snap from 'ol/interaction/Snap'
import Translate from 'ol/interaction/Translate'
import Overlay from 'ol/Overlay'
import { Style, Circle, Fill, Stroke, Text, RegularShape } from 'ol/style'
import { fromLonLat, toLonLat } from 'ol/proj'
import { defaults as varsayilanKontroller } from 'ol/control/defaults'
import ScaleLine from 'ol/control/ScaleLine'
import MousePosition from 'ol/control/MousePosition'
import { easeOut } from 'ol/easing'
import { getDistance } from 'ol/sphere'
import 'ol/ol.css'

import Feature from 'ol/Feature'
import PointGeom from 'ol/geom/Point'
import PolygonGeom from 'ol/geom/Polygon'
import LineStringGeom from 'ol/geom/LineString'

import {
  clearSession, kalanOturumMetni, scheduleAutoLogout,
  GIRIS_ANIMASYON_ANAHTARI, girisAnimasyonuOynasinMi,
} from '../auth'
import { yerAra, yeriCoz } from '../geocode'
import Dunya from '../Dunya'
import {
  DRAW_TYPES, DRAW_TYPE_KEYS, geometryToWkt, wktToFeature, describeGeometry,
  RENK_SECENEKLERI, ANALIZ_RENGI,
} from '../geo'
import {
  listele, kaydet, sil, geriAl, guncelle, aktiflikDegistir, kesisimAnalizi,
  geoServerDurumu, konumAnalizi,
} from '../api'
import {
  wmsKatmaniOlustur, isiHaritasiKatmaniOlustur, ISI_GRADYANI,
  poiKatmaniOlustur,
} from '../wms'
// Ödev 14: ağırlıklı uygunluk ızgarasını haritaya çizen yardımcılar
import { izgaraKaynagiOlustur, uygunlukKatmaniOlustur, uygunlukRengiCss } from '../isiIzgarasi'
import {
  cizgiUzunlugu, okSayisi, okOranlari, okAcisi, soluklastir, rotaOzeti,
} from '../rotaOklari'
// Ödev 15: kategoriye özgü POI simgeleri (çizim verisi sunucudan geliyor)
import { ikonHaritasiKur, ikonBul, ikonSvg } from '../poiIkon'
import { kendiYetkilerim, calismaAlanim, illeriGetir } from '../adminApi'
import {
  poileriListele, poiKategorileriniListele, poiEkle, poiGuncelle, poiSil,
  poiAra, resmiTatilleriGetir, kategoriOner, poiStilleri as poiStilleriniGetir,
  EN_AZ_ARAMA,
} from '../poiApi'
import {
  GUN_ADLARI, GUN_TAM_ADLARI, MESAI_TIPLERI, bosPlan, resmiKurumPlani,
  planiNormallestir, planOzeti, gunuDegistir, haftaIcineUygula, bugunTatilMi,
  suAnDurum,
} from '../mesai'
import {
  guzergahlariListele, durakEkle as durakEkleIstek, durakSil as durakSilIstek,
  durakGuncelle as durakGuncelleIstek,
} from '../ulasimApi'
import { YETKILER, EKLEME_YETKISI } from '../yetkiler'
// Ödev 16: "Yönetim" düğmesi artık menünün TANIMINA bakıyor
import { ilkYonetimEkrani } from '../yonetimMenusu'
import HesapSecici from '../HesapSecici.jsx'
import TemaDugmesi from '../TemaDugmesi.jsx'
import KonumAnaliziPaneli from './KonumAnaliziPaneli.jsx'
import {
  TipIkonu, DuzenleIkonu, SilIkonu, DunyaIkonu, AnalizIkonu, IsiIkonu,
  SaatIkonu, RolIkonu, PoiIkonu, DurakIkonu, GuzergahIkonu,
} from '../icons'

// Türkiye'nin yaklaşık merkezi (boylam, enlem) — 4326 cinsinden yazıp
// fromLonLat ile haritanın diline (3857) çeviriyoruz.
const TURKEY_CENTER = [35.24, 39.0]
const TURKEY_ZOOM = 6.4

// ---------------------------------------------------------------------------
//  Ödev 11: izinli alan dışını söndüren maske
//
//  Web Mercator'ın (EPSG:3857) sınırı ±20037508 metre. Maskenin dış halkası
//  bu kutu; izinli alanlar onun İÇİNE DELİK olarak açılıyor.
//
//  Delik açmanın koşulu: deliğin sarım yönü dış halkanın TERSİ olmalı.
//  Tuval "nonzero" dolgu kuralı kullanıyor; aynı yöne sarılmış bir halka
//  delik açmaz, üstüne ikinci kez boyar. Bu yüzden aşağıdaki yardımcı
//  halkaların yönünü zorluyor.
// ---------------------------------------------------------------------------
const DUNYA_SINIRI = 20037508.34
const DUNYA_HALKASI = [
  [-DUNYA_SINIRI, -DUNYA_SINIRI],
  [DUNYA_SINIRI, -DUNYA_SINIRI],
  [DUNYA_SINIRI, DUNYA_SINIRI],
  [-DUNYA_SINIRI, DUNYA_SINIRI],
  [-DUNYA_SINIRI, -DUNYA_SINIRI],
]

/** Ayakkabı bağı (shoelace) formülü: pozitif alan = saat yönünün TERSİ. */
function halkaSaatYonununTersiMi(halka) {
  let toplam = 0
  for (let i = 0; i < halka.length - 1; i += 1) {
    const [x1, y1] = halka[i]
    const [x2, y2] = halka[i + 1]
    toplam += (x2 - x1) * (y2 + y1)
  }
  return toplam < 0
}

/**
 * Bir geometrinin (Polygon veya MultiPolygon) DIŞ halkalarını, maskede delik
 * olarak kullanılabilecek yönde döndürür.
 *
 * MultiPolygon gerekli: Ödev 10'dan sonra izinli alan bölge/il seçimiyle
 * tanımlanabiliyor ve çoğu zaman çok parçalı oluyor.
 */
function halkalariCikar(geometri) {
  const poligonlar = geometri.getType() === 'MultiPolygon'
    ? geometri.getPolygons()
    : [geometri]

  return poligonlar.map((p) => {
    const halka = p.getLinearRing(0).getCoordinates()
    // DUNYA_HALKASI saat yönünün tersinde; delikler saat yönünde olmalı.
    return halkaSaatYonununTersiMi(halka) ? [...halka].reverse() : halka
  })
}

// Dördüncü araç: çizim değil, var olan geometriyi düzenleme modu.
// Çizim tiplerinden ayrı tutuluyor çünkü OpenLayers Draw'a verilecek bir tip değil.
const DUZENLE = 'Duzenle'

// Envanter analizi aracı (Ödev 4 / Görev 3). Poligon çizdirir ama kaydetmez;
// çizilen alanla kesişen envanterleri sayar.
const ANALIZ = 'Analiz'

// Ödev 14 / Madde 1: KONUM ANALİZİ alanının çizim aracı.
//
// ANALIZ aracından ayrı bir araç: o, çizim biter bitmez kesişim sorgusu
// atıyor; bu ise yalnızca alanı BELİRLİYOR — analiz, kullanıcı kriterleri
// girip "Analizi Başlat" dedikten sonra çalışıyor. Tek araca yükleseydik
// aynı çizimin iki farklı sonucu olurdu.
const KONUM_ALAN = 'KonumAlan'

/** Konum analizi alanının rengi — mor. Analiz pembesiyle karışmasın. */
const KONUM_RENGI = '#8b5cf6'

// Ödev 12: POI ekleme aracı. Nokta çizdirir ama tbl_point'e DEĞİL, poi
// tablosuna yazar — bu yüzden DRAW_TYPES'ın bir üyesi değil, ayrı bir araç.
// Açılan form da farklı: ad + kategori + mesai saatleri (renk/görsel yok).
const POI = 'POI'

/** POI katmanının rengi — üç çizim tipinin hiçbirine benzemesin. */
const POI_RENGI = '#8e44ad'

// Ödev 16 / Madde 2: "Durak Ekle" aracı.
//
// POI aracının kardeşi ama AYRI: nokta koyuyor, farklı bir tabloya
// (durak) yazıyor ve formu farklı — ad + GÜZERGAH açılır listesi.
// Aynı aracı kullansaydık formın hangi kaydı üreteceği her tıklamada
// belirsiz olurdu; üstelik ikisinin yetkisi de farklı (POI Ekleme ↔
// Durak Ekleme) — ödev notu ulaşım rolünün POI ekleyememesini istiyor.
const DURAK = 'Durak'

/** Durak formunun başlangıç hâli. */
const BOS_DURAK_FORMU = { ad: '', guzergahId: '', aciklama: '' }

/**
 * Paneldeki POI listesi bir kerede kaç satır gösterir / "daha fazla" kaç ekler.
 *
 * Ödev 14 ile POI sayısı binlere çıktı (analiz veri seti). Hepsini birden
 * DOM'a basmak paneli yavaşlatıyor ve kimsenin işine yaramıyor: dört bin
 * satırı gözle taramak yerine üstteki arama barı kullanılıyor.
 */
const POI_LISTE_ADIMI = 60

// Ödev 6: hangi araç hangi yetkiyi ister?
// Üç çizim tipi zaten EKLEME_YETKISI'nde eşlenmiş; düzenleme ve analiz araçları
// da kendi yetkilerine bağlanıyor. Tek sözlük olması, hem düğmeyi kilitleyen
// hem klavye kısayolunu engelleyen kodun AYNI kaynağa bakmasını sağlıyor.
const ARAC_YETKISI = {
  ...EKLEME_YETKISI,
  [DUZENLE]: YETKILER.kayitGuncelleme,
  [ANALIZ]: YETKILER.analizCalistirma,
  [KONUM_ALAN]: YETKILER.analizCalistirma,
  [POI]: YETKILER.poiEkleme,
  [DURAK]: YETKILER.durakEkleme,
}

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
const GIRIS_ANAHTARI = GIRIS_ANIMASYON_ANAHTARI   // login ekranıyla ortak bayrak

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
  const cizgiMi = type === 'LineString'

  // Renk artık KAYIT BAZINDA (Ödev 4 / Görev 2). Her feature için yeni Style
  // üretmek saniyede yüzlerce nesne demek olurdu; bu yüzden renge göre
  // önbelleğe alıyoruz — aynı renkteki tüm kayıtlar aynı Style'ı paylaşıyor.
  //
  // ⚠️ Burada JavaScript'in Map'i KULLANILAMAZ: dosyanın başındaki
  // `import Map from 'ol/Map'` bu ismi gölgeliyor, `new Map()` bir OpenLayers
  // haritası üretirdi. Anahtarlarımız zaten renk METNİ olduğu için
  // prototipsiz düz bir nesne hem yeterli hem daha basit.
  const onbellek = Object.create(null)

  const stilUret = (renk) => {
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

    return {
      sade: new Style(gorunum),
      etiketli: new Style({
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
      }),
    }
  }

  return (feature) => {
    // Kaydın kendi rengi yoksa tipin varsayılan rengine düş.
    const renk = feature.get('renk') || DRAW_TYPES[type].color
    if (!onbellek[renk]) onbellek[renk] = stilUret(renk)
    const stiller = onbellek[renk]

    const ad = feature.get('ad')
    if (!ad) return stiller.sade
    stiller.etiketli.getText().setText(ad)
    return stiller.etiketli
  }
}

/**
 * Envanter analizi için çizilen GEÇİCİ poligon (Ödev 4 / Görev 3).
 * Kesikli, canlı pembe — kayıtlı hiçbir renge benzemiyor ki kullanıcı
 * "bu kaydedilmedi, sadece analiz için" mesajını görsel olarak alsın.
 */
/**
 * Analizde BULUNAN envanterlerin vurgusu.
 * Sayı vermek yetmiyordu — kullanıcı "hangileri?" diye soruyordu.
 * Kayıtların üstüne, analiz renginde bir halka/kalınlaştırma bindiriyoruz;
 * kaydın kendi rengi altta görünmeye devam ettiği için kimlik kaybolmuyor.
 */
const analizBulunanStili = new Style({
  image: new Circle({
    radius: 12,
    fill: new Fill({ color: 'rgba(255, 77, 125, 0.22)' }),
    stroke: new Stroke({ color: ANALIZ_RENGI, width: 2.5 }),
  }),
  stroke: new Stroke({ color: ANALIZ_RENGI, width: 7 }),
  fill: new Fill({ color: 'rgba(255, 77, 125, 0.14)' }),
})

const analizStili = new Style({
  stroke: new Stroke({ color: ANALIZ_RENGI, width: 3, lineDash: [10, 6] }),
  fill: new Fill({ color: 'rgba(255, 77, 125, 0.13)' }),
})

/**
 * KONUM ANALİZİ alanının sınırı (Ödev 14 / Madde 1).
 *
 * Dolgu YOK: alanın içini zaten uygunluk yüzeyi boyuyor, üstüne ikinci bir
 * renk basmak ısı haritasını okunmaz hâle getirirdi. Yalnızca kesikli mor
 * bir çerçeve — "analiz tam olarak burada çalıştı" demek için yeterli.
 *
 * İl seçildiğinde de aynı stil kullanılıyor: kullanıcı çizdiği alanla
 * seçtiği ilin sınırını ekranda AYNI dille görsün, ikisi de "analiz sınırı".
 */
const konumAlanStili = new Style({
  stroke: new Stroke({ color: KONUM_RENGI, width: 2.5, lineDash: [9, 6] }),
})

/**
 * Analizin önerdiği aday konumun işareti (Ödev 14).
 *
 * Numaralı bir madalyon: sıra numarası panelde de aynı sayı olduğu için
 * kullanıcı listedeki satırla haritadaki noktayı gözüyle eşleştirebiliyor.
 * Rengi adayın PUANINA göre değişiyor — uygunluk rampasının aynısı, yani
 * işaretin rengi altındaki yüzeyin rengiyle uyumlu.
 */
function konumAdayStili(feature) {
  const sira = feature.get('sira')
  const renk = feature.get('renk') || KONUM_RENGI

  return new Style({
    image: new Circle({
      radius: 13,
      fill: new Fill({ color: renk }),
      // Beyaz halka: yüzey hangi renkte olursa olsun madalyon ondan ayrışsın.
      stroke: new Stroke({ color: '#ffffff', width: 3 }),
    }),
    text: new Text({
      text: String(sira),
      font: '700 12px "Segoe UI", system-ui, sans-serif',
      // Zemin rengi değiştiği için yazı hem koyu hem açık üstünde okunmalı:
      // koyu metin + beyaz kontur her iki durumda da çalışıyor.
      fill: new Fill({ color: '#15202b' }),
      stroke: new Stroke({ color: 'rgba(255,255,255,0.85)', width: 2 }),
    }),
  })
}

/**
 * COĞRAFİ YETKİ ALANI (Ödev 7 / Madde 2) — kullanıcının çizim yapabildiği bölge.
 *
 * Dolgu YOK, yalnızca kalın kenar: alanın içi haritanın kendi içeriğidir,
 * üzerine renk basmak kayıtları okunmaz hâle getirirdi. Kesikli kenar
 * "bu bir sınır, bir kayıt değil" mesajını veriyor.
 */
/**
 * İzinli alanın DIŞINI söndüren maske (Ödev 11).
 *
 * Renk uygulamanın koyu zemin tonundan; "burası kapalı" hissi versin ama
 * altındaki harita tamamen kaybolmasın — kullanıcı nereye bakacağını yine
 * de görebilmeli.
 */
const maskeStili = new Style({
  fill: new Fill({ color: 'rgba(9, 20, 27, 0.5)' }),
})

const izinliAlanStili = new Style({
  stroke: new Stroke({ color: '#52b3c7', width: 3, lineDash: [12, 6] }),
})

/**
 * Sabitlenen termometre ölçümünün haritadaki işareti (Ödev 11).
 *
 * İçi BOŞ bir halka: ölçülen nokta zaten ısı yüzeyinin bir yeri, üstünü dolu
 * bir daireyle kapatmak tam da okunmak istenen rengi gizlerdi. Beyaz dış
 * kontur, hem koyu hem açık zeminde görünür kalmasını sağlıyor.
 */
const olcumStili = [
  new Style({
    image: new Circle({
      radius: 9,
      stroke: new Stroke({ color: '#ffffff', width: 4 }),
    }),
  }),
  new Style({
    image: new Circle({
      radius: 9,
      stroke: new Stroke({ color: '#09141b', width: 2 }),
    }),
  }),
]

/**
 * POI katmanının görünümü (Ödev 12).
 *
 * Çizim noktalarından AYIRT EDİLEBİLİR olması şart: ikisi de haritada birer
 * daire ve aynı anda ekranda duruyorlar. Ayrımı üç şey veriyor — mor renk,
 * içi boş halka (çizim noktaları dolu) ve etiketin altına yazılan kategori.
 *
 * Stil FONKSİYONU: etiket metni her feature'da farklı olduğu için sabit bir
 * Style yetmiyor (kayitStili ile aynı gerekçe).
 */
function poiStili(stiller = [], secim = null) {
  // Ödev 15: kategori id → simge. Boş liste = stiller henüz inmedi;
  // o durumda aşağıdaki daire görünümüne düşüyoruz.
  const ikonlar = ikonHaritasiKur(stiller)

  // Ödev 13'ten kalan YEDEK görünüm: mor daire.
  // Simge bulunamayan kategori (stiller inmedi ya da kategori yeni açıldı)
  // haritadan KAYBOLMASIN diye duruyor — "hiçbir şey çizme" en kötü seçenek.
  const daire = (pasif) => new Circle({
    radius: 8,
    fill: new Fill({ color: pasif ? 'rgba(142, 68, 173, 0.25)' : 'rgba(142, 68, 173, 0.85)' }),
    stroke: new Stroke({ color: '#ffffff', width: 2.5 }),
    declutterMode: 'obstacle',
  })

  const aktifDaire = daire(false)
  const pasifDaire = daire(true)

  // Tek bir Style nesnesi yeniden kullanılıyor: her feature için yenisini
  // kurmak, binlerce POI'de her karede binlerce nesne demek olurdu.
  const stil = new Style({
    text: new Text({
      font: '600 12px system-ui, -apple-system, "Segoe UI", sans-serif',
      fill: new Fill({ color: '#4a2060' }),
      stroke: new Stroke({ color: 'rgba(255,255,255,0.92)', width: 3.5 }),
      offsetY: -19,
      overflow: false,
    }),
  })

  return (feature) => {
    // Kategori seçili değilse HİÇ ÇİZME.
    //
    // null dönmek OpenLayers'ta yalnızca çizimi değil TIKLAMA TESTİNİ de
    // kapatıyor — gizli bir POI yanlışlıkla seçilemiyor. Katmanı topluca
    // gizlemek yerine burada süzmemizin sebebi bu: "hangi kategoriler açık"
    // bilgisi tek yerde kalıyor.
    if (secim && !secim.has(feature.get('kategoriId'))) return null

    const aktif = feature.get('aktif')
    const ikon = ikonBul(ikonlar, feature.get('kategoriId'))

    stil.setImage(ikon ?? (aktif ? aktifDaire : pasifDaire))

    // Pasif POI silinmiş değil, askıya alınmış: simge duruyor ama solıyor.
    // Saydamlık SIMGEYE uygulanıyor çünkü hazır bir resmin içindeki renklere
    // karışılamıyor (aynı gerekçe SLD'de de yazılı).
    //
    // ol/style/Style'da setOpacity YOK — yalnızca ImageStyle türevlerinde
    // (Icon, Circle) var. Style üzerinde çağırmak render sırasında TypeError
    // atıyor ve harita hiç çizilmiyor. Bu kod yolu yalnızca GeoServer
    // KAPALIYKEN çalıştığı için Ödev 15'te fark edilmemişti (Ödev 16'da
    // durak stili aynı hatayı tekrarlayınca ortaya çıktı).
    stil.getImage().setOpacity(aktif ? 1 : 0.35)

    stil.getText().setText(aktif ? (feature.get('ad') ?? '') : '')
    return stil
  }
}

/**
 * POI katmanının GÖRÜNMEZ hâli (Ödev 13 / Madde 1).
 *
 * GeoServer'ın POI stilleri devredeyken noktaları WMS çiziyor: kategoriye
 * göre farklı simge, renk ve yakın zoom'da isim etiketi. Vektör katmanı yine
 * de haritada duruyor çünkü TIKLAMA ondan geçiyor — WMS bir resimdir, içindeki
 * tek tek kayıtları tarayıcı göremez (bkz. wms.js başlığı).
 *
 * O yüzden katman burada neredeyse şeffaf bir daireyle çiziliyor: ekranda
 * WMS'in simgesi görünüyor, tıklama ise vektör katmanına düşüyor. Yarıçap
 * simgeden biraz BÜYÜK (14 > 7): dokunmatik ekranda ve fareyle hedefi
 * tutturmak kolay olsun.
 *
 * İki katmanı da çizdirseydik her POI'nin üstünde iki simge üst üste binerdi;
 * vektör katmanını tamamen kaldırsaydık POI'ye tıklanamaz, bilgi paneli
 * (Ödev 12) çalışmaz hâle gelirdi.
 */
function poiVurusStili(secim = null) {
  const vurus = new Style({
    image: new Circle({
      radius: 14,
      // Tamamen saydam değil: sıfır alfa bazı tarayıcılarda hiç çizilmiyor
      // sayılıp isabet testinin dışında kalabiliyor. 0.01 gözle görülmez ama
      // katmanın "burada bir şey var" demesi için yeterli.
      fill: new Fill({ color: 'rgba(142, 68, 173, 0.01)' }),
    }),
  })

  /**
   * SÜRÜKLENEN nokta belirgin çiziliyor.
   *
   * Bu katman normalde neredeyse şeffaf: ekranda görünen simge WMS'ten
   * geliyor. Ama WMS bir RESİM ve sürükleme sırasında güncellenmiyor — yani
   * işaretlemeseydik kullanıcı bir şeyi sürüklerken hiçbir şeyin hareket
   * ettiğini GÖRMEZDİ.
   */
  const surukleme = new Style({
    image: new Circle({
      radius: 11,
      fill: new Fill({ color: 'rgba(232, 161, 60, 0.9)' }),
      stroke: new Stroke({ color: '#ffffff', width: 3 }),
    }),
  })

  return (feature) => {
    // WMS o kategoriyi ÇİZMİYORSA tıklama hedefi de olmamalı; yoksa boş bir
    // yere tıklayan kullanıcı görünmeyen bir POI'nin bilgi kartını açardı.
    if (secim && !secim.has(feature.get('kategoriId'))) return null
    return feature.get('suruklenlyor') ? surukleme : vurus
  }
}

/**
 * DURAK simgesi (Ödev 16).
 *
 * Rengi HATTINDAN geliyor: birkaç güzergah aynı anda açıkken bir durağın
 * hangi hatta ait olduğunu anlamanın tek yolu bu. İçinde SIRA NUMARASI
 * yazıyor — yönetim panelindeki listeyle haritayı gözle eşleştirmeyi
 * sağlıyor (analiz aday madalyonlarındaki numaranın aynı fikri).
 *
 * Kare değil DAİRE: POI'ler zaten kategoriye özgü simgelerle çiziliyor
 * (Ödev 15); durak onlardan bakışta ayrılmalı ve numarayı taşıyabilecek
 * kadar yer vermeli.
 */
function durakStili() {
  const stil = new Style({
    image: new Circle({
      radius: 10,
      fill: new Fill({ color: '#7a7f87' }),
      stroke: new Stroke({ color: '#ffffff', width: 2.5 }),
      declutterMode: 'obstacle',
    }),
    text: new Text({
      font: '700 10px system-ui, -apple-system, "Segoe UI", sans-serif',
      fill: new Fill({ color: '#ffffff' }),
      offsetY: 1,
    }),
  })

  return (feature) => {
    // Sürüklenen durak büyür ve turuncuya döner: kullanıcı neyi taşıdığını
    // görsün. Taslak çizim rengiyle aynı ton — ikisi de "henüz kalıcı değil"
    // demek.
    if (feature.get('suruklenlyor')) {
      stil.getImage().getFill().setColor('#e8a13c')
      stil.getImage().setOpacity(1)
      stil.getText().setText('')
      return stil
    }

    // Ödev 17: hattı kapatılmışsa durağı da çizme.
    //
    // Katmanı gizlemek YETMEZDİ: durak ve hat AYNI katmanda değil ama bütün
    // hatlar aynı iki katmanı paylaşıyor. Tek tek gizlemenin yolu stilden
    // null dönmek — OpenLayers'ta "bu feature çizilmesin" demenin yolu bu.
    if (feature.get('gizli')) return null

    const renk = feature.get('renk') || '#7a7f87'
    stil.getImage().getFill().setColor(renk)
    stil.getText().setText(String(feature.get('sira') ?? ''))

    // Pasif durak silinmiş değil, askıya alınmış: yerinde duruyor ama solıyor.
    //
    // Saydamlık STYLE'da değil IMAGE'da: ol/style/Style'ın setOpacity metodu
    // YOK (yalnızca ImageStyle türevlerinde var) ve çağırmak render sırasında
    // TypeError atıyor — harita komple çizilmiyor.
    stil.getImage().setOpacity(feature.get('aktif') === false ? 0.35 : 1)
    return stil
  }
}

/**
 * GÜZERGAH ÇİZGİSİ (Ödev 16 + Ödev 17).
 *
 * İKİ FARKLI ÇİZGİ, TEK STİL:
 *
 *   • OSRM ROTASI (Ödev 17) — yollara oturmuş gerçek sürüş güzergahı;
 *     sunucudan `rotaWkt` olarak geliyor. DÜZ çiziliyor.
 *
 *   • DÜZ HAT (Ödev 16'nın davranışı) — durakları doğrudan birleştiren
 *     çizgi. Yalnızca rota yoksa kullanılıyor ve KESİKLİ çiziliyor:
 *     kullanıcı "bu kuş uçuşu, gerçek güzergah değil" bilgisini bakışta
 *     almalı. Aynı görünümü verseydik, OSRM kapalıyken binaların içinden
 *     geçen bir çizgi gerçek hat sanılırdı.
 *
 * ROTA GÜNCEL DEĞİLSE (durak değişti ama OSRM o an kapalıydı) çizgi SOLGUN
 * gösteriliyor. Veriyi silmiyoruz ama doğruymuş gibi de göstermiyoruz.
 *
 * İki katmanlı: altta kalın beyaz bir taban, üstte hattın rengi. Beyaz
 * taban olmasaydı koyu bir uydu zemininde ya da yoğun OSM çizgileri
 * arasında hat kaybolurdu — gerçek metro haritaları da aynı numarayı
 * kullanıyor.
 */
function guzergahStili() {
  const taban = new Style({
    stroke: new Stroke({ color: 'rgba(255,255,255,0.85)', width: 8, lineCap: 'round', lineJoin: 'round' }),
  })
  const hat = new Style({
    stroke: new Stroke({ color: '#2d7dd2', width: 4, lineCap: 'round', lineJoin: 'round' }),
  })

  return (feature) => {
    if (feature.get('gizli')) return null

    const renk = feature.get('renk') || '#2d7dd2'
    const rotaVar = Boolean(feature.get('rotaVar'))
    const guncel = feature.get('rotaGuncel') !== false

    hat.getStroke().setColor(guncel ? renk : soluklastir(renk))
    taban.getStroke().setColor(guncel ? 'rgba(255,255,255,0.85)' : 'rgba(255,255,255,0.45)')

    // lineDash'i açıkça null'a çekmek ŞART: stil nesnesi bütün hatlar
    // arasında PAYLAŞILIYOR, bir önceki feature'dan kalan desen aksi hâlde
    // düz çizilmesi gereken hatlara da bulaşırdı.
    hat.getStroke().setLineDash(rotaVar ? null : [10, 8])

    // Ödev 17: "Rota yönü harita üzerinde ok işaretleri ile gösterilmelidir."
    // Oklar yalnızca GERÇEK rotada gösteriliyor: düz kuş uçuşu çizgide
    // "yön" göstermek, olmayan bir güzergah hakkında bilgi vermek olurdu.
    const oklar = rotaVar
      ? yonOklari(feature.getGeometry(), renk, guncel)
      : []

    return [taban, hat, ...oklar]
  }
}

/**
 * YÖN OKLARI (Ödev 17 / Madde 1) — stil nesnelerine dönüştürme.
 *
 * Hesabın kendisi `rotaOklari.js`'te: kaç ok, nereye, hangi açıyla.
 * Burası yalnızca o sonuçları OpenLayers stiline çeviriyor.
 *
 * Oklar ayrı FEATURE olarak eklenmiyor, çizgiyi çizen stilin İÇİNDE
 * üretiliyor. Ayrı feature olsalardı kaynakta yüzlerce fazladan nesne
 * dolaşır, tıklama testine karışır ve hat gizlendiğinde ayrıca gizlenmeleri
 * gerekirdi.
 */
function yonOklari(cizgi, renk, guncel) {
  if (!cizgi || typeof cizgi.getCoordinateAt !== 'function') return []

  const uzunluk = cizgiUzunlugu(cizgi.getCoordinates())
  if (uzunluk === 0) return []

  const noktaAl = (oran) => cizgi.getCoordinateAt(oran)

  return okOranlari(okSayisi(uzunluk)).map((oran) => new Style({
    geometry: new PointGeom(noktaAl(oran)),
    image: new RegularShape({
      points: 3,
      radius: 7,
      rotation: okAcisi(noktaAl, oran),
      rotateWithView: true,
      fill: new Fill({ color: guncel ? renk : soluklastir(renk) }),
      stroke: new Stroke({ color: 'rgba(255,255,255,0.92)', width: 1.5 }),
    }),
  }))
}

/** Henüz kaydedilmemiş çizim: kesikli turuncu — "bu geçici" mesajını verir. */
const taslakStili = new Style({
  image: new Circle({
    radius: 7,
    fill: new Fill({ color: '#e8a13c' }),
    stroke: new Stroke({ color: '#ffffff', width: 2 }),
  }),
  stroke: new Stroke({ color: '#e8a13c', width: 3, lineDash: [8, 6] }),
  fill: new Fill({ color: 'rgba(232, 161, 60, 0.20)' }),
})

/** Listede fareyle üzerine gelinen kaydın haritadaki vurgusu. */
const vurguStili = new Style({
  image: new Circle({
    radius: 11,
    fill: new Fill({ color: 'rgba(232, 161, 60, 0.92)' }),
    stroke: new Stroke({ color: '#ffffff', width: 3 }),
  }),
  stroke: new Stroke({ color: '#e8a13c', width: 6 }),
  fill: new Fill({ color: 'rgba(232, 161, 60, 0.35)' }),
})

const BOS_KAYITLAR = { Point: [], LineString: [], Polygon: [] }

/**
 * POI formunun boş hâli.
 *
 * `plan` alanı Ödev 13 / Madde 3 ile geldi: mesai artık tek bir saat aralığı
 * değil, GÜN GÜN tanımlanan bir plan (bkz. frontend/src/mesai.js).
 * `oneri` ise Madde 4'ün ürünü: kategori otomatik seçildiyse hangi gerekçeyle
 * seçildiğini kullanıcıya söyleyebilmek için saklanıyor.
 */
const BOS_POI_FORMU = () => ({ isim: '', kategoriId: '', plan: bosPlan(), oneri: null })

/**
 * Mesai planı alanları (Ödev 13 / Madde 3).
 *
 * Ödev 12'de tek bir saat aralığı ve "7/24" kutusu vardı. Şimdi üç kip var:
 *
 *   Haftalık     → gün gün açık/kapalı ve saat
 *   7/24         → gün listesi anlamsız, gizleniyor
 *   Resmî kurum  → hafta içi sabit saat + hafta sonu kapalı ŞABLONU yükleniyor
 *                  ve "resmî tatillerde kapalı" bayrağı KİLİTLİ açık kalıyor
 *
 * NEDEN RESMÎ KURUM AYRI BİR KİP DE "sadece bir onay kutusu" DEĞİL?
 * Çünkü iki şeyi birden yapıyor: hazır bir haftalık şablon yüklüyor VE tatil
 * kuralını dayatıyor. Kullanıcı "resmî kurum" dediğinde on iki alanı tek tek
 * doldurmak zorunda kalmıyor; ödevin istediği "resmî kurum modu seçilince
 * ortak tatillerde kapalı, çalışma saati belli" davranışı tam olarak bu.
 *
 * Saatler kilitli DEĞİL: kurumdan kuruma yarım saat oynuyor. Kilitlenen tek
 * şey tatil bayrağı — o, kipin tanımının parçası.
 *
 * EKLEME ve DÜZENLEME formlarının ikisi de bu bileşeni kullanıyor; ayrı ayrı
 * yazsaydık biri değiştiğinde diğerinin unutulması an meselesiydi.
 */
function MesaiPlaniAlanlari({ plan, degistir, onEk, tatiller, tatillerUyarisi }) {
  const surekli = plan.tip === MESAI_TIPLERI.surekli
  const resmi = plan.tip === MESAI_TIPLERI.resmi

  /**
   * Kip değişimi. "Resmî kurum" seçildiğinde ŞABLON YÜKLENİYOR, diğer
   * kiplerde yalnızca tip değişiyor: kullanıcının 7/24'ü açıp kapatması
   * elle girdiği saatleri silmemeli.
   */
  const tipiDegistir = (tip) => {
    if (tip === MESAI_TIPLERI.resmi) return degistir(resmiKurumPlani())

    degistir({
      ...plan,
      tip,
      // Resmî kurumdan çıkarken tatil kilidi de kalkıyor; bayrak kullanıcının
      // seçimine dönüyor (işaretli kalıyor, ama artık değiştirilebilir).
      resmiTatilKapali: plan.resmiTatilKapali,
    })
  }

  // "Hafta içine uygula" için kaynak saatler: ilk AÇIK hafta içi günü.
  const ornekGun = plan.gunler.find((g) => g.gun <= 5 && g.acik) ?? plan.gunler[0]

  return (
    <div className="mesai-alanlari">
      <div className="mesai-kipler" role="radiogroup" aria-label="Mesai kipi">
        {[
          { deger: MESAI_TIPLERI.haftalik, etiket: 'Haftalık' },
          { deger: MESAI_TIPLERI.surekli, etiket: '7/24' },
          { deger: MESAI_TIPLERI.resmi, etiket: 'Resmî kurum' },
        ].map((kip) => (
          <label key={kip.deger} className={plan.tip === kip.deger ? 'secili' : ''}>
            <input
              type="radio"
              name={`${onEk}-mesai-tip`}
              checked={plan.tip === kip.deger}
              onChange={() => tipiDegistir(kip.deger)}
            />
            {kip.etiket}
          </label>
        ))}
      </div>

      {/* 7/24 seçiliyken gün satırları anlamsız — gizleniyor, kilitlenmiyor:
          kullanılamayacak bir alanı ekranda tutmanın faydası yok. */}
      {!surekli && (
        /* GÜN SATIRLARI KATLI DURUYOR.
           Yedi satır × (onay kutusu + iki saat seçici) = on dört alan; hepsi
           birden açıkken form popup'ı ekranın tamamını kaplıyordu ve altındaki
           Kaydet düğmesi görünmüyordu. Çoğu kullanıcı hazır kiplerden birini
           seçip geçiyor, gün gün düzenleme istisna.

           <details> kullanmamızın sebebi: açılıp kapanma, klavye erişimi ve
           ekran okuyucu duyurusu tarayıcıdan bedava geliyor.

           Özet satırı KATLIYKEN DE bilgi veriyor — "Günler" yazan boş bir
           kutu, açmadan hiçbir şey söylemezdi. */
        <details className="akordiyon">
          <summary>
            Günler
            <span className="akordiyon-ozet">{planOzeti(plan) || '—'}</span>
          </summary>

          <div className="akordiyon-govde">
          <div className="mesai-gunler">
            {plan.gunler.map((gun) => (
              <div key={gun.gun} className={`mesai-gun${gun.acik ? '' : ' kapali'}`}>
                <label className="mesai-gun-ad" title={GUN_TAM_ADLARI[gun.gun - 1]}>
                  <input
                    type="checkbox"
                    checked={gun.acik}
                    onChange={(e) =>
                      degistir(gunuDegistir(plan, gun.gun, { acik: e.target.checked }))}
                    aria-label={`${GUN_TAM_ADLARI[gun.gun - 1]} açık mı`}
                  />
                  {GUN_ADLARI[gun.gun - 1]}
                </label>

                {gun.acik ? (
                  <div className="mesai-saatler">
                    <input
                      type="time"
                      value={gun.acilis ?? ''}
                      onChange={(e) =>
                        degistir(gunuDegistir(plan, gun.gun, { acilis: e.target.value }))}
                      aria-label={`${GUN_TAM_ADLARI[gun.gun - 1]} açılış saati`}
                    />
                    <span aria-hidden="true">–</span>
                    <input
                      type="time"
                      value={gun.kapanis ?? ''}
                      onChange={(e) =>
                        degistir(gunuDegistir(plan, gun.gun, { kapanis: e.target.value }))}
                      aria-label={`${GUN_TAM_ADLARI[gun.gun - 1]} kapanış saati`}
                    />
                  </div>
                ) : (
                  <span className="mesai-kapali-yazi">kapalı</span>
                )}
              </div>
            ))}
          </div>

          {/* Yedi satırı tek tek doldurmak mesai girişinde en sık yapılan
              hareket; tek düğmeye indiriyoruz. */}
          <button
            type="button"
            className="btn-ghost mesai-toplu"
            onClick={() =>
              degistir(haftaIcineUygula(plan, ornekGun.acilis ?? '09:00', ornekGun.kapanis ?? '18:00'))}
          >
            Pzt–Cum için {ornekGun.acilis ?? '09:00'}–{ornekGun.kapanis ?? '18:00'} uygula
          </button>
          </div>
        </details>
      )}

      <label className="mesai-tatil">
        <input
          type="checkbox"
          checked={plan.resmiTatilKapali}
          // Resmî kurum kipinde seçenek değil kural: kilitli açık.
          disabled={resmi}
          onChange={(e) => degistir({ ...plan, resmiTatilKapali: e.target.checked })}
        />
        Resmî tatillerde kapalı
        {resmi && <em> (resmî kurum kipinde zorunlu)</em>}
      </label>

      {/* Tatil listesi yalnızca ilgili olduğunda açılıyor. Ödevin istediği
          "ortak tatillerde kapalı" bilgisinin somut karşılığı bu liste:
          kullanıcı hangi günlerde kapalı olacağını görüyor. */}
      {plan.resmiTatilKapali && (
        <details className="mesai-tatiller">
          <summary>
            Kapalı olunacak resmî tatiller
            {tatiller?.length ? ` (${tatiller.length} gün)` : ''}
          </summary>

          {tatillerUyarisi && <p className="popup-uyari">{tatillerUyarisi}</p>}

          <ul>
            {(tatiller ?? []).map((t) => (
              <li key={t.tarih}>
                <span className="tatil-tarih">
                  {new Date(`${t.tarih}T00:00:00`).toLocaleDateString('tr-TR', {
                    day: '2-digit', month: 'short',
                  })}
                </span>
                <span>{t.ad}</span>
                {t.yarimGun && <em className="tatil-yarim">yarım gün</em>}
              </li>
            ))}
          </ul>
        </details>
      )}

      <p className="mesai-onizleme">
        Kaydedilecek: <code>{planOzeti(plan) || '—'}</code>
      </p>
    </div>
  )
}

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
  const analizSourceRef = useRef(null) // geçici analiz poligonu (veritabanına gitmez)
  const analizBulunanRef = useRef(null) // analizde bulunan envanterlerin vurgu katmanı
  // Ödev 14 — konum analizi: alan sınırı, aday işaretçileri ve uygunluk yüzeyi
  const konumAlanKaynagiRef = useRef(null)
  const konumAdayKaynagiRef = useRef(null)
  const konumIsiKatmanRef = useRef(null)
  const izinliAlanRef = useRef(null)    // coğrafi yetki sınırı (Ödev 7)
  // Ödev 16 — ulaşım: durak noktaları ve onlardan türeyen güzergah çizgileri
  const durakKaynagiRef = useRef(null)
  const guzergahKaynagiRef = useRef(null)
  // Katmanın kendisi de lazım: tıklama testi hangi katmanların dikkate
  // alınacağını katman nesnesiyle süzüyor (bkz. kayitKatmanlari).
  const durakKatmanRef = useRef(null)

  const poiKaynagiRef = useRef(null)    // POI katmanının kaynağı (Ödev 12)
  const poiKatmanRef = useRef(null)
  const wmsKatmanRef = useRef(null)     // GeoServer WMS katmanı (Ödev 8)
  const wmsKaynakRef = useRef(null)     // aynı katmanın kaynağı — tazelemek için
  const isiKatmanRef = useRef(null)     // ısı haritası katmanı (Ödev 9)
  const isiKaynakRef = useRef(null)
  const isiDegerOkuRef = useRef(null)   // tıklanan noktanın yoğunluğu (Ödev 11)
  const maskeKaynagiRef = useRef(null)  // izinli alan dışını söndüren maske (Ödev 11)
  const olcumKaynagiRef = useRef(null)  // sabitlenen termometre ölçümünün işaretçisi (Ödev 11)
  // Sabitlenen ölçümün koordinatı. State'in kendisini değil ref'i okuyoruz:
  // "değer resmi hazır" bildirimi harita katmanının içinden geliyor ve o
  // kapanış (closure) React state'inin eski hâlini görürdü.
  const sabitOlcumKonumuRef = useRef(null)
  // Termometre gezinirken saniyede onlarca kez okunmasın diye son okuma zamanı.
  const sonOlcumZamaniRef = useRef(0)
  // "Burası kapalı" uyarısı arka arkaya tıklamada tekrar tekrar çıkmasın.
  const sonKapaliUyariRef = useRef(0)
  // Çizim aracının "buraya izin var mı?" sorusu için: izinli alanların
  // birleşimi, harita projeksiyonunda. Ref çünkü OpenLayers'ın condition
  // fonksiyonu React state'ini göremez — her tıklamada en güncelini okumalı.
  const izinliGeometriRef = useRef(null)
  const aramaGirdiRef = useRef(null)    // "/" kısayolunun odaklanacağı arama kutusu
  // Klavye dinleyicisi, aşağıda TANIMLANAN aracSec'i çağırmak zorunda. Doğrudan
  // referans versek "temporal dead zone" hatası alırdık (bu projede bir kez yaşandı).
  // Ref üzerinden erişmek hem o sorunu çözüyor hem de dinleyicinin her araç
  // değişiminde sökülüp yeniden kurulmasını engelliyor.
  const aracSecRef = useRef(() => {})
  const popupElement = useRef(null)    // popup'ın DOM kökü (OpenLayers konumlandırıyor)
  const popupOverlayRef = useRef(null)
  const toastZamanlayiciRef = useRef(null)
  const sahneTemizleRef = useRef(null)   // açılış sahnesinin zamanlayıcı/dinleyici temizliği

  // --- Ekran durumu ---------------------------------------------------------
  const [activeTool, setActiveTool] = useState(null)      // null | 'Point' | 'LineString' | 'Polygon'
  const [pending, setPending] = useState(null)            // çizildi, henüz kaydedilmedi
  // Renk varsayılanı: kullanıcı dokunmasa bile geçerli bir değer gitsin.
  const [form, setForm] = useState({
    name: '', description: '', imageUrl: '', color: RENK_SECENEKLERI[0].deger,
  })

  // Envanter analizi sonucu (Ödev 4 / Görev 3): { total, pointCount, ... } | null
  const [analizSonuc, setAnalizSonuc] = useState(null)
  const [analizYukleniyor, setAnalizYukleniyor] = useState(false)

  // ---- Ödev 14: konum analizi ----
  // Panel AÇILIP KAPANIYOR: sol panelde yedi bölüm var, sekizincisi sürekli
  // açık dursaydı asıl işi (çizim, kayıt listesi) aşağı iterdi.
  const [konumPaneliAcik, setKonumPaneliAcik] = useState(false)
  const [konumSonuc, setKonumSonuc] = useState(null)
  const [konumYukleniyor, setKonumYukleniyor] = useState(false)
  const [konumHata, setKonumHata] = useState(null)
  // Panelde "Haritada çiz" ile belirlenen alanın WKT'si (kaydedilmez).
  const [konumAlanWkt, setKonumAlanWkt] = useState(null)
  // İl listesi — panel ilk açıldığında bir kez indiriliyor (81 satır, geometrisiz).
  const [iller, setIller] = useState([])
  const [illerHatasi, setIllerHatasi] = useState(null)
  // İlk yüklemede panel bomboş kalmasın diye iskelet satırlar gösteriyoruz
  const [kayitlarYukleniyor, setKayitlarYukleniyor] = useState(true)
  const [records, setRecords] = useState(BOS_KAYITLAR)
  const [visible, setVisible] = useState({ Point: true, LineString: true, Polygon: true })
  const [activeTab, setActiveTab] = useState('Point')
  const [toast, setToast] = useState(null)                // { tur: 'ok' | 'hata', mesaj }
  const [saving, setSaving] = useState(false)
  const [remaining, setRemaining] = useState('')
  // Açılış sahnesinin evresi: null (kapalı) | 'kure' (gezegen sahnede) | 'inis' (Türkiye'ye zoom)
  // İLK RENDER'DA da sahne açık olsun: aksi hâlde harita bir kare boyunca
  // Türkiye görünümünde çizilir, sonra sahne üstüne biner — login'den gelirken
  // göze çarpan bir "sıçrama" olurdu.
  const [uzaySahnesi, setUzaySahnesi] = useState(() => (girisAnimasyonuOynasinMi() ? 'kure' : null))
  // Haritada tıklanan geometrinin popup içeriği: { dto, tip, ozet } | null
  const [secili, setSecili] = useState(null)
  // Ödev 5 / Madde 4: detay popup'ında düzenleme modu
  const [duzenleForm, setDuzenleForm] = useState(null)   // { name, color } | null
  const [kaydediliyor, setKaydediliyor] = useState(false)

  // Ödev 6: giriş yapan kullanıcının SAHİP OLDUĞU yetkilerin adları.
  // Araç düğmeleri, silme düğmeleri ve yönetim bağlantısı buna göre açılıp kapanıyor.
  // Bu SADECE nezaket: asıl kontrol sunucuda, [YetkiGerekli] özniteliğinde.
  // Düğmeyi gizlemek güvenlik değildir — isteği elle atan yetkisiz kullanıcı yine 403 alır.
  // Amaç, kullanıcıyı yapamayacağı bir işe kalkıştırıp sonunda hata göstermemek.
  const [yetkilerim, setYetkilerim] = useState([])

  /**
   * Giriş yapan kullanıcının bu yetkisi var mı? (rolden veya doğrudan)
   *
   * Tanım, beslendiği state'in HEMEN YANINDA duruyor. Önceden 2200 satır
   * aşağıdaydı ve bu bir hataya yol açtı: sürükleme etkileşimi (yukarıda)
   * bu yardımcıyı çağırıyor ama `const` henüz başlatılmamış oluyordu —
   * ReferenceError ile bütün harita ekranı çöküyordu.
   *
   * Türetilmiş bir değeri kaynağından uzağa koymak, o değeri kullanan yeni
   * kodun nereye yazılabileceğini sessizce kısıtlıyor.
   */
  const yetkiVar = (ad) => yetkilerim.includes(ad)

  // ---- Ödev 12: POI ----
  // Kayıtlar SÜZÜLMEDEN geliyor: POI ortak referans verisi, herkes hepsini
  // görüyor (çizimlerden bilerek farklı — bkz. README, Ödev 12 bölümü).
  const [poiler, setPoiler] = useState([])
  // Panelde bir kerede kaç POI satırı çizilecek (Ödev 14).
  //
  // Ödev 14'ün analiz veri setiyle POI sayısı binlere çıktı; listenin
  // tamamını DOM'a basmak paneli gözle görülür şekilde yavaşlatıyordu.
  // Liste zaten GEZİNMEK için; ARAMAK için haritanın üstündeki arama barı var.
  const [poiListeSiniri, setPoiListeSiniri] = useState(POI_LISTE_ADIMI)
  const [poiKategorileri, setPoiKategorileri] = useState([])
  const [poiTaslak, setPoiTaslak] = useState(null)     // çizildi, kaydedilmedi
  const [poiForm, setPoiForm] = useState(BOS_POI_FORMU)
  const [poiKaydediliyor, setPoiKaydediliyor] = useState(false)
  /**
   * HANGİ POI KATEGORİLERİ GÖRÜNSÜN?
   *
   * ---- NEDEN TEK BİR AÇ/KAPAT DEĞİL? ----
   *
   * Önceden tek bir "POI" onay kutusu vardı ve varsayılan AÇIKTI. Veritabanında
   * dört binden fazla POI olduğu için harita açılır açılmaz simgelerle
   * doluyordu: ne çizimler, ne duraklar, ne de hatlar seçilebiliyordu.
   * Kullanıcının şikâyeti tam olarak buydu.
   *
   * "Hepsi açık / hepsi kapalı" da yetmezdi: kullanıcı çoğu zaman TEK bir
   * kategoriyle ilgileniyor (yalnızca eczaneler, yalnızca okullar).
   *
   * ---- NEDEN BOŞ BAŞLIYOR? ----
   *
   * Boş küme = hiçbir POI görünmüyor. Harita temiz açılıyor ve kullanıcı ne
   * istiyorsa onu ekliyor. Tersi (hepsi açık başlasın, istemediğini kapatsın)
   * kalabalığı varsayılan yapardı — yani düzeltmek istediğimiz şeyi.
   *
   * Seçilenleri tutuyoruz, gizlenenleri DEĞİL: sunucuya yeni bir kategori
   * eklendiğinde kendiliğinden görünmesin, kullanıcı bilinçli olarak açsın.
   */
  const [poiKategoriSecimi, setPoiKategoriSecimi] = useState(() => new Set())

  const poiKategorisiDegistir = useCallback((kategoriId) => {
    setPoiKategoriSecimi((onceki) => {
      const yeni = new Set(onceki)
      if (yeni.has(kategoriId)) yeni.delete(kategoriId)
      else yeni.add(kategoriId)
      return yeni
    })
  }, [])
  // Bilgi kartındaki düzenleme modu: { isim, kategoriId, plan } | null
  const [poiDuzenle, setPoiDuzenle] = useState(null)

  /**
   * Durak düzenleme — bilgi kutucuğunun içinde açılan form.
   *
   * POI'deki desenin aynısı ve bilerek: kullanıcı iki farklı nesne için iki
   * farklı akış öğrenmek zorunda kalmasın. Ayrı bir ekrana götürmek de
   * mümkündü ama durak düzenlemenin tek bağlamı HARİTADAKİ YERİ — o bağlamı
   * kaybetmek işi zorlaştırırdı.
   *
   * null → form kapalı. Dolu → { ad, aciklama, guzergahId }
   */
  const [durakDuzenle, setDurakDuzenle] = useState(null)
  const [durakDuzenleKaydediliyor, setDurakDuzenleKaydediliyor] = useState(false)

  // ---- Ödev 16: ulaşım modülü ----
  //
  // Güzergahlar duraklarıyla BİRLİKTE geliyor (tek istek): harita hattı ve
  // durak listesi ikisini de aynı cevaptan kuruyor, iki istek arasında
  // sıranın değişmesi ihtimali de kalmıyor.
  const [guzergahlar, setGuzergahlar] = useState([])
  const [durakTaslak, setDurakTaslak] = useState(null)   // çizildi, kaydedilmedi
  const [durakForm, setDurakForm] = useState(BOS_DURAK_FORMU)
  const [durakKaydediliyor, setDurakKaydediliyor] = useState(false)
  const [ulasimGorunur, setUlasimGorunur] = useState(true)

  // Ödev 17: "Katman kontrolü gibi güzergahlar üzerinde de aç/kapat
  // yapılabilsin."
  //
  // Kapalı olanları tutuyoruz, açık olanları DEĞİL. Sebep: yeni eklenen bir
  // güzergah varsayılan olarak GÖRÜNÜR olmalı. Açıkları tutsaydık, sunucuya
  // yeni giren her hat listede olmadığı için gizli başlar ve kullanıcı
  // eklediği hattı haritada bulamazdı.
  const [gizliGuzergahlar, setGizliGuzergahlar] = useState(() => new Set())

  const guzergahGorunurluguDegistir = useCallback((id) => {
    setGizliGuzergahlar((onceki) => {
      const yeni = new Set(onceki)
      if (yeni.has(id)) yeni.delete(id)
      else yeni.add(id)
      return yeni
    })
  }, [])

  // Ödev 13 / Madde 3: resmî tatil takvimi (backend'den, içinde bulunulan yıl).
  // { yil, tatiller: [{ tarih, ad, yarimGun }], diniBayramlarTanimli }
  const [resmiTatiller, setResmiTatiller] = useState(null)

  // Ödev 13 / Madde 4: seçilen yerin adı/türü çözülüyor mu?
  // Ters coğrafi kodlama ağdan geliyor, form bu sırada "çözülüyor…" diyor.
  const [yerCozuluyor, setYerCozuluyor] = useState(false)

  // Ödev 13 / Madde 1: POI'ler GeoServer'ın kategori stilleriyle mi çiziliyor?
  // true iken vektör katmanı görünmez bir tıklama hedefine dönüşüyor
  // (bkz. poiVurusStili).
  const poiWmsKatmanRef = useRef(null)
  const [poiWmsAktif, setPoiWmsAktif] = useState(false)

  // POI stillerinin tanımları — kategori tablosundan türüyor.
  // [{ stil, kategoriId, ad, tamYol, renk, sekil }]
  //
  // TEK KAYNAK, İKİ İŞ: WMS isteğinin STYLES parametresi ve paneldeki lejant.
  // Renkleri arayüzde sabit tutsaydık SLD ile eşleşmeleri el emeğine kalırdı;
  // yönetici yeni kategori eklediğinde lejant onu hiç bilmezdi.
  const [poiStilleri, setPoiStilleri] = useState([])

  // Ödev 7: kullanıcının çalışma alanı — { kisitli, alanlar: [{ id, name, wkt, kaynak }] }
  // Haritada sınır olarak çiziliyor ve panelde özetleniyor.
  const [calismaAlani, setCalismaAlani] = useState({ kisitli: false, alanlar: [] })

  // Ödev 8: veri kaynağının durumu — { etkin, ayakta, baseUrl, workspace, katmanlar }
  // null iken henüz okunmadı demektir; panel o sırada rozet göstermiyor.
  const [geoDurum, setGeoDurum] = useState(null)
  // Ödev 9 / Madde 1: "genel gösterimlerde WMS". Bu yüzden WMS katmanı
  // varsayılan olarak AÇIK; vektör (WFS) katmanları tıklama, düzenleme ve
  // vurgulama için duruyor.
  const [wmsAcik, setWmsAcik] = useState(true)

  // Ödev 9 / Madde 2: "Isı Haritası Analizi" açık mı?
  const [isiAcik, setIsiAcik] = useState(false)
  // Ödev 11 — termometre. İKİ ayrı ölçüm tutuluyor:
  //   sabitOlcum → tıklanarak SABİTLENEN nokta; haritada işaretçisi durur
  //   anlikOlcum → imlecin ALTINDAKİ değer; gezdikçe canlı değişir
  // Ekranda öncelik anlık olanda: fare haritadayken parmağını gezdirdiğin
  // yerin değerini görüyorsun, haritadan çıkınca sabitlediğin ölçüme dönüyor.
  // Tek state ile yapılsaydı, gezinmek sabitlenen ölçümü silerdi.
  const [sabitOlcum, setSabitOlcum] = useState(null)
  const [anlikOlcum, setAnlikOlcum] = useState(null)
  // Harita nesnesi kuruldu mu? WMS katmanı haritaya SONRADAN ekleniyor
  // (durum bilgisi ağdan geldiğinde), o yüzden "harita hazır mı?" sorusunun
  // bir state ile takip edilmesi gerekiyor — ref değişimi render tetiklemez.
  const [haritaHazir, setHaritaHazir] = useState(false)

  // --- Arama (Ödev 13 / Madde 2) ---
  //
  // Ödev 12'de burası yalnızca Nominatim'e soran bir "yer arama" kutusuydu.
  // Ödev 13 onu Google Maps benzeri bir ARAMA BARINA çevirdi: aynı kutu iki
  // kaynağı birden sorguluyor ve sonuçları iki grupta gösteriyor.
  //
  //   poiSonuclari → KENDİ POI'lerimiz (/api/poi/ara). Sonuca tıklayınca
  //                  harita o noktaya uçuyor ve bilgi paneli açılıyor.
  //   sonuclar     → Nominatim'den gelen gerçek dünya yerleri. Bunlar
  //                  haritada kayıtlı değil; yeni bir kayıt/POI oluşturmanın
  //                  başlangıç noktası oluyorlar.
  //
  // Neden tek kutu? Kullanıcı "Millî Kütüphane" yazarken onun kayıtlı bir POI
  // mi yoksa haritadaki bir yer mi olduğunu bilmek zorunda değil. İki ayrı
  // kutu, bu ayrımı bilme yükünü kullanıcıya bindirirdi.
  const [arama, setArama] = useState('')
  const [sonuclar, setSonuclar] = useState([])
  const [poiSonuclari, setPoiSonuclari] = useState([])
  const [araniyor, setAraniyor] = useState(false)
  const [aramaHatasi, setAramaHatasi] = useState(null)
  // Sonuç listesi açık mı? Odak kaybolunca kapanıyor; arama metni duruyor ki
  // kullanıcı ne aradığını görsün.
  const [aramaAcik, setAramaAcik] = useState(false)

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

  // Yetki matrisini bir kez çekip "Yönetim" bağlantısını göstereceğimize karar veriyoruz.
  // Hata durumunda sessiz kalıyoruz: yetki okunamadıysa bağlantıyı göstermemek
  // doğru davranış — kullanıcıya "yetkini öğrenemedim" uyarısı vermenin faydası yok.
  useEffect(() => {
    let iptalEdildi = false

    kendiYetkilerim(goLogin)
      .then((matris) => {
        if (iptalEdildi) return
        // granted = rolden VEYA doğrudan geliyor; ikisinin birleşimi.
        setYetkilerim(matris.permissions.filter((y) => y.granted).map((y) => y.name))
      })
      .catch(() => { /* yetki okunamadı → bağlantı gizli kalır */ })

    // Ödev 7: çizim yapabileceğim alan. Kısıt yoksa kisitli=false gelir.
    calismaAlanim(goLogin)
      .then((alan) => { if (!iptalEdildi) setCalismaAlani(alan) })
      .catch(() => { /* okunamadıysa sınır çizilmez; sunucu yine de kuralı uygular */ })

    // Ödev 8: veri kaynağı GeoServer mı, ayakta mı? Hata durumunda sessiz
    // kalıyoruz — bu bilgi bir SÜS; kayıt listesi zaten kendi hatasını gösterir.
    geoServerDurumu(goLogin)
      .then((durum) => { if (!iptalEdildi) setGeoDurum(durum) })
      .catch(() => { /* durum okunamadı → rozet ve WMS düğmesi çıkmaz */ })

    // Temizlik: bileşen sökülmüşken setState çağırmayalım (React uyarısı verir).
    return () => { iptalEdildi = true }
  }, [goLogin])

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
  /**
   * Verilen harita koordinatı izinli alanların içinde mi? (Ödev 11)
   * Kısıt tanımlanmamışsa her yer serbest — Ödev 7'deki kuralın aynısı.
   */
  const izinliMi = useCallback((koordinat) => {
    const geometriler = izinliGeometriRef.current
    if (!geometriler || geometriler.length === 0) return true

    // intersectsCoordinate hem Polygon hem MultiPolygon üzerinde çalışıyor;
    // bölge seçimiyle tanımlanan çok parçalı alanlar için şart.
    return geometriler.some((g) => g.intersectsCoordinate(koordinat))
  }, [])

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

  /**
   * Verilen WKT poligonuyla kesişen envanterleri sayar (Ödev 4 / Görev 3).
   * Hesabı PostGIS yapıyor; burada sadece isteği atıp sonucu gösteriyoruz.
   */
  const analizCalistir = useCallback(async (wkt, haricTutulanId) => {
    setAnalizYukleniyor(true)
    try {
      const sonuc = await kesisimAnalizi(wkt, haricTutulanId, goLogin)
      setAnalizSonuc(sonuc)

      // Bulunanları HARİTADA da göster: sadece sayı vermek "hangileri?"
      // sorusunu cevapsız bırakıyordu.
      const vurgu = analizBulunanRef.current
      if (vurgu) {
        vurgu.clear()
        sonuc.items.forEach((item) => {
          // Analiz sonucu WKT taşıyor; onu haritanın projeksiyonuna çevirip
          // vurgu katmanına klon olarak koyuyoruz (orijinaller kendi
          // katmanlarında kalsın, iki katmana birden feature konulamaz).
          const feature = wktToFeature(item.wkt)
          feature.setId(`analiz-${item.geometryType}-${item.id}`)
          vurgu.addFeature(feature)
        })
      }

      return sonuc
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
      return null
    } finally {
      setAnalizYukleniyor(false)
    }
  }, [goLogin, bildir])

  /** Analiz poligonunu, bulunan vurgularını ve sonucu haritadan kaldırır. */
  const analizTemizle = useCallback(() => {
    analizSourceRef.current?.clear()
    analizBulunanRef.current?.clear()
    setAnalizSonuc(null)
  }, [])

  /** Analiz listesindeki bir kayda tıklanınca haritayı ona götür. */
  const analizOgesineGit = useCallback((item) => {
    const feature = analizBulunanRef.current?.getFeatureById(
      `analiz-${item.geometryType}-${item.id}`,
    )
    odaklanFeature(feature)
  }, [odaklanFeature])

  // ------------------------------------------------------------------------
  //  Ödev 14 — KONUM ANALİZİ
  //
  //  Panel kriterleri ve alanı topluyor; buradaki üç fonksiyon sonucun
  //  HARİTADAKİ karşılığını yönetiyor: uygunluk yüzeyi, alan sınırı,
  //  aday madalyonları.
  // ------------------------------------------------------------------------

  /** Analizin haritadaki bütün izlerini siler. */
  const konumAnaliziTemizle = useCallback(() => {
    konumAlanKaynagiRef.current?.clear()
    konumAdayKaynagiRef.current?.clear()

    // Katmanı haritadan ÇIKARMIYORUZ, yalnızca kaynağını boşaltıp
    // gizliyoruz — katman sırası bozulmasın (bkz. katman kurulumu).
    konumIsiKatmanRef.current?.setSource(null)
    konumIsiKatmanRef.current?.setVisible(false)

    setKonumSonuc(null)
    setKonumHata(null)
  }, [])

  /**
   * Analizi çalıştırır ve sonucu haritaya basar.
   *
   * @param {{ilPlakalari: number[]|null, wkt: string|null,
   *          kriterler: {kategoriId: number, agirlik: number}[]}} istek
   */
  const konumAnaliziCalistir = useCallback(async (istek) => {
    setKonumYukleniyor(true)
    setKonumHata(null)

    try {
      const sonuc = await konumAnalizi(istek, goLogin)
      setKonumSonuc(sonuc)

      // ---- 1) Uygunluk yüzeyi ----
      const kaynak = izgaraKaynagiOlustur(sonuc.izgara)
      konumIsiKatmanRef.current?.setSource(kaynak)
      konumIsiKatmanRef.current?.setVisible(Boolean(kaynak))

      // ---- 2) Analiz alanının sınırı ----
      // Sunucudan dönen WKT kullanılıyor, istemcinin gönderdiği değil:
      // il seçiminde alan SUNUCUDA birleştiriliyor, ekranda görünen sınır
      // gerçekten analizin çalıştığı sınır olsun.
      const alanKaynagi = konumAlanKaynagiRef.current
      alanKaynagi?.clear()

      let alanFeature = null
      if (sonuc.alanWkt) {
        alanFeature = wktToFeature(sonuc.alanWkt)
        alanKaynagi?.addFeature(alanFeature)
      }

      // ---- 3) Aday konumlar ----
      const adayKaynagi = konumAdayKaynagiRef.current
      adayKaynagi?.clear()

      const enYuksek = sonuc.izgara.enYuksekSkor || 1
      sonuc.adaylar.forEach((aday) => {
        const feature = new Feature({
          geometry: new PointGeom(fromLonLat([aday.boylam, aday.enlem])),
        })
        feature.setId(`konum-aday-${aday.sira}`)
        feature.set('sira', aday.sira)
        // Skor 0–100 ölçeğinde geliyor; rampa 0–enYuksekSkor'a yayıldığı için
        // önce 0–1'e, sonra rampanın oranına çeviriyoruz (panelle aynı hesap).
        feature.set('renk', uygunlukRengiCss((aday.skor / 100) / enYuksek))
        adayKaynagi?.addFeature(feature)
      })

      // ---- 4) Haritayı alana getir ----
      if (alanFeature) {
        mapRef.current?.getView().fit(alanFeature.getGeometry().getExtent(), {
          padding: [70, 70, 70, 70],
          duration: 500,
          easing: easeOut,
        })
      }

      return sonuc
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') {
        // Hata PANELDE gösteriliyor, alt bildirimde değil: kullanıcının
        // düzeltmesi gereken şey (puan toplamı, alan seçimi) panelin
        // içinde ve mesajın oraya yakın durması gerekiyor.
        setKonumHata(err.message)
      }
      return null
    } finally {
      setKonumYukleniyor(false)
    }
  }, [goLogin])

  // Panele geçen iki geri çağrı SABİT kimlikte olmalı: panelin "il sekmesine
  // dönüldüyse çizim aracını kapat" etkisi bunları bağımlılık olarak
  // izliyor. Satır içi ok fonksiyonu verseydik kimlik her render'da
  // değişir, etki her render'da yeniden koşardı.
  const konumAlaniCizmeyeBasla = useCallback(() => aracSecRef.current(KONUM_ALAN), [])
  const konumAlaniCizmeyiBirak = useCallback(() => setActiveTool(null), [])

  /** Panelde bir aday konuma tıklanınca haritayı oraya götür. */
  const konumAdayinaGit = useCallback((aday) => {
    const view = mapRef.current?.getView()
    if (!view) return

    view.animate({
      center: fromLonLat([aday.boylam, aday.enlem]),
      // 12 ≈ mahalle ölçeği: adayın çevresindeki POI'ler ve sokaklar görünsün
      // ama yüzeyin bağlamı da kaybolmasın.
      zoom: Math.max(view.getZoom() ?? 0, 12),
      duration: 600,
      easing: easeOut,
    })
  }, [])

  const popupKapat = useCallback(() => {
    setSecili(null)
    setDuzenleForm(null)      // düzenleme modu açıksa o da kapansın
    setPoiDuzenle(null)
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
  /**
   * Açılış sahnesini oynatır: harita uzaya çekilir, gezegen diski belirir,
   * sonra Türkiye'ye iniş yapar.
   *
   * Hem sayfa ilk açıldığında (bayrakYaz = true) hem de "Dünya" düğmesinden
   * (bayrakYaz = false) çağrılıyor. Tek fonksiyon olduğu için iki yol da
   * birebir aynı davranıyor.
   *
   * @param {boolean} bayrakYaz Animasyon tamamlanınca "bu oturumda oynatıldı"
   *   bayrağını yaksın mı? Sadece otomatik açılışta true.
   */
  /**
   * Açılış sahnesi oynarken VERİ KATMANLARI gizleniyor.
   *
   * NEDEN? Sahnenin anlattığı şey "uzaydan dünyaya iniş". Gezegenin üstünde
   * duran kayıt işaretleri, POI simgeleri ve hat çizgileri o anlatıyı bozuyor:
   * uzaydan bakan biri onları görmez ve daha somut olarak, 42vmin'lik disk
   * içinde üst üste binmiş yüzlerce simge gezegeni lekeli gösteriyordu.
   *
   * Katmanları AYRI bir yerden gizlemiyoruz — her katmanın görünürlüğünü zaten
   * yöneten effect'lere bu koşulu EKLİYORUZ. Ayrı bir "gizle/geri getir" adımı
   * yazsaydık, sahne bitince kullanıcının kendi kapattığı bir katmanı yanlışlıkla
   * geri açardık; burada tek doğruluk kaynağı korunuyor.
   */
  const sahneGizliyor = uzaySahnesi !== null

  const sahneyiOynat = useCallback((bayrakYaz = false) => {
    const map = mapRef.current
    if (!map) return

    const view = map.getView()
    const viewport = map.getViewport()

    // Önceki sahnenin zamanlayıcısı/dinleyicileri kalmışsa temizle.
    // Bu olmadan düğmeye üst üste basmak birden fazla sahne başlatırdı.
    if (sahneTemizleRef.current) sahneTemizleRef.current()

    popupKapat()
    view.cancelAnimations()
    view.setCenter(fromLonLat(TURKEY_CENTER))
    view.setZoom(UZAY_ZOOM)          // haritayı uzaya çek
    setUzaySahnesi('kure')           // gezegen diski sahneye girer

    const bitir = (tamamlandi) => {
      if (sahneTemizleRef.current) sahneTemizleRef.current()
      if (tamamlandi && bayrakYaz) sessionStorage.setItem(GIRIS_ANAHTARI, '1')
      setUzaySahnesi(null)
    }

    // Kullanıcı beklemek istemiyorsa ilk dokunuşta sahneyi kes.
    const atla = () => {
      view.cancelAnimations()
      view.setZoom(TURKEY_ZOOM)
      bitir(false)
    }
    viewport.addEventListener('pointerdown', atla)
    viewport.addEventListener('wheel', atla, { passive: true })

    // Küre önce kısa bir süre sahnede dursun — göz onu "gezegen" olarak
    // okumaya fırsat bulsun. Hemen zoomlarsak sadece bir geçiş efekti gibi durur.
    const zamanlayici = setTimeout(() => {
      setUzaySahnesi('inis')   // CSS: daire büyümeye ve sahne solmaya başlar

      // Aynı anda OpenLayers zoom animasyonu. İkisi bağımsız çalışır ama
      // eşzamanlı başladıkları için tek bir hareket gibi algılanır.
      view.animate(
        { zoom: TURKEY_ZOOM, duration: INIS_SURESI, easing: easeOut },
        bitir,
      )
    }, KURE_BEKLEME)

    // ⚠️ Dinleyicileri sahne bitince MUTLAKA kaldırıyoruz. Kalsalardı, kullanıcı
    // animasyondan çok sonra haritaya tıkladığında "atla" tetiklenir ve zoom
    // aniden Türkiye seviyesine geri dönerdi.
    sahneTemizleRef.current = () => {
      clearTimeout(zamanlayici)
      viewport.removeEventListener('pointerdown', atla)
      viewport.removeEventListener('wheel', atla)
      sahneTemizleRef.current = null
    }
  }, [popupKapat])

  const yukle = useCallback(async () => {
    setKayitlarYukleniyor(true)
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
          feature.set('renk', dto.color)      // stil fonksiyonu rengi buradan okuyor
          source.addFeature(feature)
        })
      })

      setRecords(gelen)

      // WMS karoları sunucuda boyanmış RESİMLERDİR; veri değişince kendiliğinden
      // güncellenmezler, tarayıcıdaki karo önbelleği eski resmi tutar.
      // refresh() önbelleği boşaltıp karoları yeniden istetir.
      wmsKaynakRef.current?.refresh()
      isiKaynakRef.current?.refresh()
    } catch (err) {
      // 401 ise authFetch zaten login'e yönlendirdi; diğer hataları gösteriyoruz.
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    } finally {
      setKayitlarYukleniyor(false)
    }
  }, [goLogin, bildir])

  /**
   * POI'leri ve kategori sözlüğünü çeker, haritaya basar (Ödev 12).
   *
   * Geometri kayıtlarından AYRI bir yükleme: farklı uç, farklı katman ve
   * farklı bir sözleşme (POI'de renk/görsel yok, kategori ve mesai var).
   * yukle() içine sıkıştırsaydık iki ilgisiz cevabı tek try/catch paylaşırdı;
   * kategori listesi alınamadığında harita kayıtları da gösterilemez olurdu.
   */
  const poileriYukle = useCallback(async () => {
    try {
      const gelen = await poileriListele(goLogin)

      const kaynak = poiKaynagiRef.current
      if (kaynak) {
        kaynak.clear()
        gelen.forEach((dto) => {
          const feature = wktToFeature(dto.wkt)
          feature.setId(`poi-${dto.id}`)
          feature.set('ad', dto.isim)          // stil fonksiyonu etiketi buradan okuyor
          feature.set('aktif', dto.isActive)
          // Ödev 15: stil fonksiyonu simgeyi kategoriye göre seçiyor.
          // dto'nun tamamı zaten feature'da duruyor ama stil her karede
          // çağrılıyor; tek bir sayıyı doğrudan okumak, her seferinde
          // nesnenin içine inmekten ucuz ve niyeti de açık yazıyor.
          feature.set('kategoriId', dto.kategoriId)
          feature.set('dto', dto)
          feature.set('tip', POI)              // popup hangi kartı çizeceğini buradan biliyor
          kaynak.addFeature(feature)
        })
      }

      setPoiler(gelen)
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    }
  }, [goLogin, bildir])

  /**
   * Ödev 16 — güzergahları ve durakları yükleyip haritaya basar.
   *
   * TEK İSTEK: /api/ulasim/guzergahlar cevabı durakları SIRALI hâlde
   * içinde taşıyor. Durakları ayrı istemek iki cevap arasında sıranın
   * değişmesine açık olurdu ve hat çizgisi yanlış sırayla çizilebilirdi.
   *
   * İkİ KAYNAK DOLDURULUYOR:
   *   durakKaynagi     → nokta feature'ları (tıklanabilir, bilgi kutucuğu)
   *   guzergahKaynagi  → duraklardan TÜRETİLEN çizgi (tıklanamaz, sadece hat)
   *
   * Çizgi yalnızca İKİ VE DAHA FAZLA durağı olan hatlar için çiziliyor:
   * tek noktalı bir LineString geçersiz geometridir ve OpenLayers onu
   * çizmeye çalışırken konsola hata basar.
   */
  const ulasimiYukle = useCallback(async () => {
    try {
      const gelen = await guzergahlariListele(goLogin)

      const durakKaynagi = durakKaynagiRef.current
      const guzergahKaynagi = guzergahKaynagiRef.current

      if (durakKaynagi && guzergahKaynagi) {
        durakKaynagi.clear()
        guzergahKaynagi.clear()

        gelen.forEach((guzergah) => {
          const koordinatlar = []

          guzergah.duraklar.forEach((durak) => {
            const feature = wktToFeature(durak.wkt)
            feature.setId(`durak-${durak.id}`)
            feature.set('renk', guzergah.renk)     // stil rengi buradan okuyor
            feature.set('sira', durak.sira)        // simgenin içindeki numara
            feature.set('aktif', durak.isActive)
            feature.set('guzergahId', guzergah.id)   // hat bazlı aç/kapat için
            feature.set('dto', durak)
            feature.set('tip', DURAK)              // popup hangi kartı çizecek
            durakKaynagi.addFeature(feature)

            koordinatlar.push(feature.getGeometry().getCoordinates())
          })

          // ---- Hattın çizgisi (Ödev 17) ----
          //
          // ÖNCELİK SIRASI:
          //   1. OSRM rotası varsa O çiziliyor — yollara oturmuş gerçek
          //      güzergah, yön oklarıyla birlikte.
          //   2. Yoksa Ödev 16'daki düz çizgiye düşülüyor (kesikli çizilerek
          //      "bu kuş uçuşu" mesajı veriliyor).
          //
          // Düz çizgiyi tamamen kaldırmadık: OSRM kurulmamış bir makinede
          // harita hattı hiç göstermezdi ve modül çalışmıyor gibi görünürdü.
          const rotaCizgisi = guzergah.rotaWkt
            ? wktToFeature(guzergah.rotaWkt).getGeometry()
            : null

          const hatGeom = rotaCizgisi
            ?? (koordinatlar.length >= 2 ? new LineStringGeom(koordinatlar) : null)

          if (hatGeom) {
            const hat = new Feature({ geometry: hatGeom })
            hat.setId(`guzergah-${guzergah.id}`)
            hat.set('renk', guzergah.renk)
            hat.set('guzergahId', guzergah.id)          // hat bazlı aç/kapat için
            hat.set('rotaVar', Boolean(rotaCizgisi))    // düz mü, gerçek rota mı
            hat.set('rotaGuncel', guzergah.rotaGuncel !== false)
            guzergahKaynagi.addFeature(hat)
          }
        })
      }

      setGuzergahlar(gelen)
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    }
  }, [goLogin, bildir])

  /**
   * Kategori sözlüğü — POI formundaki açılır liste.
   * Ayrı çekiliyor çünkü oturum boyunca neredeyse hiç değişmiyor; her POI
   * kaydından sonra yeniden indirmenin anlamı yok.
   */
  const poiKategorileriniYukle = useCallback(async () => {
    try {
      setPoiKategorileri(await poiKategorileriniListele(goLogin))
    } catch {
      /* okunamadıysa açılır liste boş kalır; form "kategori seçin" diyerek durur */
    }
  }, [goLogin])

  /**
   * Resmî tatil takvimi (Ödev 13 / Madde 3).
   *
   * Yılda bir değişen bir liste; oturum başına bir kez okunuyor. Form
   * "resmî tatillerde kapalı" işaretliyken hangi günlerin kapalı olacağını
   * bu listeden gösteriyor.
   *
   * Okunamazsa sessizce geçiyoruz: mesai planı yine kaydedilebilir, sadece
   * tatil listesi görünmez. Tatil listesinin yokluğu POI eklemeyi
   * engellememeli.
   */
  /**
   * POI stil tanımlarını çeker (Ödev 13 iyileştirmesi).
   *
   * GeoServer'a GİTMİYOR: kategori tablosundan türüyor. Bu yüzden GeoServer
   * kapalı olsa bile lejant doğru kalıyor — yalnızca haritadaki WMS katmanı
   * eksik olur.
   */
  const poiStilleriniYukle = useCallback(async () => {
    try {
      setPoiStilleri(await poiStilleriniGetir(goLogin))
    } catch {
      /* stil listesi alınamadı: POI'ler vektör katmanıyla çizilmeye devam eder */
    }
  }, [goLogin])

  const resmiTatilleriYukle = useCallback(async () => {
    try {
      setResmiTatiller(await resmiTatilleriGetir(null, goLogin))
    } catch {
      /* liste gösterilemez; plan kaydı etkilenmiyor */
    }
  }, [goLogin])

  /**
   * Kayıt üzerinde işlem yapan uçların (güncelle / sil / aktiflik) ortak hata yolu.
   *
   * 404'ü ayrı ele alıyoruz. Bu ekran açıldığında kayıtları BİR KEZ çekiyor ve
   * her feature'a o anki dto'yu iliştiriyor. Sekme açık dururken veritabanı
   * değişirse (kayıt başka bir yerden silinirse, veritabanı sıfırlanıp yeniden
   * seed edilirse) elimizdeki id'ler artık karşılıksızdır; sunucu haklı olarak
   * "Id=… olan kayıt bulunamadı" der. Kullanıcı için bu mesaj tek başına
   * anlamsızdır — hangi id? neden yok? O yüzden burada:
   *   1) ne olduğunu düz Türkçe söylüyoruz,
   *   2) listeyi tazeleyip ekranı veritabanıyla yeniden aynı hizaya getiriyoruz.
   * Böylece ekran "sayfayı yenile" demeye gerek kalmadan kendini toparlıyor.
   */
  const hatayiGoster = useCallback(async (err) => {
    if (err.message === 'Oturum süresi doldu') return   // authFetch zaten login'e attı

    if (err.status === 404) {
      popupKapat()
      await yukle()
      bildir('hata', 'Bu kayıt sunucuda bulunamadı. Ekrandaki liste eskimişti, yenilendi.')
      return
    }

    bildir('hata', err.message)
  }, [bildir, popupKapat, yukle])

  /**
   * Sürüklenerek değiştirilen bir geometriyi sunucuya yazar.
   * Ad/açıklama/görsel aynı kalır; sadece WKT yenilenir.
   */
  const geometriGuncelle = useCallback(async (feature) => {
    const dto = feature.get('dto')
    const tip = feature.get('tip')
    if (!dto) return

    try {
      const yeni = await guncelle(
        DRAW_TYPES[tip].endpoint,
        dto.id,
        {
          name: dto.name,
          description: dto.description,
          imageUrl: dto.imageUrl,
          wkt: geometryToWkt(feature.getGeometry()),   // 3857 → 4326
        },
        goLogin,
      )
      // Feature'a iliştirdiğimiz kaydı da tazele ki popup güncel WKT'yi göstersin.
      feature.set('dto', yeni)
      setRecords((onceki) => ({
        ...onceki,
        [tip]: onceki[tip].map((k) => (k.id === yeni.id ? yeni : k)),
      }))
      bildir('ok', `"${dto.name}" güncellendi.`)
    } catch (err) {
      await hatayiGoster(err)
    }
  }, [goLogin, bildir, hatayiGoster])

  // Çalışma alanı sınırını haritaya çiz (Ödev 7) + dışını SÖNDÜR (Ödev 11).
  //
  // Ödev 7'de yalnızca sınır çiziliyordu; kullanıcı dışarı çizmeye çalışıp
  // sunucudan hata alıyordu. Artık izin verilmeyen bölge görsel olarak da
  // kapalı: üstüne yarı saydam bir maske biniyor ve çizim aracı oradaki
  // tıklamaları hiç kabul etmiyor. Sunucudaki kontrol son savunma hattı
  // olarak duruyor — arayüz nezaket, kural sunucuda.
  useEffect(() => {
    const kaynak = izinliAlanRef.current
    const maskeKaynagi = maskeKaynagiRef.current
    if (!kaynak || !maskeKaynagi) return

    kaynak.clear()
    maskeKaynagi.clear()
    izinliGeometriRef.current = null

    if (calismaAlani.alanlar.length === 0) return

    const geometriler = calismaAlani.alanlar.map((alan) => {
      const feature = wktToFeature(alan.wkt)
      kaynak.addFeature(feature)
      return feature.getGeometry()
    })

    izinliGeometriRef.current = geometriler

    // Dünyayı kaplayan tek bir poligon; izinli alanlar içine DELİK olarak
    // açılıyor. "Dışarısı" poligonlarını tek tek hesaplamaktan çok daha ucuz.
    maskeKaynagi.addFeature(new Feature(
      new PolygonGeom([DUNYA_HALKASI, ...geometriler.flatMap(halkalariCikar)]),
    ))
  }, [calismaAlani])

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

    // Analiz poligonu kendi kaynağında: kayıtlı katmanlarla karışmasın,
    // "Temizle" dendiğinde tek clear() ile silinsin.
    const analizSource = new VectorSource()
    analizSourceRef.current = analizSource

    const analizBulunanSource = new VectorSource()
    analizBulunanRef.current = analizBulunanSource

    // Ödev 14 — konum analizi katmanları.
    // Üçü de BAŞTAN kuruluyor (sonuç gelince değil): katman sırası bir kez
    // belirlensin, sonradan eklenen katmanın dizinin sonuna düşüp yanlış
    // yere girmesi sorunu yaşanmasın (Ödev 9'da tam olarak bu olmuştu).
    const konumAlanSource = new VectorSource()
    konumAlanKaynagiRef.current = konumAlanSource

    const konumAdaySource = new VectorSource()
    konumAdayKaynagiRef.current = konumAdaySource

    const konumIsiKatmani = uygunlukKatmaniOlustur()
    konumIsiKatmanRef.current = konumIsiKatmani

    // Ödev 16: ulaşım. İki ayrı kaynak çünkü ikisi farklı şeyler:
    // duraklar TIKLANABİLİR noktalar, güzergah çizgisi ise onlardan
    // türeyen bir GÖRÜNÜM — tıklama mantığına hiç girmemeli.
    const durakSource = new VectorSource()
    durakKaynagiRef.current = durakSource

    const guzergahSource = new VectorSource()
    guzergahKaynagiRef.current = guzergahSource

    // Ödev 12: POI katmanı. Üç çizim katmanından AYRI çünkü ayrı bir tablo,
    // ayrı bir uç ve ayrı bir görünüm. Aynı kaynağa koysaydık "bu nokta
    // çizim mi POI mi?" sorusu her tıklamada yeniden sorulurdu.
    const poiSource = new VectorSource()
    poiKaynagiRef.current = poiSource

    // Coğrafi yetki sınırı (Ödev 7). Ayrı kaynak: kullanıcının kayıtlarıyla
    // karışmasın, tıklama/analiz mantığına hiç girmesin — bu bir VERİ değil KURAL.
    const izinliAlanSource = new VectorSource()
    izinliAlanRef.current = izinliAlanSource

    // Ödev 11: izinli alan dışını söndüren maske
    const maskeSource = new VectorSource()
    maskeKaynagiRef.current = maskeSource

    // Ödev 11: sabitlenen termometre ölçümünün işaretçisi
    const olcumSource = new VectorSource()
    olcumKaynagiRef.current = olcumSource

    const poiKatmani = new VectorLayer({
      source: poiSource,
      style: poiStili(poiStilleri, poiKategoriSecimi),
      declutter: DECLUTTER_GRUBU,
    })
    poiKatmanRef.current = poiKatmani

    // Açılış sahnesi oynatılsın mı? Karar auth.js'te (login ekranı da aynı
    // bayrağı kullanıyor: giriş yapılınca sıfırlanıyor ki sahne mutlaka oynasın).
    const girisOynat = girisAnimasyonuOynasinMi()

    const view = new View({
      // Merkez sahne boyunca sabit: kaydırma yok, sadece zoom.
      // Sahne oynayacaksa zoom'u sahneyiOynat() zaten uzaya çekecek.
      center: fromLonLat(TURKEY_CENTER),                  // 4326 → 3857
      zoom: TURKEY_ZOOM,
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
        // POI en üstte: kategorili, isimli bir kayıt; serbest çizimlerin
        // altında kalıp kaybolmaması gerekiyor. Etiketleri de aynı declutter
        // grubunda, böylece POI adı bir çizim etiketinin üstüne binmiyor.
        poiKatmani,
        // Ödev 16: önce HAT (altta), sonra DURAKLAR (üstte). Ters sırada
        // kalın çizgi durak simgelerinin üzerinden geçer ve numaraları örterdi.
        new VectorLayer({ source: guzergahSource, style: guzergahStili(), zIndex: 320 }),
        (durakKatmanRef.current = new VectorLayer({
          source: durakSource,
          style: durakStili(),
          // Durak numaraları POI adlarıyla aynı çakışma grubunda: bir durak
          // simgesinin üzerine POI adı binmesin.
          declutter: DECLUTTER_GRUBU,
          zIndex: 330,
        })),
        new VectorLayer({ source: drawSource, style: taslakStili }),
        // Bulunanlar analiz poligonunun ALTINDA: poligonun kesikli kenarı üstte kalsın
        new VectorLayer({ source: analizBulunanSource, style: analizBulunanStili }),
        new VectorLayer({ source: analizSource, style: analizStili }),
        // Ödev 14: uygunluk yüzeyi (zIndex 450, katmanın kendi tanımında),
        // üstünde analiz alanının sınırı ve aday madalyonları.
        konumIsiKatmani,
        new VectorLayer({ source: konumAlanSource, style: konumAlanStili, zIndex: 460 }),
        new VectorLayer({ source: konumAdaySource, style: konumAdayStili, zIndex: 470 }),
        new VectorLayer({ source: highlightSource, style: vurguStili }),
        // Sınır EN ÜSTTE: altındaki kayıtlar onu kapatmasın.
        // Maske EN ÜSTE yakın: altındaki her şeyi (temel harita, WMS, kayıtlar)
        // söndürsün. Sınır çizgisi onun da üstünde kalıyor ki kenar net görünsün.
        //
        // zIndex ŞART: ısı haritası katmanı haritaya SONRADAN (durum bilgisi
        // ağdan gelince) ekleniyor ve dizinin sonuna yazılıyordu — maskenin
        // ÜSTÜNE çıkıyor, ısı haritası açıkken kapalı bölge sönük görünmüyordu.
        // Sıralamayı diziye değil zIndex'e bağlayınca sonradan eklenen katman
        // araya doğru yerde giriyor (ısı: 500, maske: 900).
        new VectorLayer({ source: maskeSource, style: maskeStili, zIndex: 900 }),
        new VectorLayer({ source: izinliAlanSource, style: izinliAlanStili, zIndex: 910 }),
        // Ölçüm işaretçisi en üstte: maske onu söndürmesin.
        new VectorLayer({ source: olcumSource, style: olcumStili, zIndex: 920 }),
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

    // --- Uzaydan Türkiye'ye iniş (ilk açılış) ---
    if (girisOynat) sahneyiOynat(true)

    // Ödev 8: WMS katmanını ekleyecek effect bu bayrağı bekliyor.
    setHaritaHazir(true)

    yukle()

    // Ödev 12: POI'ler ve kategori sözlüğü. Burada çağrılıyor çünkü POI
    // katmanı ancak bu effect'te var oluyor; daha erken çağırsaydık cevap
    // gelene kadar basılacak bir katman olmazdı.
    poileriYukle()
    // Ödev 16: güzergahlar ve duraklar. Yetki İSTEMİYOR — "Ulaşım
    // Kullanıcısı" rolünün hiç yetkisi yok ve tam da bu yüzden görüyor.
    ulasimiYukle()
    poiKategorileriniYukle()
    resmiTatilleriYukle()
    poiStilleriniYukle()

    // Temizlik: bileşen kaldırılınca haritayı DOM'dan ayır.
    // React StrictMode geliştirmede effect'i iki kez çalıştırır; bu satır
    // olmasaydı sayfada iki harita üst üste binerdi.
    return () => {
      if (sahneTemizleRef.current) sahneTemizleRef.current()
      view.cancelAnimations()   // bileşen kalkarken devam eden animasyon kalmasın
      map.setTarget(null)
      mapRef.current = null
      poiKatmanRef.current = null
      durakKatmanRef.current = null
      setHaritaHazir(false)
    }
  }, [yukle, sahneyiOynat, poileriYukle, ulasimiYukle, poiKategorileriniYukle,
      resmiTatilleriYukle, poiStilleriniYukle])

  // ------------------------------------------------------------------------
  //  Ödev 8: GeoServer WMS katmanı
  //
  //  Harita kurulumunun İÇİNDE değil, ayrı bir effect'te. Sebep: katman
  //  adlarını (staj:tbl_point ...) backend'den öğreniyoruz ve o cevap harita
  //  kurulduktan sonra geliyor. Kurulum effect'ini beklemeye zorlamak yerine
  //  katmanı hazır olduğunda haritaya ekliyoruz.
  //
  //  insertAt(1, ...) → temel harita (OSM) hemen üstüne, vektör katmanların
  //  ALTINA yerleştirir. Böylece ikisi birlikte açıkken vektörler görünür
  //  kalır; WMS'i tek başına görmek için vektör katmanları kapatmak yeter.
  // ------------------------------------------------------------------------
  useEffect(() => {
    const map = mapRef.current

    // etkin=false → okuma zaten GeoServer'dan yapılmıyor, WMS de anlamsız.
    // ayakta=false → sunucu kapalı, katman sadece boş karo üretirdi.
    if (!map || !haritaHazir || !geoDurum?.etkin || !geoDurum?.ayakta) return undefined

    const { katman, kaynak } = wmsKatmaniOlustur(geoDurum.katmanlar, goLogin)
    wmsKatmanRef.current = katman
    wmsKaynakRef.current = kaynak
    map.getLayers().insertAt(1, katman)

    return () => {
      map.removeLayer(katman)
      wmsKatmanRef.current = null
      wmsKaynakRef.current = null
    }
  }, [haritaHazir, geoDurum, goLogin])

  // Düğme ile katmanın görünürlüğünü eşitle. Ayrı effect: katman haritaya
  // sonradan eklendiği için "önce düğmeye basıldı, sonra katman geldi"
  // sırası da doğru çalışsın.
  useEffect(() => {
    wmsKatmanRef.current?.setVisible(wmsAcik && !sahneGizliyor)
  }, [wmsAcik, haritaHazir, geoDurum, sahneGizliyor])

  // ------------------------------------------------------------------------
  //  Ödev 13 / Madde 1: POI katmanı GeoServer'dan, KATEGORİ STİLLERİYLE
  //
  //  Ödev 12'de POI'ler tarayıcıda çizilen mor halkalardı: hepsi aynı
  //  görünüyordu ve kategori bilgisi yalnızca etiketin altında yazıyordu.
  //  Artık noktaları GeoServer boyuyor; her kategori kendi SLD'siyle farklı
  //  simge ve renk alıyor, adlar da belirli bir zoom'dan sonra çıkıyor.
  //
  //  Katman haritaya SONRADAN ekleniyor (durum bilgisi ağdan geldiğinde);
  //  sırasını diziye değil zIndex'e bağlıyoruz — ısı haritası effect'indeki
  //  gerekçenin aynısı.
  //
  //  GeoServer kapalıysa bu effect hiç çalışmıyor ve poiWmsAktif false
  //  kalıyor: vektör katmanı Ödev 12'deki mor görünümüne dönüyor, harita
  //  çalışmaya devam ediyor. Tek görüntüleme yolu bırakmak, GeoServer'ı
  //  uygulamanın çalışma şartı hâline getirirdi.
  // ------------------------------------------------------------------------
  useEffect(() => {
    const map = mapRef.current
    if (!map || !haritaHazir || !geoDurum?.etkin || !geoDurum?.ayakta) return undefined
    // Stil listesi kategori tablosundan geliyor; boşsa (henüz yüklenmedi ya da
    // hiç kategori yok) WMS katmanını kurmuyoruz — stilsiz bir istek
    // GeoServer'ın varsayılan stilini çizerdi ve kategori ayrımı kaybolurdu.
    if (!geoDurum.poiKatmani || poiStilleri.length === 0) return undefined

    const { katman } = poiKatmaniOlustur(
      geoDurum.poiKatmani, poiStilleri.map((s) => s.stil), goLogin)
    poiWmsKatmanRef.current = katman
    map.addLayer(katman)
    setPoiWmsAktif(true)

    return () => {
      map.removeLayer(katman)
      poiWmsKatmanRef.current = null
      setPoiWmsAktif(false)
    }
  }, [haritaHazir, geoDurum, poiStilleri, goLogin])

  /**
   * POI WMS katmanı — YALNIZCA SEÇİLİ KATEGORİLER çiziliyor.
   *
   * GeoServer isteği zaten kategori başına ayrı bir STYLES taşıyor (her
   * kategorinin kendi SLD'si var). Dolayısıyla süzme, listeden istenmeyen
   * stilleri ÇIKARMAKLA oluyor — sunucuya CQL süzgeci göndermeye gerek yok.
   *
   * Hiçbir kategori seçili değilse katman tamamen gizleniyor: boş bir
   * LAYERS parametresiyle istek atmak GeoServer'dan hata döndürürdü.
   */
  useEffect(() => {
    const katman = poiWmsKatmanRef.current
    if (!katman) return

    const secili = poiStilleri.filter((st) => poiKategoriSecimi.has(st.kategoriId))

    katman.setVisible(secili.length > 0 && !sahneGizliyor)

    if (secili.length > 0) {
      katman.getSource().updateParams({
        LAYERS: secili.map(() => geoDurum.poiKatmani).join(','),
        STYLES: secili.map((st) => st.stil).join(','),
      })
    }
  }, [poiKategoriSecimi, poiStilleri, poiWmsAktif, sahneGizliyor, geoDurum])

  // Ödev 16: ulaşım katmanları TEK anahtarla açılıp kapanıyor.
  //
  // Durak ve hat ayrı ayrı kapatılabilseydi "hat var ama durakları yok" gibi
  // yarım bir görünüm mümkün olurdu; çizgi zaten duraklardan türediği için
  // ikisi tek bir şeyin parçası.
  useEffect(() => {
    const map = mapRef.current
    if (!map) return

    map.getLayers().getArray().forEach((katman) => {
      const kaynak = typeof katman.getSource === 'function' ? katman.getSource() : null
      if (kaynak === durakKaynagiRef.current || kaynak === guzergahKaynagiRef.current) {
        katman.setVisible(ulasimGorunur && !sahneGizliyor)
      }
    })
  }, [ulasimGorunur, haritaHazir, sahneGizliyor])

  // Ödev 17: HAT BAZLI aç/kapat — "katman kontrolü gibi güzergahlar üzerinde
  // de aç/kapat yapılabilsin."
  //
  // Katman gizlemekle yapılamıyor: bütün hatlar aynı iki katmanı (durak +
  // çizgi) paylaşıyor. Bunun yerine her feature'a `gizli` özelliği yazılıyor
  // ve stil fonksiyonu o özelliği görünce null dönüyor.
  //
  // feature.set(...) OpenLayers'ta kendiliğinden yeniden çizim tetikliyor;
  // ayrıca render çağırmaya gerek yok.
  useEffect(() => {
    if (!haritaHazir) return

    const uygula = (kaynak) => kaynak?.getFeatures().forEach((f) => {
      f.set('gizli', gizliGuzergahlar.has(f.get('guzergahId')))
    })

    uygula(durakKaynagiRef.current)
    uygula(guzergahKaynagiRef.current)
  }, [gizliGuzergahlar, haritaHazir, guzergahlar])

  // Vektör katmanının stili: WMS çiziyorsa görünmez tıklama hedefi, çizmiyorsa
  // Ödev 12'nin mor halkası (gerekçe: poiVurusStili).
  useEffect(() => {
    // Seçim stile REF ile değil, doğrudan setStyle ile veriliyor: seçim
    // değiştiğinde stil fonksiyonunun kendisi yenileniyor ve OpenLayers
    // katmanı baştan çiziyor. Ref kullansaydık yeniden çizimi elle
    // tetiklemek gerekirdi.
    poiKatmanRef.current?.setStyle(
      poiWmsAktif
        ? poiVurusStili(poiKategoriSecimi)
        : poiStili(poiStilleri, poiKategoriSecimi),
    )

    // poiStilleri BAĞIMLILIK LİSTESİNDE: simgeler stil ucundan geliyor ve
    // harita kurulduktan SONRA iniyor. Listede olmasaydı kategori simgeleri
    // ilk yüklemede hiç görünmez, ancak WMS açılıp kapanınca ortaya
    // çıkardı — "bazen çalışıyor" türünden bir hata.
  }, [poiWmsAktif, haritaHazir, poiStilleri])

  // ------------------------------------------------------------------------
  //  Ödev 9 / Madde 2: Isı haritası katmanı
  //
  //  Ayrı bir katman çünkü ayrı bir STİL istiyor. Aynı WMS ucuna gidiyor,
  //  tek fark STYLES=isi_haritasi. Yoğunluk hesabı GeoServer'da (gs:Heatmap).
  //
  //  EN ÜSTE ekleniyor: ısı yüzeyi altındaki her şeyi örtsün, yoğunluk
  //  okunabilir kalsın.
  // ------------------------------------------------------------------------
  useEffect(() => {
    const map = mapRef.current
    if (!map || !haritaHazir || !geoDurum?.etkin || !geoDurum?.ayakta) return undefined
    if (!geoDurum.noktaKatmani || !geoDurum.isiHaritasiStili) return undefined

    const { katman, kaynak, degerOku } = isiHaritasiKatmaniOlustur(
      geoDurum.noktaKatmani,
      geoDurum.isiHaritasiStili,
      geoDurum.isiDegerStili,
      goLogin,
      // Değer resmi belleğe alındı: sabitlenmiş ölçüm varsa tazele.
      // Bu olmadan, resim gelmeden tıklayan kullanıcının kutusunda
      // "ölçüm hazırlanıyor" kalıcı olarak asılı kalıyordu.
      () => {
        const konum = sabitOlcumKonumuRef.current
        if (!konum) return
        const sonuc = degerOku(konum)
        setSabitOlcum(sonuc.durum === 'disarida' ? null : { ...sonuc, koordinat: konum })
      },
    )
    // Maskenin (900) ALTINDA, kayıt katmanlarının (0) ÜSTÜNDE.
    katman.setZIndex(500)

    isiKatmanRef.current = katman
    isiKaynakRef.current = kaynak
    isiDegerOkuRef.current = degerOku
    map.addLayer(katman)

    return () => {
      map.removeLayer(katman)
      isiKatmanRef.current = null
      isiKaynakRef.current = null
      isiDegerOkuRef.current = null
    }
  }, [haritaHazir, geoDurum, goLogin])

  useEffect(() => {
    isiKatmanRef.current?.setVisible(isiAcik && !sahneGizliyor)
    // Isı haritası kapanınca ölçüm de anlamsızlaşıyor: ekranda "0.42" yazan
    // bir kutu kalması, kapalı bir katmanın değerini okuyormuş gibi görünürdü.
    if (!isiAcik) {
      setSabitOlcum(null)
      setAnlikOlcum(null)
    }
  }, [isiAcik, haritaHazir, geoDurum, sahneGizliyor])

  // Sabitlenen ölçümün haritadaki işaretçisi (Ödev 11).
  //
  // Değer kutusu ekranın köşesinde duruyor; işaretçi olmasaydı kullanıcı
  // "bu sayı neresinin?" diye kalırdı — özellikle tıkladıktan sonra fareyi
  // başka yere götürdüğünde.
  useEffect(() => {
    const kaynak = olcumKaynagiRef.current
    if (!kaynak) return

    kaynak.clear()
    sabitOlcumKonumuRef.current = sabitOlcum?.koordinat ?? null
    if (!sabitOlcum) return

    kaynak.addFeature(new Feature(new PointGeom(sabitOlcum.koordinat)))
  }, [sabitOlcum])

  // ------------------------------------------------------------------------
  //  Yer arama — kullanıcı yazdıkça Nominatim'e sorar
  //
  //  İki koruma var:
  //   1. DEBOUNCE (450 ms): her tuşta istek atmıyoruz. Hem Nominatim'in
  //      "saniyede 1 istek" kuralına uyuyoruz hem gereksiz trafik olmuyor.
  //   2. ABORT: yeni tuşa basılınca önceki istek iptal ediliyor. Yoksa yavaş
  //      dönen eski bir cevap, yeni aramanın sonuçlarının üstüne yazabilirdi
  //      (yarış durumu — "race condition").
  // ------------------------------------------------------------------------
  useEffect(() => {
    const metin = arama.trim()
    if (metin.length < EN_AZ_ARAMA) {
      setSonuclar([])
      setPoiSonuclari([])
      setAramaHatasi(null)
      return undefined
    }

    const denetleyici = new AbortController()
    setAraniyor(true)

    const zamanlayici = setTimeout(async () => {
      // İki kaynak PARALEL sorgulanıyor (Ödev 13 / Madde 2). Sırayla
      // çağırsaydık toplam bekleme ikisinin TOPLAMI olurdu; paralelde
      // yavaş olan kadar bekleniyor.
      //
      // allSettled (all değil): Nominatim'e ulaşılamadığında kendi POI
      // sonuçlarımız da kaybolmasın. Kendi verimiz dış bir servisin
      // erişilebilirliğine bağlı olmamalı.
      const [poiSonuc, yerSonuc] = await Promise.allSettled([
        poiAra(metin, denetleyici.signal, goLogin),
        // Nominatim üç harften önce anlamlı sonuç vermiyor ve saniyede bir
        // istek sınırı var; kendi aramamız iki harften başlıyor.
        metin.length >= 3 ? yerAra(metin, denetleyici.signal) : Promise.resolve([]),
      ])

      if (denetleyici.signal.aborted) return

      setPoiSonuclari(poiSonuc.status === 'fulfilled' ? (poiSonuc.value ?? []) : [])
      setSonuclar(yerSonuc.status === 'fulfilled' ? yerSonuc.value : [])

      // Hata mesajı yalnızca DIŞ servis için: kendi ucumuzun hatası zaten
      // authFetch üzerinden oturum akışına düşüyor.
      const yerHatasi = yerSonuc.status === 'rejected' && yerSonuc.reason?.name !== 'AbortError'
      setAramaHatasi(yerHatasi ? 'Harita arama servisine ulaşılamadı (POI araması çalışıyor).' : null)

      setAraniyor(false)
    }, 450)

    return () => {
      clearTimeout(zamanlayici)
      denetleyici.abort()
    }
  }, [arama, goLogin])

  // ------------------------------------------------------------------------
  //  Oturum: otomatik çıkış + kalan süre sayacı
  // ------------------------------------------------------------------------
  useEffect(() => {
    scheduleAutoLogout(goLogin)

    // Sayaç artık ERİŞİM token'ını değil OTURUMU gösteriyor (Eksik 5).
    //
    // Eskisi 10 dakikadan geriye sayıyordu; token sessizce yenilendiği için
    // o sayaç her 10 dakikada bir 0:00'a inip başa dönecek ve "1 dakika
    // kaldı" uyarısını sebepsiz yere yakacaktı. Oysa kullanıcı için anlamlı
    // olan tek süre, yeniden giriş yapması gereken an.
    const interval = setInterval(() => setRemaining(kalanOturumMetni()), 1000)

    return () => clearInterval(interval)
  }, [goLogin])

  // ------------------------------------------------------------------------
  //  Çizim etkileşimi — activeTool her değiştiğinde yeniden kurulur
  // ------------------------------------------------------------------------
  useEffect(() => {
    const map = mapRef.current
    // Sadece çizim tiplerinde kurulur; 'Duzenle' aracı ayrı bir effect'te ele alınıyor.
    // Bu kontrol olmasaydı new Draw({ type: 'Duzenle' }) hata fırlatırdı.
    if (!map || !DRAW_TYPE_KEYS.includes(activeTool)) return undefined

    const draw = new Draw({
      source: drawSourceRef.current,
      type: activeTool,              // 'Point' | 'LineString' | 'Polygon'
      // Çizim SIRASINDAKİ görünüm (henüz tamamlanmamış "sketch").
      // Bu satır olmasaydı OpenLayers kendi varsayılan parlak mavi stilini
      // kullanırdı ve yarım çizim, tamamlanmış taslaktan farklı görünürdü.
      style: taslakStili,

      // Ödev 11: izinli alan DIŞINDAKİ tıklamalar hiç kabul edilmiyor.
      // condition false dönerse OpenLayers o tıklamayı yok sayıyor —
      // kullanıcı sönük bölgeye bastığında hiçbir şey olmuyor, "kapalı"
      // olduğu tıklamadan anlaşılıyor. Önceden tıklama çalışıyor, kayıt
      // sunucudan 400 ile dönüyordu; hata mesajını beklemek kötü bir öğretmen.
      condition: (olay) => izinliMi(olay.coordinate),
    })

    // Yeni çizime başlanınca önceki taslağı temizle (aynı anda tek taslak).
    draw.on('drawstart', () => {
      drawSourceRef.current.clear()
      setPending(null)
    })

    // Çizim bitti (Ödev 4 / Görev 2): öznitelik giriş POP-UP'ı açılır.
    // Popup, çizilen geometrinin üzerine tutturuluyor — kullanıcı neyi
    // isimlendirdiğini görerek yazsın diye.
    draw.on('drawend', (evt) => {
      const geometry = evt.feature.getGeometry()
      setSecili(null)                        // bilgi kartı açıksa kapansın
      setPending({
        type: activeTool,
        wkt: geometryToWkt(geometry),        // 3857 → 4326 dönüşümü burada
        ozet: describeGeometry(geometry),
      })
      setForm({
        name: '', description: '', imageUrl: '',
        color: DRAW_TYPES[activeTool].color,   // tipin rengi ön seçili gelsin
      })
      popupOverlayRef.current?.setPosition(popupKonumu(geometry))
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
  }, [activeTool, popupKonumu, izinliMi])

  // ------------------------------------------------------------------------
  //  ENVANTER ANALİZİ aracı (Ödev 4 / Görev 3)
  //
  //  Poligon çizdirir ama VERİTABANINA KAYDETMEZ. Çizim bittiği anda
  //  alanla kesişen envanterler sayılır. Poligon haritada kalır; kullanıcı
  //  "Temizle" diyene kadar durur, böylece sonucu inceleyebilir.
  // ------------------------------------------------------------------------
  useEffect(() => {
    const map = mapRef.current
    if (!map || activeTool !== ANALIZ) return undefined

    const draw = new Draw({
      source: analizSourceRef.current,
      type: 'Polygon',
      style: analizStili,
    })

    // Yeni analiz başlarken önceki alanı ve sonucu sil (tek analiz aynı anda).
    draw.on('drawstart', () => {
      analizSourceRef.current.clear()
      analizBulunanRef.current?.clear()
      setAnalizSonuc(null)
    })

    draw.on('drawend', (evt) => {
      // Kaydetmiyoruz — sadece WKT'ye çevirip sunucuya "bunu kesenleri say" diyoruz.
      analizCalistir(geometryToWkt(evt.feature.getGeometry()))
    })

    map.addInteraction(draw)
    drawRef.current = draw

    return () => {
      draw.abortDrawing()
      map.removeInteraction(draw)
      drawRef.current = null
    }
  }, [activeTool, analizCalistir])

  // ------------------------------------------------------------------------
  //  KONUM ANALİZİ ALANI aracı (Ödev 14 / Madde 1)
  //
  //  Envanter analizi aracının kardeşi ama işi bitince SORGU ATMIYOR: sadece
  //  alanı belirliyor. Analiz, kullanıcı kriterleri girip "Analizi Başlat"
  //  dediğinde çalışıyor — çünkü alan tek başına yeterli bir girdi değil.
  //
  //  Coğrafi yetki KONTROLÜ YOK (çizim araçlarındaki `condition: izinliMi`
  //  burada yok): bu poligon bir KAYIT değil, bir sorgu penceresi. POI'ler
  //  ortak referans verisi olduğu ve listeleme zaten süzülmediği için,
  //  analizi kullanıcının çizim alanına hapsetmek tutarsız olurdu.
  // ------------------------------------------------------------------------
  useEffect(() => {
    const map = mapRef.current
    if (!map || activeTool !== KONUM_ALAN) return undefined

    const draw = new Draw({
      source: konumAlanKaynagiRef.current,
      type: 'Polygon',
      style: konumAlanStili,
    })

    draw.on('drawstart', () => {
      // Yeni alan çiziliyorsa eski sonuç artık geçersiz: yüzey ile sınırın
      // uyuşmadığı bir ara durum ekranda kalmasın.
      konumAnaliziTemizle()

      // WKT'yi de sıfırlıyoruz. Kullanıcı çizimi yarıda bırakırsa (Esc)
      // haritada poligon kalmaz ama panel "alan çizildi" demeye devam
      // ederdi — ekranla durumun çeliştiği tek nokta burasıydı.
      setKonumAlanWkt(null)
    })

    draw.on('drawend', (evt) => {
      setKonumAlanWkt(geometryToWkt(evt.feature.getGeometry()))

      // Araç kendini kapatıyor: kullanıcı bir alan çizdi, sıradaki iş
      // kriterleri girmek. Araç açık kalsaydı paneldeki bir tıklama
      // istemeden ikinci bir poligon başlatabilirdi.
      setActiveTool(null)
    })

    map.addInteraction(draw)
    drawRef.current = draw

    return () => {
      draw.abortDrawing()
      map.removeInteraction(draw)
      drawRef.current = null
    }
  }, [activeTool, konumAnaliziTemizle])

  // Panel ilk açıldığında il listesini indir (81 satır, geometri İÇERMEZ).
  //
  // Açılışta değil, PANEL AÇILINCA: analiz panelini hiç açmayan bir kullanıcı
  // için bu istek boşuna olurdu. `iller.length` koşulu ikinci açılışta
  // yeniden indirmeyi engelliyor.
  useEffect(() => {
    if (!konumPaneliAcik || iller.length > 0) return

    let iptal = false

    illeriGetir(goLogin)
      .then((liste) => { if (!iptal) setIller(liste) })
      .catch((err) => {
        if (!iptal && err.message !== 'Oturum süresi doldu') setIllerHatasi(err.message)
      })

    return () => { iptal = true }
  }, [konumPaneliAcik, iller.length, goLogin])

  // ------------------------------------------------------------------------
  //  Ödev 13 / Madde 4 — seçilen yerden AD ve KATEGORİ otomatik doluyor
  //
  //  "Kayıtlı bir yer seçilirken konumlandırma otomatik olsun (Millî
  //  Kütüphane seçilirse direkt Eğitim › Kütüphane gelsin)."
  //
  //  Kaynak iki türlü olabiliyor ama işlenişi aynı:
  //     • arama sonucundan bir yer seçildi        → yer nesnesi zaten elimizde
  //     • POI aracıyla haritaya tıklandı          → ters coğrafi kodlama ile
  //                                                 çözülüyor
  //  Bu yüzden ortak bir fonksiyon: iki yol da AYNI öneri kuralından geçsin.
  // ------------------------------------------------------------------------

  /**
   * Seçilen yerin adını ve önerilen kategorisini forma yazar.
   *
   * Ad ÜZERİNE YAZMIYOR: kullanıcı zaten bir şey yazdıysa ona dokunmuyoruz.
   * Öneri bir kolaylıktır, kullanıcının girdisinin önüne geçemez.
   *
   * Öneri bulunamazsa hiçbir şey olmuyor — kategori boş kalıyor ve kullanıcı
   * kendisi seçiyor. Rastgele bir kategori atamak, yanlış veriyi sessizce
   * kaydetmenin en kolay yolu olurdu.
   */
  const poiOnerisiUygula = useCallback(async (yer) => {
    if (!yer) return

    setPoiForm((onceki) => ({ ...onceki, isim: onceki.isim || yer.ad || '' }))

    try {
      const oneri = await kategoriOner(
        { tur: yer.tur, sinif: yer.sinif, isim: yer.ad }, undefined, goLogin,
      )
      if (!oneri) return

      setPoiForm((onceki) => ({
        ...onceki,
        // Kullanıcı bu arada kendi seçimini yaptıysa ona dokunma.
        kategoriId: onceki.kategoriId || String(oneri.kategoriId),
        oneri,
      }))
    } catch {
      /* öneri alınamadı; form elle doldurulabilir durumda kalıyor */
    }
  }, [goLogin])

  /**
   * Haritada tıklanan noktada kayıtlı bir yer var mı? (ters coğrafi kodlama)
   *
   * Nominatim ağdan cevap verdiği için form önce boş açılıyor, cevap gelince
   * doluyor. Bekleme sırasında "çözülüyor…" yazıyoruz: alanların kendiliğinden
   * dolması, sebebi görünmediğinde tuhaf durur.
   */
  const tiklananYeriTani = useCallback(async (lonLat) => {
    setYerCozuluyor(true)
    try {
      await poiOnerisiUygula(await yeriCoz(lonLat[0], lonLat[1]))
    } catch {
      /* adres servisi yanıt vermedi; form elle doldurulur */
    } finally {
      setYerCozuluyor(false)
    }
  }, [poiOnerisiUygula])

  // ------------------------------------------------------------------------
  //  POI EKLEME aracı (Ödev 12 / Madde 2)
  //
  //  Nokta çizdirir ama tbl_point'e değil poi tablosuna yazar; bu yüzden
  //  ayrı bir Draw etkileşimi. Taslak, çizim araçlarıyla AYNI geçici
  //  katmana konuyor (kesikli turuncu) — "henüz kaydedilmedi" görünümü
  //  kullanıcı için zaten tanıdık.
  //
  //  condition: izinli alan kontrolü burada da var. POI de haritaya yapılan
  //  bir kayıt; coğrafi yetki alanının dışına konamıyor (sunucu da reddediyor).
  // ------------------------------------------------------------------------
  useEffect(() => {
    const map = mapRef.current
    if (!map || activeTool !== POI) return undefined

    const draw = new Draw({
      source: drawSourceRef.current,
      type: 'Point',
      style: taslakStili,
      condition: (olay) => izinliMi(olay.coordinate),
    })

    draw.on('drawstart', () => {
      drawSourceRef.current.clear()
      setPoiTaslak(null)
    })

    draw.on('drawend', (evt) => {
      const geometry = evt.feature.getGeometry()
      setSecili(null)      // bilgi kartı açıksa kapansın
      setPending(null)     // çizim formu açıksa kapansın (aynı popup'ı paylaşıyorlar)
      setPoiTaslak({
        wkt: geometryToWkt(geometry),
        ozet: describeGeometry(geometry),
      })
      // Form boş açılıyor; aşağıdaki çözümleme cevap verirse ad ve kategori
      // kendiliğinden doluyor. Kategori ELLE ön seçili GELMİYOR: listenin ilk
      // maddesi çoğu zaman bir kök kategori ("Yeme-İçme") ve kullanıcı
      // farkına varmadan onu kaydederdi.
      setPoiForm(BOS_POI_FORMU())
      popupOverlayRef.current?.setPosition(popupKonumu(geometry))

      // Ödev 13 / Madde 4: "haritadan seçilen … kayıtlı bir yer seçilirken
      // konumlandırma otomatik olsun". Tıklanan noktada OpenStreetMap'te
      // kayıtlı bir yer varsa adını ve türünü çözüp formu dolduruyoruz.
      tiklananYeriTani(toLonLat(geometry.getCoordinates()))
    })

    map.addInteraction(draw)
    drawRef.current = draw

    return () => {
      draw.abortDrawing()
      map.removeInteraction(draw)
      drawRef.current = null
    }
  }, [activeTool, popupKonumu, izinliMi, tiklananYeriTani])

  // ------------------------------------------------------------------------
  //  Ödev 16 / Madde 2 — "DURAK EKLE" aracı
  //
  //  POI aracının kardeşi: nokta çizdiriyor, açılan formda ad ve GÜZERGAH
  //  isteniyor. Ayrı bir araç olmasının gerekçesi DURAK sabitinin yanında.
  //
  //  condition: izinli alan kontrolü burada da var — durak da haritaya
  //  yapılan bir kayıt, coğrafi yetki alanının dışına konamıyor (sunucu da
  //  reddediyor). Üç araç (çizim, POI, durak) aynı kuralı paylaşıyor.
  // ------------------------------------------------------------------------
  useEffect(() => {
    const map = mapRef.current
    if (!map || activeTool !== DURAK) return undefined

    const draw = new Draw({
      source: drawSourceRef.current,
      type: 'Point',
      style: taslakStili,
      condition: (olay) => izinliMi(olay.coordinate),
    })

    draw.on('drawstart', () => {
      drawSourceRef.current.clear()
      setDurakTaslak(null)
    })

    draw.on('drawend', (evt) => {
      const geometry = evt.feature.getGeometry()
      setSecili(null)       // bilgi kartı açıksa kapansin
      setPending(null)      // çizim formu açıksa kapansin
      setPoiTaslak(null)    // POI formu açıksa kapansin (aynı popup'ı paylaşıyorlar)

      setDurakTaslak({
        wkt: geometryToWkt(geometry),
        ozet: describeGeometry(geometry),
      })

      // GÜZERGAH ÖN SEÇİLİ GELİYOR (POI kategorisinin aksine).
      //
      // POI'de kategori bilerek boş bırakılmıştı: listenin ilk maddesi genelde
      // bir kök kategori ve kullanıcı farkına varmadan onu kaydedebilirdi.
      // Burada durum farklı — hatlar arasında "yanlış ama makul" bir
      // varsayılan yok; üstelik operatör genelde AYNI hattın duraklarını
      // arka arkaya giriyor. Bu yüzden son seçilen hat korunuyor, hiç
      // seçilmemişse ilk AKTİF hat geliyor.
      setDurakForm((onceki) => ({
        ...BOS_DURAK_FORMU,
        guzergahId: onceki.guzergahId
          || String(guzergahlar.find((g) => g.isActive)?.id ?? ''),
      }))

      popupOverlayRef.current?.setPosition(popupKonumu(geometry))
    })

    map.addInteraction(draw)
    drawRef.current = draw

    return () => {
      draw.abortDrawing()
      map.removeInteraction(draw)
      drawRef.current = null
    }
  }, [activeTool, popupKonumu, izinliMi, guzergahlar])

  // ------------------------------------------------------------------------
  //  DÜZENLEME etkileşimi — kaydedilmiş geometriyi sürükleyerek değiştirme
  //
  //  Modify: köşeleri sürüklemeyi, kenara tıklayıp yeni köşe eklemeyi ve
  //          Alt+tıkla köşe silmeyi sağlar.
  //  Snap:   imleç mevcut bir köşeye/kenara yaklaşınca oraya "yapışır".
  //          Komşu poligonlar arasında boşluk/çakışma kalmasını önler — GIS'te
  //          topolojik doğruluk için standart beklenti budur.
  // ------------------------------------------------------------------------
  useEffect(() => {
    const map = mapRef.current
    if (!map || activeTool !== DUZENLE) return undefined

    const etkilesimler = []

    // Üç kaynak için ayrı Modify: her biri kendi katmanının feature'larını düzenler.
    DRAW_TYPE_KEYS.forEach((key) => {
      const modify = new Modify({ source: sourcesRef.current[key] })

      // Sürükleme bittiğinde değişen her feature'ı sunucuya yaz.
      modify.on('modifyend', (evt) => {
        evt.features.forEach((feature) => geometriGuncelle(feature))
      })

      map.addInteraction(modify)
      etkilesimler.push(modify)
    })

    // ⚠️ Snap HER ZAMAN EN SONA eklenir. Snap, fare olaylarını diğer
    // etkileşimlerden ÖNCE yakalayıp koordinatı düzeltmesi için en son
    // eklenmiş olmalıdır (OpenLayers etkileşimleri ters sırada işler).
    DRAW_TYPE_KEYS.forEach((key) => {
      const snap = new Snap({ source: sourcesRef.current[key] })
      map.addInteraction(snap)
      etkilesimler.push(snap)
    })

    return () => etkilesimler.forEach((i) => map.removeInteraction(i))
  }, [activeTool, geometriGuncelle])

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

    // POI katmanı da tıklanabilir olmalı: ödev "eklenen POI'ye tıklandığında
    // bilgi paneli açılmalıdır" diyor. Listeye eklemeseydik POI'ler haritada
    // görünür ama tıklanamaz kalırdı.
    // Ödev 16: DURAK katmanı da tıklanabilir olmalı — ödev metni
    // "duraklara tıklandığında bilgi kutucuğu açılmalıdır" diyor. Listeye
    // eklenmezse durak ekranda görünür ama tıklama onu hiç görmez.
    // (Güzergah ÇİZGİSİ bilerek YOK: o bir kayıt değil, duraklardan türeyen
    // bir görünüm — tıklanınca açılacak bir kartı da yok.)
    const kayitKatmanlari = [
      ...Object.values(layersRef.current), poiKatmanRef.current, durakKatmanRef.current,
    ].filter(Boolean)

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

    /**
     * Isı haritası açıkken imlecin/tıklamanın altındaki yoğunluğu okur.
     * Kapalıysa null döner — ölçüm yalnızca yüzey ekrandayken anlamlı.
     */
    const olcumOku = (koordinat) => {
      if (!isiKatmanRef.current?.getVisible()) return null
      const sonuc = isiDegerOkuRef.current?.(koordinat)
      if (!sonuc || sonuc.durum === 'disarida') return null
      // "hazirlaniyor" da bir SONUÇ: hiçbir şey göstermemek yerine ekranda
      // "ölçüm hazırlanıyor" yazıyoruz. Sessizlik bozukluk gibi görünüyordu.
      return { ...sonuc, koordinat }
    }

    const fareHareketi = (evt) => {
      if (evt.dragging) return

      // ---- Termometre: gezinirken CANLI okuma (Ödev 11) ----
      // Çizim aracı açıkken de çalışıyor: ısı yüzeyi bir OKUMA katmanı,
      // hangi aracın seçili olduğuyla ilgisi yok.
      if (isiKatmanRef.current?.getVisible()) {
        const simdi = Date.now()
        // Fare hareketi saniyede onlarca olay üretiyor; her birinde React'i
        // yeniden çizdirmenin anlamı yok. ~16 kare/sn yeterince akıcı.
        if (simdi - sonOlcumZamaniRef.current > 60) {
          sonOlcumZamaniRef.current = simdi
          setAnlikOlcum(olcumOku(evt.coordinate))
        }
      }

      // ---- Çizim modunda: kapalı bölgede imleç "yasak" olsun (Ödev 11) ----
      // Maske "burası kapalı" diyor ama imleç hâlâ artı işaretiyse kullanıcı
      // tıklamayı deniyor ve hiçbir şey olmuyor. Yasak imleci bunu tıklamadan
      // ÖNCE haber veriyor.
      if (activeTool) {
        map.getViewport().style.cursor =
          DRAW_TYPE_KEYS.includes(activeTool) && !izinliMi(evt.coordinate)
            ? 'not-allowed'
            : ''
        return
      }

      const feature = pikseldekiFeature(evt.pixel)
      const yeniId = feature ? feature.getId() : null
      if (yeniId === sonVurguId) return

      sonVurguId = yeniId
      map.getViewport().style.cursor = feature ? 'pointer' : ''
      vurgulaFeature(feature)
    }

    // Fare haritadan çıkınca canlı ölçüm biter: ekranda kalan sayı artık
    // "imlecin altındaki" değildir, yanlış bilgi olurdu. Sabitlenen ölçüm
    // duruyor, kutu boş kalmıyor.
    const fareCikti = () => setAnlikOlcum(null)

    // 'click' değil 'singleclick': OpenLayers çift tıklamayı ayırt edebilmek için
    // ~250 ms bekler. 'click' kullansaydık çift tıklayarak zoom yaparken
    // aşağıdaki kod da iki kez tetiklenir, harita zıplardı.
    const tekTiklama = (evt) => {
      if (activeTool) {
        // Çizim modunda tıklama çizime aittir. Tek istisna: kapalı bölgeye
        // tıklandıysa Draw etkileşimi olayı zaten yutuyor (condition false)
        // ve ekranda hiçbir şey olmuyor. Kullanıcı "bozuk mu?" diye kalmasın
        // diye tek cümlelik bilgi veriyoruz — hata değil, bilgi.
        if (DRAW_TYPE_KEYS.includes(activeTool) && !izinliMi(evt.coordinate)) {
          const simdi = Date.now()
          if (simdi - sonKapaliUyariRef.current > 4000) {
            sonKapaliUyariRef.current = simdi
            bildir('uyari', 'Burası çizim alanınızın dışında — sönük bölgeye çizim yapılamaz.')
          }
        }
        return
      }

      // Ödev 11 — "termometre": ısı haritası açıkken tıklanan HER nokta
      // ölçülüyor, kayıtlı bir nokta olması gerekmiyor. Değer, ekrandaki
      // yüzeyin gri tonlamalı ikizinden okunuyor (bkz. wms.js → degerOku).
      // Tıklama ölçümü SABİTLİYOR: haritada işaretçisi kalıyor, fare
      // uzaklaşınca kutuda o değer görünmeye devam ediyor.
      const isiAcikMi = isiKatmanRef.current?.getVisible()
      if (isiAcikMi) setSabitOlcum(olcumOku(evt.coordinate))

      const feature = pikseldekiFeature(evt.pixel)

      if (feature) {
        // Şekle tıklandı → bilgi kartını aç.
        // Otomatik zoom YAPMIYORUZ: popup açılırken harita da hareket etseydi
        // kart ekranda kayar, okumak zorlaşırdı. Yaklaşmak isteyen kartın
        // içindeki "Yakınlaş" düğmesini kullanıyor.
        popupAc(feature)
      } else {
        popupKapat()

        // Boş alana tıklandı → oraya kaydır. AMA ısı haritası açıkken DEĞİL:
        // her ölçümde harita kayıyordu; üstelik gs:Heatmap yoğunluğu her istek
        // için yeniden normalleştirdiğinden kayan haritada aynı noktanın değeri
        // de değişiyordu. Ölçüm yaparken harita sabit duruyor.
        if (!isiAcikMi) {
          map.getView().animate({ center: evt.coordinate, duration: 450, easing: easeOut })
        }
      }
    }

    map.on('pointermove', fareHareketi)
    map.on('singleclick', tekTiklama)
    map.getViewport().addEventListener('pointerleave', fareCikti)

    return () => {
      map.un('pointermove', fareHareketi)
      map.un('singleclick', tekTiklama)
      map.getViewport().removeEventListener('pointerleave', fareCikti)
      map.getViewport().style.cursor = ''
    }
  }, [activeTool, popupAc, popupKapat, vurgulaFeature, izinliMi, bildir])

  // ------------------------------------------------------------------------
  //  Klavye kısayolları
  // ------------------------------------------------------------------------
  useEffect(() => {
    const onKeyDown = (e) => {
      const tag = e.target.tagName
      const yaziyor = tag === 'INPUT' || tag === 'TEXTAREA' || e.target.isContentEditable

      // "/" → arama kutusuna atla. Yazarken çalışmamalı, o yüzden ilk kontrol bu.
      if (e.key === '/' && !yaziyor) {
        e.preventDefault()
        aramaGirdiRef.current?.focus()
        aramaGirdiRef.current?.select()
        // Kutu artık haritanın üstünde ve sonuç listesi ancak "açık"
        // işaretliyken çiziliyor; kısayolla gelen kullanıcı da listeyi görsün.
        setAramaAcik(true)
        return
      }

      // Esc, metin alanındayken de çalışsın: açık popup'ı kapatmanın en doğal yolu.
      if (e.key === 'Escape') {
        drawRef.current?.abortDrawing()      // yarım çizimi iptal et
        drawSourceRef.current?.clear()
        setPending(null)
        setPoiTaslak(null)
        popupKapat()
        e.target.blur?.()                    // odak metin alanındaysa bırak
        return
      }

      // Bundan sonrası yalnızca yazı yazılmıyorken. Bu kontrol olmadan ad
      // alanında Backspace'e basmak çizimin son noktasını silerdi.
      if (yaziyor) return

      if (e.key === 'Backspace' && drawRef.current) {
        e.preventDefault()                   // tarayıcı "geri" gitmesin
        drawRef.current.removeLastPoint()
        return
      }

      // Değiştirici tuşlarla birlikte basılmışsa tarayıcının kısayolu olabilir
      if (e.ctrlKey || e.metaKey || e.altKey) return

      // Araç kısayolları: 1/2/3 çizim, D düzenle, A analiz, P POI
      const kisayollar = {
        '1': DRAW_TYPE_KEYS[0],
        '2': DRAW_TYPE_KEYS[1],
        '3': DRAW_TYPE_KEYS[2],
        d: DUZENLE,
        a: ANALIZ,
        p: POI,
      }
      const arac = kisayollar[e.key.toLowerCase()]
      if (arac) {
        e.preventDefault()
        aracSecRef.current(arac)
      }
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [popupKapat])

  // ------------------------------------------------------------------------
  //  Katman görünürlüğü
  // ------------------------------------------------------------------------
  useEffect(() => {
    DRAW_TYPE_KEYS.forEach(
      (key) => layersRef.current[key]?.setVisible(visible[key] && !sahneGizliyor),
    )
  }, [visible, sahneGizliyor])

  /**
   * POI VEKTÖR katmanı — tıklama hedefi.
   *
   * Katmanın kendisi seçim varken açık; hangi POI'nin çizileceğine stil
   * fonksiyonu karar veriyor (seçili olmayan kategori için null dönüyor).
   *
   * NEDEN KATMANI TOPLUCA GİZLEMİYORUZ? Gizleseydik "hangi kategoriler açık"
   * bilgisini iki yerde tutmamız gerekirdi. Tek yer: stil fonksiyonu.
   * Katman görünürlüğü yalnızca "hiç seçim yok" ve "sahne oynuyor"
   * durumlarını kapatıyor.
   *
   * Feature'a null stil dönmek OpenLayers'ta hem ÇİZİMİ hem de TIKLAMA
   * TESTİNİ kapatıyor — yani gizli bir POI yanlışlıkla seçilemiyor.
   */
  useEffect(() => {
    poiKatmanRef.current?.setVisible(poiKategoriSecimi.size > 0 && !sahneGizliyor)
  }, [poiKategoriSecimi, haritaHazir, sahneGizliyor])

  // ------------------------------------------------------------------------
  //  SÜRÜKLEYEREK TAŞIMA (POI ve duraklar)
  // ------------------------------------------------------------------------
  //
  //  ---- NEDEN Translate, Modify DEĞİL? ----
  //
  //  Modify köşe EKLEMEYE ve TAŞIMAYA yarıyor; nokta geometrisinde "köşe
  //  ekleme" anlamsız ve kullanıcı yanlışlıkla noktayı çoğaltabiliyor.
  //  Translate bütün geometriyi taşıyor — nokta için doğru araç bu.
  //
  //  ---- NEDEN İYİMSER GÜNCELLEME? ----
  //
  //  Sürükleme bittiği anda nokta zaten yeni yerinde duruyor (OpenLayers onu
  //  oraya taşıdı). Sunucudan cevap gelene kadar eski yere geri koymak,
  //  kullanıcının gözünde noktanın "zıplaması" olurdu. Sunucu reddederse
  //  (yetki yok, coğrafi alan dışı) listeyi tazeleyip gerçek konumu geri
  //  getiriyoruz.

  /** Sürüklenen feature'ın taşınmadan önceki konumu — hata durumunda geri dönüş. */
  const suruklemeBaslangicRef = useRef(null)

  /**
   * Sürükleme yetkisi var mı?
   *
   * Sunucu asıl kontrolü yapıyor (sahiplik + coğrafi alan). Buradaki kontrol
   * yalnızca ETKİLEŞİMİ HİÇ KURMAMAK için: yetkisi olmayan kullanıcı bir
   * noktayı sürükleyip sonra "yapamazsınız" hatası almasın.
   */
  const poiTasinabilir = yetkiVar(YETKILER.poiEkleme) || yetkiVar(YETKILER.poiYonetimi)
  const durakTasinabilir = yetkiVar(YETKILER.durakEkleme) || yetkiVar(YETKILER.guzergahYonetimi)

  useEffect(() => {
    const map = mapRef.current
    if (!map || !haritaHazir) return undefined
    if (!poiTasinabilir && !durakTasinabilir) return undefined

    const katmanlar = [
      poiTasinabilir ? poiKatmanRef.current : null,
      durakTasinabilir ? durakKatmanRef.current : null,
    ].filter(Boolean)

    if (katmanlar.length === 0) return undefined

    const tasi = new Translate({ layers: katmanlar })

    // Sürükleme BAŞLARKEN: konumu sakla ve feature'ı işaretle.
    //
    // İşaret stil fonksiyonuna gidiyor: POI katmanı WMS açıkken neredeyse
    // görünmez çiziliyor (tıklama hedefi), yani kullanıcı sürüklerken hiçbir
    // şeyin hareket ettiğini GÖRMEZDİ. İşaretli feature belirgin çiziliyor.
    tasi.on('translatestart', (olay) => {
      const feature = olay.features.item(0)
      if (!feature) return
      suruklemeBaslangicRef.current = feature.getGeometry().clone()
      feature.set('suruklenlyor', true)
      popupKapat()          // açık bir bilgi kutucuğu sürüklenen noktayı takip etmiyor
    })

    tasi.on('translateend', async (olay) => {
      const feature = olay.features.item(0)
      if (!feature) return

      feature.set('suruklenlyor', false)

      const dto = feature.get('dto')
      const tip = feature.get('tip')
      const wkt = geometryToWkt(feature.getGeometry())
      const eski = suruklemeBaslangicRef.current
      suruklemeBaslangicRef.current = null

      try {
        if (tip === POI) {
          // Yalnızca KONUM gönderiliyor; ad, kategori ve mesai planı DTO'dan
          // aynen geri yazılıyor. Göndermeseydik sunucu onları boş sayıp
          // silerdi (PUT tam kaydı günceller).
          await poiGuncelle(dto.id, {
            isim: dto.isim,
            kategoriId: dto.kategoriId,
            mesaiPlani: dto.mesaiPlani,
            wkt,
          }, goLogin)
          await poileriYukle()
          // WMS bir RESİM: kaynağı tazelemezsek simge eski yerinde kalır.
          poiWmsKatmanRef.current?.getSource().refresh()
          bildir('ok', `"${dto.isim}" taşındı.`)
        } else if (tip === DURAK) {
          await durakGuncelleIstek(dto.id, {
            ad: dto.ad,
            guzergahId: dto.guzergahId,
            aciklama: dto.aciklama,
            wkt,
          }, goLogin)
          // Durak taşınınca hattın ROTASI da değişiyor; ulasimiYukle güncel
          // rotayı da getiriyor (sunucu OSRM'i kendisi yeniliyor).
          await ulasimiYukle()
          bildir('ok', `"${dto.ad}" taşındı, rota güncellendi.`)
        }
      } catch (err) {
        // Reddedildi: noktayı ESKİ yerine geri koy. Koymasaydık ekranda
        // kaydedilmemiş bir konum kalır ve kullanıcı taşımanın tuttuğunu
        // sanırdı.
        if (eski) feature.setGeometry(eski)
        if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
      }
    })

    map.addInteraction(tasi)
    return () => map.removeInteraction(tasi)
  }, [
    haritaHazir, poiTasinabilir, durakTasinabilir,
    goLogin, bildir, popupKapat, poileriYukle, ulasimiYukle,
  ])

  // ------------------------------------------------------------------------
  //  Eylemler
  // ------------------------------------------------------------------------

  /**
   * Arama sonucuna tıklanınca: haritayı oraya götür ve o noktayı
   * KAYDEDİLMEYİ BEKLEYEN taslak olarak hazırla.
   *
   * Elle çizim akışının aynısını kullanıyoruz (pending + form) — tek fark,
   * geometrinin fareyle değil aramadan gelmesi. Böylece kaydetme, WKT üretimi
   * ve projeksiyon dönüşümü için ikinci bir yol açmıyoruz.
   */
  const aramaSonucuSec = (yer, poiOlarak = false) => {
    const map = mapRef.current
    if (!map) return

    const koordinat = fromLonLat([yer.lon, yer.lat])   // 4326 → 3857
    const geometri = new PointGeom(koordinat)

    // Taslak katmanına koy: turuncu kesikli "henüz kaydedilmedi" görünümü
    drawSourceRef.current.clear()
    drawSourceRef.current.addFeature(new Feature({ geometry: geometri }))

    popupKapat()

    if (poiOlarak) {
      // ---- Ödev 13 / Madde 4: aranan yer doğrudan POI olarak konumlanıyor ----
      //
      // Konum, ad ve kategori ARAMA SONUCUNDAN geliyor; kullanıcı haritada
      // yeri bulup tıklamak zorunda kalmıyor. "Millî Kütüphane" araması →
      // nokta yerine oturuyor, ad doluyor, kategori Eğitim › Kütüphane
      // seçiliyor.
      setActiveTool(POI)
      setPoiTaslak({
        wkt: geometryToWkt(geometri),   // 3857 → 4326
        ozet: describeGeometry(geometri),
      })
      setPoiForm({ ...BOS_POI_FORMU(), isim: yer.ad })
      poiOnerisiUygula(yer)
    } else {
      // ---- Ödev 12'deki davranış: serbest çizim taslağı ----
      // Elle çizim akışının aynısı (pending + form); tek fark geometrinin
      // fareyle değil aramadan gelmesi. Böylece kaydetme, WKT üretimi ve
      // projeksiyon dönüşümü için ikinci bir yol açmıyoruz.
      setActiveTool(null)          // çizim aracı açıksa kapat, karışmasın
      setPending({
        type: 'Point',
        wkt: geometryToWkt(geometri),
        ozet: describeGeometry(geometri),
      })
      setForm({ name: yer.ad, description: yer.tamAd, imageUrl: '' })
    }

    // Popup'ı taslağın üstüne KONUMLANDIR.
    //
    // Bu satır Ödev 12'de eksikti: popupKapat() overlay'i gizliyor
    // (setPosition(undefined)) ve arama yolunda onu geri açan bir çağrı yoktu.
    // Taslak haritada beliriyor, form ise hiç görünmüyordu — paneldeki
    // "sağdaki formu doldurup kaydedin" ipucu boşa çıkıyordu. Elle çizim
    // yolunda aynı çağrı drawend'de yapılıyor (bkz. Draw effect'leri).
    popupOverlayRef.current?.setPosition(popupKonumu(geometri))

    map.getView().animate({ center: koordinat, zoom: 14, duration: 700, easing: easeOut })

    // Sonuç listesini kapat ama arama metnini bırak (kullanıcı görsün ne aradığını)
    setAramaAcik(false)
  }

  /**
   * KAYITLI bir POI arama sonucuna tıklanınca (Ödev 13 / Madde 2):
   * "harita o noktaya otomatik zoom yapsın".
   *
   * Yeni bir taslak AÇMIYOR — bu kayıt zaten var. Haritadaki feature'ı bulup
   * ona odaklanıyoruz; odaklanma bilgi panelini de açıyor, yani kullanıcı
   * aradığı POI'nin kategorisini ve mesaisini tek adımda görüyor.
   *
   * Feature bulunamazsa (POI katmanı kapalıysa ya da liste henüz
   * tazelenmediyse) koordinata uçmakla yetiniyoruz: aramanın "beni oraya
   * götür" sözü her hâlükârda tutuluyor.
   */
  const poiAramaSonucuSec = (sonuc) => {
    const map = mapRef.current
    if (!map) return

    setAramaAcik(false)

    const feature = poiKaynagiRef.current?.getFeatureById(`poi-${sonuc.id}`)
    if (feature) {
      odaklanFeature(feature)
      // Bilgi panelini de açıyoruz: kullanıcı POI'yi ARAYARAK buldu, bir de
      // haritada bulup tıklaması gerekmesin.
      popupAc(feature)
      return
    }

    const eslesme = sonuc.wkt.match(/POINT\s*\(([-\d.]+)\s+([-\d.]+)\)/i)
    if (!eslesme) return

    map.getView().animate({
      center: fromLonLat([parseFloat(eslesme[1]), parseFloat(eslesme[2])]),
      zoom: 15,
      duration: 700,
      easing: easeOut,
    })
  }

  const aramayiTemizle = () => {
    setArama('')
    setSonuclar([])
    setPoiSonuclari([])
    setAramaHatasi(null)
    setAramaAcik(false)
  }

  /** Araç kullanılabilir mi? Sözlükte karşılığı yoksa yetki aranmaz. */
  const aracKullanilabilir = (key) => !ARAC_YETKISI[key] || yetkiVar(ARAC_YETKISI[key])

  /**
   * Tatil listesi eksikse kullanıcıya söylüyoruz (Ödev 13 / Madde 3).
   *
   * Ramazan ve Kurban bayramları hicrî takvime bağlı olduğu için backend'de
   * yıl yıl elle tutuluyor (bkz. ResmiTatiller). Tanımlı olmayan bir yılda
   * liste yalnızca sabit tatilleri içerir. Bunu SÖYLEMEK zorundayız: eksik
   * bir listeyi tam listeymiş gibi göstermek, kullanıcıya "bayramda açığız"
   * dedirtirdi.
   */
  const tatilUyarisi = resmiTatiller && !resmiTatiller.diniBayramlarTanimli
    ? `${resmiTatiller.yil} yılının dinî bayram tarihleri tanımlı değil; `
      + 'liste yalnızca sabit tatilleri içeriyor.'
    : null

  /** Bugün resmî tatil mi? Panelde küçük bir uyarı olarak gösteriliyor. */
  const bugunkuTatil = bugunTatilMi(resmiTatiller?.tatiller)


  /**
   * Kategori id → stil tanımı. Arama sonucu satırındaki renkli nokta bunu
   * kullanıyor; haritadaki simgeyle BİREBİR aynı renk çıkıyor çünkü ikisi de
   * aynı listeden geliyor.
   */
  const stilSozlugu = new Map(poiStilleri.map((st) => [st.kategoriId, st]))
  const kategoriRengi = (kategoriId) => stilSozlugu.get(kategoriId)?.renk ?? '#7a7f87'

  /**
   * Açık duran POI kartının CANLI açık/kapalı durumu (Ödev 13 iyileştirmesi).
   *
   * Tarayıcının saatiyle hesaplanıyor: sunucunun cevabı istek anına
   * sabitlenir, saatlerce açık kalan bir sekmede yanlış rozet gösterirdi.
   * Planı olmayan (Ödev 12'den kalan) kayıtlarda null — rozet hiç çıkmıyor,
   * çünkü "bilmiyoruz" ile "kapalı" farklı şeyler.
   */
  const seciliPoiDurumu = secili?.tip === POI
    ? suAnDurum(secili.dto.mesaiPlani, resmiTatiller?.tatiller)
    : null

  const aracSec = (key) => {
    // Yetki kontrolü BURADA da yapılıyor, sadece düğmede değil: araçlar klavye
    // kısayoluyla da (1/2/3/D/A) açılabiliyor. Düğmeyi kilitleyip kısayolu açık
    // bırakmak, kullanıcıyı çizim yapıp kaydederken 403 yemeye götürürdü.
    if (!aracKullanilabilir(key)) {
      bildir('hata', `Bu araç için "${ARAC_YETKISI[key]}" yetkiniz yok.`)
      return
    }

    // Aynı butona tekrar basmak aracı kapatır (toggle davranışı).
    setActiveTool((onceki) => (onceki === key ? null : key))
    drawSourceRef.current?.clear()
    setPending(null)
    setPoiTaslak(null)
    popupKapat()
  }

  // Klavye dinleyicisinin çağırabilmesi için güncel fonksiyonu ref'te tut
  aracSecRef.current = aracSec

  const vazgec = () => {
    drawSourceRef.current?.clear()
    setPending(null)
    setPoiTaslak(null)
    popupKapat()
  }

  const handleSave = async (e) => {
    e.preventDefault()
    if (!pending) return

    setSaving(true)
    try {
      const kaydedilen = await kaydet(
        DRAW_TYPES[pending.type].endpoint,
        {
          name: form.name.trim(),
          // Boş metin yerine null: veritabanında "değer yok"un doğru karşılığı NULL'dur
          description: form.description.trim() || null,
          imageUrl: form.imageUrl.trim() || null,
          color: form.color,                 // Ödev 4 / Görev 2
          wkt: pending.wkt,
        },
        goLogin,
      )
      const kaydedilenTip = pending.type
      drawSourceRef.current.clear()
      setPending(null)
      popupKapat()
      setActiveTab(kaydedilenTip)         // kaydedilen tipin sekmesine geç
      await yukle()
      bildir('ok', `${DRAW_TYPES[kaydedilenTip].label} kaydedildi.`)

      // Ödev 4 / Görev 3 — Çizilen Poligon Analizi:
      // Yeni bir poligon kaydedildiğinde, içinde kalan envanter otomatik sayılır.
      // Kendisini saymamak için id'si hariç tutuluyor.
      if (kaydedilenTip === 'Polygon') {
        const sonuc = await analizCalistir(pending.wkt, kaydedilen?.id)
        if (sonuc) {
          bildir('ok', `"${kaydedilen.name}" alanında ${sonuc.total} envanter var.`)
        }
      }

      // Odağı aktif araç düğmesine geri ver: form kapanınca odak boşlukta kalmasın,
      // klavye kullanıcısı arka arkaya çizim yapabilsin.
      aracGrubuRef.current?.querySelector('.tool-btn.active')?.focus()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    } finally {
      setSaving(false)
    }
  }

  // ------------------------------------------------------------------------
  //  Ödev 12 — POI eylemleri
  // ------------------------------------------------------------------------

  /** Çizilen taslağı kategori ve mesai bilgisiyle birlikte kaydeder. */
  const poiKaydet = async (e) => {
    e.preventDefault()
    if (!poiTaslak) return

    setPoiKaydediliyor(true)
    try {
      const kaydedilen = await poiEkle({
        isim: poiForm.isim.trim(),
        kategoriId: Number(poiForm.kategoriId),
        // Ödev 13 / Madde 3: artık metin değil PLAN gönderiliyor. Okunur özet
        // metni sunucu plandan üretiyor — iki kaynağın çelişmemesi için
        // (bkz. PoiService.MesaiCoz).
        mesaiPlani: poiForm.plan,
        wkt: poiTaslak.wkt,
      }, goLogin)

      drawSourceRef.current.clear()
      setPoiTaslak(null)
      popupKapat()
      await poileriYukle()
      bildir('ok', `"${kaydedilen.isim}" POI olarak kaydedildi.`)

      // Odağı aktif araç düğmesine geri ver: arka arkaya POI girilebilsin.
      aracGrubuRef.current?.querySelector('.tool-btn.active')?.focus()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    } finally {
      setPoiKaydediliyor(false)
    }
  }

  // ------------------------------------------------------------------------
  //  Ödev 16 — durak eylemleri
  // ------------------------------------------------------------------------

  /** Çizilen durağı ad ve güzergah bilgisiyle kaydeder. */
  const durakKaydet = async (e) => {
    e.preventDefault()
    if (!durakTaslak) return

    setDurakKaydediliyor(true)
    try {
      const kaydedilen = await durakEkleIstek({
        ad: durakForm.ad.trim(),
        guzergahId: Number(durakForm.guzergahId),
        aciklama: durakForm.aciklama.trim() || null,
        wkt: durakTaslak.wkt,
        // sira GÖNDERİLMİYOR: sunucu durağı hattin SONUNA ekliyor. Haritadan
        // durak koyarken en doğal davranış bu; araya sokmak isteyen zaten
        // yönetim panelinde sürükle-bırakla taşıyor.
      }, goLogin)

      drawSourceRef.current.clear()
      setDurakTaslak(null)
      popupKapat()
      await ulasimiYukle()
      bildir('ok', `"${kaydedilen.ad}" durağı "${kaydedilen.guzergahAdi}" hattına eklendi.`)

      // Güzergah seçimini KORUYORUZ (form sıfırlanırken de): operatör
      // genelde aynı hattın duraklarını arka arkaya giriyor.
      setDurakForm((onceki) => ({ ...BOS_DURAK_FORMU, guzergahId: onceki.guzergahId }))

      // Odağı aktif araç düğmesine geri ver: arka arkaya durak girilebilsin.
      aracGrubuRef.current?.querySelector('.tool-btn.active')?.focus()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    } finally {
      setDurakKaydediliyor(false)
    }
  }

  /** Bilgi kutucuğundan durağı siler. */
  const duragiSil = async (dto) => {
    if (!window.confirm(
      `"${dto.ad}" durağı silinecek.\n\n`
      + 'Kayıt veritabanından tamamen silinmez; is_deleted = true yapılarak '
      + 'gizlenir ve hattın kalan durakları yeniden sıralanır.\n\nDevam edilsin mi?',
    )) return

    try {
      await durakSilIstek(dto.id, goLogin)
      popupKapat()
      await ulasimiYukle()
      bildir('ok', `"${dto.ad}" durağı silindi.`)
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    }
  }

  /** Bilgi kartında düzenleme moduna geç: mevcut değerlerle formu doldur. */
  const poiDuzenlemeyeBasla = () => {
    if (!secili || secili.tip !== POI) return
    setPoiDuzenle({
      isim: secili.dto.isim,
      kategoriId: String(secili.dto.kategoriId),
      // Sunucu planı çözülmüş hâlde gönderiyor; Ödev 12'den kalan, planı
      // olmayan kayıtlarda eski metinden türetiyor (bkz. PoiService.DtoyaCevir).
      // Yine de null gelebilir — o zaman varsayılan planla açılıyor.
      plan: planiNormallestir(secili.dto.mesaiPlani),
    })
  }

  /**
   * POI'nin adını, kategorisini ve mesai saatini günceller.
   * KONUM gönderilmiyor (wkt yok): sunucu boş wkt'de geometriye dokunmuyor.
   * POI'yi taşımak için silip yeniden eklemek gerekiyor — bir ilgi noktasının
   * yeri, adı gibi sık düzeltilen bir bilgi değil.
   */
  const poiDuzenlemeKaydet = async (e) => {
    e.preventDefault()
    if (!secili || !poiDuzenle) return

    setPoiKaydediliyor(true)
    try {
      await poiGuncelle(secili.dto.id, {
        isim: poiDuzenle.isim.trim(),
        kategoriId: Number(poiDuzenle.kategoriId),
        mesaiPlani: poiDuzenle.plan,
      }, goLogin)

      popupKapat()
      await poileriYukle()
      bildir('ok', `"${poiDuzenle.isim}" güncellendi.`)
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    } finally {
      setPoiKaydediliyor(false)
    }
  }

  /**
   * Durak düzenlemesini kaydeder.
   *
   * wkt GÖNDERİLMİYOR → konum değişmiyor. Konum bu formdan düzenlenmiyor
   * çünkü haritada zaten SÜRÜKLENEBİLİR; koordinatı elle yazdırmak hem
   * hataya açık hem de gereksiz.
   */
  const durakDuzenlemeKaydet = async (e) => {
    e.preventDefault()
    if (!secili || !durakDuzenle) return

    setDurakDuzenleKaydediliyor(true)
    try {
      await durakGuncelleIstek(secili.dto.id, {
        ad: durakDuzenle.ad.trim(),
        guzergahId: Number(durakDuzenle.guzergahId),
        aciklama: durakDuzenle.aciklama.trim() || null,
      }, goLogin)

      popupKapat()
      await ulasimiYukle()
      bildir('ok', `"${durakDuzenle.ad}" güncellendi.`)
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    } finally {
      setDurakDuzenleKaydediliyor(false)
    }
  }

  const poiyiSil = async (dto) => {
    if (!window.confirm(
      `"${dto.isim}" POI'si silinecek.\n\n`
      + 'Kayıt veritabanından tamamen silinmez; is_deleted = true yapılarak '
      + 'gizlenir. Geri alma yönetim panelinden yapılır.\n\nDevam edilsin mi?',
    )) return

    try {
      await poiSil(dto.id, goLogin)
      popupKapat()
      await poileriYukle()
      bildir('ok', `"${dto.isim}" silindi.`)
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') bildir('hata', err.message)
    }
  }

  /** Listeden bir POI'ye tıklanınca haritada ona yaklaş. */
  const poiyeOdaklan = (dto) => odaklanFeature(poiKaynagiRef.current?.getFeatureById(`poi-${dto.id}`))

  /** Fareyle üzerine gelinen POI'yi haritada vurgula. */
  const poiyiVurgula = (dto) => vurgulaFeature(poiKaynagiRef.current?.getFeatureById(`poi-${dto.id}`))

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
  /**
   * Aktif/pasif değiştirme (Ödev 3 / Görev 1'deki is_active kolonunun arayüzdeki karşılığı).
   * Silmekten farkı: kayıt listede kalır, sadece "Pasif" rozetiyle işaretlenir.
   */
  const handleAktiflik = async (type, dto) => {
    try {
      await aktiflikDegistir(DRAW_TYPES[type].endpoint, dto.id, !dto.isActive, goLogin)
      popupKapat()
      await yukle()
      bildir('ok', dto.isActive ? `"${dto.name}" askıya alındı.` : `"${dto.name}" aktif edildi.`)
    } catch (err) {
      await hatayiGoster(err)
    }
  }

  const handleDelete = async (type, dto) => {
    // Ödev 5 / Madde 5: silmeden önce onay penceresi.
    // Onaya rağmen "Geri al" bildirimini de bırakıyoruz — onay yanlışlıkla
    // tıklamayı engeller, geri alma ise yanlış kaydı seçmeyi kurtarır.
    const onay = window.confirm(
      `"${dto.name}" kaydı silinecek.\n\n`
      + 'Kayıt veritabanından tamamen silinmez; is_deleted = true yapılarak '
      + 'gizlenir ve istenirse geri alınabilir.\n\nDevam edilsin mi?',
    )
    if (!onay) return

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
            await hatayiGoster(err)
          }
        },
      })
    } catch (err) {
      await hatayiGoster(err)
    }
  }

  // ------------------------------------------------------------------------
  //  Ödev 5 / Madde 4 — detay popup'ından güncelleme
  // ------------------------------------------------------------------------

  /** Düzenleme moduna geç: mevcut değerlerle formu doldur. */
  const duzenlemeyeBasla = () => {
    if (!secili) return
    setDuzenleForm({
      name: secili.dto.name,
      color: secili.dto.color || DRAW_TYPES[secili.tip].color,
    })
  }

  const duzenlemeVazgec = () => setDuzenleForm(null)

  /**
   * Ad, renk ve GEOMETRİYİ kaydeder.
   *
   * Geometri, haritada o an duran feature'dan okunuyor: kullanıcı "Düzenle"
   * dedikten sonra köşeleri sürükleyip şekli değiştirebiliyor ve kaydettiğinde
   * güncel hâli gidiyor. Böylece tek ekrandan hem öznitelik hem konum
   * güncellenebiliyor — ödevin istediği bu.
   */
  const duzenlemeKaydet = async (e) => {
    e.preventDefault()
    if (!secili || !duzenleForm) return

    setKaydediliyor(true)
    try {
      const feature = featureBul(secili.tip, secili.dto)
      const govde = {
        name: duzenleForm.name.trim(),
        description: secili.dto.description || null,
        imageUrl: secili.dto.imageUrl || null,
        color: duzenleForm.color,
        // Haritadaki güncel geometri → 4326 WKT
        wkt: feature ? geometryToWkt(feature.getGeometry()) : null,
      }

      await guncelle(DRAW_TYPES[secili.tip].endpoint, secili.dto.id, govde, goLogin)

      setDuzenleForm(null)
      popupKapat()
      await yukle()
      bildir('ok', `"${govde.name}" güncellendi.`)
    } catch (err) {
      await hatayiGoster(err)
    } finally {
      setKaydediliyor(false)
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

  const handleLogout = () => {
    clearSession()
    navigate('/login', { replace: true })
  }

  // ---- Ödev 6: yetkiye göre arayüz ----
  //
  // "Yönetim" düğmesi hem GÖRÜNÜRLÜĞÜNÜ hem HEDEFİNİ menü tanımından
  // alıyor (Ödev 16 düzeltmesi). Önceden yalnızca Kullanıcı/Rol yetkisine
  // bakıyor ve sabit /admin/users'a gidiyordu; Ulaşım Operatörü'nün ikisi de
  // yok ama "Güzergah Yönetimi" ekranı var — düğme hiç görünmediği için
  // kendi paneline ulaşamıyordu. (Aynı boşluk POI Yönetimi için de vardı.)
  const yonetimAdresi = ilkYonetimEkrani(yetkilerim)
  const guncelleyebilir = yetkiVar(YETKILER.kayitGuncelleme)

  /**
   * Kullanıcının HERHANGİ bir çizim aracı var mı?
   *
   * Ödev 7'den beri kural "yetkisi olmayan araç hiç görünmesin" idi ama bu,
   * araçları TEK TEK gizliyordu: hiçbir yetkisi olmayan bir Kullanıcı
   * hesabında "Çizim Araçları" başlığı, boş bir düğme kutusu, "Çizime
   * başlamak için bir araç seçin" yönergesi ve kısayol künyesi kalıyordu —
   * hepsi yapamayacağı bir işi anlatıyordu.
   *
   * Artık bölümün ÇİZİMLE İLGİLİ tarafı topluca gizleniyor. Analiz ayrı
   * tutuluyor: Kullanıcı rolünün "Analiz Çalıştırma" yetkisi var, yani
   * çizemeyen biri yine de envanter sayabiliyor.
   */
  const cizimAraciVar = DRAW_TYPE_KEYS.some(aracKullanilabilir)
    || aracKullanilabilir(POI)
    // Ödev 16: "Durak Ekle" de bu bölümde duruyor. Listeye eklenmeseydi
    // Ulaşım Operatörü — çizim ve POI yetkisi OLMADIĞI için — bölümü hiç
    // göremez, dolayısıyla kendi aracına da ulaşamazdı.
    || aracKullanilabilir(DURAK)
    || guncelleyebilir

  /** Analiz düğmelerinden biri görünecek mi? */
  const analizAraciVar = yetkiVar(YETKILER.analizCalistirma)

  /** Bölümün gösterilecek hiçbir şeyi kalmadıysa başlığı da çizmiyoruz. */
  const aracBolumuVar = cizimAraciVar || analizAraciVar
  const silebilir = yetkiVar(YETKILER.kayitSilme)

  // Kapalı araçların insan okuyabilir adları — panelde tek satırlık açıklama için.
  const kilitliAraclar = [
    ...DRAW_TYPE_KEYS.filter((k) => !aracKullanilabilir(k)).map((k) => DRAW_TYPES[k].label),
    ...(guncelleyebilir ? [] : ['Düzenle']),
    ...(yetkiVar(YETKILER.analizCalistirma) ? [] : ['Envanter Analizi']),
    ...(yetkiVar(YETKILER.poiEkleme) ? [] : ['POI Ekle']),
    ...(yetkiVar(YETKILER.durakEkleme) ? [] : ['Durak Ekle']),
  ]

  // Termometrede ne görünecek? Fare haritadayken CANLI okuma, haritadan
  // çıkınca son SABİTLENEN ölçüm. İkisi de yoksa kutu yönerge gösteriyor —
  // eskiden hiç çizilmiyordu ve "termometre yok" gibi duruyordu.
  const gosterilenOlcum = anlikOlcum ?? sabitOlcum

  /**
   * Ölçülen noktanın ÇEVRESİ (Ödev 11).
   *
   * Yoğunluk değeri tek başına yetmiyordu: boş bir yere tıklayan kullanıcı
   * "0.00" görüyor ve bunu "sonuç vermedi" diye okuyordu. Oysa 0.00 da bir
   * sonuç — eksik olan, o sonucu okunur kılan bağlam.
   *
   * Mesafe `ol/sphere`'in getDistance'ı ile küresel olarak hesaplanıyor;
   * düzlem geometrisiyle (3857'de Öklit) hesaplasaydık Web Mercator'ın
   * enleme bağlı gerilmesi yüzünden kuzeyde ciddi şekilde şişerdi.
   */
  const olcumCevresi = useMemo(() => {
    if (!gosterilenOlcum || gosterilenOlcum.durum !== 'ok') return null

    const kaynak = sourcesRef.current.Point
    const noktalar = kaynak?.getFeatures() ?? []
    if (noktalar.length === 0) return null

    const merkez = toLonLat(gosterilenOlcum.koordinat)
    const yaricap = gosterilenOlcum.yaricapMetre ?? 0

    let enYakin = null
    let icerideki = 0

    noktalar.forEach((f) => {
      const geometri = f.getGeometry()
      if (!geometri || geometri.getType() !== 'Point') return

      const mesafe = getDistance(merkez, toLonLat(geometri.getCoordinates()))
      if (yaricap > 0 && mesafe <= yaricap) icerideki += 1
      if (!enYakin || mesafe < enYakin.mesafe) {
        enYakin = { mesafe, ad: f.get('ad') || 'isimsiz kayıt' }
      }
    })

    return { enYakin, icerideki, yaricap }
  }, [gosterilenOlcum])


  const toplamKayit = DRAW_TYPE_KEYS.reduce((t, k) => t + records[k].length, 0)
  // Uyarı yalnızca m:ss biçiminde ve son dakikada yanar. "3 gün" / "5 sa"
  // gibi değerlerde ':' yok; Number(...) NaN döner ve karşılaştırma zaten
  // false olur — yine de niyeti kodda açıkça yazıyoruz.
  const sureAzaldi = remaining.includes(':') && Number(remaining.split(':')[0]) < 1

  // ------------------------------------------------------------------------
  //  Arayüz
  // ------------------------------------------------------------------------
  return (
    <div className="map-layout">
      <header className="topbar">
        {/* Emoji yerine CSS'teki marka noktası + ince SVG ikonlar:
            karışık emoji kullanımı arayüzü amatör gösteriyordu. */}
        <span className="topbar-title">Harita Uygulaması</span>
        <span className="topbar-right">
          <span className={`badge${sureAzaldi ? ' badge-uyari' : ''}`}>
            <SaatIkonu /> {remaining}
          </span>
          {/* Ödev 11: hesap değiştirici. Rozet bir menü — aynı bileşen
              yönetim panelinde de kullanılıyor. */}
          <HesapSecici />
          {/* Karanlık / aydınlık tema. Hesap seçicinin yanında: ikisi de
              "oturumun kendisiyle" ilgili, kayıtlarla değil. */}
          <TemaDugmesi />
          {/* Ödev 6: yalnızca yönetim yetkisi olan kullanıcıya gösterilir */}
          {yonetimAdresi && (
            <button
              type="button"
              className="logout-btn yonetim"
              onClick={() => navigate(yonetimAdresi)}
            >
              <RolIkonu size={13} /> Yönetim
            </button>
          )}
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

          {/* ================= ARAMA BARI (Ödev 13 / Madde 2) =================
              "Ekrana Google Maps benzeri bir Arama Barı ekleyin."

              Sağ paneldeki kutudan haritanın ÜSTÜNE taşındı: aranan şey
              haritada bir yer olduğu için arama kutusunun da haritanın
              üstünde durması bekleniyor — Google Maps, Yandex ve OSM'in
              hepsinde böyle.

              Tek kutu, İKİ kaynak: kayıtlı POI'lerimiz ve OpenStreetMap
              yerleri. Kullanıcı "Millî Kütüphane" yazarken bunun kayıtlı bir
              POI mi yoksa haritadaki bir yer mi olduğunu bilmek zorunda değil.

              YETKİ KONTROLÜ YOK — bilerek: ödev "bu arama özelliği Kullanıcı
              (User) rolüne de açık olmalıdır" diyor. Sunucudaki uç da yetki
              istemiyor (bkz. PoiController.Ara). */}
          <div className="arama-bari">
            <div className="arama-kutusu">
              <input
                ref={aramaGirdiRef}
                type="search"
                value={arama}
                onChange={(e) => { setArama(e.target.value); setAramaAcik(true) }}
                onFocus={() => setAramaAcik(true)}
                // Odak kaybında listeyi HEMEN kapatmıyoruz: sonuca tıklamak da
                // odağı kaybettiriyor ve liste tıklamadan önce yok oluyordu.
                onBlur={() => setTimeout(() => setAramaAcik(false), 180)}
                placeholder="POI veya yer ara…  ( / )"
                aria-label="POI veya yer ara"
              />
              {arama && (
                <button type="button" className="arama-temizle" onClick={aramayiTemizle}
                        aria-label="Aramayı temizle">×</button>
              )}
            </div>

            {aramaAcik && arama.trim().length >= EN_AZ_ARAMA && (
              <div className="arama-panel">
                {araniyor && <p className="arama-durum">Aranıyor…</p>}

                {/* ---- Kayıtlı POI'ler ---- */}
                {poiSonuclari.length > 0 && (
                  <>
                    <p className="arama-grup">Kayıtlı POI'ler</p>
                    <ul className="arama-sonuclari">
                      {poiSonuclari.map((p) => (
                        <li key={`poi-${p.id}`}>
                          <button type="button" onClick={() => poiAramaSonucuSec(p)}>
                            <span
                              className="arama-nokta"
                              style={{ background: kategoriRengi(p.kategoriId) }}
                              aria-hidden="true"
                            />
                            <span className="arama-ad">
                              <strong>
                                {p.isim}
                                {!p.isActive && <em className="pasif-etiket"> (pasif)</em>}
                                {/* Şu an açık mı? Sunucu hesaplayıp gönderiyor —
                                    arama sonucunda planın kendisi taşınmıyor.
                                    Planı olmayan kayıtta rozet HİÇ çıkmıyor. */}
                                {p.suAnAcik === true && <em className="durum-rozet acik">Açık</em>}
                                {p.suAnAcik === false && <em className="durum-rozet kapali">Kapalı</em>}
                              </strong>
                              <small>
                                {p.kategoriYolu}
                                {p.mesaiDurumu ? ` · ${p.mesaiDurumu}` : ''}
                              </small>
                            </span>
                          </button>
                        </li>
                      ))}
                    </ul>
                  </>
                )}

                {/* ---- OpenStreetMap yerleri ---- */}
                {sonuclar.length > 0 && (
                  <>
                    <p className="arama-grup">Haritada yerler</p>
                    <ul className="arama-sonuclari">
                      {sonuclar.map((yer) => (
                        <li key={yer.id}>
                          <button type="button" onClick={() => aramaSonucuSec(yer)}>
                            <TipIkonu tip="Point" size={14} />
                            <span className="arama-ad">
                              <strong>{yer.ad}</strong>
                              <small>{yer.tamAd}</small>
                            </span>
                          </button>

                          {/* Ödev 13 / Madde 4: aranan yeri doğrudan POI
                              yapmanın kestirmesi. Konum, ad ve kategori
                              kendiliğinden doluyor. Yalnızca POI ekleme
                              yetkisi olana gösteriliyor. */}
                          {yetkiVar(YETKILER.poiEkleme) && (
                            <button
                              type="button"
                              className="arama-poi-ekle"
                              onClick={() => aramaSonucuSec(yer, true)}
                              title="Bu yeri POI olarak ekle (kategori otomatik seçilir)"
                            >
                              <PoiIkonu size={13} /> POI
                            </button>
                          )}
                        </li>
                      ))}
                    </ul>
                  </>
                )}

                {aramaHatasi && <p className="arama-durum hata">{aramaHatasi}</p>}

                {!araniyor && poiSonuclari.length === 0 && sonuclar.length === 0 && (
                  <p className="arama-durum">Sonuç bulunamadı.</p>
                )}

                <p className="arama-ipucu muted">
                  POI sonucuna tıklayınca harita oraya yaklaşır ve bilgi paneli açılır.
                  Yer sonucu taslak nokta olarak hazırlanır.
                </p>
              </div>
            )}
          </div>

          {/* ---------- Isı haritası lejantı (Ödev 9 / Madde 2) ----------
              Ödev 9'da GeoServer'ın GetLegendGraphic servisinden RESİM olarak
              alınıyordu; SLD değişince kendiliğinden güncellenmesi avantajdı
              ama Arial yazı tipi ve kalın renk kutularıyla arayüzün yanında
              yamalı duruyordu. Artık aynı rampadan (wms.js → ISI_GRADYANI)
              CSS ile çiziliyor; SLD ile eşlemesi elle korunuyor ve iki dosyada
              da birbirine işaret eden uyarı var.
              Yalnızca ısı haritası açıkken gösteriliyor. */}
          {isiAcik && (
            <div className="isi-panel">
              {/* Termometre (Ödev 11): imlecin altındaki / sabitlenen değer.
                  Kayıtlı bir nokta olması gerekmiyor — yüzeyin herhangi bir
                  yeri ölçülebilir.

                  aria-live gezinirken KAPALI: canlı okuma saniyede birkaç kez
                  değiştiği için ekran okuyucu sürekli konuşurdu. Fare
                  ayrıldığında sabitlenen ölçüm bir kez duyuruluyor. */}
              <div className="termometre" role="status" aria-live={anlikOlcum ? 'off' : 'polite'}>
                <div className="termometre-ust">
                  <span className="termometre-etiket">
                    {gosterilenOlcum?.durum === 'hazirlaniyor'
                      ? 'Ölçüm hazırlanıyor'
                      : anlikOlcum ? 'İmleç altında' : sabitOlcum ? 'Sabitlenen' : 'Ölçüm'}
                  </span>
                  {/* Tam sıfır bir ARIZA değil, bir CEVAP: bu çevrede kayıt
                      yok. Çıplak "0.00" bunu anlatmıyor, "sonuç gelmedi" gibi
                      okunuyordu — lejantın en solu da zaten "veri yok" diyor,
                      kutu da aynı dili konuşsun. */}
                  <strong
                    className={`termometre-deger${
                      gosterilenOlcum?.durum === 'ok' && gosterilenOlcum.deger === 0 ? ' bos' : ''
                    }`}
                  >
                    {gosterilenOlcum?.durum !== 'ok'
                      ? '—'
                      : gosterilenOlcum.deger === 0
                        ? 'veri yok'
                        : gosterilenOlcum.deger.toFixed(2)}
                  </strong>
                </div>

                {/* Çubuk lejantla AYNI gradyanı kullanıyor — ikisi de
                    wms.js → ISI_GRADYANI'dan besleniyor, ayrı ayrı yazılsaydı
                    biri güncellenip diğeri unutulabilirdi. */}
                <div className="termometre-cubuk" style={{ background: ISI_GRADYANI }}>
                  {gosterilenOlcum?.durum === 'ok' && (
                    <span
                      className="termometre-isaret"
                      style={{ left: `${Math.min(100, Math.max(0, gosterilenOlcum.deger * 100))}%` }}
                    />
                  )}
                </div>

                <p className="termometre-konum">
                  {gosterilenOlcum
                    ? toLonLat(gosterilenOlcum.koordinat).map((n) => n.toFixed(4)).join(' · ')
                    : 'İmleci haritada gezdirin; tıklarsanız ölçüm sabitlenir.'}
                </p>

                {/* Değerin BAĞLAMI: "0.00" tek başına "sonuç yok" gibi
                    okunuyordu. En yakın kaydın uzaklığı, sıfırın neden sıfır
                    olduğunu da söylüyor. */}
                {olcumCevresi && (
                  <p className="termometre-cevre">
                    <span>
                      {olcumCevresi.yaricap > 0 && (
                        <>
                          <strong>{olcumCevresi.icerideki}</strong> nokta
                          {' '}~{Math.round(olcumCevresi.yaricap / 1000)} km içinde
                        </>
                      )}
                    </span>
                    {olcumCevresi.enYakin && (
                      <span>
                        en yakın: {olcumCevresi.enYakin.ad}
                        {' · '}{Math.round(olcumCevresi.enYakin.mesafe / 1000)} km
                      </span>
                    )}
                  </p>
                )}
              </div>

              <figure className="isi-lejant">
                <figcaption>
                  Yoğunluk
                  <span>0 – 1</span>
                </figcaption>
                <div
                  className="isi-lejant-cubuk"
                  style={{ background: ISI_GRADYANI }}
                  role="img"
                  aria-label="Yoğunluk ölçeği: solda veri yok, sağda en yoğun"
                />
                <div className="isi-lejant-etiket">
                  <span>veri yok</span>
                  <span>seyrek</span>
                  <span>en yoğun</span>
                </div>
              </figure>
            </div>
          )}

          {/* Açılış sahnesi. Haritanın ÜSTÜNE biner ama pointer-events: none olduğu
              için tıklamalar haritaya geçer — böylece "atla" davranışı çalışır. */}
          {uzaySahnesi && (
            <div className={`uzay-sahnesi ${uzaySahnesi}`} aria-hidden="true">
              {/* Uzay: tüm alanı kaplar, ortasındaki dairesel delikten aşağısı görünür */}
              <div className="uzay-katmani" />

              {/* GEZEGEN YÜZEYİ (mavi-yeşil dünya).
                  Deliğin içini dolduruyor; iniş başlayınca soluyor ve altındaki
                  gerçek harita ortaya çıkıyor. Gerekçe: Dunya.jsx başlığı. */}
              <Dunya solgun={uzaySahnesi === 'inis'} />

              {/* Küresel hacim: sol üstten ışık, sağ altta gölge + atmosfer
                  halkası. Gezegenin ÜSTÜNDE duruyor ki ışık hem okyanusa hem
                  karaya birlikte düşsün — altında kalsaydı düz bir çıkartma
                  gibi görünürdü. */}
              <div className="kure-isik" />
            </div>
          )}

          <div className="harita-araclari">
            <button type="button" className="harita-btn" onClick={turkiyeyeDon}
                    title="Türkiye görünümüne dön">
              <svg viewBox="0 0 20 20" aria-hidden="true">
                <path d="M10 2.5 2.5 9h2v8h4v-5h3v5h4V9h2L10 2.5Z" />
              </svg>
              <span>Türkiye</span>
            </button>

            {/* Açılış sahnesini istediğin zaman tekrar oynat */}
            <button type="button" className="harita-btn" onClick={() => sahneyiOynat(false)}
                    title="Açılış animasyonunu tekrar oynat (uzaydan Türkiye'ye iniş)">
              <DunyaIkonu />
              <span>Dünya</span>
            </button>
          </div>

          {/* POPUP — EN SONDA OLMALI.
              OpenLayers Overlay, bu div'i DOM'dan alıp kendi kapsayıcısına taşır.
              Artık React'in çocuk listesiyle gerçek DOM uyuşmadığı için, React
              bu div'in ÖNÜNE yeni bir kardeş eklemeye çalışırsa
              "insertBefore: node is not a child of this node" hatası alır ve
              tüm sayfa çöker. Popup'ı en sona koyarak React'in ondan sonra
              hiçbir şey eklemesi gerekmemesini garantiliyoruz.
              (Uzay sahnesi koşullu olarak eklenip kaldırıldığı için bu şart.) */}
          <div ref={popupElement} className="harita-popup">
            {/* ÖZNİTELİK GİRİŞ POP-UP'I (Ödev 4 / Görev 2)
                drawend anında açılır; İsim ve Renk zorunlu alanlardır. */}
            {pending && (
              <form className="popup-form" onSubmit={handleSave}>
                <div className="popup-baslik">
                  <span className="dot" style={{ background: form.color }} />
                  <strong>Yeni {DRAW_TYPES[pending.type].label}</strong>
                  <button type="button" className="popup-kapat" onClick={vazgec}
                          aria-label="Vazgeç">×</button>
                </div>

                <p className="popup-ozet">{pending.ozet}</p>

                <label htmlFor="p-ad">İsim *</label>
                <input
                  id="p-ad"
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  placeholder="Örn: Anıtkabir"
                  maxLength={200}
                  required
                  autoFocus
                />

                <label>Renk *</label>
                <div className="renk-secici">
                  {RENK_SECENEKLERI.map((r) => (
                    <button
                      key={r.deger}
                      type="button"
                      className={`renk-nokta${form.color === r.deger ? ' secili' : ''}`}
                      style={{ background: r.deger }}
                      title={r.ad}
                      aria-label={r.ad}
                      aria-pressed={form.color === r.deger}
                      onClick={() => setForm({ ...form, color: r.deger })}
                    />
                  ))}
                  {/* Hazır renkler yetmezse tarayıcının renk seçicisi */}
                  <input
                    type="color"
                    className="renk-ozel"
                    value={form.color}
                    onChange={(e) => setForm({ ...form, color: e.target.value })}
                    title="Özel renk seç"
                  />
                </div>

                <label htmlFor="p-aciklama">Açıklama</label>
                <input
                  id="p-aciklama"
                  value={form.description}
                  onChange={(e) => setForm({ ...form, description: e.target.value })}
                  placeholder="İsteğe bağlı"
                  maxLength={1000}
                />

                <label htmlFor="p-gorsel">Görsel adresi</label>
                <input
                  id="p-gorsel"
                  type="url"
                  value={form.imageUrl}
                  onChange={(e) => setForm({ ...form, imageUrl: e.target.value })}
                  placeholder="https://..."
                  maxLength={500}
                />

                <details className="popup-wkt">
                  <summary>WKT (EPSG:4326)</summary>
                  <code>{pending.wkt}</code>
                </details>

                <div className="popup-eylemler">
                  <button type="submit" className="btn-primary"
                          disabled={saving || !form.name.trim()}>
                    {saving ? 'Kaydediliyor…' : 'Kaydet'}
                  </button>
                  <button type="button" className="btn-ghost" onClick={vazgec} disabled={saving}>
                    Vazgeç
                  </button>
                </div>
              </form>
            )}

            {/* ---------- Ödev 12: POI GİRİŞ FORMU ----------
                Çizim formundan farkı: renk/görsel yok, kategori ve mesai var.
                Aynı popup kutusunu paylaşıyorlar; hangisinin açılacağına
                taslak durumları karar veriyor. */}
            {/* ---------- Ödev 16: YENİ DURAK formu ----------
                Ödev metni: "Durak ismi girilsin ve mevcut güzergahlar dropdown
                üzerinden seçilsin." Form tam olarak bu ikisini istiyor;
                konum zaten haritadan geldi, sırayı sunucu veriyor. */}
            {durakTaslak && (
              <form className="popup-form" onSubmit={durakKaydet}>
                <div className="popup-baslik">
                  <span
                    className="dot"
                    style={{
                      background: guzergahlar.find(
                        (g) => String(g.id) === durakForm.guzergahId,
                      )?.renk ?? '#7a7f87',
                    }}
                  />
                  <strong>Yeni Durak</strong>
                  <button type="button" className="popup-kapat" onClick={vazgec}
                          aria-label="Vazgeç">×</button>
                </div>

                <p className="popup-ozet">{durakTaslak.ozet}</p>

                {guzergahlar.length === 0 ? (
                  // Hiç hat yokken durak eklenemez (1-N ilişkinin gereği).
                  // Boş bir açılır liste göstermek yerine ne yapılacağını söylüyoruz.
                  <p className="popup-ipucu">
                    Henüz güzergah tanımlanmamış. Durak bir hatta ait olmak zorunda —
                    önce <strong>Yönetim › Güzergah Yönetimi</strong> ekranından bir hat açın.
                  </p>
                ) : (
                  <>
                    <label htmlFor="durak-ad">Durak adı *</label>
                    <input
                      id="durak-ad"
                      value={durakForm.ad}
                      onChange={(e) => setDurakForm({ ...durakForm, ad: e.target.value })}
                      placeholder="Örn: Kızılay"
                      maxLength={150}
                      required
                      autoFocus
                    />

                    <label htmlFor="durak-guzergah">Güzergah *</label>
                    <select
                      id="durak-guzergah"
                      value={durakForm.guzergahId}
                      onChange={(e) => setDurakForm({ ...durakForm, guzergahId: e.target.value })}
                      required
                    >
                      <option value="" disabled>— güzergah seçin —</option>
                      {guzergahlar.map((g) => (
                        // Pasif hat KAPALI: sunucu da reddediyor, düğmeye
                        // basıp 400 almaktansa listede seçilemez olması iyi.
                        <option key={g.id} value={g.id} disabled={!g.isActive}>
                          {g.ad} ({g.durakSayisi} durak){!g.isActive ? ' — pasif' : ''}
                        </option>
                      ))}
                    </select>

                    <label htmlFor="durak-aciklama">Açıklama</label>
                    <input
                      id="durak-aciklama"
                      value={durakForm.aciklama}
                      onChange={(e) => setDurakForm({ ...durakForm, aciklama: e.target.value })}
                      placeholder="Peron, aktarma notu…"
                      maxLength={500}
                    />

                    <p className="popup-ipucu">
                      Durak, seçilen hattın <strong>sonuna</strong> ekleniyor.
                      Sırayı değiştirmek için Güzergah Yönetimi ekranında
                      sürükle-bırak kullanın.
                    </p>

                    <div className="popup-eylemler">
                      <button type="submit" className="btn-primary" disabled={durakKaydediliyor}>
                        {durakKaydediliyor ? 'Kaydediliyor…' : 'Kaydet'}
                      </button>
                      <button type="button" className="btn-ghost" onClick={vazgec}
                              disabled={durakKaydediliyor}>
                        Vazgeç
                      </button>
                    </div>
                  </>
                )}
              </form>
            )}

            {poiTaslak && (
              <form className="popup-form" onSubmit={poiKaydet}>
                <div className="popup-baslik">
                  <span className="dot" style={{ background: POI_RENGI }} />
                  <strong>Yeni POI</strong>
                  <button type="button" className="popup-kapat" onClick={vazgec}
                          aria-label="Vazgeç">×</button>
                </div>

                <p className="popup-ozet">{poiTaslak.ozet}</p>

                {/* Ödev 13 / Madde 4: seçilen yer çözülürken kullanıcı,
                    alanların kendiliğinden dolacağını bilsin. */}
                {yerCozuluyor && <p className="popup-ipucu">Seçilen yer tanınıyor…</p>}

                <label htmlFor="poi-ad">İsim *</label>
                <input
                  id="poi-ad"
                  value={poiForm.isim}
                  onChange={(e) => setPoiForm({ ...poiForm, isim: e.target.value })}
                  placeholder="Örn: Hacı Arif Bey Lokantası"
                  maxLength={200}
                  required
                  autoFocus
                />

                <label htmlFor="poi-kategori">Kategori *</label>
                <select
                  id="poi-kategori"
                  value={poiForm.kategoriId}
                  onChange={(e) =>
                    setPoiForm({ ...poiForm, kategoriId: e.target.value, oneri: null })}
                  required
                >
                  <option value="" disabled>— kategori seçin —</option>
                  {poiKategorileri.map((k) => (
                    <option key={k.id} value={k.id}>
                      {' '.repeat(k.seviye * 4)}{k.seviye > 0 ? '└ ' : ''}{k.ad}
                    </option>
                  ))}
                </select>

                {/* Otomatik seçilen kategori GEREKÇESİYLE gösteriliyor
                    (Ödev 13 / Madde 4). Sessizce dolan bir alan kullanıcıyı
                    "ben mi seçtim?" diye düşündürür; ata-çocuk yolunu yazmak
                    ödevin istediği hiyerarşiyi de ekrana taşıyor. Kullanıcı
                    listeden başka bir şey seçince bu satır kayboluyor. */}
                {poiForm.oneri && String(poiForm.oneri.kategoriId) === poiForm.kategoriId && (
                  <p className="popup-ipucu oneri">
                    Otomatik seçildi: <strong>{poiForm.oneri.tamYol}</strong>
                    {' '}<small>({poiForm.oneri.gerekce})</small>
                  </p>
                )}

                {poiKategorileri.length === 0 && (
                  <p className="popup-uyari">
                    Tanımlı kategori yok. Yönetici, <strong>POI Yönetimi</strong> ekranından
                    kategori eklemeli.
                  </p>
                )}

                <label>Mesai saatleri</label>
                <MesaiPlaniAlanlari
                  plan={poiForm.plan}
                  degistir={(plan) => setPoiForm({ ...poiForm, plan })}
                  onEk="poi"
                  tatiller={resmiTatiller?.tatiller}
                  tatillerUyarisi={tatilUyarisi}
                />

                <details className="popup-wkt">
                  <summary>WKT (EPSG:4326)</summary>
                  <code>{poiTaslak.wkt}</code>
                </details>

                <div className="popup-eylemler">
                  <button type="submit" className="btn-primary"
                          disabled={poiKaydediliyor || !poiForm.isim.trim() || !poiForm.kategoriId}>
                    {poiKaydediliyor ? 'Kaydediliyor…' : 'Kaydet'}
                  </button>
                  <button type="button" className="btn-ghost" onClick={vazgec}
                          disabled={poiKaydediliyor}>
                    Vazgeç
                  </button>
                </div>
              </form>
            )}

            {/* ---------- Ödev 12: POI BİLGİ PANELİ ----------
                "Eklenen POI'ye tıklandığında bilgi paneli açılmalıdır."
                Geometri kartından ayrı bir blok: gösterilen alanlar farklı
                (kategori yolu, mesai saatleri, ekleyen kullanıcı). */}
            {/* ---------- Ödev 16: DURAK BİLGİ KUTUCUĞU ----------
                Ödev metni: "Duraklara tıklandığında bilgi kutucuğu açılmalıdır."
                Kart hattı ve sırayı öne çıkarıyor — bir durağa bakan kişinin ilk
                sorusu "hangi hat, kaçıncı durak". */}
            {secili && secili.tip === DURAK && !pending && !poiTaslak && !durakTaslak && (
              <>
                <div className="popup-baslik">
                  <span className="dot" style={{ background: secili.dto.guzergahRengi }} />
                  <strong>{secili.dto.ad}</strong>
                  <button type="button" className="popup-kapat" onClick={popupKapat}
                          aria-label="Kapat">×</button>
                </div>

                <p className="durak-hat">
                  <GuzergahIkonu size={14} />
                  <strong>{secili.dto.guzergahAdi}</strong>
                  <span className="durak-sira-rozet"
                        style={{ background: secili.dto.guzergahRengi }}>
                    {secili.dto.sira}. durak
                  </span>
                </p>

                {durakDuzenle ? (
                  <form className="popup-duzenle" onSubmit={durakDuzenlemeKaydet}>
                    <label htmlFor="durak-d-ad">Durak adı</label>
                    <input
                      id="durak-d-ad"
                      value={durakDuzenle.ad}
                      onChange={(e) => setDurakDuzenle({ ...durakDuzenle, ad: e.target.value })}
                      maxLength={150}
                      required
                      autoFocus
                    />

                    <label htmlFor="durak-d-hat">Güzergah</label>
                    {/* Durağı başka bir hatta TAŞIMAK da buradan: sunucu sırayı
                        yeniden hesaplıyor ve iki hattın da rotasını yeniliyor. */}
                    <select
                      id="durak-d-hat"
                      value={durakDuzenle.guzergahId}
                      onChange={(e) => setDurakDuzenle({ ...durakDuzenle, guzergahId: e.target.value })}
                    >
                      {guzergahlar.map((g) => (
                        <option key={g.id} value={g.id}>{g.ad}</option>
                      ))}
                    </select>

                    <label htmlFor="durak-d-aciklama">Açıklama</label>
                    <input
                      id="durak-d-aciklama"
                      value={durakDuzenle.aciklama}
                      onChange={(e) => setDurakDuzenle({ ...durakDuzenle, aciklama: e.target.value })}
                      maxLength={500}
                      placeholder="peron, aktarma bilgisi…"
                    />

                    <div className="popup-eylemler">
                      <button type="submit" className="btn-primary"
                              disabled={durakDuzenleKaydediliyor}>
                        {durakDuzenleKaydediliyor ? 'Kaydediliyor…' : 'Kaydet'}
                      </button>
                      <button type="button" className="btn-ghost"
                              onClick={() => setDurakDuzenle(null)}>
                        Vazgeç
                      </button>
                    </div>

                    <p className="tool-hint muted">
                      Konumu değiştirmek için durağı <strong>haritada sürükleyin</strong>.
                    </p>
                  </form>
                ) : (
                  <>
                    <dl className="popup-detay">
                      {secili.dto.aciklama && (
                        <>
                          <dt>Açıklama</dt>
                          <dd>{secili.dto.aciklama}</dd>
                        </>
                      )}

                      <dt>Durum</dt>
                      <dd>{secili.dto.isActive ? 'Aktif' : 'Pasif'}</dd>

                      <dt>Ekleyen</dt>
                      <dd>{secili.dto.kullaniciAdi ?? 'bilinmiyor'}</dd>

                      <dt>Eklenme</dt>
                      <dd>{new Date(secili.dto.createdDate).toLocaleString('tr-TR')}</dd>
                    </dl>

                    {/* Düzenle/sil yalnızca yetkisi olana görünüyor. Asıl kontrol
                        sunucuda: sahibi olmayan bir operatör 400 alır. */}
                    {(yetkiVar(YETKILER.guzergahYonetimi) || yetkiVar(YETKILER.durakEkleme)) && (
                      <div className="popup-eylemler">
                        <button
                          type="button"
                          className="btn-ghost"
                          onClick={() => setDurakDuzenle({
                            ad: secili.dto.ad,
                            guzergahId: secili.dto.guzergahId,
                            aciklama: secili.dto.aciklama ?? '',
                          })}
                        >
                          Düzenle
                        </button>
                        <button type="button" className="btn-ghost sil"
                                onClick={() => duragiSil(secili.dto)}>
                          <SilIkonu /> Durağı sil
                        </button>
                      </div>
                    )}
                  </>
                )}
              </>
            )}

            {secili && secili.tip === POI && !pending && !poiTaslak && (
              <>
                <div className="popup-baslik">
                  <span className="dot" style={{ background: POI_RENGI }} />
                  <strong>{secili.dto.isim}</strong>
                  <button type="button" className="popup-kapat" onClick={popupKapat}
                          aria-label="Kapat">×</button>
                </div>

                {!secili.dto.isActive && <span className="pasif-rozet">Pasif</span>}

                {/* Ödev 13 iyileştirmesi — mesai planının somut karşılığı.
                    Özet metin ("Pzt-Cum 09:00-18:00") insana bilgi verir ama
                    "şimdi gidebilir miyim?" sorusunu cevaplamaz. Bu rozet onu
                    cevaplıyor ve tarayıcının saatiyle canlı hesaplanıyor. */}
                {seciliPoiDurumu && !poiDuzenle && (
                  <p className={`mesai-durum${seciliPoiDurumu.acik ? ' acik' : ' kapali'}`}>
                    <span className="mesai-durum-nokta" aria-hidden="true" />
                    {seciliPoiDurumu.aciklama}
                  </p>
                )}

                {poiDuzenle ? (
                  <form className="popup-duzenle" onSubmit={poiDuzenlemeKaydet}>
                    <label htmlFor="poi-d-ad">İsim</label>
                    <input
                      id="poi-d-ad"
                      value={poiDuzenle.isim}
                      onChange={(e) => setPoiDuzenle({ ...poiDuzenle, isim: e.target.value })}
                      maxLength={200}
                      required
                      autoFocus
                    />

                    <label htmlFor="poi-d-kategori">Kategori</label>
                    <select
                      id="poi-d-kategori"
                      value={poiDuzenle.kategoriId}
                      onChange={(e) => setPoiDuzenle({ ...poiDuzenle, kategoriId: e.target.value })}
                      required
                    >
                      {poiKategorileri.map((k) => (
                        <option key={k.id} value={k.id}>
                          {' '.repeat(k.seviye * 4)}{k.seviye > 0 ? '└ ' : ''}{k.ad}
                        </option>
                      ))}
                    </select>

                    <label>Mesai saatleri</label>
                    <MesaiPlaniAlanlari
                      plan={poiDuzenle.plan}
                      degistir={(plan) => setPoiDuzenle({ ...poiDuzenle, plan })}
                      onEk="poi-d"
                      tatiller={resmiTatiller?.tatiller}
                      tatillerUyarisi={tatilUyarisi}
                    />

                    <div className="popup-eylemler">
                      <button type="submit" className="btn-primary"
                              disabled={poiKaydediliyor || !poiDuzenle.isim.trim()}>
                        {poiKaydediliyor ? 'Kaydediliyor…' : 'Kaydet'}
                      </button>
                      <button type="button" className="btn-ghost"
                              onClick={() => setPoiDuzenle(null)} disabled={poiKaydediliyor}>
                        Vazgeç
                      </button>
                    </div>
                  </form>
                ) : (
                  <>
                    <dl className="popup-bilgi">
                      <dt>Kategori</dt>
                      <dd>{secili.dto.kategoriYolu}</dd>
                      <dt>Mesai</dt>
                      <dd>
                        {secili.dto.mesaiSaatleri || '—'}
                        {/* Ödev 13 / Madde 3'ün somut karşılığı: "resmî
                            tatillerde kapalı" işaretli bir POI'ye BUGÜN
                            resmî tatilde bakıldığında durum doğrudan
                            söyleniyor. Kullanıcı tatil takvimini kendisi
                            kontrol etmek zorunda kalmıyor. */}
                        {bugunkuTatil && secili.dto.mesaiPlani?.resmiTatilKapali && (
                          <span className="tatil-rozet">
                            Bugün {bugunkuTatil.ad}
                            {bugunkuTatil.yarimGun ? ' — öğleden sonra kapalı' : ' — kapalı'}
                          </span>
                        )}
                      </dd>
                      <dt>Konum</dt>
                      <dd>{secili.ozet}</dd>
                      <dt>Ekleyen</dt>
                      <dd>{secili.dto.kullaniciAdi ?? 'bilinmiyor'}</dd>
                      <dt>Eklendi</dt>
                      <dd>{new Date(secili.dto.createdDate).toLocaleString('tr-TR')}</dd>
                      {secili.dto.modifiedDate && (
                        <>
                          <dt>Güncellendi</dt>
                          <dd>{new Date(secili.dto.modifiedDate).toLocaleString('tr-TR')}</dd>
                        </>
                      )}
                    </dl>

                    <div className="popup-eylemler">
                      {/* Düzenleme/silme düğmeleri yalnızca POI yetkisi olana
                          gösteriliyor. Kaydın SAHİBİ olup olmadığına sunucu
                          karar veriyor; aynı kuralı burada tekrarlamak, biri
                          güncellenip diğeri unutulan iki kural üretirdi. */}
                      {yetkiVar(YETKILER.poiEkleme) && (
                        <button type="button" className="btn-primary" onClick={poiDuzenlemeyeBasla}>
                          Düzenle
                        </button>
                      )}
                      <button
                        type="button"
                        className="btn-ghost"
                        onClick={() => poiyeOdaklan(secili.dto)}
                        title="Haritada bu POI'ye yaklaş"
                      >
                        Yakınlaş
                      </button>
                      {yetkiVar(YETKILER.poiEkleme) && (
                        <button
                          type="button"
                          className="btn-ghost sil"
                          onClick={() => poiyiSil(secili.dto)}
                          title="Sil (soft delete)"
                        >
                          <SilIkonu />
                        </button>
                      )}
                    </div>
                  </>
                )}
              </>
            )}

            {/* ---------- GEOMETRİ BİLGİ KARTI ----------

                KOŞUL BİR İZİN LİSTESİ, YASAK LİSTESİ DEĞİL.

                Önceden `secili.tip !== POI` yazıyordu ve bu bir hataya yol
                açtı: Ödev 16'da DURAK tipi eklendiğinde koşul güncellenmedi,
                dolayısıyla bir durağa tıklandığında BU kart da çizilmeye
                çalıştı ve `DRAW_TYPES['Durak']` undefined olduğu için
                `.color` okunurken bütün ekran çöktü.

                Yasak listesi, her yeni tip eklendiğinde burayı hatırlamayı
                gerektiriyor — hatırlanmadığında da derleme değil, ÇALIŞMA
                ZAMANI hatası veriyor. İzin listesi bunu yapıya bağlıyor:
                kart yalnızca gerçekten bir çizim tipi seçildiğinde çiziliyor,
                yeni tipler kendiliğinden dışarıda kalıyor. */}
            {secili && DRAW_TYPE_KEYS.includes(secili.tip) && !pending && !poiTaslak && (
              <>
                <div className="popup-baslik">
                  <span className="dot"
                        style={{ background: secili.dto.color || DRAW_TYPES[secili.tip].color }} />
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

                {!secili.dto.isActive && <span className="pasif-rozet">Pasif</span>}

                <dl className="popup-bilgi">
                  <dt>Tip</dt>
                  <dd>{DRAW_TYPES[secili.tip].label}</dd>
                  <dt>Konum</dt>
                  <dd>{secili.ozet}</dd>
                  <dt>Eklendi</dt>
                  <dd>{new Date(secili.dto.insertedDate).toLocaleString('tr-TR')}</dd>
                  {secili.dto.modifiedDate && (
                    <>
                      <dt>Güncellendi</dt>
                      <dd>{new Date(secili.dto.modifiedDate).toLocaleString('tr-TR')}</dd>
                    </>
                  )}
                </dl>

                {/* ---- Ödev 5 / Madde 4: düzenleme formu ---- */}
                {duzenleForm ? (
                  <form className="popup-duzenle" onSubmit={duzenlemeKaydet}>
                    <label htmlFor="d-ad">İsim</label>
                    <input
                      id="d-ad"
                      value={duzenleForm.name}
                      onChange={(e) => setDuzenleForm({ ...duzenleForm, name: e.target.value })}
                      maxLength={200}
                      required
                      autoFocus
                    />

                    <label>Renk</label>
                    <div className="renk-secici">
                      {RENK_SECENEKLERI.map((r) => (
                        <button
                          key={r.deger}
                          type="button"
                          className={`renk-nokta${duzenleForm.color === r.deger ? ' secili' : ''}`}
                          style={{ background: r.deger }}
                          title={r.ad}
                          aria-label={r.ad}
                          aria-pressed={duzenleForm.color === r.deger}
                          onClick={() => setDuzenleForm({ ...duzenleForm, color: r.deger })}
                        />
                      ))}
                      <input
                        type="color"
                        className="renk-ozel"
                        value={duzenleForm.color}
                        onChange={(e) => setDuzenleForm({ ...duzenleForm, color: e.target.value })}
                        title="Özel renk seç"
                      />
                    </div>

                    <p className="duzenle-ipucu">
                      Konumu değiştirmek için <kbd>D</kbd> ile <strong>Düzenle</strong> aracını
                      açıp köşeleri sürükleyin, sonra buradan kaydedin.
                    </p>

                    <div className="popup-eylemler">
                      <button type="submit" className="btn-primary"
                              disabled={kaydediliyor || !duzenleForm.name.trim()}>
                        {kaydediliyor ? 'Kaydediliyor…' : 'Kaydet'}
                      </button>
                      <button type="button" className="btn-ghost" onClick={duzenlemeVazgec}
                              disabled={kaydediliyor}>
                        Vazgeç
                      </button>
                    </div>
                  </form>
                ) : (
                  <div className="popup-eylemler">
                    {/* Ödev 6: yetkisi olmayana düğme HİÇ gösterilmiyor.
                        Kilitli göstermek yerine gizlemek, dar popup'ta
                        kullanılamayacak düğmeyle yer kaplamamak için. */}
                    {guncelleyebilir && (
                      <button type="button" className="btn-primary" onClick={duzenlemeyeBasla}>
                        Düzenle
                      </button>
                    )}
                    <button
                      type="button"
                      className="btn-ghost"
                      onClick={() => odaklan(secili.tip, secili.dto)}
                      title="Haritada bu kayda yaklaş"
                    >
                      Yakınlaş
                    </button>
                    {silebilir && (
                      <button
                        type="button"
                        className="btn-ghost sil"
                        onClick={() => handleDelete(secili.tip, secili.dto)}
                        title="Sil (soft delete — geri alınabilir)"
                      >
                        <SilIkonu />
                      </button>
                    )}
                  </div>
                )}
              </>
            )}
          </div>

        </div>


        <aside className="side-panel">
          {/* ---------- ⓪ POI LEJANDI (Ödev 13 / Madde 1) ----------
              Arama kutusu buradan haritanın üstündeki bara taşındı (Madde 2);
              yerini POI stillerinin lejandı aldı.

              Lejant, "her kategori için ayrı SLD" işinin ekrandaki karşılığı:
              hangi simgenin/rengin hangi kategoriye ait olduğu yazmasa
              kullanıcı haritadaki mor yıldızın ne anlama geldiğini bilemezdi.

              Yalnızca stiller GERÇEKTEN devredeyken gösteriliyor. GeoServer
              kapalıyken POI'ler tek renk çiziliyor ve o lejant yalan olurdu. */}
          {poiWmsAktif && (
            <section className="panel-section">
              <h2>POI Kategorileri</h2>

              {/* KATLI DURUYOR: stiller artık kategori tablosundan üretildiği
                  için liste sabit beş satır değil — seed'de on dört, gerçek bir
                  kurulumda daha da uzun olabilir. Açık bıraksaydık panelin
                  yarısını kaplar, altındaki çizim araçlarını aşağı iterdi.
                  Özet satırı kaç kategori olduğunu katlıyken de söylüyor. */}
              {/* AKORDİYON VARSAYILAN AÇIK (open):
                  POI'ler artık kapalı başlıyor ve kullanıcının onları
                  açabileceği tek yer burası. Katlı bıraksaydık "POI'ler nerede?"
                  sorusunun cevabı bir tık daha uzakta olurdu. */}
              <details className="akordiyon panel-akordiyon" open>
                <summary>
                  Gösterilecek kategoriler
                  <span className="akordiyon-ozet">
                    {poiKategoriSecimi.size > 0
                      ? `${poiKategoriSecimi.size} / ${poiStilleri.length}`
                      : 'kapalı'}
                  </span>
                </summary>

                <div className="akordiyon-govde">
                  {/* Toplu seçim: on beş kategoriyi tek tek açmak zorunda
                      bırakmak, "hepsini görmek" isteyen kullanıcıya on beş tık
                      demek olurdu. */}
                  <div className="poi-toplu">
                    <button
                      type="button"
                      className="btn-ghost kucuk"
                      onClick={() => setPoiKategoriSecimi(
                        new Set(poiStilleri.map((st) => st.kategoriId)),
                      )}
                      disabled={poiKategoriSecimi.size === poiStilleri.length}
                    >
                      Hepsini göster
                    </button>
                    <button
                      type="button"
                      className="btn-ghost kucuk"
                      onClick={() => setPoiKategoriSecimi(new Set())}
                      disabled={poiKategoriSecimi.size === 0}
                    >
                      Hiçbirini
                    </button>
                  </div>

                  <ul className="poi-lejant secilebilir">
                    {poiStilleri.map((st) => (
                      <li key={st.stil} title={st.tamYol}>
                        <label className="poi-kategori-anahtar">
                        <input
                          type="checkbox"
                          checked={poiKategoriSecimi.has(st.kategoriId)}
                          onChange={() => poiKategorisiDegistir(st.kategoriId)}
                          aria-label={`${st.ad} kategorisini haritada göster`}
                        />
                        {/* Ödev 15: lejant artık renkli bir nokta değil, HARİTADAKİ
                            SİMGENİN TA KENDİSİ. Çizim parçaları stil ucundan
                            geliyor, yani GeoServer'ın bastığı SVG ile birebir aynı
                            kaynak. Kopyalanmış bir path listesi olsaydı ikisi
                            zamanla ayrışabilirdi. */}
                        {st.ikonParcalari?.length ? (
                          <span
                            className="poi-simge ikon"
                            aria-hidden="true"
                            dangerouslySetInnerHTML={{
                              __html: ikonSvg(st.ikonParcalari, st.renk, 18),
                            }}
                          />
                        ) : (
                          <span className={`poi-simge ${st.sekil}`} style={{ background: st.renk }} />
                        )}
                        <span className="poi-kategori-ad">{st.ad}</span>
                        </label>
                      </li>
                    ))}
                  </ul>
                </div>
              </details>

              {/* Boş durum, bir uyarı değil BİLGİ: harita bilerek temiz
                  açılıyor. Kullanıcı "POI'ler kayboldu mu?" diye düşünmesin. */}
              {poiKategoriSecimi.size === 0 && (
                <p className="arama-durum">
                  POI'ler <strong>gizli</strong>. Görmek istediğiniz kategorileri
                  yukarıdan işaretleyin.
                </p>
              )}

              <p className="arama-ipucu muted">
                Simgeleri GeoServer çiziyor: <strong>her kategori için ayrı bir SLD</strong>,
                kategori tablosundan üretiliyor. POI adları yakınlaşınca (z ≈ 12–13)
                nokta üzerinde beliriyor.
                <br />
                Simge, kategoriye <strong>yönetim panelinden</strong> seçiliyor; seçilmemişse
                üst kategorininki miras alınıyor.
              </p>

              {bugunkuTatil && (
                <p className="arama-durum">
                  Bugün <strong>{bugunkuTatil.ad}</strong>
                  {bugunkuTatil.yarimGun ? ' (yarım gün)' : ''} — resmî tatilde kapalı
                  işaretli POI'ler kapalı.
                </p>
              )}
            </section>
          )}

          {/* ---------- ① ÇİZİM ARAÇLARI ----------
              Bölümün TAMAMI koşullu: hiçbir çizim ve analiz yetkisi olmayan
              bir kullanıcıda başlık bile çıkmıyor. Önceden boş bir düğme
              kutusu ve "Çizime başlamak için bir araç seçin" yönergesi
              kalıyordu — yapamayacağı bir işi anlatan ölü bir bölüm.

              Başlık da duruma göre değişiyor: yalnızca analiz yetkisi olan
              bir Kullanıcı hesabında "Çizim Araçları" yazmak yanlış olurdu. */}
          {aracBolumuVar && (
          <section className="panel-section">
            <h2>{cizimAraciVar ? 'Çizim Araçları' : 'Analiz'}</h2>

            {cizimAraciVar && (
            <div className="tool-group" ref={aracGrubuRef}>
              {/*
                Ödev 7 / ek madde: yetkisi olmayan araç KİLİTLİ değil, HİÇ YOK.
                Önce soluklaştırıp kilitliyorduk; "yetkisi yoksa butonları hiç
                gösterme" istendiği için filtreye çevrildi. Kullanıcı artık
                kullanamayacağı bir düğmeyle hiç karşılaşmıyor; hangi araçların
                gizlendiği aşağıdaki not satırında yazıyor.
              */}
              {DRAW_TYPE_KEYS.filter(aracKullanilabilir).map((key) => (
                <button
                  key={key}
                  type="button"
                  className={`tool-btn${activeTool === key ? ' active' : ''}`}
                  onClick={() => aracSec(key)}
                  aria-pressed={activeTool === key}
                  title={`${DRAW_TYPES[key].label} çiz`}
                >
                  <span className="tool-icon"><TipIkonu tip={key} /></span>
                  {DRAW_TYPES[key].label}
                </button>
              ))}
            </div>
            )}

            {/* Ödev 12: POI ekleme aracı — OPERATÖRÜN aracı.
                Ayrı satırda çünkü çizim tiplerinden biri değil: nokta koyar
                ama farklı bir tabloya, kategori ve mesai bilgisiyle. */}
            {aracKullanilabilir(POI) && (
              <button
                type="button"
                className={`tool-btn genis poi${activeTool === POI ? ' active' : ''}`}
                onClick={() => aracSec(POI)}
                aria-pressed={activeTool === POI}
                title="Haritaya kategorili bir ilgi noktası (POI) ekle"
              >
                <span className="tool-icon"><PoiIkonu /></span>
                POI Ekle
              </button>
            )}

            {/* Ödev 16 / Madde 2: "Durak Ekle" aracı — ulaşım operatörünün aracı.
                POI Ekle'den ayrı bir satır ve ayrı bir YETKİ: ödev notu, ulaşım
                rolünün POI ekleyememesini istiyor. İki araç aynı yetkiye bağlı
                olsaydı birini vermek diğerini de açardı. */}
            {aracKullanilabilir(DURAK) && (
              <button
                type="button"
                className={`tool-btn genis durak${activeTool === DURAK ? ' active' : ''}`}
                onClick={() => aracSec(DURAK)}
                aria-pressed={activeTool === DURAK}
                title="Haritaya durak ekle ve bir güzergaha bağla"
              >
                <span className="tool-icon"><DurakIkonu /></span>
                Durak Ekle
              </button>
            )}

            {/* Düzenleme aracı ayrı bir satırda: çizim yapmıyor, var olanı değiştiriyor */}
            {guncelleyebilir && (
              <button
                type="button"
                className={`tool-btn genis${activeTool === DUZENLE ? ' active' : ''}`}
                onClick={() => aracSec(DUZENLE)}
                aria-pressed={activeTool === DUZENLE}
                title="Kaydedilmiş geometrileri sürükleyerek düzenle"
              >
                <span className="tool-icon"><DuzenleIkonu /></span>
                Düzenle
              </button>
            )}

            {/* Envanter Analizi (Ödev 4 / Görev 3) — çizer, saymaya yarar, KAYDETMEZ */}
            {yetkiVar(YETKILER.analizCalistirma) && (
              <button
                type="button"
                className={`tool-btn genis analiz${activeTool === ANALIZ ? ' active' : ''}`}
                onClick={() => aracSec(ANALIZ)}
                aria-pressed={activeTool === ANALIZ}
                title="Geçici poligon çizip altında kalan envanteri say"
              >
                <span className="tool-icon"><AnalizIkonu /></span>
                Envanter Analizi
              </button>
            )}

            {/* Isı Haritası Analizi (Ödev 9 / Madde 2) — çizim yapmaz, açılıp
                kapanan bir GÖSTERİM. Yoğunluğu GeoServer hesaplıyor. */}
            {yetkiVar(YETKILER.analizCalistirma) && geoDurum?.ayakta && (
              <button
                type="button"
                className={`tool-btn genis isi${isiAcik ? ' active' : ''}`}
                onClick={() => setIsiAcik((a) => !a)}
                aria-pressed={isiAcik}
                title="Noktaların konum yoğunluğunu GeoServer'da hesaplat"
              >
                <span className="tool-icon"><IsiIkonu /></span>
                Isı Haritası Analizi
              </button>
            )}

            {/* Konum Analizi (Ödev 14) — paneli açıp kapatan düğme.
                GeoServer'a bağlı DEĞİL (Isı Haritası Analizi'nin aksine):
                yüzeyi kendi sunucumuz hesaplıyor, GeoServer kapalıyken de
                çalışıyor. "Kullanıcı" rolünün de bu yetkisi var, yani ödevin
                istediği gibi salt görüntüleyen hesap da analiz yapabiliyor. */}
            {yetkiVar(YETKILER.analizCalistirma) && (
              <button
                type="button"
                className={`tool-btn genis konum${konumPaneliAcik ? ' active' : ''}`}
                onClick={() => setKonumPaneliAcik((a) => !a)}
                aria-pressed={konumPaneliAcik}
                aria-expanded={konumPaneliAcik}
                title="Alan seç, kriter ver, ağırlıklı uygunluk haritası üret"
              >
                <span className="tool-icon"><IsiIkonu /></span>
                Konum Analizi
              </button>
            )}

            {isiAcik && (
              <p className="tool-hint">
                Yoğunluk yüzeyi <strong>GeoServer'da</strong> üretiliyor
                (SLD içindeki <code>gs:Heatmap</code>). Değerler her görüntü için
                <strong> 0–1</strong> aralığına ölçekleniyor.
                <br />
                Haritada <strong>herhangi bir yere</strong> tıklayın — kayıtlı
                nokta olması gerekmiyor, o noktanın değeri sağ altta çıkar.
              </p>
            )}

            {!cizimAraciVar ? null : activeTool === POI ? (
              <p className="tool-hint">
                Haritada POI'nin yerine tıklayın; açılan formda <strong>isim</strong>,
                <strong> kategori</strong> ve <strong>mesai saatleri</strong> girin.
                <br />
                Kategoriler yönetim panelindeki <strong>POI Yönetimi</strong> ekranından tanımlanır.
              </p>
            ) : activeTool === ANALIZ ? (
              <p className="tool-hint">
                Analiz alanını çizin; köşeleri tıklayıp çift tıkla bitirin.
                <br />
                Alanla <strong>en ufak teması</strong> olan envanterler de sayılır.
                Bu poligon <strong>veritabanına kaydedilmez</strong>.
              </p>
            ) : activeTool === DUZENLE ? (
              <p className="tool-hint">
                Köşeleri sürükleyerek şekli değiştirin. Kenara tıklamak yeni köşe ekler,
                <kbd>Alt</kbd> + tıklamak köşeyi siler.
                <br />
                Fare mevcut köşelere yapışır; bırakınca değişiklik kaydedilir.
              </p>
            ) : activeTool === DURAK ? (
              <p className="tool-hint">
                Durağın yerine tıklayın; açılan formda <strong>durak adı</strong> girip
                <strong> güzergahı</strong> seçin.
                <br />
                Durak seçilen hattın <strong>sonuna</strong> eklenir; sırası
                <strong> Güzergah Yönetimi</strong> ekranından sürükle-bırakla değişir.
              </p>
            ) : activeTool === KONUM_ALAN ? (
              /* Ödev 14 — bu araç KENDİ İPUCUNU zaten panelde veriyor; burada
                 tekrar etmek yerine kullanıcıyı oraya yönlendiriyoruz.
                 Bu dal ŞART: aşağıdaki `DRAW_TYPES[activeTool]` yalnızca üç
                 çizim tipini tanıyor, dördüncü bir araç oraya düşerse ekran
                 komple hata sınırına çarpıyor (bir kez yaşandı). */
              <p className="tool-hint">
                Konum analizinin <strong>hedef bölgesini</strong> çiziyorsunuz.
                Köşeleri tıklayıp çift tıkla bitirin; kriterleri aşağıdaki
                <strong> Konum Analizi</strong> bölümünden vereceksiniz.
              </p>
            ) : DRAW_TYPES[activeTool] ? (
              <p className="tool-hint">
                {DRAW_TYPES[activeTool].hint}
                <br />
                <kbd>Esc</kbd> iptal · <kbd>⌫</kbd> son noktayı sil
              </p>
            ) : (
              <p className="tool-hint muted">Çizime başlamak için bir araç seçin.</p>
            )}

            {/* Kısayol künyesi — araç seçili değilken görünür, yer kaplamasın.
                Çizim yetkisi yoksa kısayolların da bir anlamı yok. */}
            {!activeTool && cizimAraciVar && (
              <p className="kisayol-kunye">
                <kbd>1</kbd><kbd>2</kbd><kbd>3</kbd> araçlar ·
                <kbd>P</kbd> POI · <kbd>D</kbd> düzenle · <kbd>A</kbd> analiz · <kbd>/</kbd> ara
              </p>
            )}

            {/* Araçlar gizlendi ama sebebi yazılı: aksi hâlde EKSİK bir menü
                "uygulama bozuk" gibi görünürdü.
                Not YALNIZCA en az bir aracı olan kullanıcıya gösteriliyor:
                hiçbir çizim yetkisi olmayan birine "Nokta, Çizgi, Poligon,
                Düzenle, POI Ekle gizlendi" demek, gizlemeye çalıştığımız
                listeyi metin olarak geri getirmek olurdu. */}
            {cizimAraciVar && kilitliAraclar.length > 0 && (
              <p className="tool-hint yetki-notu">
                Yetkiniz olmadığı için gizlendi: <strong>{kilitliAraclar.join(', ')}</strong>.
                <br />
                Yetkiler yönetim panelinden rolünüze veya hesabınıza eklenebilir.
              </p>
            )}

            {/* Ödev 7 / Madde 2: coğrafi sınır varsa kullanıcı bunu bilmeli.
                Haritadaki kesikli mavi çerçeve nerede çizebileceğini gösteriyor.
                Çizemeyen birine "çizim alanınız sınırlı" demek anlamsız. */}
            {cizimAraciVar && calismaAlani.kisitli && (
              <div className="calisma-alani">
                <strong>Çizim alanınız sınırlı.</strong> Haritadaki kesikli mavi
                çerçevenin dışına çizim yapamazsınız.
                <ul>
                  {calismaAlani.alanlar.map((alan) => (
                    <li key={alan.id}>{alan.name} · {alan.kaynak}</li>
                  ))}
                </ul>
              </div>
            )}
          </section>
          )}

          {/* ---------- ② ANALİZ SONUCU (Ödev 4 / Görev 3) ---------- */}
          {(analizYukleniyor || analizSonuc) && (
            <section className="panel-section analiz-sonuc">
              <h2>
                <span className="dot" style={{ background: ANALIZ_RENGI }} />
                Analiz Sonucu
              </h2>

              {analizYukleniyor ? (
                <p className="muted">Kesişim hesaplanıyor…</p>
              ) : (
                <>
                  <p className="analiz-toplam">
                    <strong>{analizSonuc.total}</strong> envanter
                    <span className="muted"> bu alanla kesişiyor</span>
                  </p>

                  <div className="analiz-kirilim">
                    {DRAW_TYPE_KEYS.map((key) => {
                      const sayi = key === 'Point' ? analizSonuc.pointCount
                        : key === 'LineString' ? analizSonuc.lineCount
                          : analizSonuc.polygonCount
                      return (
                        <span key={key} className="analiz-rozet">
                          <TipIkonu tip={key} size={13} />
                          {sayi}
                        </span>
                      )
                    })}
                  </div>

                  {analizSonuc.items.length > 0 && (
                    <ul className="analiz-liste">
                      {analizSonuc.items.map((item) => (
                        <li
                          key={`${item.geometryType}-${item.id}`}
                          onClick={() => analizOgesineGit(item)}
                          title="Haritada bu kayda yaklaş"
                        >
                          <span className="dot" style={{
                            background: item.color || DRAW_TYPES[item.geometryType].color,
                          }} />
                          <span className="analiz-ad">{item.name}</span>
                          <TipIkonu tip={item.geometryType} size={12} />
                        </li>
                      ))}
                    </ul>
                  )}

                  <button type="button" className="btn-ghost genis" onClick={analizTemizle}>
                    Analizi temizle
                  </button>
                </>
              )}
            </section>
          )}

          {/* ---------- ②b KONUM ANALİZİ (Ödev 14) ----------
              Ayrı bir bileşen: alan seçimi, kriterler ve puan toplamı kendi
              içinde yaşayan bir durum; MapPage yalnızca isteği ve sonucun
              haritaya çizimini biliyor. */}
          {konumPaneliAcik && yetkiVar(YETKILER.analizCalistirma) && (
            <KonumAnaliziPaneli
              kategoriler={poiKategorileri}
              stiller={poiStilleri}
              iller={iller}
              illerHatasi={illerHatasi}
              cizilenAlanWkt={konumAlanWkt}
              cizimAktif={activeTool === KONUM_ALAN}
              onCizimBaslat={konumAlaniCizmeyeBasla}
              onCizimIptal={konumAlaniCizmeyiBirak}
              sonuc={konumSonuc}
              yukleniyor={konumYukleniyor}
              hata={konumHata}
              onCalistir={konumAnaliziCalistir}
              onTemizle={konumAnaliziTemizle}
              onAdayaGit={konumAdayinaGit}
            />
          )}

          {/* ---------- Ödev 16: ULAŞIM ----------
              Hatların lejantı + tek katman anahtarı. Yönetim ekranına
              gitmeden "hangi renk hangi hat" sorusunu cevaplıyor.

              BÖLÜM HERKESE AÇIK: güzergah listesi yetki istemeyen bir uçtan
              geliyor ve "Ulaşım Kullanıcısı" rolünün de görmesi gerekiyor.
              Hiç hat yoksa bölüm hiç çizilmiyor — boş bir başlık, modülü
              kullanmayan bir kurulumda ölü yer kaplardı. */}
          {guzergahlar.length > 0 && (
            <section className="panel-section">
              <h2>
                <span className="tool-icon"><GuzergahIkonu /></span>
                Ulaşım
                <span className="sayi">{guzergahlar.length}</span>
              </h2>

              <label className="layer-toggle">
                <input
                  type="checkbox"
                  checked={ulasimGorunur}
                  onChange={(e) => setUlasimGorunur(e.target.checked)}
                />
                <span className="dot" style={{ background: '#7a7f87' }} />
                Duraklar ve hatlar
                <span className="sayi">
                  {guzergahlar.reduce((t, g) => t + g.durakSayisi, 0)}
                </span>
              </label>

              {/* Ödev 17: "Katman kontrolü gibi güzergahlar üzerinde de
                  aç/kapat yapılabilsin."

                  Her hat kendi onay kutusu. Lejant zaten buradaydı; onu
                  tıklanabilir yapmak, ayrı bir "hat filtresi" bölümü açmaktan
                  daha az yer kaplıyor ve renk/ad/sayı bağlamı yanında
                  duruyor. */}
              <ul className="guzergah-lejant">
                {guzergahlar.map((g) => {
                  const gorunur = !gizliGuzergahlar.has(g.id)
                  return (
                    <li key={g.id} className={`${g.isActive ? '' : 'pasif'}${gorunur ? '' : ' gizli'}`}>
                      <label className="guzergah-anahtar">
                        <input
                          type="checkbox"
                          checked={gorunur}
                          onChange={() => guzergahGorunurluguDegistir(g.id)}
                          aria-label={`${g.ad} hattını haritada göster`}
                        />
                        <span className="guzergah-cizgi" style={{ background: g.renk }} />
                        <span className="guzergah-ad">{g.ad}</span>
                        <span className="sayi">{g.durakSayisi}</span>
                      </label>

                      {/* Rota durumu — üç hâl, üçü de farklı bir şey söylüyor. */}
                      {g.rotaWkt && g.rotaGuncel && (
                        <small className="guzergah-rota">
                          {rotaOzeti(g)}
                        </small>
                      )}
                      {g.rotaWkt && !g.rotaGuncel && (
                        <small className="guzergah-rota eski" title="Duraklar değişti ama rota yenilenemedi.">
                          rota güncel değil
                        </small>
                      )}
                      {!g.rotaWkt && g.durakSayisi >= 2 && (
                        <small className="guzergah-rota yok" title="Hat kuş uçuşu çiziliyor.">
                          rota yok
                        </small>
                      )}
                    </li>
                  )
                })}
              </ul>

              <p className="tool-hint muted">
                Kesikli çizgi <strong>kuş uçuşu</strong> demek: o hat için OSRM
                rotası henüz üretilmemiş. Düz çizgi ve <strong>yön okları</strong>,
                yollara oturmuş gerçek güzergahı gösteriyor.
              </p>
            </section>
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

            {/* ---- Ödev 12: POI katmanı ----
                Burada TEK BİR AÇ/KAPAT YOK — bilerek.

                Önceden vardı ve varsayılan açıktı; dört binden fazla POI
                haritayı simgelerle dolduruyor, çizimler ve duraklar
                seçilemiyordu. Artık görünürlük KATEGORİ BAZINDA, "POI
                Kategorileri" bölümünden yönetiliyor (yukarıda).

                İki yerden yönetilseydi ("POI kapalı ama kategori açık" gibi)
                hangisinin kazandığı belirsiz kalırdı. Bu satır o yüzden
                yalnızca özet gösteriyor. */}
            <div className="layer-toggle ozet">
              <span className="dot" style={{ background: POI_RENGI }} />
              POI
              <span className="sayi">
                {poiKategoriSecimi.size === 0
                  ? 'gizli'
                  : `${poiKategoriSecimi.size} kategori`}
              </span>
            </div>

            {/* ---- Ödev 8: GeoServer WMS katmanı ----
                Yukarıdaki üç katman WFS'ten gelen VEKTÖR verisidir (tıklanabilir).
                Bu ise aynı verinin GeoServer'da boyanmış RESİM hâli. */}
            {geoDurum?.etkin && (
              <>
                <label
                  className="layer-toggle wms-toggle"
                  title={geoDurum.ayakta ? undefined : 'GeoServer kapalı'}
                >
                  <input
                    type="checkbox"
                    checked={wmsAcik}
                    disabled={!geoDurum.ayakta}
                    onChange={(e) => setWmsAcik(e.target.checked)}
                  />
                  <span className="dot wms" />
                  GeoServer WMS
                  <span className="sayi">resim</span>
                </label>
                <p className="katman-not">
                  {geoDurum.ayakta
                    ? 'Genel gösterim bu katmandan geliyor (sunucuda boyanmış resim). Üstteki üç vektör katmanı tıklama ve düzenleme için.'
                    : 'GeoServer’a ulaşılamıyor — katman açılamıyor.'}
                </p>
              </>
            )}

            {/* Veri kaynağı rozeti: "bu kayıtlar gerçekten GeoServer'dan mı
                geliyor?" sorusunun ekrandaki cevabı. */}
            {geoDurum && (
              <p className="veri-kaynagi">
                <span className={`kaynak-nokta${geoDurum.ayakta ? ' ayakta' : ''}`} />
                Veri kaynağı:{' '}
                <strong>{geoDurum.etkin ? 'GeoServer WFS' : 'PostGIS (doğrudan)'}</strong>
                {geoDurum.etkin && <span className="muted"> · {geoDurum.workspace}</span>}
              </p>
            )}
          </section>

          {/* ---------- ④ POI LİSTESİ (Ödev 12) ----------
              Kayıtlı geometrilerden AYRI bir bölüm: POI'ler sahibine göre
              süzülmüyor, yani buradaki liste haritadaki her POI'yi gösteriyor
              — kimin eklediğinden bağımsız. */}
          <section className="panel-section">
            <h2>
              <span className="dot" style={{ background: POI_RENGI }} />
              POI <span className="sayi">{poiler.length}</span>
            </h2>

            {poiler.length === 0 ? (
              <p className="bos-durum">
                <span className="bos-ikon"><PoiIkonu size={30} /></span>
                Henüz POI eklenmemiş.
                {yetkiVar(YETKILER.poiEkleme) && <> <kbd>P</kbd> ile eklemeye başlayın.</>}
              </p>
            ) : (
              <>
              <ul className="geom-list poi-list" onMouseLeave={temizleVurgu}>
                {poiler.slice(0, poiListeSiniri).map((dto) => (
                  <li
                    key={dto.id}
                    className={dto.isActive ? '' : 'pasif'}
                    onMouseEnter={() => poiyiVurgula(dto)}
                    onClick={() => poiyeOdaklan(dto)}
                  >
                    <div className="geom-bilgi">
                      <strong>{dto.isim}</strong>
                      {!dto.isActive && <span className="pasif-rozet">Pasif</span>}
                      <span className="poi-kategori">{dto.kategoriYolu}</span>
                      <span className="muted poi-alt">
                        {dto.mesaiSaatleri || 'mesai belirtilmemiş'}
                        {dto.kullaniciAdi && ` · ${dto.kullaniciAdi}`}
                      </span>
                    </div>
                  </li>
                ))}
              </ul>

              {/* Listenin kesildiğini SÖYLEMEK şart: sessiz kesme
                  "POI'lerim kaybolmuş" diye okunur. Haritada hepsi duruyor. */}
              {poiler.length > poiListeSiniri && (
                <p className="muted liste-siniri">
                  {poiler.length.toLocaleString('tr-TR')} POI'den ilk{' '}
                  {poiListeSiniri.toLocaleString('tr-TR')} tanesi listeleniyor —
                  aramak için üstteki arama barını kullanın.
                  <button
                    type="button"
                    className="btn-ghost"
                    onClick={() => setPoiListeSiniri((s) => s + POI_LISTE_ADIMI * 4)}
                  >
                    Daha fazla göster
                  </button>
                </p>
              )}
              </>
            )}
          </section>

          {/* ---------- ⑤ KAYITLI GEOMETRİLER ---------- */}
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
                  <TipIkonu tip={key} size={15} /> {records[key].length}
                </button>
              ))}
            </div>

            {kayitlarYukleniyor && records[activeTab].length === 0 ? (
              /* İSKELET: veri gelene kadar boş panel yerine yapı göster.
                 aria-hidden — ekran okuyucuya sahte satır okutmanın anlamı yok;
                 durumu aşağıdaki aria-live bölgesi bildiriyor. */
              <ul className="geom-list iskelet" aria-hidden="true">
                {[0, 1, 2].map((i) => (
                  <li key={i}>
                    <div className="geom-bilgi">
                      <span className="iskelet-satir" style={{ width: `${68 - i * 12}%` }} />
                      <span className="iskelet-satir kisa" />
                    </div>
                  </li>
                ))}
              </ul>
            ) : records[activeTab].length === 0 ? (
              <p className="bos-durum">
                <span className="bos-ikon"><TipIkonu tip={activeTab} size={30} /></span>
                Henüz {DRAW_TYPES[activeTab].label.toLowerCase()} kaydı yok.
              </p>
            ) : (
              <ul className="geom-list" onMouseLeave={temizleVurgu}>
                {records[activeTab].map((dto) => (
                  <li
                    key={dto.id}
                    className={dto.isActive ? '' : 'pasif'}
                    onMouseEnter={() => vurgula(activeTab, dto)}
                    onClick={() => odaklan(activeTab, dto)}
                  >
                    <div className="geom-bilgi">
                      <strong>{dto.name}</strong>
                      {!dto.isActive && <span className="pasif-rozet">Pasif</span>}
                      {dto.description && <span className="muted"> — {dto.description}</span>}
                      <code className="wkt-onizleme">{dto.wkt}</code>
                    </div>
                    {silebilir && (
                      <button
                        type="button"
                        className="sil-btn"
                        title="Sil"
                        onClick={(e) => { e.stopPropagation(); handleDelete(activeTab, dto) }}
                      >
                        <SilIkonu />
                      </button>
                    )}
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
