import { describe, it, expect } from 'vitest'
import Feature from 'ol/Feature'
import LineStringGeom from 'ol/geom/LineString'
import PointGeom from 'ol/geom/Point'

import { alternatifStili, guzergahStili } from '../pages/MapPage'

// ============================================================================
//  Alternatif çizgilerinin GÖRÜNÜMÜ
//
//  Bu testin sebebi bir geri bildirim: "alternatif yollar tıklanabilir ve
//  belirgin olsun." İlk sürümde seçili olmayan alternatifler 3 piksel ve
//  %42 opaklıktaydı — haritada tıklanacak bir şey gibi değil, arka planda
//  duran soluk bir iz gibi görünüyorlardı.
//
//  Kalınlık/opaklık gibi değerler gözle ayarlanır ama SESSİZCE geri
//  kayabilirler: biri stili düzenlerken "seçili olmayan" dalını inceltirse
//  hiçbir test kırılmaz, hiçbir hata çıkmaz — yalnızca özellik yeniden
//  kullanılamaz hâle gelir. Aşağıdaki testler o kaymayı yakalıyor.
// ============================================================================

/** Test için sahte bir alternatif feature'ı. */
const alternatif = (ozellikler = {}) => {
  const f = new Feature({
    geometry: new LineStringGeom([[0, 0], [100, 0], [200, 100]]),
  })
  f.setProperties({ alternatifSira: 0, etiket: '12 dk', ...ozellikler })
  return f
}

/** Stil dizisindeki asıl çizgi = beyaz tabandan SONRAKİ stroke. */
const cizgiStili = (stiller) => stiller[1].getStroke()
const tabanStili = (stiller) => stiller[0].getStroke()
const etiketStili = (stiller) => stiller.find((st) => st.getText())

describe('alternatifStili — üç kademe', () => {
  const stil = alternatifStili()

  it('seçili OLMAYAN alternatif de kalın ve doygun çizilir', () => {
    const stiller = stil(alternatif({ secili: false }))

    // İlk sürümdeki değerler 3 piksel / 0.42 opaklıktı; ikisi de geri
    // gelmemeli. Çizgi tıklanabilir bir nesne, dekor değil.
    expect(cizgiStili(stiller).getWidth()).toBeGreaterThanOrEqual(5)
    expect(cizgiStili(stiller).getColor()).toBe('rgba(232, 161, 60, 0.88)')
  })

  it('beyaz taban HER çizgide var — zeminden koparan şey o', () => {
    const secisiz = stil(alternatif({ secili: false }))
    const secili = stil(alternatif({ secili: true }))

    // Taban, çizgiden daima daha kalın: iki yanında "hava boşluğu" bırakıyor.
    expect(tabanStili(secisiz).getWidth()).toBeGreaterThan(cizgiStili(secisiz).getWidth())
    expect(tabanStili(secili).getWidth()).toBeGreaterThan(cizgiStili(secili).getWidth())
  })

  it('fare üzerindeyken seçili olmayandan KALIN, seçiliden ince', () => {
    const normal = cizgiStili(stil(alternatif({ secili: false }))).getWidth()
    const vurgulu = cizgiStili(stil(alternatif({ secili: false, vurgu: true }))).getWidth()
    const secili = cizgiStili(stil(alternatif({ secili: true }))).getWidth()

    expect(vurgulu).toBeGreaterThan(normal)
    expect(secili).toBeGreaterThan(vurgulu)
  })

  it('hepsi KESİKLİ kalır — hiçbiri kaydedilmiş değil', () => {
    expect(cizgiStili(stil(alternatif({ secili: false }))).getLineDash()).toBeTruthy()
    expect(cizgiStili(stil(alternatif({ secili: true }))).getLineDash()).toBeTruthy()
  })
})

