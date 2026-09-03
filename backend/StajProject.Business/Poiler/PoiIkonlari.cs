using System.Globalization;
using System.Text;

namespace StajProject.Business.Poiler;

// ============================================================================
//  POI KATEGORİ İKONLARI — tek kaynak
//
//  Ödev 13'te her kategori kendi rengini ve ŞEKLİNİ (circle/square/cross…)
//  alıyordu. Şekiller kategoriyi ayırt etmeye yetiyordu ama hiçbir şey
//  ANLATMIYORDU: bir kare gördüğünde "burası otel mi eczane mi?" sorusunun
//  cevabı yalnızca lejantta vardı. Bu dosya, kategoriye özgü gerçek simgeler
//  getiriyor — çatal-bıçak, fincan, yatak, haç, kitap…
//
//  NEDEN TEK DOSYADA VE NEDEN BURADA?
//  Aynı çizim ÜÇ yerde birden görünüyor:
//
//    1. GeoServer'daki harita       → SLD'nin <ExternalGraphic>'i (SVG dosyası)
//    2. Arayüzdeki vektör katmanı   → OpenLayers ol/style/Icon (data URI)
//    3. Panel/lejant ve yönetim     → React içinde <svg>
//
//  Üçünü ayrı ayrı tanımlasaydık bir ikon değiştiğinde üç yeri güncellemek
//  gerekirdi; biri unutulduğunda haritayla lejant çelişirdi. Burada tanımlanıp
//  API üzerinden (GET /api/poi/ikonlar ve /api/poi/stiller) dışarı veriliyor,
//  yani frontend'de KOPYASI YOK.
//
//  NEDEN HAZIR BİR İKON KÜTÜPHANESİ (Font Awesome, Material) DEĞİL?
//  İkonun GeoServer tarafında da çizilmesi gerekiyor; bu ya sunucuya font
//  kurmayı (ttf:// işaretleri) ya da lisanslı SVG'leri veri dizinine
//  kopyalamayı gerektirirdi. İkisi de "projeyi klonla, çalıştır" akışını
//  bozardı. Buradaki simgeler basit geometrilerden elle çizildi: kimseye
//  bağımlı değiliz ve aynı yol verisi üç yerde de aynen kullanılabiliyor.
//
//  Tüm çizimler 24×24'lük bir kutuda (viewBox="0 0 24 24"). Yollar dolgulu
//  siluet: 14–18 piksellik bir haritada ince çizgiler kayboluyor, dolu
//  şekiller okunuyor.
// ============================================================================

/// <summary>
/// İkonun tek bir çizim parçası.
/// </summary>
/// <param name="D">SVG <c>path</c> elemanının <c>d</c> özniteliği.</param>
/// <param name="Beyaz">
/// true ise bu parça KATEGORİ RENGİYLE değil beyazla boyanır — simgenin
/// içindeki oyuklar (hastanenin haçı, evin kapısı, kitabın satırları) için.
/// Ayrı bir renk alanı yerine tek bir bayrak: ikonlarda kategori rengi ve
/// beyaz dışında bir tona ihtiyaç duyulmadı, alan eklemek kullanılmayan bir
/// esneklik olurdu.
/// </param>
public sealed record IkonParcasi(string D, bool Beyaz = false);

/// <summary>Bir kategori ikonu: anahtarı, görünen adı ve çizim parçaları.</summary>
/// <param name="Anahtar">Veritabanında saklanan değer — "fincan", "yatak"…</param>
/// <param name="Ad">Yönetim panelindeki seçicide görünen ad.</param>
/// <param name="Parcalar">Sırayla çizilecek yollar (önce gövde, sonra beyaz oyuklar).</param>
public sealed record PoiIkonu(string Anahtar, string Ad, IReadOnlyList<IkonParcasi> Parcalar);

public static class PoiIkonlari
{
    /// <summary>Bütün çizimlerin ortak kutusu.</summary>
    public const string ViewBox = "0 0 24 24";

    /// <summary>
    /// Kategorinin ikonu yoksa (ve atasının da yoksa) kullanılan simge.
    /// Harita iğnesi: "burada bir şey var ama türü tanımlı değil" demenin
    /// en nötr yolu.
    /// </summary>
    public const string Varsayilan = "pin";

