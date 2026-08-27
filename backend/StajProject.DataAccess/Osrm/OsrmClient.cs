using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace StajProject.DataAccess.Osrm;

/// <summary>
/// OSRM HTTP istemcisi (Ödev 17 / Madde 1).
///
/// Kullanılan uç: <c>GET /route/v1/{profil}/{lon,lat};{lon,lat};…</c>
///
/// Sorgu parametreleri ve NEDEN:
///   overview=full      → çizginin TAM geometrisi. Varsayılan "simplified"
///                        uzun hatlarda köşeleri atıyor ve çizgi yoldan
///                        gözle görülür biçimde sapıyor.
///   geometries=geojson → varsayılan "polyline" kodlanmış bir metin; onu
///                        çözmek için ayrı bir kod çözücü yazmak gerekirdi.
///                        GeoJSON biraz daha uzun ama doğrudan okunuyor.
///   continue_straight=false
///                      → OSRM'in duraklarda U dönüşü yaptırmasını engelliyor.
///                        Varsayılan davranışta ara noktaya "düz devam et"
///                        kısıtı konuyor ve durak yolun karşı tarafındaysa
///                        rota bir sonraki kavşağa kadar gidip dönüyordu.
/// </summary>
public class OsrmClient : IOsrmClient
{
    /// <summary>OSRM'in "her şey yolunda" cevabı.</summary>
    private const string BasariKodu = "Ok";

    private readonly HttpClient _http;
    private readonly OsrmSettings _ayarlar;
    private readonly ILogger<OsrmClient> _logger;

    public OsrmClient(HttpClient http, OsrmSettings ayarlar, ILogger<OsrmClient> logger)
    {
        _http = http;
        _ayarlar = ayarlar;
        _logger = logger;

        _http.Timeout = TimeSpan.FromSeconds(ayarlar.TimeoutSeconds);
    }

    public bool Etkin => _ayarlar.Enabled;

