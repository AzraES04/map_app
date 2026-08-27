// ============================================================================
//  KITA SINIRLARI — kaba kıyı şeritleri, [boylam, enlem] çiftleri
//
//  ---- NEDEN GERÇEK KOORDİNAT? ----
//
//  İlk denemede kıtalar serbest SVG eğrileriyle çizilmişti ve sonuç dünyaya
//  benzemiyordu: mavi bir dairenin üstünde rastgele yeşil lekeler. Sebep
//  şuydu — göz kıyı şeridinin AYRINTISINI bu ölçekte görmüyor ama ORANLARI
//  ve BİRBİRİNE GÖRE KONUMLARI hemen fark ediyor. Afrika'nın güneye doğru
//  sivrilmesi, Arabistan'ın üçgenliği, Hindistan'ın çıkıntısı: bunlar
//  "tanıma" duygusunu taşıyan şeyler ve elle çizilen eğrilerde tutturulamıyor.
//
//  Gerçek boylam/enlem kullanmak ayrıca ikinci bir kazanç veriyor: noktalar
//  ORTOGRAFİK izdüşümle küreye yansıtılıyor (bkz. Dunya.jsx), yani kenarlara
//  doğru kısalma kendiliğinden oluşuyor. Düz çizimde bunu taklit etmek
//  imkânsızdı — küre değil çıkartma gibi duruyordu.
//
//  ---- AYRINTI DÜZEYİ ----
//
//  Kıyılar KABA: her kıta 20-60 nokta. Gerçek bir kıyı şeridi verisi
//  (Natural Earth) yüz binlerce nokta ve megabaytlarca dosya — 42vmin'lik
//  bir disk için anlamsız. Buradaki noktalar büyük burunları, körfezleri ve
//  yarımadaları tutuyor; gerisi zaten görünmüyor.
//
//  Sıra ÖNEMLİ: her dizi kapalı bir çokgenin köşeleri. Kendini kesen bir
//  sıralama SVG'de "delik" gibi görünen bozuk dolgular üretir.
// ============================================================================

/**
 * AVRASYA — tek parça.
 *
 * Avrupa ve Asya ayrı çizilmiyor çünkü ayrı DEĞİLLER: tek bir kara kütlesi.
 * İkiye bölseydik Karadeniz'in kuzeyinde var olmayan bir kıyı çizgisi
 * belirirdi.
 *
 * İzlenen yol: İberya'dan kuzeye Atlantik kıyısı → İskandinavya → Sibirya'nın
 * kuzey kıyısı → Kamçatka → Çin → Güneydoğu Asya → Hindistan → Arabistan →
 * Anadolu → Balkanlar → Akdeniz'in kuzey kıyısı → İberya'ya kapanış.
 */
export const AVRASYA = [
  // Atlantik kıyısı, kuzeye
  [-9, 43], [-4, 48], [-1, 49], [2, 51], [4, 53], [8, 54], [11, 55],
  [8, 58], [5, 62], [12, 65], [18, 69], [28, 71],
  // Rusya'nın kuzey kıyısı
  [40, 68], [55, 68], [70, 73], [80, 73], [105, 77], [130, 72], [160, 70],
  // Uzak Doğu
  [170, 66], [163, 60], [155, 52], [140, 53], [130, 43], [122, 40],
  // Çin ve Güneydoğu Asya
  [120, 34], [118, 25], [108, 21], [109, 11], [104, 10], [100, 8],
  [98, 14], [94, 21], [89, 22],
  // Hindistan
  [80, 15], [77, 8], [73, 18], [70, 23],
  // İran ve Basra
  [62, 25], [57, 25], [50, 28], [48, 30],
  // Arabistan'ın çevresi
  [55, 25], [58, 22], [52, 16], [45, 13], [43, 16], [39, 21], [35, 28],
  // Levant ve Anadolu
  [34, 31], [36, 36], [30, 36], [27, 37], [26, 40],
  // Balkanlar ve Akdeniz'in kuzeyi, batıya
  [23, 38], [20, 40], [16, 42], [13, 45], [13, 41], [16, 38], [10, 44],
  [3, 43], [0, 39], [-6, 36], [-9, 37],
]

