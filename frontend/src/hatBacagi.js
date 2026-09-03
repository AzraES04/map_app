// ============================================================================
//  HAT ÇİZGİSİNİN HANGİ BACAĞINA TIKLANDI? (Ödev 18 — harita üzerinden seçim)
//
//  Bir güzergahın haritadaki çizgisi TEK bir feature'dır: duraklardan
//  türetilmiş (ya da OSRM'in ürettiği) uzun bir LineString. Kullanıcı bu
//  çizginin bir yerine tıkladığında sorduğu şey aslında şudur:
//
//      "Şu an baktığım bu parça, hangi duraktan hangi durağa gidiyor?"
//
//  Cevap, alternatif yolları isteyebilmemiz için gerekli: sunucunun
//  `/duraklar/{id}/alternatifler` ucu bacağı VARIŞ DURAĞININ kimliğiyle
//  tanımlıyor (bacak = bir önceki durak → bu durak).
//
//  NEDEN "EN YAKIN DURAĞA BAK" DEĞİL?
//  İlk akla gelen çözüm tıklanan noktaya en yakın iki durağı bulmaktır.
//  Gerçek rotalarda çalışmıyor: yol bir vadiyi dolaştığında ya da hat kendi
//  üzerine kıvrıldığında, kuş uçuşu en yakın durak çoğu zaman BAŞKA bir
//  bacağın durağı oluyor. Ödev 18'in canlıda yakalanan hatası tam olarak bu
//  cinstendi (ara nokta komşu bacağa düşüyordu) — aynı tuzağa iki kez
//  düşmemek için burada ÇİZGİ BOYUNCA İLERLEME ölçülüyor.
//
//  YÖNTEM:
//    1. Hem tıklanan nokta hem de her durak, çizgiye DİK olarak izdüşürülür.
//    2. Her izdüşümün çizgi başından itibaren kaç birim ilerde olduğu
//       (yol boyu mesafe) hesaplanır.
//    3. Tıklamanın ilerlemesinden BÜYÜK ilk durak, o bacağın varış durağıdır.
//
//  Böylece çizgi ne kadar kıvrılırsa kıvrılsın, "hangi iki durağın arasında
//  kaldım" sorusu doğru cevaplanır.
//
//  Birim önemli değil: hesap tamamen görelidir (ilerlemeler birbiriyle
//  karşılaştırılıyor), o yüzden harita projeksiyonunun metresi de derecesi de
//  aynı sonucu verir. Bu yüzden fonksiyonlar OpenLayers'a hiç bağlı değil ve
//  tek başına test edilebiliyor.
// ============================================================================

/**
 * Bir noktanın DOĞRU PARÇASI üzerindeki izdüşümü.
 *
 * Klasik nokta-doğru parçası izdüşümü: noktayı parçanın doğrultusuna
 * yansıtıp oranı [0, 1] aralığına KIRPIYORUZ. Kırpma şart — kırpmasaydık
 * parçanın dışına düşen izdüşümler kabul edilir ve çizginin başına/sonuna
 * tıklandığında saçma mesafeler çıkardı.
 *
 * @returns {{ oran: number, uzaklikKare: number }} oran = parça üzerindeki
 *   konum (0 = başı, 1 = sonu), uzaklikKare = noktanın parçaya uzaklığının
 *   KARESİ (karekök almıyoruz; yalnızca karşılaştırma yapacağız, kök almak
 *   sonucu değiştirmeden hesap eklerdi).
 */
export function parcayaIzdusum(baslangic, bitis, nokta) {
  const dx = bitis[0] - baslangic[0]
  const dy = bitis[1] - baslangic[1]
  const uzunlukKare = dx * dx + dy * dy

  // Sıfır uzunluklu parça (üst üste iki koordinat): izdüşüm başlangıcın
  // kendisi. Bölme yapmaya kalksaydık NaN üretirdik.
  if (uzunlukKare === 0) {
    const ax = nokta[0] - baslangic[0]
    const ay = nokta[1] - baslangic[1]
    return { oran: 0, uzaklikKare: ax * ax + ay * ay }
  }

  const ham = ((nokta[0] - baslangic[0]) * dx + (nokta[1] - baslangic[1]) * dy) / uzunlukKare
  const oran = Math.min(1, Math.max(0, ham))

  const px = baslangic[0] + oran * dx
  const py = baslangic[1] + oran * dy
  const fx = nokta[0] - px
  const fy = nokta[1] - py

  return { oran, uzaklikKare: fx * fx + fy * fy }
}

