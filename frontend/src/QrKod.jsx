import { useEffect, useRef, useState } from 'react'
import QRCode from 'qrcode'

// ============================================================================
//  QR KODU
//
//  ---- NEDEN BİR BAĞIMLILIK EKLENDİ? ----
//
//  Bu proje bağımlılık eklemekte cimri davrandı: sürükle-bırak için
//  react-beautiful-dnd yerine tarayıcının kendi API'si kullanıldı, TOTP
//  algoritması için hazır bir kütüphane yerine 30 satırlık kendi kodumuz
//  yazıldı. Ölçüt her seferinde aynıydı: "bunu kendimiz yazmak, denetlenmesi
//  gereken yüzeyi büyütür mü, küçültür mü?"
//
//  QR'da cevap tersine dönüyor. QR kodu üretmek Reed-Solomon hata düzeltme
//  kodları, maskeleme desenleri ve sürüm/kapasite tablolarını gerektiriyor —
//  yüzlerce satırlık, doğruluğu gözle denetlenemeyen bir matematik. Yanlış
//  yazılırsa hata mesajı alınmaz; kamera kodu okumaz, o kadar. Burada hazır
//  bir kütüphane kullanmak riski AZALTIYOR.
//
//  ---- QR OLMADAN DA ÇALIŞIR ----
//
//  Kod üretilemezse ekran boş kalmıyor: kullanıcı kurulum anahtarını elle
//  girerek devam edebiliyor (Guvenlik.jsx onu zaten gösteriyor). QR bir
//  kolaylık, tek yol değil.
// ============================================================================

/**
 * @param {object} props
 * @param {string} props.veri Kodlanacak metin (otpauth:// adresi)
 * @param {number} [props.boyut] Kenar uzunluğu (piksel)
 */
export default function QrKod({ veri, boyut = 180 }) {
  const tuvalRef = useRef(null)
  const [hata, setHata] = useState(false)

  useEffect(() => {
    const tuval = tuvalRef.current
    if (!tuval || !veri) return

    QRCode.toCanvas(tuval, veri, {
      width: boyut,
      margin: 1,
      // "Sessiz bölge" dışındaki kenar boşluğu 1 modül: varsayılan 4,
      // küçük bir kutuda ekranın çoğunu boşluğa harcıyor.
      color: {
        // Koyu tema kullanıcısı da okutabilsin diye kod HER ZAMAN beyaz
        // zemine siyah çiziliyor. Temaya uydursaydık koyu zeminde koyu kod
        // çıkar ve kameralar okuyamazdı — QR okuyucular kontrast bekliyor.
        dark: '#000000ff',
        light: '#ffffffff',
      },
      errorCorrectionLevel: 'M',
    }).catch(() => setHata(true))
  }, [veri, boyut])

  if (hata) {
    return (
      <p className="muted qr-hata">
        Kare oluşturulamadı — aşağıdaki kurulum anahtarını elle girin.
      </p>
    )
  }

  return (
    <canvas
      ref={tuvalRef}
      className="qr-tuval"
      width={boyut}
      height={boyut}
      role="img"
      aria-label="İki adımlı doğrulama kurulum karesi"
    />
  )
}
