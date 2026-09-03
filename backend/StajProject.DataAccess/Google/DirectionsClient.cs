using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace StajProject.DataAccess.Google;

/// <summary>
/// Google Directions API istemcisi.
///
/// Kullanılan uç:
/// <c>GET /maps/api/directions/json?origin=…&amp;destination=…&amp;waypoints=optimize:true|…</c>
///
/// ---- TEK İSTEKTE SIRALAMA: NEDEN ÖNEMLİ? ----
/// "Mekanlar arası en kısa mesafeyi hesaplayıp sıralı rota oluştur" isteği,
/// klasik gezgin satıcı problemi. Naif çözüm Distance Matrix API'den N×N
/// mesafe çekip sırayı kendimiz aramaktı: 15 mekan için 225 hücre, üstelik
/// Distance Matrix hücre başına faturalanıyor ve sonra hâlâ NP-zor bir
/// problemi çözmek kalıyordu.
///
/// <c>optimize:true</c> ile Google hem sırayı hem rotayı TEK istekte
/// döndürüyor (<c>waypoint_order</c>). Bir tur önerisi böylece 1 Directions
/// isteğine iniyor; süre bütçesine sığmayıp kırpma gerekirse ikinci bir
/// istek atılıyor ve orada duruluyor (bkz. TurPlanlamaServisi).
///
/// ---- TOPLU TAŞIMA NEDEN "driving" İLE SIRALANIYOR? ----
/// Directions API'de <c>mode=transit</c> ARA NOKTA KABUL ETMİYOR: sıralama
/// isteği o kipte hata döner. Bacak bacak sorsaydık N ayrı istek gerekirdi —
/// tam da kaçındığımız şey. Bu yüzden sıra "driving" ile çözülüyor (şehir içi
/// mesafe sıralaması iki kipte de aynı çıkar), süre ise iş katmanında toplu
/// taşıma katsayısıyla düzeltiliyor. Karar açıkça yazılı çünkü tahmini süreyi
/// etkiliyor.
/// </summary>
public class DirectionsClient : IDirectionsClient
{
    private const string BasariKodu = "OK";

    private readonly HttpClient _http;
    private readonly GoogleMapsSettings _ayarlar;
    private readonly IIstekButcesi _butce;
    private readonly IMemoryCache _onbellek;
    private readonly ILogger<DirectionsClient> _logger;

