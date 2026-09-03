using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using StajProject.DataAccess.Google;
using StajProject.DataAccess.Osrm;
using StajProject.DataAccess.Yerel;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

// ============================================================================
//  ANAHTARSIZ KİP — OSRM tabanlı sıralama ve yedek sezgisel
//
//  Buradaki testler tek bir soruyu koruyor: Google anahtarı YOKKEN tur
//  önerisi yine de çalışıyor mu? Bu, projeyi klonlayan birinin (jüri dahil)
//  faturalı bir hesap açmadan modülü görebilmesi demek.
//
//  OSRM'in kendisi taklit EDİLMİYOR: ayarlarda kapatılıp yedek yolun
//  çalıştığı sınanıyor. Gerçek OSRM'e bağlanan bir test, paketi Docker'ın
//  ayakta olmasına bağlardı.
// ============================================================================

public class YerelTurKaynagiTests
{
    /// <summary>OSRM kapalı: istemci doğrudan yedek sezgisele düşüyor.</summary>
    private static OsrmDirectionsClient KapaliOsrmIstemcisi()
        => new(
            new HttpClient(),
            new OsrmSettings { Enabled = false },
            NullLogger<OsrmDirectionsClient>.Instance);

    /// <summary>Ankara çevresinde, batıdan doğuya dizilmiş noktalar.</summary>
    private static Coordinate Nokta(double lon, double lat) => new(lon, lat);

    // ------------------------------------------------------------------
    //  Overpass sorgusunu YAKALAYAN sahte sunucu
    //
    //  Gerçek Overpass'e bağlanmıyoruz: test paketi gönüllü bir altyapıya
    //  ve internete bağlı olmamalı. Sınanan şey zaten cevap değil, ÜRETİLEN
    //  SORGU — tekrarlı süzgeçler Overpass'i 504'e sokuyordu.
    // ------------------------------------------------------------------

