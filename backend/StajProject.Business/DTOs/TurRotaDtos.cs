using System.ComponentModel.DataAnnotations;

namespace StajProject.Business.DTOs;

// ============================================================================
//  TUR ROTA ÖNERİSİ — istek ve cevap DTO'ları
//
//  İstek gövdesi, arayüzdeki TourBuilder'ın ürettiği payload'ın birebir
//  karşılığı (frontend/src/turPlani.js → turPayloadu). Alan adları oradaki
//  JSON ile aynı; ASP.NET camelCase gövdeyi bu PascalCase özelliklere
//  kendiliğinden bağlıyor.
// ============================================================================

/// <summary>Turun aranacağı yer.</summary>
public class TurLokasyonDto
{
    /// <summary>Coğrafi bölge süzgeci; "fark etmez" ise null.</summary>
    public string? Bolge { get; set; }

    /// <summary>
    /// BAŞLANGIÇ ŞEHRİ (plaka kodu). ZORUNLU: turun ilk durağı ve arama
    /// merkezi buradan bulunuyor.
    /// </summary>
    [Range(1, 81, ErrorMessage = "Geçerli bir şehir (plaka) seçilmelidir.")]
    public int IlPlaka { get; set; }

    /// <summary>
    /// TURA EKLENEN DİĞER ŞEHİRLER (plaka kodları) — çok şehirli / bölge turu.
    ///
    /// ---- NEDEN AYRI BİR ALAN, TEK BİR LİSTE DEĞİL? ----
    /// Bir liste ("IlPlakalari") daha derli toplu görünürdü ama sıra bilgisini
    /// kaybederdik: turun NEREDEN başladığı, listedeki ilk elemanın rastlantısı
    /// olurdu. Ayrıca var olan bütün istemciler ve testler tek şehirli gövde
    /// gönderiyor; alanı ekleyerek eskisini kırmadan genişletiyoruz.
    ///
    /// ---- NEDEN ÜÇ ŞEHİRLE SINIRLI? ----
    /// Her şehir için AYRI mekan araması yapılıyor (kendi merkezi, kendi
    /// yarıçapı) ve Overpass sorguları 1,5-7 saniye sürüyor. Beş şehirli bir
    /// istek, öneriyi yarım dakikaya çıkarırdı — kullanıcının zaten
    /// bildirdiği "rota oluşturmak çok uzun sürüyor" şikâyetine geri dönmek
    /// olurdu. Fazlası sessizce atılmıyor, uyarı olarak dönüyor.
    /// </summary>
    public List<int> EkIlPlakalari { get; set; } = new();

    /// <summary>
    /// Şehrin adı — arayüz gönderiyor ama sunucu KULLANMIYOR: merkez koordinat
    /// il tablosundan, plakaya göre bulunuyor. Adı da güvenseydik, istemciden
    /// gelen yanlış bir ad turu başka şehirde planlayabilirdi.
    /// </summary>
    public string? IlAdi { get; set; }

    /// <summary>
    /// İlçe adı; boşsa şehrin tamamı taranıyor.
    ///
    /// Koordinata çevirmek için ayrı bir Geocoding çağrısı YAPILMIYOR (bir
    /// istek daha demek olurdu): ilçe adı arama metnine ekleniyor ve Places
    /// sonuçları o ada göre kendiliğinden ilçeye kayıyor.
    /// </summary>
    [MaxLength(100, ErrorMessage = "İlçe adı en fazla 100 karakter olabilir.")]
    public string? Ilce { get; set; }
}

/// <summary>Turun süre bütçesi.</summary>
public class TurSureDto
{
    /// <summary>"Saat" (günübirlik) | "Gun" (çok günlük). Bilgi amaçlı.</summary>
    public string Birim { get; set; } = "Saat";

    /// <summary>Saat ya da gün sayısı. Bilgi amaçlı.</summary>
    public int Deger { get; set; }

    /// <summary>Çok günlük turda bir günde gezilecek saat; günübirlikte null.</summary>
    public int? GunlukSaat { get; set; }

    /// <summary>
    /// SERVİSİN OKUDUĞU TEK ALAN. Birim/değer ikilisinden istemci hesaplıyor
    /// (bkz. turPlani.js → toplamDakika); sunucu aynı hesabı tekrarlamıyor,
    /// çünkü iki yerde yapılan hesap er geç iki farklı sonuç verir.
    /// </summary>
    [Range(30, 14 * 12 * 60, ErrorMessage = "Toplam süre 30 dakika ile 14 gün arasında olmalıdır.")]
    public int ToplamDakika { get; set; }
}

