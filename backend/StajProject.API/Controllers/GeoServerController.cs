using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.DataAccess.GeoServer;

namespace StajProject.API.Controllers;

/// <summary>
/// GeoServer ile ilgili yardımcı uçlar (Ödev 8 / Madde 2).
///
/// İki iş yapar:
///   • <c>GET /api/geoserver/durum</c> — bağlantı bilgisi (harita paneli için)
///   • <c>GET /api/geoserver/wms</c>   — WMS karolarının VEKİLİ (proxy)
///
/// NEDEN WMS'İ DE BACKEND ÜZERİNDEN GEÇİRİYORUZ?
/// Tarayıcı doğrudan <c>localhost:8080/geoserver/staj/wms</c> adresine
/// gidebilirdi; daha az kod olurdu. Ama o zaman:
///   1. Ödevin "webden backende istek atın, veriyi backend getirsin" şartı
///      WMS tarafında delinirdi.
///   2. Sahiplik süzgeci (Ödev 5) kaybolurdu: GeoServer JWT'mizi tanımıyor,
///      herkesin çizimini boyayıp gönderirdi. Süzgeci tarayıcıya yazdırmak
///      ise güvenlik değildir — kullanıcı adres çubuğundan değiştirebilir.
///   3. GeoServer'ın kullanıcı adı/şifresi tarayıcıya çıkardı.
/// Vekil sayesinde CQL süzgeci SUNUCUDA ekleniyor ve GeoServer'ın kimlik
/// bilgileri backend'de kalıyor.
/// </summary>
[ApiController]
[Route("api/geoserver")]
[Authorize]
public class GeoServerController : ControllerBase
{
    private const string HaritaIstegi = "GetMap";
    private const string LejantIstegi = "GetLegendGraphic";

    /// <summary>
    /// Vekilin ilettiği WMS işlemleri. Bu kısıt olmasaydı backend'imiz
    /// GeoServer'ın tamamına açık bir kapı (open proxy) hâline gelirdi:
    /// istemci REQUEST=GetCapabilities ya da başka bir workspace'in katmanını
    /// isteyip yetkisi olmayan veriye ulaşabilirdi.
    ///
    /// <c>GetLegendGraphic</c> Ödev 9 ile eklendi: ısı haritası lejantı da
    /// GeoServer'dan geliyor. Böylece SLD'deki renk aralıkları değiştiğinde
    /// ekrandaki lejant kendiliğinden değişiyor — elle çizilmiş bir lejant
    /// stil ile sessizce uyumsuz hâle gelebilirdi.
    /// </summary>
    private static readonly string[] IzinliIstekler = { HaritaIstegi, LejantIstegi };

    private readonly IGeoServerClient _geoServer;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<GeoServerController> _logger;

