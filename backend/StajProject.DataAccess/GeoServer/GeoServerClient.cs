using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace StajProject.DataAccess.GeoServer;

/// <summary>
/// <see cref="IGeoServerClient"/>'ın HTTP gerçeklemesi.
///
/// GeoServer bir REST/OGC sunucusudur: onunla konuşmanın yolu HTTP istekleridir.
/// Bu sınıf o istekleri kurar, hataları anlamlı mesajlara çevirir ve cevabı
/// <see cref="GeoJsonOkuyucu"/>'ya verir.
///
/// HttpClient DI'dan geliyor (typed client). Neden <c>new HttpClient()</c> değil?
/// Her çağrıda yeni HttpClient üretmek soket tükenmesine (socket exhaustion)
/// yol açan bilinen bir hatadır: kapatılan bağlantılar TIME_WAIT'te bekler ve
/// bir süre sonra "address already in use" alınır. IHttpClientFactory
/// bağlantıları havuzlar ve DNS değişikliklerini de doğru yönetir.
/// </summary>
public class GeoServerClient : IGeoServerClient
{
    private readonly HttpClient _http;
    private readonly ILogger<GeoServerClient> _logger;

    public GeoServerSettings Ayarlar { get; }

    public GeoServerClient(HttpClient http, GeoServerSettings ayarlar, ILogger<GeoServerClient> logger)
    {
        _http = http;
        _logger = logger;
        Ayarlar = ayarlar;

        _http.Timeout = TimeSpan.FromSeconds(ayarlar.TimeoutSeconds);

        // HTTP Basic: "kullanıcı:şifre" Base64 ile kodlanıp başlığa konur.
        // Bunu her istekte elle eklemek yerine varsayılan başlık yapıyoruz.
        // (Base64 ŞİFRELEME DEĞİLDİR — sadece kodlamadır. Gerçek ortamda
        //  GeoServer'a HTTPS üzerinden bağlanılmalı; burada iki süreç de
        //  aynı makinede, localhost'ta konuşuyor.)
        var kimlik = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{ayarlar.Username}:{ayarlar.Password}"));

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", kimlik);
    }

    // ------------------------------------------------------------------
    //  WFS — veri (GeoJSON)
    // ------------------------------------------------------------------

    public async Task<List<GeoServerFeature>> OzellikGetirAsync(
        string tablo,
        string? cqlFilter = null,
        string? siralama = null,
        CancellationToken iptal = default)
    {
        // Parametrelerin anlamı:
        //   typeName     → hangi katman ("staj:tbl_point")
        //   outputFormat → cevap biçimi; GeoJSON istiyoruz (varsayılan GML olurdu)
        //   srsName      → koordinatların hangi sistemde döneceği
        //   CQL_FILTER   → GeoServer'a özel süzgeç parametresi (OGC standardı değil,
        //                  ama tüm GeoServer sürümlerinde var ve XML Filter'dan
        //                  kat kat okunaklı)
        //   sortBy       → sıralama; "inserted_date D" = azalan
        //
        // ---- NEDEN WFS 1.0.0, 2.0.0 DEĞİL? ----
        // Sebep tek kelimeyle EKSEN SIRASI.
        //
        // WFS 1.1.0 ve 2.0.0, EPSG:4326'yı OGC'nin resmî tanımıyla, yani
        // (ENLEM, BOYLAM) sırasıyla yorumluyor. WFS 1.0.0 ise her koordinatı
        // (X, Y) = (BOYLAM, ENLEM) kabul ediyor. Bu projenin tamamı — WKT'ler,
        // GeoJSON, PostGIS kolonları, frontend — boylam/enlem sırasında çalışıyor.
        //
        // Fark, CQL süzgecine bir GEOMETRİ yazdığımızda ortaya çıkıyor
        // (kesişim analizi). 2.0.0'da aynı poligon başka bir yeri işaret ediyor,
        // sorgu hata vermeden "0 sonuç" dönüyordu.
        //
        // 2.0.0'da kalıp geometriyi EWKT ile ("SRID=4326;POLYGON(...)") göndermeyi
        // de denedik: CQL_FILTER'da NOKTALI VİRGÜL "filtre listesi" ayıracı olduğu
        // için üç koşullu filtrelerde ayrıştırıcı çöküyor
        // ("Could not parse CQL filter list").
        //
        // 1.0.0 hem eski hem de bu iş için doğru sürüm: belirsizlik yok,
        // GeoServer tam destekliyor, sortBy ve CQL_FILTER sorunsuz çalışıyor.
        // (Tek fark: parametre adı çoğul "typeNames" değil tekil "typeName".)
        var parametreler = new List<KeyValuePair<string, string>>
        {
            new("service", "WFS"),
            new("version", "1.0.0"),
            new("request", "GetFeature"),
            new("typeName", Ayarlar.KatmanAdi(tablo)),
            new("outputFormat", "application/json"),
            new("srsName", "EPSG:4326"),
        };

        if (!string.IsNullOrWhiteSpace(cqlFilter))
        {
            parametreler.Add(new("CQL_FILTER", cqlFilter));
        }

        if (!string.IsNullOrWhiteSpace(siralama))
        {
            parametreler.Add(new("sortBy", siralama));
        }

        var adres = AdresKur(Ayarlar.ServisAdresi("wfs"), parametreler);

        _logger.LogDebug("GeoServer WFS isteği: {Adres}", adres);

        var govde = await MetinIsteAsync(adres, iptal);
        return GeoJsonOkuyucu.Oku(govde);
    }

