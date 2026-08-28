// ============================================================================
//  ŞEHİR IŞIKLARI — gece tarafındaki metropoller
//
//  ---- NEDEN VAR? ----
//
//  Gezegen artık gece yüzünden görünüyor: karalar koyu, yeşil yok. Ama
//  yalnızca koyu lekelerden oluşan bir küre "gezegen" değil "gölge" gibi
//  okunuyordu. Şehir ışıkları o karanlığa ÖLÇEK veriyor — bakan kişi
//  "burası yaşanan bir yer" bilgisini tek bakışta alıyor.
//
//  ---- NEDEN RASTGELE NOKTA DEĞİL? ----
//
//  Rastgele serpiştirilmiş sarı noktalar tam da kullanıcının "fake duruyor"
//  dediği şeye dönerdi. Bunlar gerçek metropollerin koordinatları; ortografik
//  izdüşümle küreye yansıtılıyor, yani kenarlara doğru kendiliğinden
//  sıkışıyorlar ve gezegen döndürülse doğru yerde kalırlar.
//
//  Parlaklık (p) nüfus/aydınlatma yoğunluğuna kabaca oranlı: İstanbul ve
//  Kahire, Riyad'dan daha parlak. Hepsi eşit olsaydı harita değil delik
//  deşik bir kâğıt gibi görünürdü.
//
//  ---- LİSTE NEDEN KISA? ----
//
//  Kadraj Avrupa–Afrika–Ortadoğu–Batı Asya'yı gösteriyor (bkz. ORTOGRAFİK
//  MERKEZ). Görünmeyen yarıküredeki şehirleri eklemek ölü veri olurdu;
//  izdüşüm onları zaten eliyor ama liste de gereksiz uzardı.
// ============================================================================

/**
 * [boylam, enlem, parlaklık] — parlaklık 0–1.
 *
 * Avrupa'da noktalar birbirine çok yakın olduğu için tek tek şehir yerine
 * birkaç yoğunluk merkezi seçildi: bu ölçekte Amsterdam ile Brüksel'i ayrı
 * çizmek tek bir bulanık lekeden başka bir şey üretmiyor.
 */
export const SEHIRLER = [
  // ---- Türkiye (inişin gideceği yer — biraz daha belirgin) ----
  [28.98, 41.01, 1.00],   // İstanbul
  [32.86, 39.93, 0.80],   // Ankara
  [27.14, 38.42, 0.62],   // İzmir
  [35.32, 37.00, 0.52],   // Adana
  [37.38, 37.06, 0.46],   // Gaziantep

  // ---- Avrupa ----
  [-0.13, 51.51, 0.92],   // Londra
  [2.35, 48.86, 0.90],    // Paris
  [13.40, 52.52, 0.78],   // Berlin
  [12.50, 41.90, 0.74],   // Roma
  [-3.70, 40.42, 0.76],   // Madrid
  [4.90, 52.37, 0.70],    // Randstad
  [16.37, 48.21, 0.60],   // Viyana
  [21.01, 52.23, 0.58],   // Varşova
  [23.73, 37.98, 0.58],   // Atina
  [26.10, 44.44, 0.54],   // Bükreş
  [-9.14, 38.72, 0.56],   // Lizbon
  [37.62, 55.75, 0.86],   // Moskova
  [30.52, 50.45, 0.62],   // Kiev

  // ---- Ortadoğu ----
  [31.24, 30.04, 0.94],   // Kahire
  [35.21, 31.77, 0.60],   // Kudüs–Tel Aviv
  [44.36, 33.31, 0.62],   // Bağdat
  [51.39, 35.69, 0.78],   // Tahran
  [46.72, 24.71, 0.70],   // Riyad
  [55.27, 25.20, 0.72],   // Dubai

  // ---- Afrika ----
  [3.38, 6.52, 0.72],     // Lagos
  [-7.59, 33.57, 0.56],   // Kazablanka
  [3.06, 36.75, 0.54],    // Cezayir
  [10.18, 36.81, 0.46],   // Tunus
  [32.53, 15.50, 0.44],   // Hartum
  [38.75, 9.03, 0.46],    // Addis Ababa
  [36.82, -1.29, 0.46],   // Nairobi
  [28.05, -26.20, 0.58],  // Johannesburg
  [18.42, -33.92, 0.44],  // Cape Town

  // ---- Güney/Orta Asya (kadrajın doğu kenarı) ----
  [67.00, 24.86, 0.66],   // Karaçi
  [77.21, 28.61, 0.72],   // Delhi
  [72.88, 19.08, 0.70],   // Mumbai
  [69.24, 41.30, 0.44],   // Taşkent

  // ---- Kuzey Avrupa / İskandinavya ----
  [18.07, 59.33, 0.50],   // Stockholm
  [10.75, 59.91, 0.44],   // Oslo
  [24.94, 60.17, 0.42],   // Helsinki
]
