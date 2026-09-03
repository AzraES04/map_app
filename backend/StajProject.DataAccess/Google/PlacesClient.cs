using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace StajProject.DataAccess.Google;

/// <summary>
/// Google Places API (New) — Text Search istemcisi.
///
/// Kullanılan uç: <c>POST /v1/places:searchText</c>
///
/// ---- NEDEN TEXT SEARCH, NEARBY SEARCH DEĞİL? ----
/// İkisi de "şu çevrede şu tipte mekan" sorusunu cevaplıyor ama YALNIZCA Text
/// Search <c>minRating</c> parametresini destekliyor. Ödevin "4.5+ puanlı"
/// koşulunu sunucuya taşımak, aynı istekle daha çok UYGUN sonuç almak demek:
/// yerelde süzseydik 20 sonucun çoğunu atıp ikinci bir istek atmak gerekirdi.
/// Ayrıca ilçe adı ve "vegan" gibi serbest ipuçları da aynı sorguya giriyor,
/// ayrı bir arama gerekmiyor.
///
/// ---- MALİYETİ NE AZALTIYOR? ----
/// 1. FIELD MASK: Places API (New) faturayı istenen alanlara göre kesiyor.
///    Aşağıdaki maske yalnızca sıralama ve durak üretimi için GEREKENİ
///    istiyor; fotoğraf, yorum, açılış saati gibi pahalı alanlar yok.
/// 2. ÖNBELLEK: aynı arama (aynı merkez, tip, yarıçap, puan) yapılandırılan
///    süre boyunca ikinci kez Google'a gitmiyor. Merkez koordinatı ~1 km'lik
///    ızgaraya yuvarlanıyor ki "aynı şehir, bir tık farklı merkez" de aynı
///    önbellek satırına düşsün.
/// 3. BÜTÇE: <see cref="IIstekButcesi"/> günlük tavanı aşan isteği hiç
///    göndermiyor.
/// 4. Mekan başına AYRI "Place Details" çağrısı YOK — ihtiyacımız olan her
///    alan zaten arama cevabında geliyor. Naif gerekleme burada mekan sayısı
///    kadar ek istek atardı.
/// </summary>
public class PlacesClient : IPlacesClient
{
    /// <summary>
    /// İstenen alanlar. Bu maske FATURAYI belirliyor — alan eklemeden önce
    /// "sıralama ya da durak üretimi bunu gerçekten kullanıyor mu?" sorusunun
    /// cevabı evet olmalı.
    /// </summary>
    private const string AlanMaskesi =
        "places.id,places.displayName,places.location,places.rating," +
        "places.userRatingCount,places.types,places.primaryType," +
        "places.servesVegetarianFood";

    private readonly HttpClient _http;
    private readonly GoogleMapsSettings _ayarlar;
    private readonly IIstekButcesi _butce;
    private readonly IMemoryCache _onbellek;
    private readonly ILogger<PlacesClient> _logger;

