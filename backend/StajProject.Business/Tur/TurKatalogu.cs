using StajProject.DataAccess.Google;
using StajProject.Entities;

namespace StajProject.Business.Tur;

/// <summary>
/// Bir temanın Google'da NASIL aranacağı.
/// </summary>
/// <param name="Sorgu">Text Search'e giden serbest metin ("müze").</param>
/// <param name="GoogleTipi">
/// Places API (New) tip anahtarı. Yalnızca kesinlikle geçerli tipler
/// kullanılıyor: uydurma bir tip <c>INVALID_ARGUMENT</c> döndürür ve o istek
/// boşa faturalanır. Nüansı (ör. "tarihi yer") sorgu metni taşıyor, tip
/// güvenli tarafta kalıyor.
/// </param>
/// <param name="MekanTipi">Bulunan mekanların turdaki tipi — kalış süresi buradan.</param>
/// <param name="DarYaricap">
/// Arama, gezi aramalarından DAHA DAR bir alanda yapılsın mı?
///
/// Yemek molası ve konaklama için true: mola durağı o günün duraklarının
/// yanında olmak zorunda, 25 km öteki bir lokanta zaten seçilmiyor — ama
/// sorgu onu yine de tarıyor ve şehir çapında restoran aramak Overpass'te
/// gezi süzgeçlerinin toplamından pahalı.
///
/// Bayrak ARAMANIN KENDİSİNDE, mekan tipinde DEĞİL: Gastronomi temasında
/// lokantalar turun kendisi ve tam yarıçapla aranmaları gerekiyor. Tipe
/// baksaydık o tema da sessizce 10 km'ye sıkışırdı.
/// </param>
public record TemaAramasi(
    string Sorgu,
    string GoogleTipi,
    VenueType MekanTipi,
    bool DarYaricap = false);

/// <summary>
/// TUR KATALOĞU — tema, mekan tipi ve süre eşlemeleri.
///
/// ---- NEDEN AYRI, SAF BİR SINIF? ----
/// Buradaki her şey VERİ: hangi tema hangi aramaları yapar, hangi tip kaç
/// dakika sürer, hangi ulaşım tipi hangi Google kipine karşılık gelir.
/// Servisin içine gömülseydi bunları sınamak için HTTP taklidi kurmak
/// gerekirdi; ayrıca yeni bir tema eklemek servis kodunu değiştirmek olurdu.
///
/// Kalış süreleri arayüzdeki varsayılanlarla (turPlani.js → MEKAN_TIPLERI)
/// BİLEREK aynı: kullanıcı formda "müze ≈ 45 dk" görüp öneride 90 dakika
/// bulursa, iki tarafın farklı hesap yaptığını düşünür.
/// </summary>
public static class TurKatalogu
{
    /// <summary>
    /// Tema → arama listesi. Sıra ÖNEM SIRASI: ayarlardaki
    /// <c>TemaBasinaArama</c> listenin başından kesiyor, yani en tipik
    /// aramalar her zaman yapılıyor.
    /// </summary>
    private static readonly Dictionary<string, TemaAramasi[]> Aramalar = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Kulturel"] = new[]
        {
            new TemaAramasi("müze", "museum", VenueType.Museum),
            new TemaAramasi("tarihi yer ve anıt", "tourist_attraction", VenueType.Monument),
            new TemaAramasi("sanat galerisi", "art_gallery", VenueType.Museum),
            new TemaAramasi("tarihi cami", "mosque", VenueType.ReligiousSite),
        },
        ["Populer"] = new[]
        {
            new TemaAramasi("gezilecek yer", "tourist_attraction", VenueType.Monument),
            new TemaAramasi("müze", "museum", VenueType.Museum),
            new TemaAramasi("tarihi cami", "mosque", VenueType.ReligiousSite),
            new TemaAramasi("park", "park", VenueType.Park),
        },