    // ------------------------------------------------------------------
    //  WMS — resim (PNG)
    // ------------------------------------------------------------------

    public async Task<GeoServerCevabi> HaritaResmiAsync(
        IEnumerable<KeyValuePair<string, string>> parametreler,
        CancellationToken iptal = default)
    {
        var adres = AdresKur(Ayarlar.ServisAdresi("wms"), parametreler);

        _logger.LogDebug("GeoServer WMS isteği: {Adres}", adres);

        using var cevap = await IstekAtAsync(adres, iptal);
        var icerikTipi = cevap.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        // WMS hata durumunda 200 OK ile XML döndürebiliyor (ServiceExceptionReport).
        // Resim beklerken XML gelirse bunu sessizce bozuk resim olarak geçirmek
        // yerine açık hata veriyoruz.
        if (icerikTipi.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            var hata = await cevap.Content.ReadAsStringAsync(iptal);
            throw new GeoServerErisimException($"GeoServer WMS hatası: {Kirp(hata)}");
        }

        var baytlar = await cevap.Content.ReadAsByteArrayAsync(iptal);
        return new GeoServerCevabi(baytlar, icerikTipi);
    }

    // ------------------------------------------------------------------
    //  Sağlık kontrolü
    // ------------------------------------------------------------------

    public async Task<bool> AyaktaMiAsync(CancellationToken iptal = default)
    {
        try
        {
            // /rest/about/version.json hem sunucunun ayakta olduğunu hem de
            // kullanıcı adı/şifrenin doğru olduğunu tek istekte doğrular.
            var adres = $"{Ayarlar.BaseUrl.TrimEnd('/')}/rest/about/version.json";
            using var cevap = await _http.GetAsync(adres, iptal);
            return cevap.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GeoServer sağlık kontrolü başarısız");
            return false;
        }
    }

    // ------------------------------------------------------------------
    //  Stil yönetimi (REST) — Ödev 13 iyileştirmesi
    // ------------------------------------------------------------------

    /// <summary>Bu çalışma alanındaki stillerin REST kökü.</summary>
    private string StilKoku => $"{Ayarlar.BaseUrl.TrimEnd('/')}/rest/workspaces/{Ayarlar.Workspace}/styles";