/**
 * AFRİKA — kuzeyde geniş, güneye doğru sivrilen siluet.
 * Gine Körfezi'nin girintisi ve Somali burnu bilerek korundu: ikisi de
 * kıtayı bakışta tanıtan şeyler.
 */
export const AFRIKA = [
  [-6, 36], [-16, 28], [-17, 15], [-13, 8], [-8, 5], [0, 5], [6, 4],
  [9, 4], [9, -1], [12, -6], [12, -17], [15, -23], [18, -34], [25, -34],
  [32, -29], [35, -24], [40, -16], [40, -5], [42, 0], [51, 12], [44, 12],
  [37, 15], [35, 23], [32, 31], [25, 32], [15, 32], [10, 37], [0, 36],
]

/** AVUSTRALYA */
export const AVUSTRALYA = [
  [114, -22], [113, -26], [115, -34], [120, -34], [129, -32], [138, -35],
  [141, -38], [147, -38], [150, -35], [153, -28], [146, -19], [142, -11],
  [136, -12], [130, -12], [125, -14], [117, -20],
]

/** GÜNEY AMERİKA — sol ufukta, büyük kısmı kürenin arkasında kalıyor. */
export const GUNEY_AMERIKA = [
  [-81, 0], [-79, -8], [-71, -18], [-71, -30], [-73, -42], [-75, -50],
  [-68, -55], [-65, -47], [-62, -40], [-57, -35], [-48, -25], [-40, -20],
  [-35, -8], [-45, -1], [-51, 0], [-60, 5], [-72, 11], [-77, 8],
]

/** KUZEY AMERİKA — doğu kıyısı; batısı ufkun ötesinde. */
export const KUZEY_AMERIKA = [
  [-81, 25], [-75, 35], [-70, 42], [-66, 45], [-60, 47], [-56, 51],
  [-64, 60], [-78, 62], [-95, 68], [-125, 70], [-141, 69], [-130, 55],
  [-124, 48], [-120, 34], [-110, 23], [-97, 26], [-90, 29], [-83, 30],
]

/** GRÖNLAND — kuzeyde, buzla birlikte gezegenin üst kenarını tamamlıyor. */
export const GRONLAND = [
  [-45, 60], [-52, 67], [-55, 72], [-60, 76], [-45, 83], [-25, 82],
  [-20, 76], [-25, 70], [-38, 65],
]

/** MADAGASKAR — küçük ama Afrika'nın yanında olmaması gözden kaçmıyor. */
export const MADAGASKAR = [
  [49, -12], [50, -16], [47, -25], [45, -25], [43, -21], [44, -16], [47, -13],
]

/** BÜYÜK BRİTANYA */
export const BRITANYA = [
  [-5, 50], [-3, 54], [-5, 58], [-2, 58], [0, 54], [1, 51],
]

/** JAPONYA */
export const JAPONYA = [
  [131, 31], [135, 34], [140, 36], [142, 40], [145, 44], [141, 45],
  [138, 37], [133, 35], [130, 33],
]

/** İZLANDA */
export const ISLANDA = [
  [-24, 65], [-22, 66], [-16, 66], [-14, 65], [-18, 63], [-22, 64],
]

/** SRİ LANKA */
export const SRI_LANKA = [
  [80, 9], [82, 7], [81, 6], [79, 7], [79, 9],
]

/** YENİ GİNE */
export const YENI_GINE = [
  [131, -1], [141, -3], [147, -6], [150, -10], [143, -9], [137, -8], [132, -5],
]

/**
 * Çizim SIRASI: büyük kütleler önce, adalar sonra. SVG'de sonraki üste
 * biner; adaların büyük kıtaların altında kalmaması için bu sıra gerekli.
 */
export const KITALAR = [
  AVRASYA, AFRIKA, KUZEY_AMERIKA, GUNEY_AMERIKA, AVUSTRALYA,
  GRONLAND, MADAGASKAR, BRITANYA, JAPONYA, ISLANDA, SRI_LANKA, YENI_GINE,
]