/// <summary>Rota önerisi isteği.</summary>
public class TurRotaIstegiDto
{
    [Required(ErrorMessage = "Lokasyon zorunludur.")]
    public TurLokasyonDto Lokasyon { get; set; } = new();

    /// <summary>"Yaya" | "Arac" | "TopluTasima".</summary>
    [Required(ErrorMessage = "Ulaşım tipi zorunludur.")]
    public string UlasimTipi { get; set; } = "Yaya";

    [Required(ErrorMessage = "Süre zorunludur.")]
    public TurSureDto Sure { get; set; } = new();

    /// <summary>"Kulturel" | "Populer" | "Karma" | "Doga" | "Gastronomi".</summary>
    [Required(ErrorMessage = "Tur teması zorunludur.")]
    public string Tema { get; set; } = "Karma";

    /// <summary>
    /// Temanın açılımı — arayüz gönderiyor, sunucu ŞU AN kullanmıyor.
    ///
    /// Hangi aramaların yapılacağı sunucudaki katalogdan geliyor
    /// (TurKatalogu): arama sorgularını istemcinin belirlemesi, kota
    /// maliyetini istemcinin kontrol etmesi demek olurdu. Alan sözleşmede
    /// duruyor çünkü ileride "yalnızca şu tipleri istiyorum" gibi bir
    /// daraltma buradan yapılabilir.
    /// </summary>
    public List<string> TercihEdilenMekanTipleri { get; set; } = new();

    /// <summary>"Yok" | "Vejetaryen" | "Vegan".</summary>
    public string BeslenmeKisiti { get; set; } = "Yok";

    /// <summary>Beslenme kısıtının arama etiketleri ("vegan"); boşsa süzme yok.</summary>
    public List<string> BeslenmeEtiketleri { get; set; } = new();

    /// <summary>
    /// Programa GÜNDE BİR yemek molası eklensin mi?
    ///
    /// Gezi temalarında yeme-içme araması bilerek yok — bir şehir turuna
    /// rastgele kafe düşmesi canlıda yaşandı. Ama öğle yemeği rastgele bir
    /// durak değil, programın parçası; bu yüzden ayrı ve İSTEĞE BAĞLI.
    ///
    /// Mola durağı da beslenme kısıtına ve MekanKalitesi süzgecine tabi:
    /// vegan seçildiyse mola için de yalnızca uygun mekanlar aranıyor.
    /// </summary>
    public bool YemekMolasi { get; set; }

    /// <summary>
    /// Çok günlü turda her günün sonuna KONAKLAMA durağı eklensin mi?
    ///
    /// Yalnızca Birim == "Gun" ve Deger > 1 iken anlamlı; günübirlik turda
    /// yok sayılıyor. Son günün sonuna konmuyor — o akşam gidiliyor.
    ///
    /// ---- NEDEN bool? (nullable) ----
    /// Canlıda çok günlü turlarda konaklama hiç gelmiyordu. Sunucu tarafı
    /// baştan sona doğrulandı — Overpass 142 otel döndürüyor, sınıflandırma
    /// da doğru (Hotel) — yani zincirin tek kırık halkası bayrağın kendisi:
    /// istek gövdesinde ya hiç gelmiyor ya da false geliyordu.
    ///
    /// `bool` olsaydı "gönderilmedi" ile "istemiyorum" ayırt edilemezdi;
    /// ikisi de false. Nullable ile ayrılıyor ve GÖNDERİLMEDİĞİNDE çok günlü
    /// turun varsayılanı AÇIK oluyor: kullanıcı "gece konaklaması sun"
    /// dedi, sessizce atlamak yanlış varsayılan.
    ///
    /// Açıkça false gönderen istemci yine kapatabiliyor.
    /// </summary>
    public bool? Konaklama { get; set; }