    private sealed class SorguYakalayan : HttpMessageHandler
    {
        public string? SonGovde { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage istek,
            CancellationToken iptal)
        {
            var govde = istek.Content is null
                ? string.Empty
                : await istek.Content.ReadAsStringAsync(iptal);

            // Gövde "data=<sorgu>" biçiminde ve FORM kodlu geliyor: boşluklar
            // '+' olarak taşınıyor ve UnescapeDataString onları çözmüyor.
            // Önce '+' → boşluk, sonra yüzde kodlaması.
            var ham = govde.StartsWith("data=", StringComparison.Ordinal) ? govde[5..] : govde;
            SonGovde = Uri.UnescapeDataString(ham.Replace('+', ' '));

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"elements\":[]}"),
            };
        }
    }

    private static (OsmPlacesClient istemci, SorguYakalayan sunucu) Kur()
    {
        var yakalayan = new SorguYakalayan();

        var istemci = new OsmPlacesClient(
            new HttpClient(yakalayan) { BaseAddress = new Uri("https://overpass.test/") },
            new YerelKaynakSettings { Enabled = true },
            new GoogleMapsSettings { Enabled = true },
            new FakeIstekButcesi(),
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            NullLogger<OsmPlacesClient>.Instance);

        return (istemci, yakalayan);
    }

    private static PlacesAramasi Arama(
        string sorgu, string tip, int yaricap = 25_000, int enFazla = 150)
        => new(sorgu, tip, 39.9334, 32.8597, yaricap, 0, enFazla);

    [Fact]
    public async Task Osrm_kapaliyken_bile_siralama_uretiliyor()
    {
        var istemci = KapaliOsrmIstemcisi();

        var rota = await istemci.SiraliRotaAsync(
            new[]
            {
                Nokta(32.80, 39.92),   // başlangıç
                Nokta(32.86, 39.92),   // uzak ara nokta
                Nokta(32.82, 39.92),   // yakın ara nokta
                Nokta(32.90, 39.92),   // varış
            },
            SeyahatKipi.Yaya);

        Assert.NotNull(rota);

        // En yakın komşu: başlangıca en yakın ara nokta (indis 1 = "yakın")
        // önce gelmeli. Sıra ham girdi sırasında kalsaydı tur önce 6 km
        // doğuya gidip geri dönerdi.
        Assert.Equal(new[] { 1, 0 }, rota!.Sira.ToArray());
    }

    [Fact]
    public async Task Yedek_yolda_bacak_sayisi_ve_sureler_tutarli()
    {
        var istemci = KapaliOsrmIstemcisi();

        var noktalar = new[]
        {
            Nokta(32.80, 39.92),
            Nokta(32.82, 39.92),
            Nokta(32.84, 39.92),
        };

        var yaya = await istemci.SiraliRotaAsync(noktalar, SeyahatKipi.Yaya);
        var arac = await istemci.SiraliRotaAsync(noktalar, SeyahatKipi.Arac);

        // Durak sayısı - 1 bacak: iş katmanının bütçe hesabı bu sayıya güveniyor.
        Assert.Equal(noktalar.Length - 1, yaya!.Bacaklar.Count);

        // Aynı mesafe, farklı kip → araç daha hızlı.
        Assert.Equal(yaya.ToplamMetre, arac!.ToplamMetre, 3);
        Assert.True(arac.ToplamSaniye < yaya.ToplamSaniye);
    }

    [Fact]
    public async Task Yedek_yolda_rota_cizgisi_UYDURULMUYOR()
    {
        var istemci = KapaliOsrmIstemcisi();

        var rota = await istemci.SiraliRotaAsync(
            new[] { Nokta(32.80, 39.92), Nokta(32.84, 39.92) },
            SeyahatKipi.Yaya);

        // Kuş uçuşu bir çizgi çizseydik haritada GERÇEK rota sanılırdı.
        // Null gelince arayüz yalnızca durakları gösteriyor.
        Assert.Null(rota!.Cizgi);
        Assert.True(rota.ToplamMetre > 0);
    }

    [Fact]
    public async Task Iki_noktadan_az_istek_reddediliyor()
    {
        var istemci = KapaliOsrmIstemcisi();

        Assert.Null(await istemci.SiraliRotaAsync(
            new[] { Nokta(32.80, 39.92) }, SeyahatKipi.Yaya));
    }

    [Fact]
    public void Yerel_yon_istemcisi_her_zaman_etkin()
    {
        // Google istemcisinde Etkin "anahtar var mı?" demekti. Burada öyle bir
        // koşul yok: OSRM kapalı olsa bile yedek hesap çalışıyor, false dönmek
        // üretilebilecek bir öneriyi engellemek olurdu.
        Assert.True(KapaliOsrmIstemcisi().Etkin);
    }

    [Fact]
    public void Osm_istemcisi_puan_verisi_tasimadigini_bildiriyor()
    {
        // Bu bayrak, iş katmanının 4.5+ süzgecini açıkça devre dışı bırakıp
        // kullanıcıyı uyarmasını sağlıyor (bkz. TurPlanlamaTests).
        var istemci = new OsmPlacesClient(
            new HttpClient(),
            new YerelKaynakSettings(),
            new GoogleMapsSettings(),
            new Fakes.FakeIstekButcesi(),
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            NullLogger<OsmPlacesClient>.Instance);

        Assert.False(istemci.PuanVerisiVar);
        Assert.True(istemci.Etkin);   // varsayılan ayarlarda açık
    }

    [Fact]
    public void Osm_istemcisi_kimlik_basligi_gonderiyor()
    {
        // Overpass'ın genel sunucusu User-Agent'sız isteklere 406 dönüyor:
        // başlık olmadan HİÇBİR sorgu çalışmıyor (canlı denendi). Bu yüzden
        // testi var — sessizce kaybolursa anahtarsız kip komple çalışmaz.
        var http = new HttpClient();

        _ = new OsmPlacesClient(
            http,
            new YerelKaynakSettings(),
            new GoogleMapsSettings(),
            new Fakes.FakeIstekButcesi(),
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            NullLogger<OsmPlacesClient>.Instance);

        Assert.NotEmpty(http.DefaultRequestHeaders.UserAgent);
        Assert.Contains("StajProject", http.DefaultRequestHeaders.UserAgent.ToString());
    }

    // ------------------------------------------------------------------
    //  SORGU ÜRETİMİ — tekilleştirme ve tavan
    // ------------------------------------------------------------------

    [Fact]
    public async Task Ayni_suzgec_sorguda_BIR_KEZ_geciyor()
    {
        var (istemci, sunucu) = Kur();

        // Yemek molası kataloğunda üç arama var ve üçü de amenity=restaurant.
        await istemci.AraAsync(new[]
        {
            Arama("yöresel lokanta", "restaurant"),
            Arama("kebapçı", "restaurant"),
            Arama("ev yemekleri", "restaurant"),
        });

        var sorgu = sunucu.SonGovde!;
        var kezSayisi = sorgu.Split("[\"amenity\"=\"restaurant\"]").Length - 1;

        // ÖLÇÜLDÜ: tekrarlı sorgu Overpass'te 504 Gateway Timeout veriyordu.
        // Kullanıcının bildirdiği iki sorun ("çok uzun sürüyor" ve
        // "konaklama sunulmuyor") aynı satırdan geliyordu.
        //
        // node + way olmak üzere 2 kez geçmeli, 6 kez değil.
        Assert.Equal(2, kezSayisi);
    }

    [Fact]
    public async Task Farkli_suzgecler_sorguda_HEPSI_var()
    {
        var (istemci, sunucu) = Kur();

        await istemci.AraAsync(new[]
        {
            Arama("müze", "museum"),
            Arama("park", "park"),
        });

        // Tekilleştirme, FARKLI süzgeçleri elemez.
        Assert.Contains("\"tourism\"=\"museum\"", sunucu.SonGovde);
        Assert.Contains("\"leisure\"=\"park\"", sunucu.SonGovde);
    }

    [Fact]
    public async Task Farkli_yaricap_ayri_sorgulaniyor()
    {
        var (istemci, sunucu) = Kur();

        await istemci.AraAsync(new[]
        {
            Arama("restoran uzak", "restaurant", yaricap: 25_000),
            Arama("restoran yakin", "restaurant", yaricap: 10_000),
        });

        // Aynı süzgeç ama FARKLI alan: ikisi de sorulmalı, yoksa dar
        // yarıçaplı mola araması sessizce kaybolurdu.
        Assert.Contains("around:25000", sunucu.SonGovde);
        Assert.Contains("around:10000", sunucu.SonGovde);
    }

    [Fact]
    public async Task Tavan_TEKIL_tipler_uzerinden_hesaplaniyor()
    {
        var (istemci, sunucu) = Kur();

        await istemci.AraAsync(new[]
        {
            Arama("lokanta 1", "restaurant", enFazla: 150),
            Arama("lokanta 2", "restaurant", enFazla: 150),
            Arama("lokanta 3", "restaurant", enFazla: 150),
        });

        // Ham toplam 450 olurdu; süzgeç bir kez sorulduğu için tavan da
        // bir kez sayılmalı. Overpass'in maliyeti bu sayıyla doğru orantılı.
        Assert.Contains("out center 150;", sunucu.SonGovde);
    }
}