    public GeoServerController(
        IGeoServerClient geoServer,
        ICurrentUserService currentUser,
        ILogger<GeoServerController> logger)
    {
        _geoServer = geoServer;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// GeoServer bağlantı durumu. Harita paneli veri kaynağını burada gösteriyor.
    /// </summary>
    [HttpGet("durum")]
    public async Task<ActionResult<GeoServerDurumDto>> Durum(CancellationToken iptal)
    {
        var ayarlar = _geoServer.Ayarlar;

        return Ok(new GeoServerDurumDto
        {
            Etkin = ayarlar.Enabled,
            // AyaktaMiAsync hata fırlatmıyor: durum ucunun kendisi GeoServer
            // kapalı diye çökmemeli, "kapalı" bilgisini dönebilmeli.
            Ayakta = await _geoServer.AyaktaMiAsync(iptal),
            BaseUrl = ayarlar.BaseUrl,
            Workspace = ayarlar.Workspace,
            Katmanlar = GeoServerKatmanlari.Tumu.Select(ayarlar.KatmanAdi).ToList(),
            NoktaKatmani = ayarlar.KatmanAdi(GeoServerKatmanlari.Nokta),
            IsiHaritasiStili = ayarlar.IsiHaritasiStili,
            IsiDegerStili = ayarlar.IsiDegerStili,
            // Ödev 13 / Madde 1: POI katmanının adı. Stil listesi burada DEĞİL —
            // kategori tablosundan türüyor ve /api/poi/stiller ucundan geliyor.
            PoiKatmani = ayarlar.KatmanAdi(GeoServerKatmanlari.Poi),
        });
    }

    /// <summary>
    /// WMS vekili: OpenLayers'ın istediği karoyu (GetMap) ya da ısı haritası
    /// lejantını (GetLegendGraphic) GeoServer'dan alır. Karolar giriş yapan
    /// kullanıcının kayıtlarıyla sınırlanır.
    /// </summary>
    [HttpGet("wms")]
    public async Task<IActionResult> Wms(CancellationToken iptal)
    {
        var kullaniciId = _currentUser.RequireUserId();

        // WMS parametre adları büyük/küçük harfe duyarsızdır (OGC kuralı);
        // OpenLayers büyük harf gönderiyor ama garantiye alıyoruz.
        var gelen = Request.Query.ToDictionary(
            p => p.Key.ToUpperInvariant(),
            p => p.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);

        var istek = gelen.GetValueOrDefault("REQUEST", HaritaIstegi);
        if (!IzinliIstekler.Contains(istek, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = $"Bu vekil yalnızca şu istekleri iletir: {string.Join(", ", IzinliIstekler)}."
            });
        }

        // Lejantta katman TEKİL parametreyle (LAYER) geliyor; haritada çoğul (LAYERS).
        var lejantMi = string.Equals(istek, LejantIstegi, StringComparison.OrdinalIgnoreCase);
        var hamKatman = lejantMi
            ? gelen.GetValueOrDefault("LAYER")
            : gelen.GetValueOrDefault("LAYERS");

        if (!KatmanlariDogrula(hamKatman, out var katmanlar, out var hata))
        {
            return BadRequest(new { message = hata });
        }

        var parametreler = lejantMi
            ? LejantParametreleriKur(gelen, katmanlar[0])
            : ParametreleriKur(gelen, katmanlar, kullaniciId);

        try
        {
            var cevap = await _geoServer.HaritaResmiAsync(parametreler, iptal);

            // Karo KİŞİYE ÖZELDİR (sahiplik süzgeci uygulandı). Ortak bir
            // ara bellekte saklanırsa başka bir kullanıcıya servis edilebilir;
            // bu yüzden saklanmasını istemiyoruz.
            Response.Headers.CacheControl = "private, no-store";

            return File(cevap.Icerik, cevap.IcerikTipi);
        }
        catch (GeoServerErisimException ex)
        {
            _logger.LogError(ex, "WMS isteği karşılanamadı");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    /// <summary>
    /// İstenen katmanların bizim yayınladığımız üç katmandan olduğunu doğrular
    /// ve hepsini "workspace:tablo" biçimine getirir.
    /// </summary>
    private bool KatmanlariDogrula(string? ham, out List<string> katmanlar, out string? hata)
    {
        katmanlar = new List<string>();
        hata = null;

        if (string.IsNullOrWhiteSpace(ham))
        {
            hata = "LAYERS parametresi zorunludur.";
            return false;
        }

        var ayarlar = _geoServer.Ayarlar;

        foreach (var parca in ham.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // "staj:tbl_point" da "tbl_point" da kabul; ikisi de aynı katman.
            var tablo = parca.Contains(':') ? parca[(parca.IndexOf(':') + 1)..] : parca;

            // Beyaz liste POI katmanını da içeriyor (Ödev 13 / Madde 1).
            // AYNI KATMANIN TEKRARLANMASINA izin veriliyor: POI'ler kategori
            // başına ayrı bir SLD ile çizildiği için istemci katmanı stil
            // sayısı kadar tekrar istiyor. Tekrar bir hata değil, tasarımın
            // kendisi — bu yüzden burada "aynı katman iki kez gelmiş" diye
            // bir kontrol YOK.
            if (!GeoServerKatmanlari.WmsIzinli.Contains(tablo))
            {
                hata = $"Bilinmeyen katman: {parca}";
                return false;
            }

            katmanlar.Add(ayarlar.KatmanAdi(tablo));
        }

        return true;
    }

    /// <summary>
    /// GeoServer'a gidecek parametre listesini kurar: istemcinin gönderdikleri
    /// (BBOX, WIDTH, HEIGHT, FORMAT...) aynen geçer, LAYERS doğrulanmış hâliyle
    /// yazılır, CQL_FILTER ise BİZİM tarafımızdan üretilir.
    /// </summary>
    private static List<KeyValuePair<string, string>> ParametreleriKur(
        IDictionary<string, string> gelen,
        List<string> katmanlar,
        int kullaniciId)
    {
        // İstemcinin gönderdiği bu dördünü YOK SAYIYORUZ: LAYERS'ı doğruladık,
        // CQL_FILTER'ı biz koyuyoruz (istemci kendi süzgecini yazarsa sahiplik
        // kuralı delinir), REQUEST ve SERVICE zaten sabit.
        var bizimBelirlediklerimiz = new[] { "LAYERS", "CQL_FILTER", "REQUEST", "SERVICE" };

        var parametreler = gelen
            .Where(p => !bizimBelirlediklerimiz.Contains(p.Key))
            .Select(p => new KeyValuePair<string, string>(p.Key, p.Value))
            .ToList();

        parametreler.Add(new("SERVICE", "WMS"));
        parametreler.Add(new("REQUEST", HaritaIstegi));
        parametreler.Add(new("LAYERS", string.Join(",", katmanlar)));

        // WMS'te CQL_FILTER katman SAYISI kadar süzgeç bekler, noktalı virgülle
        // ayrılmış: LAYERS=a,b,c → CQL_FILTER=f1;f2;f3.
        //
        // Süzgeçte artık YALNIZCA sahiplik var. Silinmiş kayıt kuralı Ödev 9'dan
        // sonra katmanın SQL View'ında (bkz. GeoServerKatmanlari) — burada
        // tekrarlamıyoruz.
        parametreler.Add(new("CQL_FILTER", string.Join(";", katmanlar.Select(k => Suzgec(k, kullaniciId)))));

        return parametreler;
    }

    /// <summary>
    /// Bir katmanın CQL süzgeci. Katman başına AYRI hesaplanıyor çünkü iki
    /// tür katmanın görünürlük kuralı farklı:
    ///
    ///   çizim katmanları → KİŞİSEL. Herkes yalnızca kendi çizimini görür
    ///                      (Ödev 5 sahiplik süzgeci, kolon inserted_user_id).
    ///   POI katmanı      → ORTAK. POI referans verisidir; giriş yapan herkes
    ///                      hepsini görür (gerekçe: IPoiService). Üstelik POI
    ///                      tablosunda inserted_user_id kolonu hiç YOK —
    ///                      aynı süzgeci ona da uygulasaydık GeoServer
    ///                      "unknown property" hatası verirdi.
    ///
    /// INCLUDE, CQL'in "hiçbir şeyi eleme" ifadesidir. Süzgeci tamamen atlamak
    /// yerine INCLUDE yazıyoruz çünkü CQL_FILTER listesindeki eleman sayısı
    /// katman sayısıyla BİREBİR eşleşmek zorunda; biri eksik olsa GeoServer
    /// süzgeçleri yanlış katmanlara kaydırırdı.
    /// </summary>
    private static string Suzgec(string katmanAdi, int kullaniciId)
        => katmanAdi.EndsWith(GeoServerKatmanlari.Poi, StringComparison.Ordinal)
            ? "INCLUDE"
            : $"inserted_user_id = {kullaniciId}";

    /// <summary>
    /// Lejant isteğinin parametreleri (Ödev 9 / Madde 2).
    ///
    /// Lejant VERİYE bakmaz — stildeki renk aralıklarını çizer. Bu yüzden
    /// CQL süzgeci eklenmiyor: kullanıcıya özel bir yanı yok, herkes aynı
    /// ölçeği görüyor.
    /// </summary>
    private static List<KeyValuePair<string, string>> LejantParametreleriKur(
        IDictionary<string, string> gelen,
        string katman)
    {
        var bizimBelirlediklerimiz = new[] { "LAYER", "REQUEST", "SERVICE" };

        var parametreler = gelen
            .Where(p => !bizimBelirlediklerimiz.Contains(p.Key))
            .Select(p => new KeyValuePair<string, string>(p.Key, p.Value))
            .ToList();

        parametreler.Add(new("SERVICE", "WMS"));
        parametreler.Add(new("REQUEST", LejantIstegi));
        parametreler.Add(new("LAYER", katman));

        return parametreler;
    }
}