    /// <summary>
    /// Güne SERBEST ZAMAN eklensin mi?
    ///
    /// ---- NEDEN GEREKLİ? ----
    /// Günlük durak sınırı (yaya 6) ile günün saat bütçesi (8 sa) uyuşmuyor:
    /// altı durak × ~40 dakika ancak dört saat ediyor ve program 13:00'te
    /// bitiyordu. Kullanıcı bunu "günler 1'de bitmesin" diye bildirdi.
    ///
    /// Çözüm durak sayısını artırmak DEĞİL — o, daha önce düzeltilen "bir
    /// günde 13 yer gez" hatasına geri dönmek olurdu. Gerçek turlarda o boşluk
    /// zaten serbest zamandır: Hamamönü'nde dolaşmak, çarşıda gezinmek.
    /// Bu yüzden uygun bir durağın kalış süresi uzatılıyor, yeni durak
    /// eklenmiyor.
    /// </summary>
    public bool SerbestZaman { get; set; }
}

/// <summary>
/// Rota önerisi cevabı.
///
/// <see cref="TourDto"/>'DAN TÜRÜYOR: arayüz öneriyi kaydedilmiş bir turla
/// aynı biçimde çiziyor (ad, duraklar, rota, tahmini süre). Ayrı bir biçim
/// tanımlasaydık istemcide iki çizim kodu olurdu.
///
/// Öneri HENÜZ KAYDEDİLMEMİŞTİR: <c>Id</c> ve durakların <c>Id</c>'si 0.
/// Kullanıcı "kaydet" derse tur şablonu o zaman oluşturuluyor.
/// </summary>
public class TurOneriDto : TourDto
{
    /// <summary>
    /// Kullanıcıya gösterilecek uyarılar: "bütçeye sığmayan 3 durak
    /// çıkarıldı", "yeterli 4.5+ mekan bulunamadı" gibi.
    ///
    /// Neden hata değil de uyarı? Bunların hiçbiri öneriyi geçersiz kılmıyor —
    /// ama sessiz kalmak, kullanıcının eksik bir turu tam sanmasına yol açardı.
    /// </summary>
    public List<string> Uyarilar { get; set; } = new();

    /// <summary>
    /// BİR GÜNDE gezilecek en fazla durak — sunucunun aday seçerken
    /// kullandığı sınırın ta kendisi (bkz. TurKatalogu.GunlukAzamiDurak).
    ///
    /// ---- NEDEN CEVABIN İÇİNDE GİDİYOR? ----
    /// Programı gün gün bölen hesap İSTEMCİDE (turProgrami.js): başlangıç
    /// saati ve günlük süre kullanıcının anlık seçimi, her değişiklikte
    /// sunucuya gitmek saat kaydırmayı ağ gecikmesine bağlardı.
    ///
    /// Ama o bölme uzun süre YALNIZCA SAATE baktı ve canlıda şu çıktı: iki
    /// günlük Ankara turunun 13 durağı birinci güne yığıldı, ikinci güne tek
    /// durak kaldı. Sebebi, iki tarafın farklı gün tanımı kullanmasıydı —
    /// sunucu "günde en fazla 6 durak" diyordu, istemci "8 saate ne sığarsa".
    /// 30 dakikalık kalışlarla 12 durak sekiz saate matematiksel olarak
    /// sığıyor; gerçek bir gezi günü öyle değil.
    ///
    /// Sayıyı istemcide TEKRAR TANIMLAMAK yerine cevaba koyuyoruz: sınır
    /// değişirse tek yerde değişsin.
    /// </summary>
    public int GunlukAzamiDurak { get; set; }

    /// <summary>
    /// Bu öneri için Google'a atılan istek sayısı (önbellekten karşılananlar
    /// HARİÇ).
    ///
    /// Cevaba KASITLI olarak konuyor: kota maliyeti görünmez bir sayı olarak
    /// kalırsa yönetilemez. Yönetim ekranı ve testler "bir tur önerisi kaç
    /// istek harcıyor?" sorusunu buradan cevaplıyor.
    /// </summary>
    public int IstekMaliyeti { get; set; }
}

/// <summary>Turistik POI içe aktarım isteği.</summary>
public class TuristikAktarimIstegiDto
{
    /// <summary>Aktarılacak illerin plaka kodları — örn. [6, 34].</summary>
    [Required(ErrorMessage = "En az bir şehir seçilmelidir.")]
    [MinLength(1, ErrorMessage = "En az bir şehir seçilmelidir.")]
    public List<int> IlPlakalari { get; set; } = new();
}

