// ============================================================================
//  GeoServer WMS katmanları (Ödev 8 / Madde 2 · Ödev 9 / Madde 1-2)
//
//  WMS ile WFS'in farkı bu dosyada somutlaşıyor:
//
//    WFS  → sunucudan KOORDİNAT gelir. OpenLayers her kaydı ayrı bir feature
//           olarak tutar; tıklanabilir, düzenlenebilir, ölçülebilir.
//
//    WMS  → sunucudan hazır boyanmış PNG gelir. Tarayıcı içindeki tek tek
//           kayıtları GÖREMEZ, sadece resmi gösterir. Buna karşılık kayıt
//           sayısı ne olursa olsun aktarılan veri sabit kalır: bir resim.
//
//  Ödev 9'un iş bölümü: "genel gösterimlerde WMS, çizim/etkileşim
//  işlemlerinde WFS". Yani haritadaki genel görüntü aşağıdaki WMS
//  katmanından geliyor; vektör katmanları tıklama, düzenleme ve vurgulama
//  için duruyor.
//
//  DİKKAT — adres /api/geoserver/wms, doğrudan localhost:8080 DEĞİL.
//  Karolar backend'in vekili üzerinden geçiyor; sahiplik süzgecini (Ödev 5)
//  ve GeoServer kimlik bilgilerini orada tutuyoruz.
// ============================================================================

import TileLayer from 'ol/layer/Tile'
import ImageLayer from 'ol/layer/Image'
import TileWMS from 'ol/source/TileWMS'
import ImageWMS from 'ol/source/ImageWMS'
import TileState from 'ol/TileState'

import { authFetch } from './auth'

/** Backend'deki vekil uç. Vite proxy'si bunu .NET'e yönlendiriyor. */
const WMS_UCU = '/api/geoserver/wms'

/**
 * ISI RAMPASI — kehribar → mercan → erik.
 *
 * ⚠ geoserver/isi-haritasi.sld içindeki ColorMap ile AYNI olmalı. Renkleri
 * boyayan GeoServer; buradaki kopya yalnızca ARAYÜZ için (lejant çubuğu ve
 * termometre çubuğu). İkisini tek kaynaktan üretmenin yolu, lejantı yine
 * GeoServer'dan (GetLegendGraphic) resim olarak almaktı ve Ödev 9'da öyle
 * yapılmıştı — ama o resim Arial yazı tipi ve kalın renk kutularıyla
 * uygulamanın geri kalanının yanında yamalı duruyordu. Bilinçli takas:
 * lejant artık CSS ile çiziliyor, karşılığında iki dosyayı elle eşlemek
 * gerekiyor (SLD'de de bu dosyaya işaret eden bir uyarı var).
 *
 * Gökkuşağı rampası bilerek terk edildi: sıralı bir büyüklüğü ton
 * değiştirerek göstermek algısal olarak yanıltıcıdır.
 */
export const ISI_RAMPASI = [
  { deger: 0.00, renk: '#ffe3a8' },
  { deger: 0.25, renk: '#ffc46b' },
  { deger: 0.50, renk: '#f89551' },
  { deger: 0.75, renk: '#e05a45' },
  { deger: 1.00, renk: '#9c2350' },
]

/** Lejant ve termometre çubuğu için CSS gradyanı. */
export const ISI_GRADYANI = `linear-gradient(90deg, ${
  ISI_RAMPASI.map((d) => `${d.renk} ${Math.round(d.deger * 100)}%`).join(', ')
})`

/**
 * OpenLayers'ın karo yükleyicisi.
 *
 * Neden özel bir yükleyici gerekiyor? OpenLayers normalde karoyu
 * <img src="..."> ile çeker. <img> etiketine Authorization başlığı
 * EKLENEMEZ — tarayıcı böyle bir imkân vermiyor. Bizim vekilimiz ise
 * [Authorize] ile korunuyor, token olmadan 401 döner.
 *
 * Çözüm: karoyu fetch ile (token başlığıyla) indirip blob'a çeviriyoruz,
 * sonra blob'un geçici adresini <img>'e veriyoruz. Resim yüklenince adresi
 * serbest bırakıyoruz, yoksa her karo bellekte kalırdı.
 */