        // ---- KARMA: GEZİLECEK YERLER, YEME-İÇME DEĞİL ----
        //
        // Önce bu temada kafe ve restoran aramaları da vardı. Canlıda üretilen
        // iki günlük Ankara turunun içinde "Melek Cafem" ve "476. Yurt
        // Kantini" çıktı; kullanıcının tepkisi haklıydı — kimse şehir turuna
        // rastgele bir kafe koymaz.
        //
        // "Karma" artık kültür + inanç + doğa karışımı demek. Yeme-içme
        // isteyen kullanıcının seçeceği tema Gastronomi; orada aramalar
        // kalite süzgecinden geçiyor (bkz. MekanKalitesi).
        ["Karma"] = new[]
        {
            new TemaAramasi("gezilecek yer", "tourist_attraction", VenueType.Monument),
            new TemaAramasi("müze", "museum", VenueType.Museum),
            new TemaAramasi("tarihi cami", "mosque", VenueType.ReligiousSite),
            new TemaAramasi("park", "park", VenueType.Park),
        },
        ["Doga"] = new[]
        {
            new TemaAramasi("park", "park", VenueType.Park),
            new TemaAramasi("mesire ve tabiat alanı", "tourist_attraction", VenueType.Park),
            new TemaAramasi("seyir tepesi manzara", "tourist_attraction", VenueType.Viewpoint),
        },
        ["Gastronomi"] = new[]
        {
            new TemaAramasi("restoran", "restaurant", VenueType.Restaurant),
            new TemaAramasi("yerel lezzet lokantası", "restaurant", VenueType.Restaurant),
            new TemaAramasi("kafe", "cafe", VenueType.Cafe),
            new TemaAramasi("tatlıcı", "cafe", VenueType.Cafe),
        },
    };

    /// <summary>
    /// YEMEK MOLASI aramaları — gezi temalarından AYRI tutuluyor.
    ///
    /// Gezi temalarında (Karma, Popüler, Kültürel…) yeme-içme araması yok:
    /// bir şehir turuna rastgele kafe düşmesi canlıda yaşandı ve doğru
    /// çözüm o aramaları kaldırmaktı. Ama "öğle yemeği" ayrı bir şey —
    /// tur programının parçası, rastgele bir durak değil.
    ///
    /// Bu yüzden mola, TEMADAN BAĞIMSIZ bir arama olarak duruyor ve yalnızca
    /// kullanıcı istediğinde (YemekMolasi) çalışıyor. Sonuçlar yine
    /// MekanKalitesi süzgecinden ve beslenme kısıtından geçiyor.
    /// </summary>
    public static readonly TemaAramasi[] YemekMolasiAramalari =
    {
        new TemaAramasi("yöresel lokanta", "restaurant", VenueType.Restaurant, DarYaricap: true),
        new TemaAramasi("kebapçı ve ocakbaşı", "restaurant", VenueType.Restaurant, DarYaricap: true),
        new TemaAramasi("ev yemekleri lokantası", "restaurant", VenueType.Restaurant, DarYaricap: true),
    };

    /// <summary>
    /// KONAKLAMA aramaları — yalnızca çok günlü turlarda.
    ///
    /// OSM'de otel kaydı var ama PUAN YOK; bu yüzden öneri "şu otelde kalın"
    /// demiyor, günün sonuna "geceleme bölgesi" olarak bir durak koyuyor.
    /// Ayrımı durağın notu söylüyor.
    /// </summary>
    public static readonly TemaAramasi[] KonaklamaAramalari =
    {
        new TemaAramasi("otel", "lodging", VenueType.Hotel, DarYaricap: true),
        new TemaAramasi("butik otel konaklama", "lodging", VenueType.Hotel, DarYaricap: true),
    };

    /// <summary>Tanınmayan tema "Karma" sayılır — öneri üretmeden reddetmek yerine.</summary>
    public static IReadOnlyList<TemaAramasi> TemaAramalari(string? tema)
        => Aramalar.TryGetValue(tema ?? string.Empty, out var liste) ? liste : Aramalar["Karma"];

    /// <summary>Katalogdaki tema adları — doğrulama ve testler için.</summary>
    public static IReadOnlyCollection<string> Temalar => Aramalar.Keys;

    /// <summary>
    /// Mekan tipine göre TAHMİNİ kalış süresi (dakika).
    ///
    /// Neden mekan başına Google'a sorulmuyor? Böyle bir alan zaten yok;
    /// "popular_times" gibi veriler ayrı ve pahalı çağrılar gerektirir.
    /// Tipe dayalı tablo, ek maliyeti sıfır olan ve tur planı için yeterince
    /// iyi bir tahmin.
    /// </summary>
    private static readonly Dictionary<VenueType, int> KalisSureleri = new()
    {
        [VenueType.Museum] = 45,
        [VenueType.Monument] = 20,
        [VenueType.ReligiousSite] = 20,
        [VenueType.Park] = 30,
        [VenueType.Viewpoint] = 15,
        [VenueType.Restaurant] = 60,
        [VenueType.Cafe] = 30,
        [VenueType.Shopping] = 45,
        [VenueType.Hotel] = 10,
        [VenueType.TransportHub] = 10,
        [VenueType.Other] = 15,
    };

    /// <summary>
    /// Bir mekana atanan kalış süresi.
    ///
    /// Taban süre tipten geliyor; ÇOK ZİYARET EDİLEN yerlere (5000+
    /// değerlendirme) %25 ekleniyor. Gerekçe: bu mekanlar genelde büyük ve
    /// kuyruklu oluyor — Anıtkabir ile mahalle müzesine aynı 45 dakikayı
    /// vermek, turun ikinci yarısını sistematik olarak geciktirirdi.
    ///
    /// Sonuç 5'in katına yuvarlanıyor: "56 dakika" gibi bir tahmin, olduğundan
    /// çok daha kesin görünürdü.
    /// </summary>
    public static int KalisSuresiDakika(VenueType tip, int degerlendirmeSayisi)
    {
        var taban = KalisSureleri.TryGetValue(tip, out var dakika) ? dakika : 15;
        var carpan = degerlendirmeSayisi >= 5_000 ? 1.25 : 1.0;

        return (int)(Math.Round(taban * carpan / 5.0) * 5);
    }

    /// <summary>
    /// Google'ın tip etiketlerinden tur mekan tipini çıkarır.
    ///
    /// Aramanın kendi tipi zaten biliniyor; bu eşleme, cevapta gelen
    /// <c>primaryType</c> daha ÖZEL olduğunda kullanılıyor — "tourist_attraction"
    /// diye aranıp gelen bir yer aslında müzeyse ona müze süresi verilsin.
    /// Eşleşme yoksa aramanın tipi korunuyor.
    /// </summary>
    private static readonly Dictionary<string, VenueType> TipEslemesi = new(StringComparer.OrdinalIgnoreCase)
    {
        ["museum"] = VenueType.Museum,
        ["art_gallery"] = VenueType.Museum,
        ["historical_landmark"] = VenueType.Monument,
        ["monument"] = VenueType.Monument,
        ["tourist_attraction"] = VenueType.Monument,
        ["viewpoint"] = VenueType.Viewpoint,
        ["tower"] = VenueType.Viewpoint,
        ["mosque"] = VenueType.ReligiousSite,
        ["church"] = VenueType.ReligiousSite,
        ["synagogue"] = VenueType.ReligiousSite,
        ["hindu_temple"] = VenueType.ReligiousSite,
        ["park"] = VenueType.Park,
        ["national_park"] = VenueType.Park,
        ["garden"] = VenueType.Park,
        ["zoo"] = VenueType.Park,
        ["restaurant"] = VenueType.Restaurant,
        ["cafe"] = VenueType.Cafe,
        ["bakery"] = VenueType.Cafe,
        ["shopping_mall"] = VenueType.Shopping,
        ["market"] = VenueType.Shopping,
        ["lodging"] = VenueType.Hotel,
        ["hotel"] = VenueType.Hotel,
        ["transit_station"] = VenueType.TransportHub,
        ["bus_station"] = VenueType.TransportHub,
        ["subway_station"] = VenueType.TransportHub,
    };

    /// <summary>
    /// Mekanın tipini belirler: önce Google'ın birincil tipi, sonra tip
    /// listesi, hiçbiri eşleşmezse aramanın tipi.
    /// </summary>
    public static VenueType MekanTipi(GooglePlace mekan, VenueType aramaTipi)
    {
        if (mekan.BirincilTip is not null && TipEslemesi.TryGetValue(mekan.BirincilTip, out var birincil))
        {
            return birincil;
        }

        foreach (var tip in mekan.Tipler)
        {
            if (TipEslemesi.TryGetValue(tip, out var eslesen))
            {
                return eslesen;
            }
        }

        return aramaTipi;
    }

    /// <summary>
    /// Yeme-içme mekanı mı? Beslenme kısıtı yalnızca bunlara uygulanıyor.
    ///
    /// Tanım <see cref="MekanKalitesi"/>'ne taşındı: aynı soruyu hem tur
    /// önerisi hem POI aktarımı soruyor ve iki yerde ayrı tanım, ikisinin
    /// zamanla ayrışması demekti.
    /// </summary>
    public static bool YemeIcme(VenueType tip) => MekanKalitesi.YemeIcme(tip);

    /// <summary>Ulaşım tipinin Google karşılığı.</summary>
    public static SeyahatKipi SeyahatKipi(string? ulasimTipi) => ulasimTipi switch
    {
        "Yaya" => DataAccess.Google.SeyahatKipi.Yaya,
        "TopluTasima" => DataAccess.Google.SeyahatKipi.ToplaTasima,
        _ => DataAccess.Google.SeyahatKipi.Arac,
    };

    /// <summary>
    /// Toplu taşıma süre düzeltmesi.
    ///
    /// Sıralama "driving" ile yapılıyor (Directions'ta transit kipi ara nokta
    /// kabul etmiyor — bkz. DirectionsClient). Sürüş süresi toplu taşımayı
    /// olduğundan hızlı gösterir: bekleme, aktarma ve durak duraklamaları yok.
    /// Bu katsayı farkı kapatıyor; kaba ama YÖNÜ doğru bir düzeltme, hiç
    /// düzeltmemek turu sistematik olarak fazla dolu planlamak olurdu.
    /// </summary>
    public const double TopluTasimaSureCarpani = 1.6;

    /// <summary>
    /// BİR GÜNDE gezilebilecek en fazla durak — ulaşım tipine göre.
    ///
    /// ---- NEDEN SÜRE BÜTÇESİ YETMİYOR? ----
    /// Süre hesabı "12 saate 15 durak sığıyor" diyebiliyor ve matematiksel
    /// olarak haklı: araçla mesafeler kısa, kalış süreleri 20'şer dakika.
    /// Ama kimse bir günde 15 yer gezmiyor — canlıda üretilen ilk Ankara
    /// turu tam olarak böyleydi ve kullanılamaz bir listeydi.
    ///
    /// Sayılar gerçek tur programlarına yakın: yürüyerek 6, toplu taşımayla
    /// 6, araçla 8 durak bir günün dolu ama yapılabilir hâli.
    /// </summary>
    public static int GunlukAzamiDurak(SeyahatKipi kip) => kip switch
    {
        DataAccess.Google.SeyahatKipi.Yaya => 6,
        DataAccess.Google.SeyahatKipi.ToplaTasima => 6,
        _ => 8,
    };

    /// <summary>
    /// Yaya turlarda iki durak arasındaki en fazla makul yürüyüş (metre).
    ///
    /// Aday elemede kullanılıyor: 5 km ötedeki 4.9 puanlı müze, yürüyerek
    /// gidilecek bir tur durağı değil — Google sıralamayı optimize eder ama
    /// "bu yürünmez" demez.
    /// </summary>
    public const double YayaAzamiBacakMetre = 3_000;
}