/// <summary>
/// İçe aktarım sonucu.
///
/// Sayılar KASITLI olarak ayrıntılı: aktarım tek seferlik ve yönetici
/// "gerçekten ne oldu?" sorusunun cevabını görmeli. "Başarılı" demek,
/// hiçbir şey eklenmemiş bir çalıştırmayı da başarılı göstermek olurdu.
/// </summary>
public class TuristikAktarimSonucuDto
{
    /// <summary>Veritabanına yazılan yeni POI sayısı.</summary>
    public int Eklenen { get; set; }

    /// <summary>Mükerrer ya da kategorisi eşleşmediği için atlanan kayıt sayısı.</summary>
    public int Atlanan { get; set; }

    /// <summary>Şehir adı → eklenen kayıt sayısı.</summary>
    public Dictionary<string, int> Sehirler { get; set; } = new();

    /// <summary>Kategori adı → eklenen kayıt sayısı.</summary>
    public Dictionary<string, int> KategoriBazinda { get; set; } = new();

    /// <summary>Engellemeyen sorunlar: "şu şehir için sonuç alınamadı" gibi.</summary>
    public List<string> Uyarilar { get; set; } = new();
}

/// <summary>
/// Öneriyi kalıcı tur şablonuna çeviren istek.
///
/// Duraklar İSTEMCİDEN geliyor: öneri sunucuda saklanmıyor (bir cevaptı) ve
/// kullanıcı kaydetmeden önce durak çıkarabiliyor. Sunucuda tutup "önerimi
/// kaydet" demek, her öneriyi kaydedilmese bile veritabanına yazmak olurdu.
/// </summary>
public class TurKaydetDto
{
    [Required(ErrorMessage = "Tur adı zorunludur.")]
    [MaxLength(200, ErrorMessage = "Tur adı en fazla 200 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000, ErrorMessage = "Açıklama en fazla 1000 karakter olabilir.")]
    public string? Description { get; set; }

    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Renk #rrggbb biçiminde olmalıdır.")]
    public string Color { get; set; } = "#7b5cd6";

    /// <summary>Yollara oturmuş rota (WKT LINESTRING); yoksa null.</summary>
    public string? RouteWkt { get; set; }

    public double? RouteDistanceMeters { get; set; }

    public double? RouteDurationSeconds { get; set; }

    /// <summary>
    /// Duraklar, SIRAYLA. En az iki durak: tek duraklı bir tur gezilecek bir
    /// şey değil, rota da çizilemez.
    /// </summary>
    [MinLength(2, ErrorMessage = "Tur en az iki durak içermelidir.")]
    public List<TurDurakKaydetDto> Waypoints { get; set; } = new();
}

/// <summary>
/// YENİ bir turun içindeki durak.
///
/// ---- NEDEN <see cref="WaypointCreateDto"/> KULLANILMIYOR? ----
/// O DTO, VAR OLAN bir tura durak eklemek için: <c>TourId</c> alanı zorunlu
/// ("Tur seçilmelidir"). Tur henüz oluşmadığı için burada öyle bir id yok ve
/// onu kullanmak, kaydetme isteğini her durak için bir doğrulama hatasıyla
/// reddetmek demekti (canlıda tam olarak bu oldu: altı duraklı bir öneri
/// "Tur seçilmelidir" mesajını altı kez döndürdü).
///
/// Sıra da yok: duraklar listedeki SIRAYLA 1..N numaralanıyor. İstemcinin
/// gönderdiği bir sıraya güvenmek, atlamalı ya da tekrarlı numaralara kapı
/// açardı.
/// </summary>
public class TurDurakKaydetDto
{
    [Required(ErrorMessage = "Durak adı zorunludur.")]
    [MaxLength(200, ErrorMessage = "Durak adı en fazla 200 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Dış sağlayıcı kimliği; boşsa sunucu "manual:{guid}" üretiyor.</summary>
    [MaxLength(200, ErrorMessage = "Mekan kimliği en fazla 200 karakter olabilir.")]
    public string? PlaceId { get; set; }

    /// <summary>Sistemdeki POI karşılığı — varsa.</summary>
    public int? PoiId { get; set; }

    /// <summary>"Museum", "Cafe"… Tanınmayan değer <c>Other</c> sayılır.</summary>
    public string VenueType { get; set; } = "Other";

    [Range(0, 1440, ErrorMessage = "Kalış süresi 0-1440 dakika arasında olmalıdır.")]
    public int DwellMinutes { get; set; }

    /// <summary>Konum — "POINT (32.85 39.93)".</summary>
    [Required(ErrorMessage = "Durak konumu (WKT) zorunludur.")]
    public string Wkt { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Not en fazla 500 karakter olabilir.")]
    public string? Note { get; set; }
}

/// <summary>
/// Oturum açma cevabına eklenen PAYLAŞIM bilgisi.
///
/// Bağlantının kendisini SUNUCU üretmiyor: uygulamanın adresini (localhost mu,
/// yayındaki alan adı mı) yalnızca tarayıcı biliyor. Sunucu KODU veriyor,
/// istemci onu kendi adresiyle birleştiriyor — sunucuya taban adres ayarı
/// eklemek, aynı bilginin iki yerde tutulması olurdu.
/// </summary>
public class TurPaylasimDto
{
    /// <summary>Katılım kodu — "K7QF2M".</summary>
    public string JoinCode { get; set; } = string.Empty;

    /// <summary>Bağlantının istemci tarafındaki yolu: "/tur/K7QF2M".</summary>
    public string Yol { get; set; } = string.Empty;
}

// ============================================================================
//  ROTA YENİDEN HESAPLAMA — durak ekleyip çıkardıktan sonra
// ============================================================================

/// <summary>
/// Elle düzenlenmiş durak listesi için rota isteği.
///
/// ---- NEDEN AYRI BİR UÇ? ----
/// <c>rota-oner</c> mekanları KENDİSİ buluyor: tema, süre ve bütçeye göre
/// aday arayıp seçiyor. Kullanıcı listeye elle bir POI eklediğinde ise
/// seçim zaten yapılmış; eksik olan tek şey, yeni sıralamanın YOLLARA OTURMUŞ
/// rotası. Aynı uçtan geçirseydik her küçük düzenleme yeni bir mekan
/// aramasını (ve dış servis maliyetini) tetiklerdi.
///
/// ---- SIRA KORUNUYOR ----
/// Noktalar VERİLDİĞİ SIRAYLA rotalanıyor, yeniden optimize edilmiyor.
/// Kullanıcı bir durağı bilerek araya soktu; sunucunun onu "daha kısa olur"
/// diye başka yere taşıması, yaptığı işi geri almak olurdu.
/// </summary>
public class TurRotaHesapIstegiDto
{
    /// <summary>Yaya / Arac / TopluTasima — süre tahminleri buna göre.</summary>
    [Required(ErrorMessage = "Ulaşım tipi zorunludur.")]
    public string UlasimTipi { get; set; } = "Yaya";

    /// <summary>Sıralı duraklar. En az iki: tek noktadan rota çıkmaz.</summary>
    [MinLength(2, ErrorMessage = "Rota için en az iki durak gerekir.")]
    public List<TurRotaNoktasiDto> Duraklar { get; set; } = new();
}

/// <summary>Rota hesabına giren tek nokta.</summary>
public class TurRotaNoktasiDto
{
    [Range(-90, 90, ErrorMessage = "Enlem -90 ile 90 arasında olmalıdır.")]
    public double Lat { get; set; }

    [Range(-180, 180, ErrorMessage = "Boylam -180 ile 180 arasında olmalıdır.")]
    public double Lon { get; set; }
}

/// <summary>
/// Yeniden hesaplanmış rota.
///
/// Durak listesi GERİ DÖNMÜYOR — istemci onu zaten biliyor, kendisi
/// gönderdi. Yalnızca çizgi ve toplamlar dönüyor; durakları da geri
/// göndermek, aynı verinin iki kopyasının ayrışma riskini doğururdu.
/// </summary>
public class TurRotaHesapSonucuDto
{
    /// <summary>Yollara oturmuş rota (WKT LINESTRING); çizilemezse null.</summary>
    public string? RouteWkt { get; set; }

    public double? RouteDistanceMeters { get; set; }

    public double? RouteDurationSeconds { get; set; }

    /// <summary>
    /// Rota çizilemediyse sebebi.
    ///
    /// HATA DEĞİL UYARI: rota olmadan da durak listesi geçerli bir tur.
    /// 503 döndürseydik dış servisin geçici bir aksaklığı, kullanıcının elle
    /// yaptığı düzenlemeyi de çöpe atardı.
    /// </summary>
    public string? Uyari { get; set; }
}
