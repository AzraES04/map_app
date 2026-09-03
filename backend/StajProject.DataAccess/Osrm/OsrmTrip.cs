using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace StajProject.DataAccess.Osrm;

/// <summary>
/// OSRM'in <c>/trip</c> servisinden dönen SIRALANMIŞ gezi.
/// </summary>
/// <param name="Sira">
/// ARA noktaların ziyaret sırası — Google Directions'ın <c>waypoint_order</c>
/// alanıyla AYNI sözleşme: değerler ara nokta listesindeki indislerdir.
/// Böylece iş katmanı iki kaynağı ayırt etmek zorunda kalmıyor.
/// </param>
/// <param name="Bacaklar">(mesafe metre, süre saniye) çiftleri.</param>
/// <param name="Cizgi">Rotanın tamamı (EPSG:4326).</param>
public record OsrmTripSonucu(
    IReadOnlyList<int> Sira,
    IReadOnlyList<(double MesafeMetre, double SureSaniye)> Bacaklar,
    LineString? Cizgi,
    double ToplamMetre,
    double ToplamSaniye);

/// <summary>
/// OSRM <c>/trip</c> — "bu noktaları en kısa sürede hangi sırayla gezerim?"
///
/// ---- NEDEN AYRI BİR DOSYA VE AYRI BİR UÇ? ----
/// <c>/route</c> verilen sırayı KORUYOR (Ödev 17'de istenen buydu: durak
/// sırasını kullanıcı belirliyor). Tur önerisinde ise sıranın kendisi
/// hesaplanacak — bu, gezgin satıcı problemi ve OSRM'in <c>/trip</c> servisi
/// tam olarak onu çözüyor.
///
/// Google Directions'ın <c>optimize:true</c> parametresinin ücretsiz ve
/// yerelde çalışan karşılığı bu: anahtarsız kipte sıralamayı OSRM yapıyor.
///
/// <c>source=first&amp;destination=last&amp;roundtrip=false</c>:
/// başlangıç ve varış SABİT, yalnızca aradakiler yeniden diziliyor —
/// Directions'taki davranışın aynısı. Varsayılan (roundtrip=true) tur
/// başladığı yere dönerdi ve son durak kullanıcının seçtiği yer olmazdı.
/// </summary>
public static class OsrmTrip
{
    /// <summary>
    /// Sıralı geziyi hesaplar.
    ///
    /// İSTİSNA FIRLATMAZ: OSRM kapalı, ulaşılamıyor ya da rota yoksa null
    /// döner (IOsrmClient'taki kuralın aynısı) — çağıran taraf o zaman
    /// kendi yedeğine düşüyor.
    /// </summary>
    public static async Task<OsrmTripSonucu?> HesaplaAsync(
        HttpClient http,
        OsrmSettings ayarlar,
        ILogger logger,
        IReadOnlyList<Coordinate> noktalar,
        CancellationToken iptal = default)
    {
        if (!ayarlar.Enabled || noktalar.Count < 2 || noktalar.Count > ayarlar.MaxNokta)
        {
            return null;
        }

        var koordinatlar = string.Join(';', noktalar.Select(n =>
            string.Create(CultureInfo.InvariantCulture, $"{n.X},{n.Y}")));

        var adres = $"{ayarlar.BaseUrl.TrimEnd('/')}/trip/v1/{ayarlar.Profil}/{koordinatlar}"
                  + "?source=first&destination=last&roundtrip=false"
                  + "&overview=full&geometries=geojson&annotations=false";

        try
        {
            using var cevap = await http.GetAsync(adres, iptal);

            if (!cevap.IsSuccessStatusCode)
            {
                logger.LogWarning("OSRM trip {Kod} döndü.", (int)cevap.StatusCode);
                return null;
            }

            await using var akis = await cevap.Content.ReadAsStreamAsync(iptal);
            var govde = await JsonSerializer.DeserializeAsync<TripCevabi>(
                akis,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                iptal);

            // OSRM 200 dönüp gövdede hata bildirebiliyor ("NoTrips",
            // "NoSegment"). Durum koduna bakmak bunları yakalamaz.
            if (govde is null
                || !string.Equals(govde.Code, "Ok", StringComparison.OrdinalIgnoreCase)
                || govde.Trips is null || govde.Trips.Count == 0)
            {
                logger.LogWarning("OSRM trip sonuç vermedi: {Kod}", govde?.Code);
                return null;
            }

            var gezi = govde.Trips[0];

            var bacaklar = (gezi.Legs ?? new List<TripBacak>())
                .Select(l => (l.Distance, l.Duration))
                .ToList();

            var koordinatListesi = gezi.Geometry?.Coordinates;
            LineString? cizgi = null;

            if (koordinatListesi is { Count: >= 2 })
            {
                cizgi = new LineString(koordinatListesi
                    .Select(c => new Coordinate(c[0], c[1]))   // GeoJSON: [boylam, enlem]
                    .ToArray())
                {
                    SRID = OsrmGeoJson.Srid,
                };
            }

            return new OsrmTripSonucu(
                SirayiCevir(govde.Waypoints, noktalar.Count),
                bacaklar,
                cizgi,
                gezi.Distance,
                gezi.Duration);
        }
        catch (TaskCanceledException) when (!iptal.IsCancellationRequested)
        {
            logger.LogWarning("OSRM trip {Saniye} saniyede cevap vermedi.", ayarlar.TimeoutSeconds);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            logger.LogWarning(ex, "OSRM trip isteği başarısız.");
            return null;
        }
    }