    /// <summary>
    /// Tanınan bütün ikonlar. Yeni bir ikon eklemek için buraya bir satır
    /// yazmak yeterli: yönetim panelindeki seçici, SLD üretimi ve arayüz
    /// aynı listeden besleniyor.
    /// </summary>
    public static readonly IReadOnlyList<PoiIkonu> Tumu = new List<PoiIkonu>
    {
        new("pin", "Harita iğnesi", new[]
        {
            new IkonParcasi("M12 2.5c-3.6 0-6.5 2.9-6.5 6.5 0 4.6 6.5 12.5 6.5 12.5s6.5-7.9 6.5-12.5c0-3.6-2.9-6.5-6.5-6.5z"),
            new IkonParcasi("M12 6.2a2.8 2.8 0 1 0 0 5.6 2.8 2.8 0 0 0 0-5.6z", Beyaz: true),
        }),

        new("catal-bicak", "Çatal-bıçak", new[]
        {
            // Çatal: üç diş + sap
            new IkonParcasi("M7 3h1.5v5.2h1V3H11v5.2h1V3h1.5v6.2c0 1.3-.9 2.4-2.1 2.7V21H9.1v-9.1C7.9 11.6 7 10.5 7 9.2V3z"),
            // Kaşık: oval kepçe + sap
            new IkonParcasi("M17 3.3c1.2 0 2.1 1.5 2.1 3.3s-.9 3.3-2.1 3.3-2.1-1.5-2.1-3.3S15.8 3.3 17 3.3z"),
            new IkonParcasi("M16.2 9.4h1.6V21h-1.6z"),
        }),

        new("fincan", "Fincan", new[]
        {
            new IkonParcasi("M4.5 8h12v6.2c0 2.6-2.1 4.8-4.8 4.8H9.3c-2.6 0-4.8-2.1-4.8-4.8V8z"),
            // Kulp
            new IkonParcasi("M17.5 9.2h1.2c1.5 0 2.8 1.2 2.8 2.8s-1.3 2.8-2.8 2.8h-1.2v-1.8h1.2c.5 0 1-.5 1-1s-.5-1-1-1h-1.2V9.2z"),
            // Tabak
            new IkonParcasi("M3 20.2h15V22H3z"),
        }),

        new("ekmek", "Ekmek", new[]
        {
            new IkonParcasi("M4.4 11.6c0-3.2 3.4-5.4 7.6-5.4s7.6 2.2 7.6 5.4v4.6c0 .9-.7 1.6-1.6 1.6H6c-.9 0-1.6-.7-1.6-1.6v-4.6z"),
            new IkonParcasi("M8.6 9.2l-1.4 7.4h1.4l1.4-7.4z", Beyaz: true),
            new IkonParcasi("M13 9.2l-1.4 7.4H13l1.4-7.4z", Beyaz: true),
        }),

        new("yatak", "Yatak", new[]
        {
            new IkonParcasi("M3 7.5h2.2v7h6.6V9.7h5.2c2.2 0 3.8 1.6 3.8 3.8v5.5h-2.2v-2.3H5.2v2.3H3V7.5z"),
            new IkonParcasi("M6.4 10.9h4.1v2.9H6.4z", Beyaz: true),
        }),

        new("ev", "Ev", new[]
        {
            new IkonParcasi("M12 3l9 7.2v1.6h-2V21H5v-9.2H3v-1.6L12 3z"),
            new IkonParcasi("M10.4 14.5h3.2V21h-3.2z", Beyaz: true),
        }),

        new("kalp", "Kalp", new[]
        {
            new IkonParcasi("M12 20.6l-1.3-1.2C6.1 15.3 3 12.5 3 9.1 3 6.3 5.2 4.1 8 4.1c1.6 0 3.1.7 4 1.9.9-1.2 2.4-1.9 4-1.9 2.8 0 5 2.2 5 5 0 3.4-3.1 6.2-7.7 10.3l-1.3 1.2z"),
        }),

        new("hastane", "Hastane", new[]
        {
            new IkonParcasi("M4 8.4l8-4.6 8 4.6V21H4V8.4z"),
            new IkonParcasi("M11 9.4h2v2.4h2.4v2H13v2.4h-2v-2.4H8.6v-2H11V9.4z", Beyaz: true),
        }),

        new("eczane", "Eczane haçı", new[]
        {
            new IkonParcasi("M12 3.2a8.8 8.8 0 1 0 0 17.6 8.8 8.8 0 0 0 0-17.6z"),
            new IkonParcasi("M10.7 7.4h2.6v3.3h3.3v2.6h-3.3v3.3h-2.6v-3.3H7.4v-2.6h3.3V7.4z", Beyaz: true),
        }),

        new("mezuniyet", "Mezuniyet külahı", new[]
        {
            new IkonParcasi("M12 4L1.8 9.2 12 14.4l8.3-4.2v5.1h1.9V9.2L12 4z"),
            new IkonParcasi("M6 12.2v3.6c0 1.9 2.7 3.4 6 3.4s6-1.5 6-3.4v-3.6l-6 3.1-6-3.1z"),
        }),

        new("kitap", "Kitap", new[]
        {
            new IkonParcasi("M4.6 4.2c0-.9.7-1.6 1.6-1.6H18c.9 0 1.6.7 1.6 1.6v15.6c0 .9-.7 1.6-1.6 1.6H6.2c-.9 0-1.6-.7-1.6-1.6V4.2z"),
            new IkonParcasi("M7.6 6.4h8.8v1.7H7.6z", Beyaz: true),
            new IkonParcasi("M7.6 10h8.8v1.7H7.6z", Beyaz: true),
            new IkonParcasi("M7.6 13.6h5.6v1.7H7.6z", Beyaz: true),
        }),

        new("market", "Alışveriş sepeti", new[]
        {
            new IkonParcasi("M4 9h16l-1.6 10.2c-.1.9-.9 1.6-1.8 1.6H7.4c-.9 0-1.7-.7-1.8-1.6L4 9z"),
            new IkonParcasi("M8.4 8.2a3.6 3.6 0 0 1 7.2 0h-1.9a1.7 1.7 0 0 0-3.4 0H8.4z"),
        }),

        new("otobus", "Otobüs", new[]
        {
            new IkonParcasi("M4.6 5.2c0-1 .8-1.8 1.8-1.8h11.2c1 0 1.8.8 1.8 1.8v10.6c0 .7-.4 1.3-1 1.6v1.6h-2.2v-1.4H7.8v1.4H5.6v-1.6c-.6-.3-1-.9-1-1.6V5.2z"),
            new IkonParcasi("M6.6 6h10.8v4.8H6.6z", Beyaz: true),
            new IkonParcasi("M8 13.2a1.4 1.4 0 1 0 0 2.8 1.4 1.4 0 0 0 0-2.8z", Beyaz: true),
            new IkonParcasi("M16 13.2a1.4 1.4 0 1 0 0 2.8 1.4 1.4 0 0 0 0-2.8z", Beyaz: true),
        }),

        new("agac", "Ağaç", new[]
        {
            new IkonParcasi("M12 2.8l4.6 7.2h-2.4l4 6.2h-4.3v4.9h-3.8v-4.9H5.8l4-6.2H7.4L12 2.8z"),
        }),

        // ---- Turistik kategoriler (OSM'den içe aktarılan duraklar) ----
        //
        // Üçü de aynı ilkeyle çizildi: basit geometri, dolgulu siluet, 24×24
        // kutu. Hazır bir ikon kütüphanesi kullanmama gerekçesi dosya
        // başlığında — bu simgeler GeoServer'ın SLD'sinde de aynen çiziliyor.

        new("muze", "Müze", new[]
        {
            // Alınlık (üçgen çatı) + sütunlar + kaide: klasik müze cephesi.
            new IkonParcasi("M12 2.6 21.4 8v1.9H2.6V8L12 2.6z"),
            new IkonParcasi("M5.4 11.2h2.4v7.2H5.4z"),
            new IkonParcasi("M10.8 11.2h2.4v7.2h-2.4z"),
            new IkonParcasi("M16.2 11.2h2.4v7.2h-2.4z"),
            new IkonParcasi("M3.2 19.6h17.6v1.9H3.2z"),
        }),

        new("anit", "Anıt", new[]
        {
            // Dikilitaş + iki kademeli kaide.
            new IkonParcasi("M12 2.4l2.2 4.4v9.4H9.8V6.8L12 2.4z"),
            new IkonParcasi("M7.4 17h9.2v1.7H7.4z"),
            new IkonParcasi("M5.6 19.9h12.8v1.7H5.6z"),
        }),

        new("manzara", "Seyir noktası", new[]
        {
            // İki dağ silueti + güneş: "buradan manzara görünür" mesajı.
            new IkonParcasi("M2.6 19.4 9 8.6l3.6 6 2-3.2 6.8 8H2.6z"),
            new IkonParcasi("M17.2 3.8a2.3 2.3 0 1 0 0 4.6 2.3 2.3 0 0 0 0-4.6z"),
        }),
    };