describe('alternatifStili — süre etiketi', () => {
  const stil = alternatifStili()

  it('etiketi çizginin ORTASINA koyar', () => {
    const stiller = stil(alternatif())
    const etiket = etiketStili(stiller)

    expect(etiket.getText().getText()).toBe('12 dk')
    // Geometri nokta olmalı: metin çizgi boyunca akmasın, tek yerde dursun.
    expect(etiket.getGeometry()).toBeInstanceOf(PointGeom)
  })

  it('etiketsiz feature çizilebilir kalır (metin stili eklenmez)', () => {
    const stiller = stil(alternatif({ etiket: '' }))

    expect(etiketStili(stiller)).toBeUndefined()
    expect(stiller).toHaveLength(2)          // taban + çizgi
  })

  it('çizgi olmayan geometride etiket ATLANIR, çökmez', () => {
    // getCoordinateAt yalnızca LineString'te var. Sunucu bir gün
    // MULTILINESTRING dönerse stil fonksiyonu patlamamalı.
    const f = alternatif()
    f.setGeometry(new PointGeom([0, 0]))

    expect(() => stil(f)).not.toThrow()
    expect(etiketStili(stil(f))).toBeUndefined()
  })
})

// ============================================================================
//  Kayıtlı rota ile alternatifler AYNI RENK olmamalı
//
//  Canlıda yakalanan sorun: hattın rengini kullanıcı seçiyor. Turuncu bir hat
//  seçildiğinde kayıtlı rota ile turuncu alternatifler aynı renge düşüyor ve
//  "hangisi şu anki yolum?" sorusu cevapsız kalıyordu. Çözüm hattın rengini
//  SOLUKLAŞTIRMAK değil (soluk turuncu yine turuncudur), renk ailesinden
//  tamamen çıkarmak: alternatifler ekrandayken kayıtlı rota GRİ çiziliyor.
// ============================================================================

const hatFeature = (ozellikler = {}) => {
  const f = new Feature({ geometry: new LineStringGeom([[0, 0], [100, 0]]) })
  f.setProperties({ renk: '#e8a13c', rotaVar: true, guzergahId: 1, ...ozellikler })
  return f
}

describe('guzergahStili — alternatifler açıkken kayıtlı rota', () => {
  const stil = guzergahStili()

  it('normalde hattın KENDİ rengini kullanır', () => {
    const stiller = stil(hatFeature())
    expect(stiller[1].getStroke().getColor()).toBe('#e8a13c')
  })

  it('alternatifler açıkken GRİYE çekilir — turuncuyla yarışmasın', () => {
    const renk = stil(hatFeature({ soluk: true }))[1].getStroke().getColor()

    expect(renk).not.toBe('#e8a13c')
    // Gri = üç kanal birbirine yakın. Renk adı yerine ÖZELLİĞİ sınıyoruz;
    // tonu ayarlamak serbest, "turuncu ailesine geri dönmek" değil.
    const [r, g, b] = renk.match(/[\d.]+/g).map(Number)
    expect(Math.max(r, g, b) - Math.min(r, g, b)).toBeLessThan(40)
  })

  it('soluk hat daha İNCE çizilir (öne çıkan alternatifler olsun)', () => {
    const normal = stil(hatFeature())[1].getStroke().getWidth()
    const soluk = stil(hatFeature({ soluk: true }))[1].getStroke().getWidth()

    expect(soluk).toBeLessThan(normal)
  })

  it('paylaşılan stil nesnesi bir sonraki hatta BULAŞMAZ', () => {
    // guzergahStili tek bir Style nesnesini bütün hatlar için kullanıyor.
    // Kalınlığı yalnızca soluk dalında yazsaydık, soluk bir hattan sonra
    // çizilen normal hat da ince kalırdı — sessiz ve gözle zor fark edilir.
    stil(hatFeature({ soluk: true }))
    const sonraki = stil(hatFeature())

    expect(sonraki[1].getStroke().getWidth()).toBe(4)
    expect(sonraki[1].getStroke().getColor()).toBe('#e8a13c')
  })

  it('gizli hat hiç çizilmez', () => {
    expect(stil(hatFeature({ gizli: true }))).toBeNull()
  })
})