    /// <summary>
    /// Stil dizininin RESOURCE adresi (Ödev 15). Katalog uçlarından (yukarıdaki
    /// <see cref="StilKoku"/>) ayrı: o "stil kaydı"nı, bu veri dizinindeki
    /// DOSYAYI yönetiyor. İkisinin yolu da aynı klasörü işaret ediyor ama
    /// GeoServer bunları farklı API'lerle açıyor.
    /// </summary>
    private string KaynakKoku
        => $"{Ayarlar.BaseUrl.TrimEnd('/')}/rest/resource/workspaces/{Ayarlar.Workspace}/styles";

    public async Task StilYazAsync(string stilAdi, string sld, CancellationToken iptal = default)
    {
        // SLD'yi UTF-8 olarak gönderiyoruz. StringContent bu aşırı yüklemede
        // charset=utf-8 başlığını da ekliyor; olmasaydı GeoServer gövdeyi
        // ISO-8859-1 varsayabilir ve stildeki Türkçe başlıklar bozulurdu.
        // (Aynı tuzağın PowerShell tarafındaki hâli gs-yapilandir.ps1'de yazılı.)
        var govde = new StringContent(sld, Encoding.UTF8, "application/vnd.ogc.sld+xml");

        var varMi = await KaynakVarMiAsync($"{StilKoku}/{Uri.EscapeDataString(stilAdi)}.json", iptal);

        // Var olanı PUT ile güncelliyoruz, yenisini POST ile açıyoruz.
        // "Önce sil sonra oluştur" kestirmesini KULLANMIYORUZ: silme, stili
        // katmandan da düşürüyor; iki istek arasında bir WMS isteği gelirse
        // katman o an stilsiz kalırdı.
        if (varMi)
        {
            await RestIstekAsync(HttpMethod.Put, $"{StilKoku}/{Uri.EscapeDataString(stilAdi)}", govde, iptal);
        }
        else
        {
            await RestIstekAsync(HttpMethod.Post, $"{StilKoku}?name={Uri.EscapeDataString(stilAdi)}", govde, iptal);
        }
    }

    /// <summary>
    /// Stil dizinine dosya yazar — GeoServer'ın Resource REST API'si (Ödev 15).
    ///
    /// Adres <c>/rest/resource/workspaces/{ws}/styles/{dosya}</c>: bu uç veri
    /// dizinindeki dosyaları doğrudan yönetiyor. PUT var olanı da yenisini de
    /// aynı şekilde yazıyor (idempotent), bu yüzden "önce var mı?" kontrolü
    /// gerekmiyor — stil yazmadaki POST/PUT ayrımının aksine.
    /// </summary>
    public async Task StilKaynagiYazAsync(
        string dosyaAdi, string icerik, string icerikTipi, CancellationToken iptal = default)
    {
        // Dosya adı bizim ürettiğimiz bir değer ("poi_kat_13.svg") ama yine de
        // kaçırıyoruz: adres parçası olarak gidiyor ve ileride kategoriden
        // türetilen bir ad kullanılırsa yol kaçışı ("../") ihtimali doğardı.
        var adres = $"{KaynakKoku}/{Uri.EscapeDataString(dosyaAdi)}";

        // charset=utf-8 şart: SVG'nin XML bildirimi UTF-8 diyor, gövde de
        // öyle gitmeli (aynı tuzağın SLD tarafındaki hâli StilYazAsync'te).
        var govde = new StringContent(icerik, Encoding.UTF8, icerikTipi);

        using var _ = await RestIstekAsync(HttpMethod.Put, adres, govde, iptal);
    }

