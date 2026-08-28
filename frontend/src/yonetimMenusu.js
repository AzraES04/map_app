// ============================================================================
//  YÖNETİM PANELİ MENÜSÜ — tek kaynak
//
//  Menü maddeleri neden ayrı bir dosyada? İki yer birden bu listeye bakıyor:
//
//    AdminLayout.jsx → soldaki dikey menüyü çiziyor
//    MapPage.jsx     → üst bardaki "Yönetim" düğmesini gösterip gizliyor ve
//                      tıklanınca kullanıcının AÇABİLECEĞİ ilk ekrana gidiyor
//
//  ÖDEV 16'DA SOMUT BİR HATAYA DÖNÜŞTÜ. Önceden "Yönetim" düğmesi yalnızca
//  Kullanıcı/Rol yetkisine bakıyordu ve adres olarak /admin/users'a
//  gidiyordu. Ulaşım Operatörü'nün ikisi de yok ama "Güzergah Yönetimi"
//  ekranı var — düğme hiç görünmüyor, dolayısıyla operatör kendi paneline
//  ulaşamıyordu. (Aynı boşluk POI Yönetimi için de vardı; yalnız POI yetkisi
//  olan bir hesap açılsaydı o da panele giremezdi.)
//
//  Liste burada durduğu sürece "yeni ekran ekle" işi tek satır ve iki yer
//  otomatik olarak birbirine uyuyor.
// ============================================================================

import { YETKILER } from './yetkiler'

/**
 * @typedef {object} YonetimEkrani
 * @property {string} yol    Rota adresi
 * @property {string} baslik Menüde görünen ad
 * @property {string} altyazi Menüdeki ikinci satır
 * @property {string} yetki  Ekranı açabilmek için gereken yetkinin adı
 * @property {string} ikon   icons.jsx'teki bileşenin anahtarı (bkz. AdminLayout)
 */

/** Sıra menüdeki sıradır; ilk madde aynı zamanda varsayılan açılış ekranı. */
export const YONETIM_EKRANLARI = [
  {
    yol: '/admin/users',
    baslik: 'Kullanıcı Listesi',
    altyazi: 'Ekle / Güncelle / Sil',
    yetki: YETKILER.kullaniciYonetimi,
    ikon: 'kullanicilar',
  },
  {
    yol: '/admin/roles',
    baslik: 'Rol Listesi',
    altyazi: 'Ekle / Güncelle / Sil',
    yetki: YETKILER.rolYonetimi,
    ikon: 'rol',
  },
  {
    // Ödev 12 / Madde 2: POI listesi + kategori yönetimi
    yol: '/admin/poi',
    baslik: 'POI Yönetimi',
    altyazi: 'POI listesi / Kategoriler',
    yetki: YETKILER.poiYonetimi,
    ikon: 'poi',
  },
  {
    // Ödev 16 / Madde 2: güzergah tanımı + durakların sıralanması
    yol: '/admin/guzergah',
    baslik: 'Güzergah Yönetimi',
    altyazi: 'Hatlar / Durak sırası',
    yetki: YETKILER.guzergahYonetimi,
    ikon: 'guzergah',
  },
  {
    // Çöp kutusu — silinen HER şey (nokta, çizgi, poligon, POI, kategori,
    // durak, güzergah, kullanıcı, rol) burada duruyor ve geri alınabiliyor.
    //
    // `yetki: null` = HERKESE görünür. Sebep: geri alma yetkisi kaydın
    // TÜRÜNE göre değişiyor (noktayı geri almak "Kayıt Silme", rolü geri
    // almak "Rol Yönetimi" istiyor) ve bu ekran tek bir yetkiyle
    // eşleşmiyor. Tek bir yetkiye bağlasaydık, örneğin yalnızca POI
    // yetkisi olan kullanıcı kendi sildiği POI'yi göremezdi.
    //
    // Ekranın kendisi zaten güvenli: liste herkese açık (silinen kaydın adı
    // zaten görünen bir bilgiydi), GERİ ALMA düğmesi ise türün yetkisi
    // yoksa hiç çıkmıyor ve sunucu ayrıca kontrol ediyor.
    yol: '/admin/cop',
    baslik: 'Çöp Kutusu',
    altyazi: 'Silinenler / Geri al',
    yetki: null,
    ikon: 'cop',
  },
]

/**
 * Kullanıcının açabileceği ilk yönetim ekranı; hiçbiri yoksa null.
 *
 * "Yönetim" düğmesi hem GÖRÜNÜRLÜĞÜNÜ hem HEDEFİNİ buradan alıyor: sabit bir
 * adrese gitseydik, o ekranın yetkisi olmayan kullanıcı düğmeye basıp 403
 * görürdü — kendi açabileceği başka bir ekran dururken.
 *
 * @param {string[]} yetkilerim Kullanıcının sahip olduğu yetki adları
 */
export function ilkYonetimEkrani(yetkilerim) {
  return YONETIM_EKRANLARI.find((e) => yetkilerim.includes(e.yetki))?.yol ?? null
}
