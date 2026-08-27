using StajProject.DataAccess.GeoServer;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Ağ olmadan çalışan sahte GeoServer istemcisi (Ödev 8).
///
/// İki işi var:
///   1. Testin verdiği GeoJSON metnini gerçek <see cref="GeoJsonOkuyucu"/>'dan
///      geçirip döndürmek — yani okuma yolu gerçekten test edilir, sahte
///      nesneler uydurulmaz.
///   2. Kendisine hangi CQL süzgeciyle gelindiğini KAYDETMEK. Ödevin en kritik
///      iddiası "süzme GeoServer'da yapılıyor"; bunu kanıtlamanın yolu isteğin
///      içeriğine bakmaktır.
/// </summary>
public class FakeGeoServerClient : IGeoServerClient
{
    /// <summary>Boş bir GeoJSON FeatureCollection — cevap tanımlanmamış katmanlar için.</summary>
    private const string BosKoleksiyon = """{"type":"FeatureCollection","features":[]}""";

    public GeoServerSettings Ayarlar { get; } = new()
    {
        BaseUrl = "http://test/geoserver",
        Workspace = "staj",
    };

    /// <summary>Tablo adı → o katman istendiğinde dönecek GeoJSON metni.</summary>
    public Dictionary<string, string> Cevaplar { get; } = new();

    /// <summary>Yapılan her çağrının kaydı — testler süzgeci buradan doğruluyor.</summary>
    public List<(string Tablo, string? Cql, string? Siralama)> Cagrilar { get; } = new();

    public bool Ayakta { get; set; } = true;

    /// <summary>Son çağrının CQL süzgeci (tek çağrılı testler için kısayol).</summary>
    public string? SonCql => Cagrilar.Count == 0 ? null : Cagrilar[^1].Cql;

    public Task<List<GeoServerFeature>> OzellikGetirAsync(
        string tablo,
        string? cqlFilter = null,
        string? siralama = null,
        CancellationToken iptal = default)
    {
        Cagrilar.Add((tablo, cqlFilter, siralama));

        var govde = Cevaplar.TryGetValue(tablo, out var hazir) ? hazir : BosKoleksiyon;
        return Task.FromResult(GeoJsonOkuyucu.Oku(govde));
    }

    public Task<GeoServerCevabi> HaritaResmiAsync(
        IEnumerable<KeyValuePair<string, string>> parametreler,
        CancellationToken iptal = default)
        => Task.FromResult(new GeoServerCevabi(Array.Empty<byte>(), "image/png"));

    public Task<bool> AyaktaMiAsync(CancellationToken iptal = default)
        => Task.FromResult(Ayakta);

    // ------------------------------------------------------------------
    //  Stil yönetimi (Ödev 13 iyileştirmesi)
    //
    //  Gerçek istemci GeoServer'ın REST API'sine yazıyor; burada bellekteki
    //  bir sözlüğe yazıyoruz. Testler böylece "hangi stiller yazıldı, hangisi
    //  silindi, katmana ne bağlandı" sorularını AĞ OLMADAN sorabiliyor.
    // ------------------------------------------------------------------

    /// <summary>Yazılmış stiller: ad → SLD gövdesi.</summary>
    public Dictionary<string, string> Stiller { get; } = new();

    /// <summary>Silinen stil adları, çağrı sırasıyla.</summary>
    public List<string> SilinenStiller { get; } = new();

    /// <summary>Katmana en son bağlanan stil listesi (sıra korunur).</summary>
    public List<string>? BaglananStiller { get; private set; }

    /// <summary>Katmana bağlama çağrısında verilen katman adı.</summary>
    public string? BaglananKatman { get; private set; }

    /// <summary>
    /// true yapılınca stil çağrıları <see cref="GeoServerErisimException"/>
    /// fırlatır — "GeoServer kapalıyken kategori yönetimi çalışmaya devam
    /// ediyor mu?" sorusunu sınamak için.
    /// </summary>
    public bool StilYazmaHataVersin { get; set; }

    public Task StilYazAsync(string stilAdi, string sld, CancellationToken iptal = default)
    {
        if (StilYazmaHataVersin)
        {
            throw new GeoServerErisimException("Sahte hata: GeoServer'a ulaşılamıyor.");
        }

        Stiller[stilAdi] = sld;
        return Task.CompletedTask;
    }

    /// <summary>Stil dizinine yazılan dosyalar: ad → içerik (Ödev 15 simgeleri).</summary>
    public Dictionary<string, string> StilKaynaklari { get; } = new();

    public Task StilKaynagiYazAsync(
        string dosyaAdi, string icerik, string icerikTipi, CancellationToken iptal = default)
    {
        // Simge dosyaları da stil yazma yolunun parçası: sunucu kapalıyken
        // ikisi birden başarısız olmalı, yoksa test "SVG yazıldı ama SLD
        // yazılmadı" gibi gerçekte olmayan bir ara duruma inanırdı.
        if (StilYazmaHataVersin)
        {
            throw new GeoServerErisimException("Sahte hata: GeoServer'a ulaşılamıyor.");
        }

        StilKaynaklari[dosyaAdi] = icerik;
        return Task.CompletedTask;
    }

    public Task<List<string>> StilleriListeleAsync(CancellationToken iptal = default)
    {
        if (StilYazmaHataVersin)
        {
            throw new GeoServerErisimException("Sahte hata: GeoServer'a ulaşılamıyor.");
        }

        return Task.FromResult(Stiller.Keys.ToList());
    }

    public Task StilSilAsync(string stilAdi, CancellationToken iptal = default)
    {
        Stiller.Remove(stilAdi);
        SilinenStiller.Add(stilAdi);
        return Task.CompletedTask;
    }

    public Task KatmanaStilBaglaAsync(
        string katman, IEnumerable<string> stiller, CancellationToken iptal = default)
    {
        BaglananKatman = katman;
        BaglananStiller = stiller.ToList();
        return Task.CompletedTask;
    }
}
