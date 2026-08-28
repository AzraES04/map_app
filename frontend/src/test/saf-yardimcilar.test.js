import { describe, it, expect } from 'vitest'
import PointGeom from 'ol/geom/Point'
import { fromLonLat } from 'ol/proj'

import { geometryToWkt, wktToFeature, describeGeometry } from '../geo'
import { ilkYonetimEkrani, YONETIM_EKRANLARI } from '../yonetimMenusu'
import { ikonSvg, ikonAdresi, IKON_VIEWBOX } from '../poiIkon'
import { uygunlukRengi, UYGUNLUK_RAMPASI } from '../isiIzgarasi'
import { YETKILER, EKLEME_YETKISI } from '../yetkiler'

// ============================================================================
//  SAF YARDIMCILAR — arayüzün "sessizce yanlış çalışabilen" parçaları
//
//  Frontend'de bugüne kadar hiç test yoktu ve bunun bedeli somut: bu modüller
//  yazıldıktan sonra üç ayrı hata ancak elle deneyerek bulundu. Buradaki
//  testler o üç hatanın da altındaki mantığı hedefliyor.
//
//  Neden ÖNCE saf fonksiyonlar? Harita bileşeni OpenLayers'a, tuvale ve ağa
//  bağlı; onu test etmek ağır bir taklit katmanı ister. Oysa yanlış cevap
//  veren şey çoğu zaman altındaki küçük fonksiyon: koordinat dönüşümü, renk
//  hesabı, menü seçimi. Ucuz ve kesin olan yerden başlıyoruz.
// ============================================================================

describe('geo.js — projeksiyon dönüşümü', () => {
  // Bu dosyanın kendi başlığı diyor ki: dönüşüm unutulursa nokta Ankara
  // yerine Gine Körfezi'ne düşer ve HATA MESAJI ALINMAZ. Tam da bu yüzden
  // teste bağlanması gereken ilk şey.
  it('harita geometrisini 4326 WKT metnine çevirir', () => {
    const ankara = new PointGeom(fromLonLat([32.8597, 39.9334]))

    const wkt = geometryToWkt(ankara)

    expect(wkt).toMatch(/^POINT/)
    const [lon, lat] = wkt.match(/-?\d+\.?\d*/g).map(Number)
    expect(lon).toBeCloseTo(32.8597, 4)
    expect(lat).toBeCloseTo(39.9334, 4)
  })

  it('geometriyi YERİNDE değiştirmez (clone kuralı)', () => {
    // geo.js'teki uyarının testi: transform() mutate ediyor. Klonlamayı
    // unutan bir düzenleme, haritadaki çizimi Afrika açıklarına sıçratırdı
    // ve bu yalnızca gözle fark edilirdi.
    const geom = new PointGeom(fromLonLat([32.8597, 39.9334]))
    const once = [...geom.getCoordinates()]

    geometryToWkt(geom)

    expect(geom.getCoordinates()).toEqual(once)
  })

  it('WKT → feature → WKT turu aynı koordinatı verir', () => {
    const wkt = 'POINT (32.8597 39.9334)'

    const geri = geometryToWkt(wktToFeature(wkt).getGeometry())

    const [lon, lat] = geri.match(/-?\d+\.?\d*/g).map(Number)
    expect(lon).toBeCloseTo(32.8597, 5)
    expect(lat).toBeCloseTo(39.9334, 5)
  })

  it('nokta için boylam/enlem özeti üretir', () => {
    const ozet = describeGeometry(new PointGeom(fromLonLat([32.8597, 39.9334])))

    expect(ozet).toContain('Boylam')
    expect(ozet).toContain('Enlem')
  })
})