    public async Task<OsrmRotaSonucu?> RotaHesaplaAsync(
        IReadOnlyList<Coordinate> noktalar,
        CancellationToken iptal = default)
    {
        if (!_ayarlar.Enabled)
        {
            return null;
        }

        // Tek noktadan rota çıkmaz; OSRM de 400 dönerdi. Boşuna ağ turu
        // atmak yerine burada duruyoruz.
        if (noktalar.Count < 2)
        {
            return null;
        }

        if (noktalar.Count > _ayarlar.MaxNokta)
        {
            _logger.LogWarning(
                "Rota isteği {Sayi} nokta içeriyor, sınır {Sinir}. Rota hesaplanmadı.",
                noktalar.Count, _ayarlar.MaxNokta);
            return null;
        }

        var adres = AdresKur(noktalar);

        try
        {
            using var cevap = await _http.GetAsync(adres, iptal);

            if (!cevap.IsSuccessStatusCode)
            {
                _logger.LogWarning("OSRM {Kod} döndü: {Adres}", (int)cevap.StatusCode, adres);
                return null;
            }

            var govde = await cevap.Content.ReadFromJsonSafeAsync(iptal);
            if (govde is null)
            {
                return null;
            }

            // OSRM 200 dönüp gövdede hata bildirebiliyor: "NoRoute" (noktalar
            // yol ağına bağlanamıyor — örn. deniz ortası), "NoSegment"
            // (yakında yol yok). Durum kodu 200 olduğu için yukarıdaki
            // kontrol bunları YAKALAMAZ.
            if (!string.Equals(govde.Code, BasariKodu, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("OSRM rota bulamadı: {Kod} — {Mesaj}", govde.Code, govde.Message);
                return null;
            }

            var rota = govde.Routes?.FirstOrDefault();
            var koordinatlar = rota?.Geometry?.Coordinates;

            if (rota is null || koordinatlar is null || koordinatlar.Count < 2)
            {
                _logger.LogWarning("OSRM cevabında kullanılabilir geometri yok: {Adres}", adres);
                return null;
            }

            var cizgi = new LineString(koordinatlar
                .Select(c => new Coordinate(c[0], c[1]))   // GeoJSON: [boylam, enlem]
                .ToArray())
            {
                SRID = OsrmGeoJson.Srid,
            };

            return new OsrmRotaSonucu(cizgi, rota.Distance, rota.Duration);
        }
        catch (TaskCanceledException) when (!iptal.IsCancellationRequested)
        {
            // Zaman aşımı. TaskCanceledException hem "süre doldu" hem
            // "çağıran iptal etti" için atılıyor; ayırt etmenin tek yolu
            // token'a bakmak.
            _logger.LogWarning("OSRM {Saniye} saniyede cevap vermedi.", _ayarlar.TimeoutSeconds);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // Ulaşılamadı ya da beklenmeyen gövde. Rota, kullanıcının asıl
            // yaptığı işin yan ürünü — bu hatayı yukarı fırlatmak, OSRM
            // kapalıyken durak eklemeyi imkânsız kılardı.
            _logger.LogWarning(ex, "OSRM'e ulaşılamadı: {Adres}", adres);
            return null;
        }
    }

    public async Task<bool> AyaktaMiAsync(CancellationToken iptal = default)
    {
        if (!_ayarlar.Enabled)
        {
            return false;
        }

        // Ayrı bir "sağlık" ucu yok; OSRM'de en ucuz kontrol Ankara'daki bir
        // noktaya en yakın yolu sormak (/nearest). Rota hesaplatmaktan çok
        // daha hafif ve yol ağının GERÇEKTEN yüklendiğini de doğruluyor —
        // yalnızca TCP bağlantısına baksaydık, veri yüklenmemiş bir OSRM
        // "ayakta" görünürdü.
        var adres = $"{Kok}/nearest/v1/{_ayarlar.Profil}/32.8597,39.9334";

        try
        {
            using var cevap = await _http.GetAsync(adres, iptal);
            if (!cevap.IsSuccessStatusCode) return false;

            var govde = await cevap.Content.ReadFromJsonSafeAsync(iptal);
            return string.Equals(govde?.Code, BasariKodu, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private string Kok => _ayarlar.BaseUrl.TrimEnd('/');

    /// <summary>
    /// İstek adresini kurar.
    ///
    /// Koordinatlar <c>InvariantCulture</c> ile yazılıyor — bu satır bir
    /// hata sınıfını kapatıyor: Türkçe kültürde ondalık ayırıcı VİRGÜL ve
    /// "32,8597" yazılırsa OSRM bunu iki ayrı sayı sanar. Yani sunucu Türkçe
    /// bir makinede çalıştığında rota sessizce bozulurdu.
    /// </summary>
    private string AdresKur(IReadOnlyList<Coordinate> noktalar)
    {
        var koordinatlar = string.Join(';', noktalar.Select(n =>
            string.Create(CultureInfo.InvariantCulture, $"{n.X},{n.Y}")));

        return $"{Kok}/route/v1/{_ayarlar.Profil}/{koordinatlar}"
             + "?overview=full&geometries=geojson&continue_straight=false";
    }
}

// ============================================================================
//  OSRM cevabının JSON karşılıkları
//
//  Yalnızca KULLANDIĞIMIZ alanlar tanımlı. Cevabın tamamını modellemek
//  (legs, steps, waypoints, annotations…) yüzlerce satır ölü kod olurdu ve
//  System.Text.Json bilinmeyen alanları zaten sessizce atlıyor.
// ============================================================================

internal static class OsrmGeoJson
{
    /// <summary>
    /// OSRM her zaman WGS84 (EPSG:4326) döner. Sabit burada duruyor çünkü
    /// DataAccess katmanı Business'taki <c>WktConverter.Srid</c>'yi göremiyor
    /// (GeoServerSettings.Srid ile aynı gerekçe).
    /// </summary>
    public const int Srid = 4326;
}

internal class OsrmCevap
{
    /// <summary>"Ok", "NoRoute", "NoSegment", "InvalidQuery"…</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("routes")]
    public List<OsrmRota>? Routes { get; set; }
}

internal class OsrmRota
{
    [JsonPropertyName("distance")]
    public double Distance { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("geometry")]
    public OsrmGeometri? Geometry { get; set; }
}

internal class OsrmGeometri
{
    /// <summary>GeoJSON LineString koordinatları: [[boylam, enlem], …]</summary>
    [JsonPropertyName("coordinates")]
    public List<List<double>>? Coordinates { get; set; }
}

internal static class OsrmHttpUzantilari
{
    /// <summary>
    /// Gövdeyi <see cref="OsrmCevap"/> olarak okur; çözülemezse null döner.
    ///
    /// Ayrı bir uzantı olmasının sebebi: rota ve sağlık kontrolü aynı gövde
    /// biçimini okuyor, iki yerde aynı JSON ayarlarını tekrarlamayalım.
    /// </summary>
    public static async Task<OsrmCevap?> ReadFromJsonSafeAsync(
        this HttpContent icerik,
        CancellationToken iptal)
    {
        await using var akis = await icerik.ReadAsStreamAsync(iptal);
        return await JsonSerializer.DeserializeAsync<OsrmCevap>(
            akis,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            iptal);
    }
}