    public async Task<List<string>> StilleriListeleAsync(CancellationToken iptal = default)
    {
        using var cevap = await RestIstekAsync(HttpMethod.Get, $"{StilKoku}.json", null, iptal);
        var govde = await cevap.Content.ReadAsStringAsync(iptal);

        // Cevap: { "styles": { "style": [ { "name": "...", "href": "..." } ] } }
        // Hiç stil yoksa GeoServer "styles" alanını BOŞ METİN olarak döndürüyor
        // (dizi değil!), o yüzden tip kontrolü şart — doğrudan dizi beklemek
        // boş bir kurulumda JsonException'a düşerdi.
        using var belge = JsonDocument.Parse(govde);

        if (!belge.RootElement.TryGetProperty("styles", out var stiller)
            || stiller.ValueKind != JsonValueKind.Object
            || !stiller.TryGetProperty("style", out var dizi)
            || dizi.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return dizi.EnumerateArray()
            .Select(s => s.TryGetProperty("name", out var ad) ? ad.GetString() : null)
            .Where(ad => !string.IsNullOrEmpty(ad))
            .Select(ad => ad!)
            .ToList();
    }

    public async Task StilSilAsync(string stilAdi, CancellationToken iptal = default)
    {
        // purge=true: katalog kaydıyla birlikte SLD dosyasını da siler.
        // Olmadan, silinen kategorinin dosyası diskte artık olarak kalırdı.
        // recurse=true: stil bir katmana bağlıysa önce bağı koparır.
        var adres = $"{StilKoku}/{Uri.EscapeDataString(stilAdi)}?purge=true&recurse=true";
        using var _ = await RestIstekAsync(HttpMethod.Delete, adres, null, iptal);
    }

    public async Task KatmanaStilBaglaAsync(
        string katman, IEnumerable<string> stiller, CancellationToken iptal = default)
    {
        // Katman adı "workspace:katman" biçiminde bekleniyor; stil adları da
        // workspace önekiyle veriliyor ki GeoServer global stillerle karıştırmasın.
        var liste = string.Join(",", stiller.Select(s =>
            $"{{\"name\":\"{Ayarlar.Workspace}:{s}\"}}"));

        var govde = new StringContent(
            $"{{\"layer\":{{\"styles\":{{\"style\":[{liste}]}}}}}}",
            Encoding.UTF8, "application/json");

        var adres = $"{Ayarlar.BaseUrl.TrimEnd('/')}/rest/layers/{Uri.EscapeDataString(katman)}";
        using var _ = await RestIstekAsync(HttpMethod.Put, adres, govde, iptal);
    }

    /// <summary>200 → var, 404 → yok. Başka bir kod gerçek bir sorundur.</summary>
    private async Task<bool> KaynakVarMiAsync(string adres, CancellationToken iptal)
    {
        try
        {
            using var cevap = await _http.GetAsync(adres, iptal);
            if (cevap.StatusCode == HttpStatusCode.NotFound) return false;
            if (cevap.IsSuccessStatusCode) return true;

            throw new GeoServerErisimException(
                $"GeoServer beklenmeyen cevap verdi ({(int)cevap.StatusCode}): {adres}");
        }
        catch (HttpRequestException ex)
        {
            throw new GeoServerErisimException(
                $"GeoServer'a ulaşılamıyor ({Ayarlar.BaseUrl}).", ex);
        }
    }

    /// <summary>
    /// GET dışı REST istekleri için ortak yol. Hata çevirisi
    /// <see cref="IstekAtAsync"/> ile aynı — tek fark HTTP metodunun ve
    /// gövdenin dışarıdan verilmesi.
    /// </summary>
    private async Task<HttpResponseMessage> RestIstekAsync(
        HttpMethod metot, string adres, HttpContent? govde, CancellationToken iptal)
    {
        using var istek = new HttpRequestMessage(metot, adres) { Content = govde };

        HttpResponseMessage cevap;
        try
        {
            cevap = await _http.SendAsync(istek, iptal);
        }
        catch (HttpRequestException ex)
        {
            throw new GeoServerErisimException(
                $"GeoServer'a ulaşılamıyor ({Ayarlar.BaseUrl}). " +
                "Sunucu çalışıyor mu? (geoserver\\gs-baslat.ps1)", ex);
        }
        catch (TaskCanceledException ex) when (!iptal.IsCancellationRequested)
        {
            throw new GeoServerErisimException(
                $"GeoServer {Ayarlar.TimeoutSeconds} saniyede cevap vermedi.", ex);
        }

        if (cevap.IsSuccessStatusCode)
        {
            return cevap;
        }

        var durum = cevap.StatusCode;
        var hataGovdesi = await cevap.Content.ReadAsStringAsync(iptal);
        cevap.Dispose();

        throw new GeoServerErisimException(
            $"GeoServer REST isteği reddetti ({(int)durum} · {metot} {adres}): {Kirp(hataGovdesi)}");
    }

    // ------------------------------------------------------------------
    //  Ortak yardımcılar
    // ------------------------------------------------------------------

    /// <summary>
    /// Sorgu dizesini kurar. Değerler <see cref="Uri.EscapeDataString"/> ile
    /// kaçırılır — CQL süzgecinde boşluk, tırnak ve parantez var; kaçırılmazsa
    /// adres bozulur ve GeoServer anlamsız bir hata döner.
    /// </summary>
    private static string AdresKur(string temel, IEnumerable<KeyValuePair<string, string>> parametreler)
    {
        var sorgu = string.Join("&", parametreler.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        return $"{temel}?{sorgu}";
    }

    private async Task<string> MetinIsteAsync(string adres, CancellationToken iptal)
    {
        using var cevap = await IstekAtAsync(adres, iptal);
        return await cevap.Content.ReadAsStringAsync(iptal);
    }

    /// <summary>
    /// İsteği atar ve ağ/HTTP hatalarını <see cref="GeoServerErisimException"/>'a
    /// çevirir. Amaç: üst katmanlar HttpRequestException gibi altyapı tiplerini
    /// hiç görmesin, kullanıcıya gösterilebilir bir mesaj alsın.
    /// </summary>
    private async Task<HttpResponseMessage> IstekAtAsync(string adres, CancellationToken iptal)
    {
        HttpResponseMessage cevap;

        try
        {
            cevap = await _http.GetAsync(adres, iptal);
        }
        catch (HttpRequestException ex)
        {
            throw new GeoServerErisimException(
                $"GeoServer'a ulaşılamıyor ({Ayarlar.BaseUrl}). " +
                "Sunucu çalışıyor mu? (geoserver\\gs-baslat.ps1)", ex);
        }
        catch (TaskCanceledException ex) when (!iptal.IsCancellationRequested)
        {
            // İptal isteğin kendisinden değil, zaman aşımından geliyor.
            throw new GeoServerErisimException(
                $"GeoServer {Ayarlar.TimeoutSeconds} saniyede cevap vermedi.", ex);
        }

        if (cevap.IsSuccessStatusCode)
        {
            return cevap;
        }

        // Durum kodunu ve gövdeyi Dispose'dan ÖNCE alıyoruz: cevap
        // kapatıldıktan sonra Content akışı okunamaz.
        var durum = cevap.StatusCode;
        var govde = await cevap.Content.ReadAsStringAsync(iptal);
        cevap.Dispose();

        var mesaj = durum switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "GeoServer kimlik doğrulaması reddetti. appsettings'teki " +
                "GeoServer:Username / GeoServer:Password değerlerini kontrol edin.",

            HttpStatusCode.NotFound =>
                $"GeoServer katmanı bulunamadı. '{Ayarlar.Workspace}' workspace'i ve " +
                "katmanlar oluşturulmuş mu? (geoserver\\gs-yapilandir.ps1)",

            _ => $"GeoServer hata döndü ({(int)durum}): {Kirp(govde)}"
        };

        throw new GeoServerErisimException(mesaj);
    }

    private static string Kirp(string metin)
    {
        var temiz = metin.Trim();
        return temiz.Length <= 300 ? temiz : temiz[..300] + "...";
    }
}