    private static readonly Dictionary<string, PoiIkonu> AnahtarIle =
        Tumu.ToDictionary(i => i.Anahtar, StringComparer.OrdinalIgnoreCase);

    /// <summary>Anahtar tanınıyor mu? Boş/null "ikon seçilmemiş" demek, o da geçerli.</summary>
    public static bool GecerliMi(string? anahtar)
        => string.IsNullOrWhiteSpace(anahtar) || AnahtarIle.ContainsKey(anahtar);

    /// <summary>Anahtara karşılık gelen ikon; tanınmıyorsa null.</summary>
    public static PoiIkonu? Bul(string? anahtar)
        => string.IsNullOrWhiteSpace(anahtar) ? null
            : AnahtarIle.TryGetValue(anahtar, out var ikon) ? ikon : null;

    /// <summary>Anahtar tanınmıyorsa varsayılan iğneye düşer — asla null dönmez.</summary>
    public static PoiIkonu BulYaDaVarsayilan(string? anahtar)
        => Bul(anahtar) ?? AnahtarIle[Varsayilan];

    /// <summary>
    /// Kategorinin EKRANDA ÇİZİLECEK simgesi: kendi ikonu → atasının ikonu →
    /// … → varsayılan iğne.
    ///
    /// Miras neden var? Yönetici "Yeme-İçme"ye çatal-bıçak verip altına
    /// "Kebapçı" açtığında o alt kategori haritada anlamsız bir iğneyle
    /// çıkmasın diye. Ana kategoriye simge vermek, bütün dalı bir kerede
    /// giydiriyor; istisna gereken çocuk kendi simgesini seçiyor.
    ///
    /// Yukarı yürüyüş 64 adımda kesiliyor: kategori döngüsü servis
    /// katmanında engelleniyor ama veriye elle sızmış bir döngü burayı
    /// sonsuza kadar döndürmemeli (aynı güvenlik PoiService.YollariHesapla
    /// ve KonumAnaliziService'te de var).
    /// </summary>
    public static string EtkinAnahtar<T>(
        T kategori,
        IReadOnlyDictionary<int, T> idIle,
        Func<T, string?> ikonAl,
        Func<T, int?> ataAl)
    {
        var gecerli = kategori;
        var guvenlik = 0;

        while (true)
        {
            var anahtar = ikonAl(gecerli);
            if (Bul(anahtar) is not null) return anahtar!;

            var ataId = ataAl(gecerli);
            if (ataId is null || !idIle.TryGetValue(ataId.Value, out var ata)) return Varsayilan;

            gecerli = ata;
            if (++guvenlik > 64) return Varsayilan;
        }
    }