    public DirectionsClient(
        HttpClient http,
        GoogleMapsSettings ayarlar,
        IIstekButcesi butce,
        IMemoryCache onbellek,
        ILogger<DirectionsClient> logger)
    {
        _http = http;
        _ayarlar = ayarlar;
        _butce = butce;
        _onbellek = onbellek;
        _logger = logger;

        _http.BaseAddress = new Uri(ayarlar.DirectionsBaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(ayarlar.TimeoutSeconds);
    }

    public bool Etkin => _ayarlar.KullanimaHazir;

    public async Task<DirectionsRotasi?> SiraliRotaAsync(
        IReadOnlyList<Coordinate> noktalar,
        SeyahatKipi kip,
        CancellationToken iptal = default)
    {
        if (!Etkin || noktalar.Count < 2)
        {
            return null;
        }

        // Ara nokta sınırı: Directions API tek istekte 25 ara nokta alıyor.
        // Aşan isteği GÖNDERMİYORUZ — MAX_WAYPOINTS_EXCEEDED cevabı da
        // faturalanan bir istek olurdu.
        var araNokta = noktalar.Count - 2;
        if (araNokta > _ayarlar.MaxAraNokta)
        {
            _logger.LogWarning(
                "Directions isteği {Sayi} ara nokta içeriyor, sınır {Sinir}. İstek atılmadı.",
                araNokta, _ayarlar.MaxAraNokta);
            return null;
        }

        var adres = AdresKur(noktalar, kip);
        var anahtar = $"directions:{kip}:{string.Join(';', noktalar.Select(Nokta))}";

        // Aynı nokta kümesi + aynı kip → aynı sıra. Kırpma turunda ilk
        // isteğin bir alt kümesi soruluyor, o farklı bir anahtar; ama
        // kullanıcı aynı öneriyi ikinci kez isterse Google'a gidilmiyor.
        if (_onbellek.TryGetValue<DirectionsRotasi>(anahtar, out var onbellekten)
            && onbellekten is not null)
        {
            _logger.LogDebug("Directions önbellekten karşılandı.");
            return onbellekten;
        }

        if (!_butce.IzinIste())
        {
            _logger.LogWarning("Directions isteği bütçe dolduğu için atılmadı.");
            return null;
        }

        try
        {
            using var cevap = await _http.GetAsync(adres, iptal);

            if (!cevap.IsSuccessStatusCode)
            {
                _logger.LogWarning("Directions {Kod} döndü.", (int)cevap.StatusCode);
                return null;
            }

            var govde = await cevap.Content.ReadFromJsonAsync<DirectionsCevabi>(
                cancellationToken: iptal);

            // Directions 200 dönüp gövdede hata bildirebiliyor: ZERO_RESULTS
            // (yol yok), OVER_QUERY_LIMIT (kota), REQUEST_DENIED (anahtar).
            // Durum koduna bakmak bunları YAKALAMAZ — OSRM'deki tuzağın aynısı.
            if (govde is null || !string.Equals(govde.Status, BasariKodu, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Directions rota bulamadı: {Durum} — {Mesaj}",
                    govde?.Status, govde?.ErrorMessage);
                return null;
            }

            var rota = govde.Routes?.FirstOrDefault();
            if (rota?.Legs is null || rota.Legs.Count == 0)
            {
                _logger.LogWarning("Directions cevabında bacak yok.");
                return null;
            }

            var bacaklar = rota.Legs
                .Select(l => new DirectionsBacagi(
                    l.Distance?.Value ?? 0,
                    l.Duration?.Value ?? 0))
                .ToList();

            var sonuc = new DirectionsRotasi(
                // waypoint_order YOKSA (ara nokta olmayan iki noktalı rota)
                // boş liste: çağıran taraf "sıra değişmedi" diye okuyor.
                Sira: rota.WaypointOrder ?? new List<int>(),
                Bacaklar: bacaklar,
                Cizgi: PolylineCozucu.CizgiyeCevir(rota.OverviewPolyline?.Points),
                ToplamMetre: bacaklar.Sum(b => b.MesafeMetre),
                ToplamSaniye: bacaklar.Sum(b => b.SureSaniye));

            _onbellek.Set(anahtar, sonuc, TimeSpan.FromMinutes(_ayarlar.OnbellekDakika));

            return sonuc;
        }
        catch (TaskCanceledException) when (!iptal.IsCancellationRequested)
        {
            _logger.LogWarning("Directions {Saniye} saniyede cevap vermedi.", _ayarlar.TimeoutSeconds);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Directions isteği başarısız.");
            return null;
        }
    }

    /// <summary>
    /// İstek adresini kurar.
    ///
    /// <c>optimize:true</c> ARA NOKTALARIN başına yazılıyor; başlangıç ve
    /// varış zaten ayrı parametreler ve Google onları yerinde bırakıyor.
    /// </summary>
    private string AdresKur(IReadOnlyList<Coordinate> noktalar, SeyahatKipi kip)
    {
        var sb = new StringBuilder("/maps/api/directions/json?");

        sb.Append("origin=").Append(Nokta(noktalar[0]));
        sb.Append("&destination=").Append(Nokta(noktalar[^1]));

        if (noktalar.Count > 2)
        {
            var ara = noktalar.Skip(1).Take(noktalar.Count - 2).Select(Nokta);
            sb.Append("&waypoints=").Append(Uri.EscapeDataString("optimize:true|" + string.Join('|', ara)));
        }

        sb.Append("&mode=").Append(KipAdi(kip));
        sb.Append("&language=").Append(_ayarlar.DilKodu);
        sb.Append("&region=").Append(_ayarlar.BolgeKodu.ToLowerInvariant());
        sb.Append("&key=").Append(Uri.EscapeDataString(_ayarlar.ApiKey));

        return sb.ToString();
    }

    /// <summary>
    /// "enlem,boylam" — Google'ın beklediği düzen. NetTopologySuite'in
    /// Coordinate'ında X boylam, Y enlemdir; ikisini karıştırmak rotayı
    /// sessizce dünyanın öbür ucuna taşırdı.
    /// </summary>
    private static string Nokta(Coordinate k) =>
        string.Create(CultureInfo.InvariantCulture, $"{k.Y:0.######},{k.X:0.######}");

    /// <summary>
    /// Seyahat kipinin Google karşılığı.
    ///
    /// Toplu taşıma "driving" olarak gidiyor — sınıf başlığındaki gerekçe:
    /// transit kipi ara nokta kabul etmiyor, sıralama o kiple yapılamıyor.
    /// </summary>
    private static string KipAdi(SeyahatKipi kip) => kip switch
    {
        SeyahatKipi.Yaya => "walking",
        SeyahatKipi.Arac => "driving",
        SeyahatKipi.ToplaTasima => "driving",
        _ => "driving",
    };

    // ---- Google cevabının karşılığı (yalnızca kullanılan alanlar) ----

    private class DirectionsCevabi
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("routes")]
        public List<DirectionsRota>? Routes { get; set; }
    }

    private class DirectionsRota
    {
        [JsonPropertyName("waypoint_order")]
        public List<int>? WaypointOrder { get; set; }

        [JsonPropertyName("legs")]
        public List<DirectionsLeg>? Legs { get; set; }

        [JsonPropertyName("overview_polyline")]
        public DirectionsPolyline? OverviewPolyline { get; set; }
    }

    private class DirectionsLeg
    {
        [JsonPropertyName("distance")]
        public DirectionsDeger? Distance { get; set; }

        [JsonPropertyName("duration")]
        public DirectionsDeger? Duration { get; set; }
    }

    private class DirectionsDeger
    {
        /// <summary>Metre ya da saniye — hangi alanın içindeyse.</summary>
        [JsonPropertyName("value")]
        public double Value { get; set; }
    }

    private class DirectionsPolyline
    {
        [JsonPropertyName("points")]
        public string? Points { get; set; }
    }
}