describe('yonetimMenusu.js — "Yönetim" düğmesinin hedefi', () => {
  // GERÇEK BİR HATANIN TESTİ: düğme önceden yalnızca Kullanıcı/Rol yetkisine
  // bakıyor ve sabit /admin/users'a gidiyordu. Ulaşım Operatörü'nün ikisi de
  // yok ama "Güzergah Yönetimi" ekranı var — düğme hiç görünmüyor, operatör
  // kendi paneline ulaşamıyordu.
  it('yalnızca güzergah yetkisi olan kullanıcıyı güzergah ekranına yollar', () => {
    expect(ilkYonetimEkrani([YETKILER.guzergahYonetimi])).toBe('/admin/guzergah')
  })

  it('yalnızca POI yetkisi olanı POI ekranına yollar', () => {
    expect(ilkYonetimEkrani([YETKILER.poiYonetimi])).toBe('/admin/poi')
  })

  it('hiç yetkisi olmayana null döner (düğme gizlenir)', () => {
    expect(ilkYonetimEkrani([])).toBeNull()
  })

  it('birden çok yetkide menüdeki İLK ekrana yollar', () => {
    const adres = ilkYonetimEkrani([YETKILER.guzergahYonetimi, YETKILER.kullaniciYonetimi])
    expect(adres).toBe('/admin/users')
  })

  it('her menü maddesinin yetkisi ya tanımlı bir yetki ya da BİLEREK null', () => {
    // Asıl yakalamak istediğimiz şey YAZIM HATASI: yanlış yazılmış bir yetki
    // adı hiçbir kullanıcıda eşleşmez ve madde sessizce, kalıcı olarak
    // kaybolur. Hata da vermez.
    //
    // `null` bu tehlikeye girmiyor: yazım hatası olamayacak kadar ayrı bir
    // değer ve "bu ekran tek bir yetkiyle eşleşmiyor" demenin açık yolu
    // (Çöp Kutusu: geri alma yetkisi KAYDIN TÜRÜNE göre değişiyor).
    const tanimliYetkiler = Object.values(YETKILER)

    for (const ekran of YONETIM_EKRANLARI) {
      if (ekran.yetki === null) continue
      expect(tanimliYetkiler).toContain(ekran.yetki)
    }
  })

  it('yetki alanı hiçbir maddede UNUTULMUYOR', () => {
    // `null` bilinçli bir karar; `undefined` ise alanı yazmayı unutmak.
    // İkisi de AdminLayout'ta aynı sonucu veriyor (madde herkese görünür),
    // o yüzden ayrımı burada yapıyoruz: unutulanı yakalayalım, bilerek
    // yazılanı geçirelim.
    for (const ekran of YONETIM_EKRANLARI) {
      expect(ekran).toHaveProperty('yetki')
      expect(ekran.yetki).not.toBeUndefined()
    }
  })

  it('yetkisi null olan madde VARSAYILAN AÇILIŞ ekranı olmuyor', () => {
    // ilkYonetimEkrani, kullanıcının açabileceği ilk ekranı buluyor.
    // Herkese açık bir madde oraya karışsaydı, hiç yönetim yetkisi olmayan
    // kullanıcıya da "Yönetim" düğmesi görünür ve panele girerdi.
    expect(ilkYonetimEkrani([])).toBeNull()
    expect(ilkYonetimEkrani([YETKILER.poiYonetimi])).toBe('/admin/poi')
  })
})