    /// <summary>
    /// OSRM'in sıra gösterimini Google'ınkine çevirir.
    ///
    /// ---- İKİ FARKLI GÖSTERİM ----
    /// OSRM: <c>waypoints[i].waypoint_index = j</c> → "i numaralı GİRDİ,
    ///       gezide j sırada ziyaret ediliyor".
    /// Google: <c>waypoint_order = [2,0,1]</c> → "ara noktaları şu SIRAYLA gez".
    ///
    /// İkisi birbirinin TERSİ kodlaması. Çevirmeden kullansaydık duraklar
    /// birbirinin koordinatına oturur ve bu hiçbir hata vermeden, sadece
    /// yanlış bir tur olarak görünürdü — çevirinin testi bu yüzden var.
    ///
    /// Bozuk/eksik cevapta BOŞ liste dönüyor; çağıran taraf onu "sıra
    /// değişmedi" diye okuyor ve girdi sırasını koruyor.
    /// </summary>
    private static IReadOnlyList<int> SirayiCevir(List<TripWaypoint>? waypoints, int noktaSayisi)
    {
        var araSayisi = noktaSayisi - 2;
        if (waypoints is null || waypoints.Count != noktaSayisi || araSayisi <= 0)
        {
            return Array.Empty<int>();
        }

        // Yalnızca ARA noktalar (ilk ve son sabit), gezideki sıralarına göre.
        var ara = waypoints
            .Select((w, girdiIndisi) => (girdiIndisi, w.WaypointIndex))
            .Where(x => x.girdiIndisi > 0 && x.girdiIndisi < noktaSayisi - 1)
            .OrderBy(x => x.WaypointIndex)
            .Select(x => x.girdiIndisi - 1)   // ara nokta listesindeki indis
            .ToList();

        return ara.Count == araSayisi ? ara : Array.Empty<int>();
    }

    // ---- Cevabın JSON karşılığı (yalnızca kullanılan alanlar) ----

    private class TripCevabi
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("trips")]
        public List<TripGezisi>? Trips { get; set; }

        [JsonPropertyName("waypoints")]
        public List<TripWaypoint>? Waypoints { get; set; }
    }

    private class TripGezisi
    {
        [JsonPropertyName("distance")]
        public double Distance { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("legs")]
        public List<TripBacak>? Legs { get; set; }

        [JsonPropertyName("geometry")]
        public OsrmGeometri? Geometry { get; set; }
    }

    private class TripBacak
    {
        [JsonPropertyName("distance")]
        public double Distance { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }
    }

    private class TripWaypoint
    {
        /// <summary>Bu girdinin gezideki sırası (0 tabanlı).</summary>
        [JsonPropertyName("waypoint_index")]
        public int WaypointIndex { get; set; }
    }
}