function karoYukleyici(onUnauthorized) {
  return async (tile, adres) => {
    const img = tile.getImage()

    try {
      const cevap = await authFetch(adres, {}, onUnauthorized)

      if (!cevap.ok) {
        // 503 → GeoServer kapalı. Karoyu hata durumuna alıyoruz;
        // OpenLayers o karoyu boş bırakır, harita çökmez.
        tile.setState(TileState.ERROR)
        return
      }

      const blobAdresi = URL.createObjectURL(await cevap.blob())
      img.addEventListener('load', () => URL.revokeObjectURL(blobAdresi), { once: true })
      img.addEventListener('error', () => URL.revokeObjectURL(blobAdresi), { once: true })
      img.src = blobAdresi
    } catch {
      tile.setState(TileState.ERROR)
    }
  }
}

/**
 * Kayıtların GENEL GÖSTERİMİ için WMS katmanı (Ödev 9 / Madde 1).
 *
 * @param {string[]} katmanlar Tam nitelikli katman adları — ["staj:vw_point", ...]
 * @param {() => void} onUnauthorized 401 gelirse çağrılır (oturumu kapat)
 * @returns {{ katman: TileLayer, kaynak: TileWMS }}
 */
export function wmsKatmaniOlustur(katmanlar, onUnauthorized) {
  const kaynak = new TileWMS({
    url: WMS_UCU,
    params: {
      LAYERS: katmanlar.join(','),
      FORMAT: 'image/png',
      // Saydamlık olmasaydı PNG'nin beyaz zemini altındaki OSM haritasını
      // tamamen kapatırdı.
      TRANSPARENT: true,
    },
    serverType: 'geoserver',
    // Karolar arası geçiş animasyonunu kapatıyoruz: her karo ayrı bir fetch
    // ile geldiği için soluklaşma efekti titrek görünüyor.
    transition: 0,
    tileLoadFunction: karoYukleyici(onUnauthorized),
  })

  const katman = new TileLayer({
    source: kaynak,
    visible: true,   // Ödev 9: genel gösterim WMS ile
    opacity: 0.9,
  })

  return { katman, kaynak }
}

// NOT — POI lejandı ARTIK BURADA DEĞİL.
//
// İlk uygulamada kategori renkleri burada sabit bir dizideydi (POI_LEJANTI) ve
// geoserver/poi-*.sld dosyalarıyla ELLE eşleniyordu. Kategori tablosu
// büyüdüğünde ya da bir kategorinin rengi değiştiğinde iki dosyayı birden
// güncellemek gerekiyordu; biri unutulsa lejant haritayla çelişirdi.
//
// Artık stiller kategori tablosundan üretiliyor ve renk/şekil bilgisi
// üreten tarafla birlikte geliyor: GET /api/poi/stiller. Lejant da, WMS
// isteğinin STYLES parametresi de aynı listeden besleniyor.

/**
 * POI katmanı — KATEGORİ BAŞINA AYRI SLD ile (Ödev 13 / Madde 1).
 *
 * Ödev "her POI kategorisi için ayrı bir Style oluşturun" diyor. Ayrı
 * stiller haritada nasıl BİR ARADA görünüyor? WMS aynı katmanı birden çok
 * kez istemeye izin veriyor:
 *
 *     LAYERS=staj:vw_poi,staj:vw_poi,…&STYLES=poi_yeme_icme,poi_saglik,…
 *
 * Her kopya kendi stiliyle çiziliyor; her stil de SLD içindeki Filter ile
 * kendi kategorisi dışındaki noktaları eliyor. Sonuç tek PNG'de kategoriye
 * göre farklı simge, renk ve — belirli bir zoom'dan sonra — POI adı.
 *
 * NEDEN TileWMS DEĞİL ImageWMS?
 * ETİKETLER yüzünden. 256×256 karolarla çalışsaydık her karo kendi içinde
 * etiketleniyor: karo sınırına denk gelen bir ad ya ikiye bölünüyor ya da
 * iki komşu karoda iki kez çiziliyor. GeoServer'ın çakışma çözücüsü (SLD'de
 * conflictResolution) yalnızca TEK bir istek içindeki etiketleri
 * görebiliyor. ImageWMS ekranın tamamını tek istekte alıyor: bütün adlar
 * aynı çakışma hesabına giriyor. (Isı haritası da aynı sebeple — orada da
 * karo başına ayrı normalleştirme sorun çıkarıyordu.)
 *
 * @param {string} katmanAdi Tam nitelikli katman — "staj:vw_poi"
 * @param {string[]} stiller Stil adları, çizim sırasında
 * @param {() => void} onUnauthorized 401 gelirse çağrılır
 */
