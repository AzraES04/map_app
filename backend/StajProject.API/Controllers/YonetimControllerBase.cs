using Microsoft.AspNetCore.Mvc;
using StajProject.Business.Geo;
using StajProject.Business.Validation;

namespace StajProject.API.Controllers;

/// <summary>
/// Yönetim panelindeki controller'ların ortak hata yönetimi (Ödev 5 / Madde 1 kalıbı).
///
/// GeometryControllerBase'deki <c>Calistir</c> deseninin aynısı; tek fark
/// yakalanan iş kuralı tipinin <see cref="IsKuraliException"/> olması.
/// Kalıbı burada da tekrarlamak yerine tek yerde tutuyoruz: yönetim tarafında
/// 15'e yakın uç var, her birine ayrı try-catch yazmak "standart" değil
/// "kopyala-yapıştır" olurdu.
/// </summary>
public abstract class YonetimControllerBase : ControllerBase
{
    private readonly ILogger _logger;

    protected YonetimControllerBase(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Değer döndüren uçlar için try-catch kalıbı.</summary>
    protected async Task<ActionResult<T>> Calistir<T>(Func<Task<ActionResult<T>>> govde)
    {
        try
        {
            return await govde();
        }
        catch (WktFormatException ex)
        {
            // Yönetim uçlarının bir kısmı GEOMETRİ de alıyor (coğrafi yetki alanı,
            // POI konumu). Bozuk WKT sunucu hatası değil, gönderilen verinin
            // hatasıdır. Bu catch olmadan aşağıdaki genel Exception dalına
            // düşüp 500 dönerdi — üstelik ExceptionHandlingMiddleware'deki
            // doğru karşılığa (400) hiç ulaşamadan, çünkü controller onu
            // burada yutmuş oluyor.
            _logger.LogWarning(ex, "Geçersiz istek verisi");
            return BadRequest(new { message = ex.Message });
        }
        catch (IsKuraliException ex)
        {
            // İş kuralı ihlali → istemcinin hatası. Mesaj kullanıcıya gösterilebilir.
            _logger.LogWarning(ex, "İş kuralı ihlali");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Beklenmeyen bir hata oluştu." });
        }
    }

    /// <summary>Gövdesiz (204/404) dönen uçlar için aynı kalıp.</summary>
    protected async Task<IActionResult> Calistir(Func<Task<IActionResult>> govde)
    {
        try
        {
            return await govde();
        }
        catch (WktFormatException ex)
        {
            _logger.LogWarning(ex, "Geçersiz istek verisi");
            return BadRequest(new { message = ex.Message });
        }
        catch (IsKuraliException ex)
        {
            _logger.LogWarning(ex, "İş kuralı ihlali");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Beklenmeyen bir hata oluştu." });
        }
    }

    protected ActionResult Bulunamadi(int id)
        => NotFound(new { message = $"Id={id} olan kayıt bulunamadı." });

    protected IActionResult BulunamadiSonuc(int id)
        => NotFound(new { message = $"Id={id} olan kayıt bulunamadı." });
}
