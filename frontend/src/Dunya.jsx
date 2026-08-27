// ============================================================================
//  Açılış sahnesindeki GEZEGEN
//
//  ---- NEDEN AYRI BİR ÇİZİM, HARİTANIN KENDİSİ DEĞİL? ----
//
//  Sahne başlangıçta haritayı çok uzaktan gösteriyordu ve üstüne bir renk
//  derecelendirmesi (saturate/contrast/hue-rotate) uyguluyordu. Sorun şu:
//  OSM bir YOL HARİTASIDIR — karalar neredeyse beyaz (#f2efe9), denizler
//  soluk mavi. CSS süzgeçleri var olan rengi güçlendirebilir ama YOKTAN renk
//  üretemez; beyaz karayı yeşile çeviremezsiniz. Sonuç, uzaydan görülen
//  mavi-yeşil dünyaya değil, soluk bir yol haritasına benziyordu.
//
//  Bu yüzden gezegen artık kendi çizimi. Sahne bittiğinde bu katman solup
//  gerçek haritaya devrediyor — yani "uzaydan bakış" ile "haritaya iniş"
//  arasında bir geçiş var, ikisi aynı görüntü olmaya çalışmıyor.
//
//  ---- KITALAR NEDEN BU YÜZDEN? ----
//
//  Kamera Türkiye'ye iniyor. Dolayısıyla gezegenin bize dönük yüzü
//  Avrupa–Afrika–Asya olmalı: iniş, görünen yüzün ortasına doğru olmalı ki
//  hareket tutarlı okunsun. Amerika kıtası sol kenarda, ufuk çizgisinde
//  kalıyor.
//
//  Şekiller BASİTLEŞTİRİLMİŞ. 42vmin'lik bir disk, iki buçuk saniye ekranda
//  duruyor; kıyı şeridi ayrıntısı bu ölçekte görünmüyor ama yanlış oranlar
//  görünüyor. Bu yüzden ayrıntı değil, SİLUET doğru tutuldu.
// ============================================================================

/**
 * @param {object} props
 * @param {boolean} props.solgun Ekranı kaplama (iniş) aşamasında mı?
 */
export default function Dunya({ solgun = false }) {
  return (
    <div className={`kure-dunya${solgun ? ' solgun' : ''}`} aria-hidden="true">
      <svg viewBox="0 0 100 100" preserveAspectRatio="xMidYMid slice">
        <defs>
          {/* Okyanus: kutuplara doğru koyulaşan, ekvatorda açılan bir mavi.
              Düz tek renk okyanus "gezegen" değil "daire" gibi okunuyordu. */}
          <radialGradient id="okyanus" cx="38%" cy="32%" r="78%">
            <stop offset="0%" stopColor="#2f7fb5" />
            <stop offset="45%" stopColor="#1d5c8f" />
            <stop offset="100%" stopColor="#0b3358" />
          </radialGradient>

          {/* Kara: yeşilden kurak sarıya. Afrika'nın kuzeyi ve Arabistan
              çöl olduğu için tek yeşil ton gerçekçi durmuyordu. */}
          <linearGradient id="kara" x1="0" y1="0" x2="0.3" y2="1">
            <stop offset="0%" stopColor="#4e8f4a" />
            <stop offset="42%" stopColor="#6da24c" />
            <stop offset="70%" stopColor="#b9a05c" />
            <stop offset="100%" stopColor="#5f8f4e" />
          </linearGradient>

          {/* Kutup buzu */}
          <radialGradient id="buz" cx="50%" cy="50%" r="50%">
            <stop offset="0%" stopColor="#ffffff" stopOpacity="0.92" />
            <stop offset="100%" stopColor="#dfeaf2" stopOpacity="0" />
          </radialGradient>

          {/* Bulut örtüsü — beyaz, çok yumuşak ve DÜŞÜK opaklıkta.
              Belirgin olsaydı kıtaları örter, gezegen "bulutlu" değil
              "kirli" görünürdü. */}
          <radialGradient id="bulut" cx="50%" cy="50%" r="50%">
            <stop offset="0%" stopColor="#ffffff" stopOpacity="0.55" />
            <stop offset="100%" stopColor="#ffffff" stopOpacity="0" />
          </radialGradient>

          {/* Her şey küreye kırpılıyor: SVG kare, gezegen daire. */}
          <clipPath id="kureKirp">
            <circle cx="50" cy="50" r="50" />
          </clipPath>
        </defs>

        <g clipPath="url(#kureKirp)">
          <circle cx="50" cy="50" r="50" fill="url(#okyanus)" />

          {/* ---------- KARALAR ---------- */}
          <g fill="url(#kara)">
            {/* Avrupa — İskandinavya, Britanya ve İberya dahil */}
            <path d="M38 26 q6-4 11-1 q4-3 8 0 q3 4-1 6 q4 1 3 5 q-3 3-8 2
                     q-2 4-7 3 q-5 0-7-4 q-4-1-4-5 q1-4 5-6 Z" />
            {/* Afrika — kuzeyde geniş, güneye doğru sivrilen klasik siluet */}
            <path d="M40 45 q8-3 16-1 q7 2 9 7 q1 6-3 10 q-2 7-6 12
                     q-3 6-8 8 q-5 1-7-4 q-2-7-4-14 q-3-8-2-13 q1-4 5-5 Z" />
            {/* Asya — Anadolu'dan Sibirya ve Çin'e uzanan büyük kütle */}
            <path d="M57 28 q10-5 20-2 q10 2 15 8 q4 5-2 8 q-6 4-14 3
                     q-6 3-13 1 q-6-1-9-6 q-3-6 3-12 Z" />
            {/* Arabistan */}
            <path d="M58 46 q6-2 9 2 q2 4-2 7 q-5 2-8-2 q-2-4 1-7 Z" />
            {/* Hindistan */}
            <path d="M70 47 q6 0 8 4 q0 6-4 11 q-4 3-6-2 q-2-7 0-11 Z" />
            {/* Güney Amerika — sol ufukta, kısmen görünür */}
            <path d="M12 56 q7-3 11 2 q3 6 0 13 q-2 9-7 12 q-4 1-5-5
                     q-2-11 0-18 Z" />
            {/* Kuzey Amerika'nın doğu kıyısı — sol üst ufuk */}
            <path d="M8 26 q9-6 16-2 q4 3 1 7 q-6 5-13 4 q-5-2-4-9 Z" />
            {/* Avustralya — sağ alt */}
            <path d="M78 67 q8-3 12 2 q2 5-3 8 q-7 2-11-2 q-2-5 2-8 Z" />
          </g>

          {/* ---------- KUTUPLAR ---------- */}
          <ellipse cx="50" cy="4" rx="46" ry="14" fill="url(#buz)" />
          <ellipse cx="50" cy="97" rx="42" ry="12" fill="url(#buz)" />

          {/* ---------- BULUTLAR ---------- */}
          <g>
            <ellipse cx="30" cy="38" rx="20" ry="8" fill="url(#bulut)" />
            <ellipse cx="66" cy="60" rx="24" ry="9" fill="url(#bulut)" />
            <ellipse cx="46" cy="80" rx="26" ry="7" fill="url(#bulut)" />
            <ellipse cx="78" cy="22" rx="16" ry="6" fill="url(#bulut)" />
          </g>
        </g>
      </svg>
    </div>
  )
}