export function poiKatmaniOlustur(katmanAdi, stiller, onUnauthorized) {
  const kaynak = new ImageWMS({
    url: WMS_UCU,
    params: {
      // Katman, stil SAYISI kadar tekrarlanıyor — eşleşme sıraya göre.
      LAYERS: stiller.map(() => katmanAdi).join(','),
      STYLES: stiller.join(','),
      FORMAT: 'image/png',
      TRANSPARENT: true,
    },
    serverType: 'geoserver',
    // Görünen alandan biraz taşarak iste: kenardaki etiketler kesilmesin.
    ratio: 1.2,
    imageLoadFunction: async (gorsel, adres) => {
      const img = gorsel.getImage()

      try {
        const cevap = await authFetch(adres, {}, onUnauthorized)
        if (!cevap.ok) return

        const blobAdresi = URL.createObjectURL(await cevap.blob())
        img.addEventListener('load', () => URL.revokeObjectURL(blobAdresi), { once: true })
        img.addEventListener('error', () => URL.revokeObjectURL(blobAdresi), { once: true })
        img.src = blobAdresi
      } catch {
        /* yükleme başarısız; OpenLayers görüntüyü boş bırakır */
      }
    },
  })

  const katman = new ImageLayer({
    source: kaynak,
    visible: true,
    // Çizim katmanlarının (0) ÜSTÜNDE, ısı haritasının (500) ALTINDA:
    // POI simgeleri çizimlerin altında kaybolmasın ama ısı yüzeyi açıkken
    // yoğunluk okunabilir kalsın.
    zIndex: 300,
  })

  return { katman, kaynak }
}

/**
 * ISI HARİTASI katmanı (Ödev 9 / Madde 2).
 *
 * Aynı WMS ucunu kullanıyor; tek fark STYLES parametresi. Yoğunluk hesabını
 * GeoServer yapıyor: SLD içindeki gs:Heatmap dönüşümü noktaları çizmeden
 * önce bir yoğunluk yüzeyine (raster) çeviriyor, RasterSymbolizer da onu
 * renklendiriyor. Tarayıcıya yine sadece PNG geliyor.
 *
 * Aynı işi istemcide yapmak tüm noktaları indirmeyi ve her karede yeniden
 * hesaplamayı gerektirirdi.
 *
 * @param {string} noktaKatmani Örn. "staj:vw_point" — ısı yalnızca noktalardan
 * @param {string} stil GeoServer'daki stil adı ("isi_haritasi")
 * @param {string} degerStili Gri tonlamalı ikizin stil adı ("isi_deger")
 * @param {() => void} onUnauthorized 401 gelirse çağrılır
 * @param {() => void} [onOlcumHazir] Değer resmi belleğe alındığında çağrılır;
 *        arayüz sabitlenmiş ölçümü bu anda tazeliyor (bkz. MapPage).
 */