    public PlacesClient(
        HttpClient http,
        GoogleMapsSettings ayarlar,
        IIstekButcesi butce,
        IMemoryCache onbellek,
        ILogger<PlacesClient> logger)
    {
        _http = http;
        _ayarlar = ayarlar;
        _butce = butce;
        _onbellek = onbellek;
        _logger = logger;

        _http.BaseAddress = new Uri(ayarlar.PlacesBaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(ayarlar.TimeoutSeconds);
    }

    public bool Etkin => _ayarlar.KullanimaHazir;

    /// <summary>Google puan ve oy sayısı döndürüyor — süzgeç uygulanabilir.</summary>
    public bool PuanVerisiVar => true;

    /// <summary>
    /// Aramaları PARALEL atar ve sonuçları tekilleştirerek birleştirir.
    ///
    /// Google'da her arama ayrı bir istek (ve ayrı bir faturalanabilir çağrı);
    /// birleştirilemiyorlar. Paralel atmak, dört aramanın gecikmesini
    /// toplamak yerine en yavaşına indiriyor — istek SAYISI değişmediği için
    /// kota açısından fark yok.
    /// </summary>
    public async Task<IReadOnlyList<GooglePlace>> AraAsync(
        IReadOnlyList<PlacesAramasi> aramalar,
        CancellationToken iptal = default)
    {
        if (!Etkin || aramalar.Count == 0)
        {
            return Array.Empty<GooglePlace>();
        }

        var cevaplar = await Task.WhenAll(aramalar.Select(a => TekAraAsync(a, iptal)));

        return cevaplar
            .SelectMany(c => c)
            .GroupBy(m => m.PlaceId, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
    }

    private async Task<IReadOnlyList<GooglePlace>> TekAraAsync(
        PlacesAramasi arama,
        CancellationToken iptal = default)
    {
        var bos = Array.Empty<GooglePlace>();

        if (!Etkin)
        {
            return bos;
        }

        var anahtar = OnbellekAnahtari(arama);

        if (_onbellek.TryGetValue<IReadOnlyList<GooglePlace>>(anahtar, out var onbellekten)
            && onbellekten is not null)
        {
            _logger.LogDebug("Places önbellekten karşılandı: {Anahtar}", anahtar);
            return onbellekten;
        }

        // Bütçe kontrolü önbellekten SONRA: önbellekten dönen cevap Google'a
        // hiç gitmiyor, onu bütçeden düşmek sayacı yanlış gösterirdi.
        if (!_butce.IzinIste())
        {
            _logger.LogWarning("Places isteği bütçe dolduğu için atılmadı.");
            return bos;
        }

        var govde = new
        {
            textQuery = arama.Sorgu,
            includedType = arama.Tip,
            // Tip verildiğinde SIKI eşleşme: "müze" sorgusu bir kafeyi de
            // getirebiliyor; tur durağının tipi kalış süresini belirlediği
            // için yanlış tip yanlış süre demek.
            strictTypeFiltering = arama.Tip is not null,
            minRating = arama.EnAzPuan,
            // Places API (New) üst sınırı 20. Anahtarsız kaynak için
            // kullanılan yüksek tavan (YerelAramaSonucu) buraya sızarsa
            // istek INVALID_ARGUMENT ile döner — o yüzden kırpılıyor.
            maxResultCount = Math.Min(arama.EnFazlaSonuc ?? _ayarlar.AramaBasinaSonuc, 20),
            languageCode = _ayarlar.DilKodu,
            regionCode = _ayarlar.BolgeKodu,
            locationBias = new
            {
                circle = new
                {
                    center = new { latitude = arama.Lat, longitude = arama.Lon },
                    radius = (double)arama.YaricapMetre,
                },
            },
        };

        try
        {
            using var istek = new HttpRequestMessage(HttpMethod.Post, "/v1/places:searchText")
            {
                Content = JsonContent.Create(govde),
            };

            // Anahtar ve maske BAŞLIKTA gidiyor, adres satırında değil:
            // sorgu dizesine konan anahtar sunucu günlüklerine ve tarayıcı
            // geçmişine düşer.
            istek.Headers.Add("X-Goog-Api-Key", _ayarlar.ApiKey);
            istek.Headers.Add("X-Goog-FieldMask", AlanMaskesi);

            using var cevap = await _http.SendAsync(istek, iptal);

            if (!cevap.IsSuccessStatusCode)
            {
                // 429 = kota aşıldı. Ayrı log'lanıyor çünkü çözümü farklı:
                // diğer hatalar koda, bu faturaya/kotaya bakmayı gerektirir.
                var seviye = (int)cevap.StatusCode == 429 ? "kota aşıldı" : "hata";
                _logger.LogWarning(
                    "Places {Kod} döndü ({Sebep}): {Sorgu}",
                    (int)cevap.StatusCode, seviye, arama.Sorgu);
                return bos;
            }

            var icerik = await cevap.Content.ReadFromJsonAsync<PlacesCevabi>(
                cancellationToken: iptal);

            var mekanlar = (icerik?.Places ?? new List<PlacesMekani>())
                .Where(m => !string.IsNullOrWhiteSpace(m.Id) && m.Location is not null)
                .Select(Cevir)
                .ToList();

            // BOŞ SONUÇ DA ÖNBELLEKLENİYOR: "bu şehirde müze bulunamadı"
            // cevabı da bir cevaptır ve tekrar sorulduğunda yine boş dönecek.
            // Önbelleklemeseydik sonuç vermeyen aramalar her denemede yeniden
            // faturalanırdı.
            _onbellek.Set(anahtar, (IReadOnlyList<GooglePlace>)mekanlar,
                TimeSpan.FromMinutes(_ayarlar.OnbellekDakika));

            return mekanlar;
        }
        catch (TaskCanceledException) when (!iptal.IsCancellationRequested)
        {
            _logger.LogWarning("Places {Saniye} saniyede cevap vermedi.", _ayarlar.TimeoutSeconds);
            return bos;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Places isteği başarısız: {Sorgu}", arama.Sorgu);
            return bos;
        }
    }

    private static GooglePlace Cevir(PlacesMekani m) => new(
        PlaceId: m.Id!,
        Ad: m.DisplayName?.Text ?? "(adsız)",
        Lat: m.Location!.Latitude,
        Lon: m.Location.Longitude,
        Puan: m.Rating,
        DegerlendirmeSayisi: m.UserRatingCount ?? 0,
        Tipler: m.Types ?? new List<string>(),
        BirincilTip: m.PrimaryType,
        Onem: OnemHesapla(m.Rating, m.UserRatingCount ?? 0),
        VejetaryenSecenegiVar: m.ServesVegetarianFood,
        // Google'da ham etiket kavramı yok; ihtiyacımız olan her alan
        // zaten tipli olarak yukarıda.
        Ekstra: null);

    /// <summary>
    /// Puan ve oy sayısını 0-1 aralığındaki ÖNEM değerine çevirir.
    ///
    /// Puan tek başına ayırt edici değil: 4.5 süzgecinden sonra herkes
    /// 4.5-5.0 arasında sıkışıyor. Oy sayısı logaritmik katılıyor —
    /// 100.000 oylu bir yer 10.000 oyluyu ezmesin ama 40 oylu bir yerin
    /// önüne geçsin.
    /// </summary>
    private static double OnemHesapla(double? puan, int oySayisi)
    {
        if (puan is null) return 0;

        var puanPayi = Math.Clamp(puan.Value / 5.0, 0, 1);          // 0-1
        var oyPayi = Math.Clamp(Math.Log10(Math.Max(1, oySayisi)) / 5.0, 0, 1);

        // Puan ağırlıklı (0.6) çünkü "iyi mi?" sorusu "ünlü mü?"den önce gelir.
        return 0.6 * puanPayi + 0.4 * oyPayi;
    }

    /// <summary>
    /// Önbellek anahtarı. Merkez koordinat ~1 km'lik ızgaraya yuvarlanıyor
    /// (0.01° ≈ 1.1 km): kullanıcı haritayı biraz kaydırdığında ya da ilçe
    /// merkezi birkaç metre farklı hesaplandığında aynı satıra düşsün.
    /// Yuvarlamasaydık önbellek neredeyse hiç isabet etmezdi.
    /// </summary>
    private static string OnbellekAnahtari(PlacesAramasi a)
    {
        var lat = Math.Round(a.Lat, 2).ToString("0.00", CultureInfo.InvariantCulture);
        var lon = Math.Round(a.Lon, 2).ToString("0.00", CultureInfo.InvariantCulture);
        var puan = a.EnAzPuan.ToString("0.0", CultureInfo.InvariantCulture);

        return $"places:{a.Sorgu}|{a.Tip}|{lat},{lon}|{a.YaricapMetre}|{puan}";
    }

    // ---- Google cevabının karşılığı. Yalnızca field mask'te İSTENEN alanlar
    //      var: cevapta olmayan bir alanı sınıfa yazmak, ileride "neden hep
    //      null?" diye aranan bir soruya dönüşürdü.
    private class PlacesCevabi
    {
        [JsonPropertyName("places")]
        public List<PlacesMekani>? Places { get; set; }
    }

    private class PlacesMekani
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public PlacesMetni? DisplayName { get; set; }

        [JsonPropertyName("location")]
        public PlacesKonumu? Location { get; set; }

        [JsonPropertyName("rating")]
        public double? Rating { get; set; }

        [JsonPropertyName("userRatingCount")]
        public int? UserRatingCount { get; set; }

        [JsonPropertyName("types")]
        public List<string>? Types { get; set; }

        [JsonPropertyName("primaryType")]
        public string? PrimaryType { get; set; }

        [JsonPropertyName("servesVegetarianFood")]
        public bool? ServesVegetarianFood { get; set; }
    }

    private class PlacesMetni
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private class PlacesKonumu
    {
        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }
    }
}
