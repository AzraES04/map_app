using StajProject.DataAccess.Google;
using StajProject.Entities;

namespace StajProject.Business.Tur;

/// <summary>
/// YEME-İÇME MEKANI ELEMESİ — zincir şubeleri ve sinyalsiz kayıtlar.
///
/// ---- BU DOSYA NEDEN VAR? ----
/// Aynı eleme iki yerde gerekiyor: POI içe aktarımında ve TUR ÖNERİSİNDE.
/// Başlangıçta yalnızca aktarımda vardı ve sonuç canlıda şu oldu — kullanıcı
/// iki günlük Ankara turu istedi, listeye "Melek Cafem", "476. Yurt Kantini"
/// ve "Kamara Coffee &amp; Community" düştü. Kural aktarımda duruyordu ama
/// öneri onu hiç çağırmıyordu.
///
/// Ortak bir yere taşındı: iki çağıran da aynı kuralı kullanıyor ve kural
/// değiştiğinde iki yer birden değişiyor.
///
/// ---- KULLANICI PUANI YERİNE NE KOYUYORUZ? ----
/// İstenen "4.5+ puanlı yerler" ölçütü OpenStreetMap'te KARŞILANAMIYOR: orada
/// kullanıcı puanı diye bir veri yok (bkz. IPlacesClient.PuanVerisiVar). Puan
/// süzgeci ancak Google anahtarı tanımlıyken çalışıyor. Anahtarsız kipte
/// elimizdeki en yakın şey, aşağıdaki iki eleme:
///
///   1. ZİNCİR ŞUBESİ DEĞİL   (marka etiketi ya da bilinen ad)
///   2. KAYDA DEĞER            (mutfağı girilmiş / ansiklopedi maddesi var)
///
/// Bu, puanın yerini tutmuyor ama "yurt kantini" ile "Çorbacı Hasan Usta"yı
/// ayırıyor — Ankara verisiyle ölçüldü: 559 adlı mekandan 34 zincir elendi,
/// geriye 117 gerçek yerel işletme kaldı.
/// </summary>
public static class MekanKalitesi
{
    /// <summary>
    /// Adında geçtiğinde ZİNCİR sayılan markalar.
    ///
    /// ASIL SÜZGEÇ BU LİSTE DEĞİL, <c>brand:wikidata</c> ETİKETİ: OSM'de bir
    /// işletme markaya bağlıysa o etiket dolu oluyor ve zincir olduğunu marka
    /// adını bilmeden söylüyor. Liste, etiketi girilmemiş şubeler için ikinci
    /// hat.
    /// </summary>
    private static readonly string[] ZincirAdlari =
    {
        "starbucks", "burger king", "mcdonald", "kfc", "popeyes", "subway",
        "domino", "pizza hut", "little caesars", "gloria jean", "caribou",
        "tchibo", "espressolab", "kahve dünyası", "simit sarayı", "cafe crown",
        "arby", "usta dönerci", "mado", "sbarro", "papa john",
    };

    /// <summary>
    /// Adında geçtiğinde TUR DURAĞI OLAMAYACAK yerler.
    ///
    /// Canlıda çıkan liste bu satırın gerekçesi: "476. Yurt Kantini" iki
    /// günlük Ankara turunun ikinci gününün TEK durağı olmuştu. Kantin, yemek
    /// fabrikası ya da personel yemekhanesi OSM'de <c>amenity=cafe</c> diye
    /// işaretlenebiliyor; turist için hiçbiri bir varış noktası değil.
    /// </summary>
    private static readonly string[] KurumsalAdlar =
    {
        "kantin", "yemekhane", "kafeterya", "büfe", "bufe", "yurt ",
        "personel", "hastane", "fakülte", "kampüs", "kampus", "okul",
        "lojman", "kışla", "misafirhane",
    };

    /// <summary>
    /// "Yöresel/kayda değer" saymak için aranan mutfak etiketleri.
    ///
    /// OSM'de PUAN YOK; bir lokantanın turist için anlamlı olup olmadığını
    /// söyleyen en iyi sinyal <c>cuisine</c>.
    /// </summary>
    private static readonly string[] YoreselMutfaklar =
    {
        "turkish", "kebab", "ottoman", "anatolian", "regional", "local",
        "meyhane", "lokanta", "manti", "pide", "lahmacun", "balik", "fish",
        "seafood", "meze", "borek", "kofte", "izgara", "grill", "barbecue",
        "breakfast", "kahvalti", "corba", "soup", "tantuni", "kokorec",
        "baklava", "dessert", "patisserie", "coffee_shop", "turkish_coffee",
        "bistro",
    };

