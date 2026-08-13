using System.Text.Json;
using StajProject.Business.Geo;

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