describe('poiIkon.js — simge üretimi', () => {
  const parcalar = [
    { d: 'M12 2.5c-3.6 0-6.5 2.9-6.5 6.5z', beyaz: false },
    { d: 'M12 6.2a2.8 2.8 0 1 0 0 5.6z', beyaz: true },
  ]

  it('kategori rengini gövdeye, beyazı oyuğa uygular', () => {
    const svg = ikonSvg(parcalar, '#d64550', 24)

    expect(svg).toContain('fill="#d64550"')
    expect(svg).toContain('fill="#ffffff"')
    expect(svg).toContain(`viewBox="${IKON_VIEWBOX}"`)
  })

  it('beyaz kontur DOLGUDAN ÖNCE geliyor', () => {
    // Sıra tersse kontur dolgunun üstünü örter, simge şişkin ve bulanık
    // görünür. Gözle fark edilmesi zor, testle kesin.
    const svg = ikonSvg(parcalar, '#d64550')

    const konturIndex = svg.indexOf('stroke="#ffffff"')
    const dolguIndex = svg.indexOf('fill="#d64550"')

    expect(konturIndex).toBeGreaterThan(-1)
    expect(konturIndex).toBeLessThan(dolguIndex)
  })

  it('beyaz parçalar için kontur çizmiyor (oyuklar dış hat almaz)', () => {
    const svg = ikonSvg(parcalar, '#d64550')
    const konturSayisi = (svg.match(/stroke="#ffffff"/g) ?? []).length

    // İki parça var ama yalnızca biri renkli → tek kontur.
    expect(konturSayisi).toBe(1)
  })

  it('base64 data adresi üretir (ham SVG gömmüyor)', () => {
    // Ham SVG'yi adrese gömmek "#" karakterini kaçırmayı gerektiriyor;
    // unutulduğunda adres sessizce kesiliyor ve simge hiç yüklenmiyor.
    const adres = ikonAdresi(parcalar, '#d64550')

    expect(adres).toMatch(/^data:image\/svg\+xml;base64,/)
    expect(atob(adres.split(',')[1])).toContain('<svg')
  })
})

describe('isiIzgarasi.js — uygunluk renk rampası', () => {
  const hexToRgb = (h) => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16))

  it('rampanın uçlarını birebir verir', () => {
    expect(uygunlukRengi(0)).toEqual(hexToRgb(UYGUNLUK_RAMPASI[0].renk))
    expect(uygunlukRengi(1)).toEqual(hexToRgb(UYGUNLUK_RAMPASI.at(-1).renk))
  })

  it('aralık dışı değerleri uçlara sabitler', () => {
    // Skor hesabı 0–1 üretiyor ama kayan nokta 1.0000001 verebilir;
    // sabitleme olmasa dizi sınırının dışına düşerdi.
    expect(uygunlukRengi(-5)).toEqual(uygunlukRengi(0))
    expect(uygunlukRengi(99)).toEqual(uygunlukRengi(1))
  })

  it('artan orana göre renk MONOTON değişiyor', () => {
    // "Eşit puan farkı = eşit renk farkı" iddiasının kaba kontrolü:
    // ardışık örnekler birbirinin aynısı olmamalı (rampa düzleşmemeli).
    const ornekler = [0, 0.2, 0.4, 0.6, 0.8, 1].map(uygunlukRengi)

    for (let i = 1; i < ornekler.length; i += 1) {
      expect(ornekler[i]).not.toEqual(ornekler[i - 1])
    }
  })

  it('her zaman geçerli bir RGB üçlüsü döner', () => {
    for (let t = 0; t <= 1; t += 0.05) {
      const rgb = uygunlukRengi(t)
      expect(rgb).toHaveLength(3)
      rgb.forEach((k) => {
        expect(Number.isInteger(k)).toBe(true)
        expect(k).toBeGreaterThanOrEqual(0)
        expect(k).toBeLessThanOrEqual(255)
      })
    }
  })
})

describe('yetkiler.js — backend ile senkron', () => {
  // Bu dosya backend'deki Yetkiler.cs'in KOPYASI. Adlar birebir aynı olmak
  // zorunda; ayrışırsa arayüz yetkiyi göremez ve düğme boşuna gizli kalır.
  // Gerçek senkron kontrolü ancak iki dili birden okuyan bir betikle olur;
  // buradaki test en azından yapının bozulmadığını garanti ediyor.
  it('çizim tiplerinin hepsi bir yetkiye eşlenmiş', () => {
    expect(EKLEME_YETKISI.Point).toBe(YETKILER.noktaEkleme)
    expect(EKLEME_YETKISI.LineString).toBe(YETKILER.cizgiEkleme)
    expect(EKLEME_YETKISI.Polygon).toBe(YETKILER.poligonEkleme)
  })

  it('yetki adları boş ya da yinelenmiş değil', () => {
    const adlar = Object.values(YETKILER)

    adlar.forEach((ad) => expect(ad.trim().length).toBeGreaterThan(0))
    expect(new Set(adlar).size).toBe(adlar.length)
  })
})