    /// <summary>
    /// Mutfağı YABANCI ZİNCİR kalıbına giren değerler.
    ///
    /// Mutfak etiketi olan ama yöresel listede yer almayan mekanlar da
    /// alınıyor (ölçümde "fish", "barbecue", "bistro" gerçek yerel
    /// işletmelere aitti); bu liste yalnızca zincir kalıbındakileri eliyor.
    /// </summary>
    private static readonly string[] ZincirMutfaklari =
    {
        "pizza", "burger", "sushi", "american", "mexican", "chinese",
        "italian", "fast_food", "asian", "sandwich", "international",
    };

    /// <summary>
    /// ETİ MERKEZE ALAN mutfaklar — vejetaryen/vegan kısıtında elenirler.
    ///
    /// Kısıt seçildiğinde bunlar ELENİYOR: kebapçının vejetaryen menüsü
    /// olabilir ama "vejetaryen tur" isteyen birine öğle molası olarak
    /// ocakbaşı önermek, kısıtı hiç uygulamamakla aynı şey.
    /// </summary>
    private static readonly string[] EtMutfaklari =
    {
        "kebab", "steak", "barbecue", "grill", "izgara", "kofte", "meat",
        "tantuni", "kokorec", "cig_kofte", "burger", "chicken", "doner",
        "lahmacun", "pide",
    };

    /// <summary>
    /// VEGAN kısıtında ayrıca elenenler — hayvansal ama "et" sayılmayanlar.
    /// </summary>
    private static readonly string[] VeganDisiMutfaklar =
    {
        "fish", "seafood", "balik", "sushi", "cheese", "ice_cream",
        "breakfast", "kahvalti",
    };

    /// <summary>
    /// Kısıta AÇIKÇA uyduğunu söyleyen mutfak/etiket değerleri.
    /// </summary>
    private static readonly string[] BitkiselMutfaklar =
    {
        "vegan", "vegetarian", "salad", "vejetaryen",
    };

    /// <summary>
    /// Mekan, verilen BESLENME KISITINA uyuyor mu?
    ///
    /// ---- NEDEN ETİKETE GÜVENEMİYORUZ? ----
    /// OSM'de doğru alan <c>diet:vegetarian</c>. Ankara'da ÖLÇTÜK: 25 km
    /// yarıçapta dönen mekanların HİÇBİRİNDE diyet etiketi yok. Bu yüzden
    /// "etiket yoksa geçir" kuralı, kısıtı sessizce hiç uygulamamak
    /// anlamına geliyordu — kullanıcının bildirdiği sorun ("vejetaryen
    /// seçeneklere dikkat etmiyor") tam olarak buydu.
    ///
    /// ---- YERİNE NE VAR? ----
    /// Üç kademe:
    ///   1. Diyet etiketi VARSA ona uyulur (en güvenilir bilgi).
    ///   2. Mutfak açıkça bitkiselse (vegan/vegetarian/salad) geçer.
    ///   3. Mutfak ETİ MERKEZE ALIYORSA elenir — kebapçı, ocakbaşı,
    ///      köfteci... Veganda balık/deniz ürünleri de eklenir.
    /// Geriye kalan (mutfağı nötr ya da yazılmamış) yerler geçiyor: hepsini
    /// elemek listeyi tamamen boşaltırdı ve mola hiç önerilemezdi.
    ///
    /// Bu bir garanti değil, bir SÜZGEÇ — ve kullanıcıya da böyle söyleniyor
    /// (öneri uyarılarında).
    /// </summary>
    public static bool BeslenmeyeUygunMu(GooglePlace mekan, VenueType tip, string? beslenme)
    {
        if (beslenme is not ("Vegan" or "Vejetaryen") || !YemeIcme(tip))
        {
            return true;
        }

        var etiketler = mekan.Ekstra ?? new Dictionary<string, string>();

        // 1. AÇIK DİYET ETİKETİ — en güvenilir bilgi, varsa o belirler.
        var alan = beslenme == "Vegan" ? "diet:vegan" : "diet:vegetarian";

        if (etiketler.TryGetValue(alan, out var diyet))
        {
            return diyet is "yes" or "only" or "limited";
        }

        // Sağlayıcı tipli alan dolduruyorsa (Google) o da açık bir cevap.
        if (mekan.VejetaryenSecenegiVar == false)
        {
            return false;
        }

        if (!etiketler.TryGetValue("cuisine", out var mutfak) || mutfak.Length == 0)
        {
            // Mutfağı yazılmamış: elemiyoruz (hepsini elemek listeyi boşaltır).
            return true;
        }

        var kucuk = mutfak.ToLowerInvariant();

        // 2. Açıkça bitkisel → geçer.
        if (BitkiselMutfaklar.Any(m => kucuk.Contains(m, StringComparison.Ordinal)))
        {
            return true;
        }

        // 3. Eti merkeze alan mutfak → elenir.
        if (EtMutfaklari.Any(m => kucuk.Contains(m, StringComparison.Ordinal)))
        {
            return false;
        }

        return beslenme != "Vegan"
            || !VeganDisiMutfaklar.Any(m => kucuk.Contains(m, StringComparison.Ordinal));
    }