/**
 * Noktanın çizgi üzerindeki İLERLEMESİ: çizgiye en yakın düştüğü yerin,
 * çizginin başlangıcından itibaren kaç birim ilerde olduğu.
 *
 * Bütün parçalar tek tek deneniyor ve EN YAKIN olan kazanıyor. "İlk yeterince
 * yakın parçada dur" gibi bir kısayol yazmadık: kendi üzerine kıvrılan bir
 * rotada ilk yeterince yakın parça yanlış bacağa ait olabilir.
 *
 * @param {number[][]} koordinatlar Çizginin köşe noktaları
 * @param {number[]} nokta
 * @returns {{ ilerleme: number, uzaklik: number } | null}
 */
export function cizgideIlerleme(koordinatlar, nokta) {
  if (!Array.isArray(koordinatlar) || koordinatlar.length < 2 || !Array.isArray(nokta)) {
    return null
  }

  let toplam = 0            // taranan parçaların toplam uzunluğu
  let enIyi = null

  for (let i = 0; i < koordinatlar.length - 1; i++) {
    const a = koordinatlar[i]
    const b = koordinatlar[i + 1]
    const { oran, uzaklikKare } = parcayaIzdusum(a, b, nokta)
    const parcaUzunlugu = Math.hypot(b[0] - a[0], b[1] - a[1])

    if (enIyi === null || uzaklikKare < enIyi.uzaklikKare) {
      enIyi = { uzaklikKare, ilerleme: toplam + oran * parcaUzunlugu }
    }

    toplam += parcaUzunlugu
  }

  if (enIyi === null) return null
  return { ilerleme: enIyi.ilerleme, uzaklik: Math.sqrt(enIyi.uzaklikKare) }
}

/**
 * Tıklanan nokta hangi bacağa ait?
 *
 * @param {number[][]} rotaKoordinatlari Hattın çizgisi
 * @param {number[][]} durakNoktalari    Durakların koordinatları, SIRAYLA
 * @param {number[]} tiklama
 * @returns {{ kalkis: number, varis: number } | null} durak dizisindeki
 *   İNDEKSLER (kalkış = bacağın başladığı durak, varış = bittiği durak).
 *   İki duraktan azı varsa bacak yoktur → null.
 */
export function bacakBul(rotaKoordinatlari, durakNoktalari, tiklama) {
  if (!Array.isArray(durakNoktalari) || durakNoktalari.length < 2) return null

  const tik = cizgideIlerleme(rotaKoordinatlari, tiklama)
  if (!tik) return null

  const ilerlemeler = durakNoktalari.map((d) => cizgideIlerleme(rotaKoordinatlari, d))
  if (ilerlemeler.some((i) => i === null)) return null

  // İlk durağı ATLIYORUZ (i = 1'den başlıyor): hattın ilk durağına GELEN bir
  // bacak yok, ondan yalnızca çıkılıyor. Sunucu da o durak için "bu hattın
  // ilk durağı" diyip boş liste dönüyor.
  for (let i = 1; i < ilerlemeler.length; i++) {
    if (ilerlemeler[i].ilerleme > tik.ilerleme) return { kalkis: i - 1, varis: i }
  }

  // Tıklama son durağın ötesinde (rota duraktan sonra biraz daha uzuyorsa
  // olur): son bacak kabul ediliyor. `null` dönmek, çizginin ucuna tıklayan
  // kullanıcıya hiçbir şey olmamış gibi görünürdü.
  return { kalkis: durakNoktalari.length - 2, varis: durakNoktalari.length - 1 }
}

/**
 * Bir bacak alternatifinin HARİTADAKİ etiketi: "12 dk" ya da "14 dk (+2)".
 *
 * Neden burada, çizen dosyada değil? Çünkü sayının listedekiyle AYNI olması
 * gerekiyor: harita "12 dk", liste "13 dk" deseydi kullanıcı hangisine
 * güveneceğini sorardı. Aynı yuvarlama kuralı tek bir yerde durup teste
 * bağlanınca bu ikisi birbirinden ayrı düşemiyor.
 *
 * "+1 dakika tabanı" listeden geliyor: OSRM 20 saniyelik bir fark verdiğinde
 * yuvarlama "+0 dk" yazardı — "daha uzun ama fark yok" gibi okunan, bilgi
 * vermeyen bir etiket. En küçük anlamlı fark 1 dakikadır.
 *
 * @param {{sureSaniye:number, sureFarkiSaniye:number, enIyi:boolean}} alt
 */
export function alternatifEtiketi(alt) {
  const dk = Math.round(alt.sureSaniye / 60)
  if (alt.enIyi) return `${dk} dk`
  const fark = Math.max(1, Math.round(alt.sureFarkiSaniye / 60))
  return `${dk} dk (+${fark})`
}
