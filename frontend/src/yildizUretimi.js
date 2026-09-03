// ============================================================================
//  YILDIZ ALANI — açılış sahnesindeki uzayın dokusu
//
//  ---- NEDEN AYRI BİR DOSYA (BİLEŞENİN İÇİNDE DEĞİL)? ----
//  Üretim saf bir fonksiyon: aynı tohum → aynı gökyüzü. Ayrı durunca hem
//  test edilebiliyor hem de bileşen yalnızca çizim işiyle kalıyor.
//
//  ---- DOSYA ADI NEDEN "Yildizlar.js" DEĞİL? ----
//  Bileşen Yildizlar.jsx. Windows dosya adında büyük/küçük harf ayırmadığı
//  için ikisi aynı ada gelseydi "./yildizlar" içe aktarımı iki dosyadan
//  birine denk gelirdi — testler geçiyor ama üretim derlemesi
//  "default is not exported" diye patlıyordu. Bir kez yaşandı.
//
//  ---- NEDEN Math.random DEĞİL? ----
//  Render sırasında rastgele sayı üretmek iki şeyi bozardı:
//    1. Sahne her açıldığında gökyüzü değişirdi. Kullanıcı "Dünya" düğmesine
//       ikinci kez bastığında AYNI sahneyi görmeli — değişen bir arka plan
//       "bir şey mi bozuldu?" hissi verir.
//    2. React bileşeni saf olmaktan çıkardı; testte her çalıştırma başka
//       sonuç verirdi.
//  Bunun yerine tohumlu bir üreteç: rastgele GÖRÜNEN ama tekrarlanabilir.
// ============================================================================

/**
 * Tohumlu sözde-rastgele üreteç (mulberry32).
 *
 * Kriptografik değil — olması da gerekmiyor; tek işi tekrarlanabilir bir
 * dağılım üretmek.
 */
function uretec(tohum) {
  let t = tohum >>> 0
  return () => {
    t = (t + 0x6d2b79f5) >>> 0
    let x = Math.imul(t ^ (t >>> 15), 1 | t)
    x = (x + Math.imul(x ^ (x >>> 7), 61 | x)) ^ x
    return ((x ^ (x >>> 14)) >>> 0) / 4294967296
  }
}

/**
 * Yıldız RENKLERİ — üçü de beyazın komşusu.
 *
 * Gerçek yıldızlar sıcaklıklarına göre maviye ya da turuncuya kaçar. Fark
 * çıplak gözle zar zor seçilir ama HEPSİ saf beyaz olduğunda göz bunu
 * "basılmış nokta" gibi okuyor. Üç ton, deseni tekdüzelikten çıkarıyor.
 */
const TONLAR = ['#ffffff', '#cfe2ff', '#ffe9cf']

/**
 * Yıldız alanını üretir.
 *
 * ---- DAĞILIM: NEDEN DÜZ RASTGELE DEĞİL? ----
 * Saf rastgele serpiştirme kümelenir: bir köşe tıka basa dolarken başka bir
 * köşe bomboş kalır ve boşluk "eksik çizilmiş" gibi durur. Bunun yerine alan
 * ızgaraya bölünüyor, her göze BİR yıldız konup gözün içinde kaydırılıyor
 * (katmanlı örnekleme). Sonuç: düzenli görünmeyen ama dengeli bir gök.
 *
 * Gözlerin bir kısmı bilerek BOŞ bırakılıyor — hepsi dolu olsaydı ızgara
 * gözle seçilir hale gelirdi.
 *
 * @param {object} secenekler
 * @param {number} secenekler.tohum   Aynı tohum aynı gökyüzünü verir.
 * @param {number} secenekler.izgara  Izgara kenarı (izgara² kadar göz).
 * @param {number} secenekler.doluluk Gözlerin ne kadarında yıldız olacağı (0-1).
 * @returns {Array<{x:number,y:number,boyut:number,parlak:boolean,opaklik:number,
 *                  ton:string,kirpisir:boolean,sure:number,gecikme:number}>}
 */
export function yildizlariUret({ tohum = 20260902, izgara = 15, doluluk = 0.62 } = {}) {
  const rnd = uretec(tohum)
  const adim = 100 / izgara
  const yildizlar = []

  for (let satir = 0; satir < izgara; satir++) {
    for (let sutun = 0; sutun < izgara; sutun++) {
      if (rnd() > doluluk) continue        // boş göz: ızgara belli olmasın

      // Gözün içinde serbest konum — kenarlara yapışmasın diye %10 pay.
      const x = (sutun + 0.1 + rnd() * 0.8) * adim
      const y = (satir + 0.1 + rnd() * 0.8) * adim

      // BÜYÜKLÜK DAĞILIMI: çok sayıda sönük, az sayıda parlak yıldız.
      // Gerçek gökyüzü böyle; hepsi orta boy olsaydı desen yapay görünürdü.
      //
      // Boyut PİKSEL cinsinden: yıldız artık SVG çemberi değil, konumlanmış
      // bir eleman (gerekçesi Yildizlar.jsx'te). Piksel, ekran oranından
      // bağımsız — geniş ekranda şişmiyor.
      const p = rnd()
      const parlak = p > 0.93
      const boyut = parlak ? 2.6 + rnd() * 1.5 : 1 + p * 1.3

      yildizlar.push({
        x,
        y,
        boyut,
        parlak,
        // Opaklık boyutla birlikte gidiyor: büyük yıldız aynı zamanda daha
        // parlak. Bağımsız olsalardı "iri ama sönük" yıldızlar çıkar ve göz
        // bunu yanlış bulurdu.
        opaklik: parlak ? 0.85 + rnd() * 0.15 : 0.22 + p * 0.5,
        ton: TONLAR[Math.floor(rnd() * TONLAR.length)],

        // KIRPIŞMA YALNIZCA AZINLIKTA. Hepsi kırpışsaydı ekran titrer,
        // sahne "ekran koruyucu" gibi görünürdü. Kırpışma atmosferden gelen
        // bir etkidir ve gözle ancak birkaç yıldızda seçilir.
        kirpisir: rnd() > 0.76,
        // Uzun ve BİRBİRİNDEN FARKLI süreler: eşit olsaydı yıldızlar aynı
        // anda sönüp yanar, ortak bir nabız duyulurdu.
        sure: 2.6 + rnd() * 3.6,
        gecikme: rnd() * 4,
      })
    }
  }

  return yildizlar
}