    /// <summary>
    /// İkonu, verilen renkle boyanmış tek başına duran bir SVG belgesine çevirir.
    ///
    /// GeoServer'ın <c>ExternalGraphic</c>'i bir DOSYA istiyor; bu metot o
    /// dosyanın içeriğini üretiyor. Renk dosyaya GÖMÜLÜ: GeoServer'ın SVG
    /// parametre yerine koyma özelliği (<c>ikon.svg?fill=…</c>) sürüme göre
    /// davranış değiştirebiliyor ve hata verdiğinde simge sessizce boş
    /// çiziliyor. Kategori başına bir dosya üretmek birkaç kilobayta mal
    /// oluyor ama sonucu belirsizlikten çıkarıyor.
    /// </summary>
    /// <param name="ikon">Çizilecek simge.</param>
    /// <param name="renk">Kategori rengi — "#rrggbb".</param>
    public static string SvgUret(PoiIkonu ikon, string renk)
    {
        var govde = new StringBuilder();

        foreach (var parca in ikon.Parcalar)
        {
            var dolgu = parca.Beyaz ? "#ffffff" : renk;
            govde.Append(CultureInfo.InvariantCulture,
                $"\n  <path fill=\"{dolgu}\" d=\"{parca.D}\"/>");
        }

        // Dış hat (stroke) DOLGUNUN ALTINA çiziliyor: aynı yolu önce kalın
        // beyaz konturla, sonra dolgusuyla basıyoruz. Böylece simge koyu bir
        // uydu görüntüsünün ya da yoğun bir OSM zeminin üstünde de kenar
        // kazanıyor ve kaybolmuyor.
        var hat = new StringBuilder();
        foreach (var parca in ikon.Parcalar.Where(p => !p.Beyaz))
        {
            hat.Append(CultureInfo.InvariantCulture,
                $"\n  <path d=\"{parca.D}\" fill=\"none\" stroke=\"#ffffff\" stroke-width=\"2.4\" stroke-linejoin=\"round\"/>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!-- ÜRETİLMİŞ DOSYA — kaynak: Business/Poiler/PoiIkonlari.cs -->
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="{ViewBox}" width="24" height="24">{hat}{govde}
            </svg>
            """;
    }
}