    /// <summary>Yeme-içme mekanı mı? Eleme yalnızca bunlara uygulanıyor.</summary>
    public static bool YemeIcme(VenueType tip)
        => tip is VenueType.Restaurant or VenueType.Cafe;

    /// <summary>
    /// Bu yeme-içme mekanı listeye girmeye değer mi?
    ///
    /// Turistik yerlerde (müze, anıt, park) süzgeç YOK: adı olan her kayıt
    /// geçerli. Yeme-içmede üç eleme var, üçü de canlı denemenin sonucu.
    ///
    /// PUAN TAŞIYAN KAYNAKTA (Google) yalnızca ÜÇÜNCÜ eleme atlanıyor: orada
    /// 4.5+ süzgeci zaten "kayda değer mi?" sorusunu cevaplıyor. İlk ikisi
    /// her kaynakta geçerli — puanı 4.7 olan bir Starbucks şubesi de, adı
    /// "yurt kantini" olan bir yer de şehir turunun durağı değil.
    /// </summary>
    public static bool YemeIcmeUygunMu(
        GooglePlace mekan, VenueType tip, bool puanVerisiVar, bool etiketVerisiVar = true)
    {
        if (!YemeIcme(tip))
        {
            return true;
        }

        var etiketler = mekan.Ekstra ?? new Dictionary<string, string>();
        var ad = mekan.Ad.ToLowerInvariant();

        // ---- 1. Kurumsal/kapalı devre mi? ----
        if (KurumsalAdlar.Any(k => ad.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // ---- 2. Zincir şubesi mi? ----
        if (etiketler.ContainsKey("brand:wikidata") || etiketler.ContainsKey("brand"))
        {
            return false;
        }

        if (ZincirAdlari.Any(z => ad.Contains(z, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // ---- 3. Kayda değer mi? ----
        //
        // Bu eleme yalnızca PUANSIZ kaynakta (OSM) gerekli: Google'da 4.5+
        // süzgeci aynı işi zaten yapıyor ve orada "mutfağı yazılmamış" diye
        // elemek, kullanıcının gördüğü ölçütle çelişirdi.
        if (puanVerisiVar)
        {
            return true;
        }

        // ---- ETIKETSIZ KAYNAK: bu eleme UYGULANAMAZ ----
        //
        // Asagidaki kontrollerin hepsi OSM etiketlerine bakiyor. Yerel POI
        // tablosunda o etiketler SAKLANMIYOR (yalnizca ad, konum, kategori).
        // Kurali yine de uygulamak, "mutfak etiketi yok" diye HER kaydi
        // elemek demekti — canlida tam olarak bu oldu: Gastronomi temasi
        // yerel yedekte 400 kayitli mekana ragmen "uygun mekan bulunamadi"
        // veriyordu. Kanit yoklugu, yoklugun kaniti degil.
        //
        // Elemeyi kaybetmiyoruz, YERINI degistiriyoruz: bu kayitlar zaten
        // turistik POI aktariminda ayiklanmisti (kategoriye ancak oradan
        // gecerek girdiler). Ad tabanli kontroller (kurumsal / zincir)
        // yukarida calisti ve onlar icin ada bakmak yetiyor.
        if (!etiketVerisiVar)
        {
            return true;
        }

        if (etiketler.ContainsKey("wikidata") || etiketler.ContainsKey("heritage"))
        {
            return true;
        }

        if (etiketler.TryGetValue("cuisine", out var mutfak) && mutfak.Length > 0)
        {
            var kucuk = mutfak.ToLowerInvariant();

            if (YoreselMutfaklar.Any(m => kucuk.Contains(m, StringComparison.Ordinal)))
            {
                return true;
            }

            return !ZincirMutfaklari.Any(z => kucuk.Contains(z, StringComparison.Ordinal));
        }

        return false;
    }
}
