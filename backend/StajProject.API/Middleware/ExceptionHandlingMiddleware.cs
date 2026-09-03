using System.Text.Json;
using StajProject.Business.Geo;
using StajProject.Business.Validation;
using StajProject.DataAccess.GeoServer;

namespace StajProject.API.Middleware;

/// <summary>
/// Tüm isteklerin etrafını saran merkezî hata yakalayıcı.
///
/// Amaç: Controller'lar SADECE servisi çağırsın. Önceden her controller
/// metodunda try/catch vardı; bu hem tekrar hem de iş katmanı hatalarının
/// HTTP'ye çevrilmesini her metoda dağıtmak demekti. Artık çeviri tek yerde.
///
/// Kural: 4xx = istemcinin hatası, 5xx = sunucunun hatası.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);   // zincirdeki bir sonraki adım (sonunda controller)
        }
        catch (WktFormatException ex)
        {
            // Geometri/görsel doğrulama hatası → gönderilen VERİ hatalı, sunucu değil.
            _logger.LogWarning(ex, "Geçersiz istek verisi");
            await YazAsync(context, StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (IsKuraliException ex)
        {
            // İş kuralı ihlali (Ödev 6): "bu kullanıcı adı zaten var" gibi.
            // Controller'lar bunu zaten yakalıyor; buradaki catch son güvenlik ağı.
            _logger.LogWarning(ex, "İş kuralı ihlali");
            await YazAsync(context, StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (DisServisException ex)
        {
            // Google Maps gibi bir dış servis yapılandırılmamış, kotası dolmuş
            // ya da cevap vermiyor. GeoServerErisimException ile aynı karar:
            // 503 ve mesaj istemciye gösteriliyor (eyleme dönüştürülebilir).
            _logger.LogError(ex, "Dış servis kullanılamıyor");
            await YazAsync(context, StatusCodes.Status503ServiceUnavailable, ex.Message);
        }
        catch (GeoServerErisimException ex)
        {
            // Ödev 8: bağımlı olduğumuz DIŞ servis (GeoServer) yok ya da hata verdi.
            // 500 demek yanıltıcı olurdu ("bizim kodumuz çöktü"); doğrusu 503:
            // "hizmet şu an verilemiyor, sebebi geçici". Mesajı istemciye
            // GÖSTERİYORUZ çünkü eyleme dönüştürülebilir: "GeoServer'ı başlat".
            _logger.LogError(ex, "GeoServer erişim hatası");
            await YazAsync(context, StatusCodes.Status503ServiceUnavailable, ex.Message);
        }
        catch (Exception ex)
        {
            // Beklenmeyen hata: ayrıntıyı log'a yaz, istemciye sızdırma.
            _logger.LogError(ex, "Beklenmeyen hata");
            await YazAsync(context, StatusCodes.Status500InternalServerError,
                "Beklenmeyen bir hata oluştu.");
        }
    }

    private static async Task YazAsync(HttpContext context, int durumKodu, string mesaj)
    {
        // Cevap gövdesi yazılmaya başlandıysa başlıklara artık dokunamayız.
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = durumKodu;
        context.Response.ContentType = "application/json; charset=utf-8";

        // Frontend'deki hataMesaji() { message } biçimini bekliyor — aynı sözleşme.
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = mesaj }));
    }
}