export function isiHaritasiKatmaniOlustur(
  noktaKatmani, stil, degerStili, onUnauthorized, onOlcumHazir,
) {
  // ---- "Termometre" ölçümü (Ödev 11) ----
  //
  // Kullanıcı haritada KAYIT OLMAYAN bir noktaya tıklayınca da yoğunluk
  // değerini görmek istiyor. Denenen ve olmayan yol WMS GetFeatureInfo:
  // gs:Heatmap gibi bir rendering transformation için GeoServer değeri değil
  // grid'in tanımını döndürüyor.
  //
  // Çözüm: ekrandaki renkli resmin YANINDA, aynı isteğin gri tonlamalı
  // hâlini de indiriyoruz (STYLES=isi_deger). Gri seviyesi değerin kendisi:
  //     deger = gri / 255
  // Gri resim ekranda GÖSTERİLMİYOR, yalnızca bellekteki bir tuvale çiziliyor.
  //
  // Renkli resimden geri çözmek de mümkündü ama rampa ara renkleri
  // interpolasyonla ürettiği için okunan değer yaklaşık olurdu.
  const olcum = { tuval: null, extent: null, en: 0, boy: 0, metrePerPiksel: 0, yaricapMetre: 0 }

  /**
   * Isı halkasının yarıçapı — SABİT MESAFE, sabit piksel değil.
   *
   * SLD'deki `radiusPixels` çıktının PİKSELİ cinsinden. Sabit bıraktığımızda
   * yarıçap ekran çözünürlüğüne ve zoom'a göre kayıyordu: 1187 piksel
   * genişliğinde bir ekranda Türkiye'ye bakarken 35 piksel ≈ 55 km oluyor,
   * yani sekiz kaydın etrafında minicik lekeler kalıyor ve haritanın
   * neredeyse tamamı 0.00 okunuyordu ("termometre bir şey vermiyor").
   *
   * Artık her istek için `radiusPixels` = HEDEF_YARICAP_METRE / (metre/piksel)
   * olarak hesaplanıyor. Böylece halka her zoom'da ve her ekranda AYNI
   * coğrafi büyüklükte: yoğunluk yüzeyi ekranın değil, verinin özelliği.
   *
   * NEDEN 200 km ve daha fazlası değil? Kayıtlar ülkeye seyrek dağıldığı için
   * "her tıklamada sıfırdan büyük bir sayı" istemek 400-500 km yarıçap
   * gerektiriyor; o noktada lekeler birbirine karışıyor ve harita yoğunluk
   * yüzeyi olmaktan çıkıp bulanık bir örtüye dönüşüyor. Boş bölgede doğru
   * cevap zaten SIFIR; arayüz onu "veri yok" diye yazıp en yakın kaydın
   * uzaklığını veriyor.
   *
   * Sınırlar GeoServer'ı korumak için: çok büyük yarıçap hesabı pahalılaştırır,
   * çok küçüğü tek pikselden ibaret lekeler üretir.
   */
  const HEDEF_YARICAP_METRE = 200000
  const EN_AZ_PIKSEL = 20
  const EN_COK_PIKSEL = 200

  /**
   * İstek adresine ENV=radius:N ekler ve ölçeği döndürür.
   * Renkli resim ile gri değer resmi AYNI adresten türetildiği için ikisi de
   * aynı yarıçapı kullanıyor — farklı olsalardı okunan sayı ekrandaki renkle
   * uyuşmazdı.
   */
  const yaricapAyarla = (adres) => {
    const p = adres.searchParams
    const bbox = (p.get('BBOX') || '').split(',').map(Number)
    const en = Number(p.get('WIDTH'))
    if (bbox.length !== 4 || !en) return null

    const metrePerPiksel = (bbox[2] - bbox[0]) / en
    const piksel = Math.min(
      EN_COK_PIKSEL,
      Math.max(EN_AZ_PIKSEL, Math.round(HEDEF_YARICAP_METRE / metrePerPiksel)),
    )

    p.set('ENV', `radius:${piksel}`)
    return { metrePerPiksel, yaricapMetre: piksel * metrePerPiksel }
  }

  // Kaçıncı istek olduğunu sayıyoruz. Harita hızlı kaydırılınca birkaç istek
  // aynı anda havada kalıyor ve BİTİŞ sırası, GÖNDERİM sırasıyla aynı olmak
  // zorunda değil; sıra numarası olmasaydı geç gelen ESKİ resim, yeni resmin
  // üstüne yazıp ölçümü sessizce yanlışlayabilirdi.
  let sonIstekNo = 0

  /** Değer resmini indirip belleğe çizer. Hata olursa ölçüm sessizce kapanır. */
  const degerResminiAl = async (renkliAdres, olcek) => {
    const istekNo = (sonIstekNo += 1)

    // Elimizdeki tuval ARTIK GEÇERSİZ: farklı bir alan/ölçek isteniyor.
    // Temizlemeseydik yeni resim gelene kadar okunan değerler eski alandan
    // gelirdi — kullanıcı haritayı kaydırdığı anda yanlış sayı görürdü.
    // (Arayüz bu boşlukta "ölçüm hazırlanıyor" diyor.)
    olcum.tuval = null
    olcum.extent = null

    try {
      const adres = new URL(renkliAdres, window.location.origin)
      const p = adres.searchParams

      // AYNI bbox ve AYNI boyut şart: gs:Heatmap her istek için ayrı
      // normalleştiriyor, farklı bir alan istenirse okunan değer ekranda
      // görünen renkle uyuşmaz.
      const bbox = (p.get('BBOX') || '').split(',').map(Number)
      const en = Number(p.get('WIDTH'))
      const boy = Number(p.get('HEIGHT'))
      if (bbox.length !== 4 || !en || !boy) return

      p.set('STYLES', degerStili)
      p.set('TRANSPARENT', 'false')   // saydamlık gri değeri bozardı

      const cevap = await authFetch(adres.pathname + '?' + p.toString(), {}, onUnauthorized)
      if (!cevap.ok || istekNo !== sonIstekNo) return

      const resim = await createImageBitmap(await cevap.blob())

      // Beklerken daha yeni bir istek başladıysa bu resmi ÇÖPE atıyoruz.
      if (istekNo !== sonIstekNo) { resim.close?.(); return }

      const tuval = document.createElement('canvas')
      tuval.width = en
      tuval.height = boy
      tuval.getContext('2d').drawImage(resim, 0, 0)
      resim.close?.()

      olcum.tuval = tuval
      olcum.extent = bbox
      olcum.en = en
      olcum.boy = boy
      olcum.metrePerPiksel = olcek?.metrePerPiksel ?? 0
      olcum.yaricapMetre = olcek?.yaricapMetre ?? 0

      // Ölçüm artık okunabilir. Sabitlenmiş bir nokta varsa arayüz onu
      // yeniden okuyor; "hazırlanıyor" yazısı kendiliğinden sayıya dönüşüyor.
      onOlcumHazir?.()
    } catch {
      olcum.tuval = null
    }
  }

  // NEDEN TileWMS DEĞİL ImageWMS?
  // gs:Heatmap yoğunluğu 0–1 aralığına HER İSTEK İÇİN AYRI normalleştiriyor.
  // 256×256 karolarla çalışsaydık her karo kendi içinde normalleşir, komşu
  // kareler arasında görünür dikişler oluşurdu. ImageWMS ekranın tamamını
  // TEK istekte alıyor: tek bir normalleştirme, dikişsiz yüzey.
  const kaynak = new ImageWMS({
    url: WMS_UCU,
    params: {
      LAYERS: noktaKatmani,
      STYLES: stil,
      FORMAT: 'image/png',
      TRANSPARENT: true,
    },
    serverType: 'geoserver',
    // Görünen alandan biraz taşarak iste: kullanıcı haritayı azıcık
    // kaydırdığında kenarda boşluk görünmesin.
    ratio: 1.2,
    imageLoadFunction: async (gorsel, adres) => {
      const img = gorsel.getImage()

      // OpenLayers'ın ürettiği adresi OLDUĞU GİBİ kullanmıyoruz: yarıçapı
      // bu istekteki ölçekten hesaplayıp ENV olarak ekliyoruz. Zaten resmi
      // elle (token'lı) indirdiğimiz için adres tamamen bizim elimizde.
      const istek = new URL(adres, window.location.origin)
      const olcek = yaricapAyarla(istek)
      const tamAdres = istek.pathname + '?' + istek.searchParams.toString()

      // Değer resmini paralel başlatıyoruz: ekrandaki resmi bekletmesin.
      degerResminiAl(tamAdres, olcek)

      try {
        const cevap = await authFetch(tamAdres, {}, onUnauthorized)
        if (!cevap.ok) return

        const blobAdresi = URL.createObjectURL(await cevap.blob())
        img.addEventListener('load', () => URL.revokeObjectURL(blobAdresi), { once: true })
        img.addEventListener('error', () => URL.revokeObjectURL(blobAdresi), { once: true })
        img.src = blobAdresi
      } catch {
        /* yükleme başarısız; OpenLayers görüntüyü boş bırakır */
      }
    },
  })

  const katman = new ImageLayer({
    source: kaynak,
    visible: false,   // "Isı Haritası Analizi" düğmesiyle açılıyor
  })

  /**
   * Verilen harita koordinatındaki (EPSG:3857) yoğunluk değeri.
   *
   * DURUM BİLGİSİYLE dönüyor, çıplak sayıyla değil. Önceden "okunamadı"
   * ile "değer sıfır" arasındaki fark kaybolduğu için ekranda hiçbir şey
   * çıkmıyordu ve kullanıcı termometreyi bozuk sanıyordu. Şimdi arayüz
   * "ölçüm hazırlanıyor" diyebiliyor.
   *
   * @returns {{ durum: 'hazirlaniyor' | 'disarida' | 'ok', deger?: number,
   *             yaricapMetre?: number }}
   */
  const degerOku = (koordinat) => {
    const { tuval, extent, en, boy } = olcum
    if (!tuval || !extent) return { durum: 'hazirlaniyor' }

    const [minX, minY, maxX, maxY] = extent
    const px = Math.floor(((koordinat[0] - minX) / (maxX - minX)) * en)
    // Ekran koordinatı yukarıdan aşağı, harita aşağıdan yukarı — y ters çevriliyor.
    const py = Math.floor(((maxY - koordinat[1]) / (maxY - minY)) * boy)

    if (px < 0 || py < 0 || px >= en || py >= boy) return { durum: 'disarida' }

    // Gri tonlama: üç kanal da aynı, birini okumak yeterli.
    const [gri] = tuval.getContext('2d').getImageData(px, py, 1, 1).data
    // Yarıçapı da veriyoruz: arayüz "bu değer kaç km'lik komşuluğun özeti"
    // sorusunu cevaplayabilsin.
    return { durum: 'ok', deger: gri / 255, yaricapMetre: olcum.yaricapMetre }
  }

  return { katman, kaynak, degerOku }
}
