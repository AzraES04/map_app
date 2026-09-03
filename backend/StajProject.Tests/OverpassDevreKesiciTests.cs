using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using StajProject.DataAccess.Google;
using StajProject.DataAccess.Yerel;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

// ============================================================================
//  OVERPASS DEVRE KESICI
//
//  Olculen sorun: overpass-api.de bu IP'ye 429 donmeye basladiginda bu durum
//  dakikalarca suruyor. Her tur onerisi 2 sorgu atiyor, her sorgu zaman
//  asimina kadar (15 sn) bekliyor — kullanici, sonucu ZATEN yerel POI
//  yedeginden gelecek bir rota icin 30 saniye bekliyordu.
//
//  Bu testler kesicinin ISINI yaptigini koruyor: birkac basarisizliktan
//  sonra ag'a HIC cikmamak. Ag'a cikmadigini "gecen sure" ile degil,
//  sahte HttpMessageHandler'in SAYDIGI istek sayisiyla olcuyoruz — sureye
//  bakan bir test yavas makinede kirilgan olurdu.
// ============================================================================

public class OverpassDevreKesiciTests
{
    /// <summary>Her istegi sayan ve istenen kodu donen sahte aktarim.</summary>
    private sealed class SayanHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _kod;
        public int IstekSayisi { get; private set; }

        public SayanHandler(HttpStatusCode kod) => _kod = kod;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            IstekSayisi++;
            return Task.FromResult(new HttpResponseMessage(_kod)
            {
                Content = new StringContent("{\"elements\":[]}"),
            });
        }
    }

    private static (OsmPlacesClient istemci, SayanHandler handler) Kur(HttpStatusCode kod)
    {
        var handler = new SayanHandler(kod);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://ornek.test/") };

        var istemci = new OsmPlacesClient(
            http,
            new YerelKaynakSettings { Enabled = true, TimeoutSeconds = 5 },
            new GoogleMapsSettings { Enabled = true },
            new FakeIstekButcesi(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<OsmPlacesClient>.Instance);

        return (istemci, handler);
    }

    private static PlacesAramasi Arama(string tip) => new(
        Sorgu: tip,
        Tip: tip,
        Lat: 39.93,
        Lon: 32.86,
        YaricapMetre: 5_000,
        EnAzPuan: 0,
        EnFazlaSonuc: 20);

    /// <summary>
    /// Devre STATIC (surece ait) oldugu icin testler birbirini etkiler.
    /// Her test kendi basina anlamli olsun diye once sifirlaniyor.
    /// </summary>
    private static void DevreyiSifirla()
        => typeof(OsmPlacesClient)
            .GetField("_devreAcikSonuTick", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static)!
            .SetValue(null, 0L);

    private static void SayaciSifirla()
        => typeof(OsmPlacesClient)
            .GetField("_ardArdaHata", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static)!
            .SetValue(null, 0);

    [Fact]
    public async Task Ard_arda_hatadan_sonra_AGA_HIC_CIKMIYOR()
    {
        DevreyiSifirla();
        SayaciSifirla();

        var (istemci, handler) = Kur(HttpStatusCode.TooManyRequests);

        // Ilk cagrilar gercekten deneniyor (ve 429 aliyor).
        await istemci.AraAsync(new[] { Arama("museum") });
        await istemci.AraAsync(new[] { Arama("museum") });

        var devredenOnce = handler.IstekSayisi;
        Assert.True(devredenOnce > 0, "ilk denemeler ag'a cikmaliydi");

        // Devre acildi: bundan sonraki cagrilar HIC istek atmamali.
        await istemci.AraAsync(new[] { Arama("park") });
        await istemci.AraAsync(new[] { Arama("mosque") });

        Assert.Equal(devredenOnce, handler.IstekSayisi);
    }

    [Fact]
    public async Task Basarili_cevap_devreyi_ACMIYOR()
    {
        DevreyiSifirla();
        SayaciSifirla();

        var (istemci, handler) = Kur(HttpStatusCode.OK);

        await istemci.AraAsync(new[] { Arama("museum") });
        await istemci.AraAsync(new[] { Arama("park") });
        await istemci.AraAsync(new[] { Arama("mosque") });

        // Servis saglikliyken kesici asla devreye girmemeli.
        Assert.Equal(3, handler.IstekSayisi);
    }
}
