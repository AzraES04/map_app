import { useMemo } from 'react'

import { yildizlariUret } from './yildizUretimi'

// ============================================================================
//  AÇILIŞ SAHNESİNİN YILDIZLARI
//
//  ---- NEDEN EKLENDİ? ----
//  Sahnede gezegenin çevresi düz bir gradyandı. Sorun "boşluk" değildi;
//  gözün tutunacağı HİÇBİR SABİT NOKTA yoktu. İniş sırasında hareket hissi
//  yalnızca kürenin büyümesinden geliyordu.
//
//  Yıldızların asıl işi PARALAKS: yakındaki gezegen hızla açılırken uzaktaki
//  yıldızlar ağır ağır kayınca beyin sahneyi derinlik olarak okuyor. Bu
//  yüzden CSS'te yıldız katmanının iniş ölçeği, gezegeninkinden bilinçli
//  olarak KÜÇÜK tutuldu (bkz. index.css → .uzay-sahnesi.inis .uzay-yildizlar).
//  Aynı oranda büyüselerdi tek bir düz resim gibi yaklaşırlardı ve kazanç
//  sıfır olurdu.
//
//  ---- NEDEN SVG DEĞİL, KONUMLANDIRILMIŞ ELEMANLAR? ----
//  İlk sürüm tek bir SVG'ydi: viewBox 100×100, preserveAspectRatio="slice".
//  Denemede ÇÖKTÜ ve sebebi öğretici:
//
//    "slice", kare viewBox'ı ekranı KAPLAYACAK kadar büyütür. 800×450'lik bir
//    pencerede ölçek 8 kat oluyor — 0.7 birimlik bir yıldız 11 piksellik bir
//    topa dönüşüyordu. Üstelik kare ekranı kapladığı için dikeyde %45'i
//    kırpılıyor, 146 yıldızın ancak 30'u görünüyordu.
//
//    "none" deseydik kırpma biterdi ama daireler geniş ekranda yatay ELİPSE
//    dönerdi. "meet" deseydik yıldızlar ekranın ortasındaki kare alana
//    sıkışır, kenarlar boş kalırdı.
//
//  Yani kare bir viewBox'ın üç seçeneği de yanlış. Konum YÜZDE (ekrana
//  oranlı), boyut PİKSEL (ekrandan bağımsız) olunca üçü de çözülüyor:
//  daireler yuvarlak, boyutları sabit, hepsi görünür.
//
//  146 eleman kulağa çok geliyor ama hiçbiri düzeni etkilemiyor (hepsi
//  position: absolute) ve animasyon yalnızca opaklıkta — tarayıcı için ucuz.
// ============================================================================

export default function Yildizlar() {
  // Tek sefer üretiliyor; tohum sabit olduğu için sonuç her render'da aynı
  // olurdu, boşuna hesaplamıyoruz.
  const yildizlar = useMemo(() => yildizlariUret(), [])

  return (
    <div className="uzay-yildizlar" aria-hidden="true">
      {yildizlar.map((y, i) => (
        <span
          key={i}
          className={`yildiz${y.parlak ? ' parlak' : ''}${y.kirpisir ? ' kirpisir' : ''}`}
          style={{
            left: `${y.x}%`,
            top: `${y.y}%`,
            width: `${y.boyut}px`,
            height: `${y.boyut}px`,
            background: y.ton,
            opacity: y.opaklik,
            // Süre ve gecikme yıldız başına farklı: ortak bir nabız
            // duyulmasın diye (bkz. yildizUretimi.js).
            animationDuration: y.kirpisir ? `${y.sure}s` : undefined,
            animationDelay: y.kirpisir ? `${y.gecikme}s` : undefined,
            // Parlak yıldızın hâlesi kendi rengini alıyor: mavi yıldız mavi,
            // sıcak yıldız turuncu parlıyor. Tek renk verseydik ton farkı
            // hâlenin altında kaybolurdu.
            boxShadow: y.parlak ? `0 0 ${y.boyut * 2.4}px ${y.ton}` : undefined,
          }}
        />
      ))}
    </div>
  )
}
