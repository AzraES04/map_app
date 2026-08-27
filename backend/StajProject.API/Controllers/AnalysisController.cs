using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.API.Authorization;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Services;
using StajProject.Business.Validation;
using StajProject.DataAccess.GeoServer;

namespace StajProject.API.Controllers;

/// <summary>
/// Mekânsal analiz uçları (Ödev 4 / Görev 3).
/// Controller yalnızca servisi çağırır — hesaplama, doğrulama, çeviri Business'ta.
/// </summary>
[ApiController]
[Route("api/analysis")]
[Authorize]
public class AnalysisController : ControllerBase
{
    private readonly IAnalysisService _analysisService;
    private readonly IKonumAnaliziService _konumAnaliziService;
    private readonly ILogger<AnalysisController> _logger;

    public AnalysisController(
        IAnalysisService analysisService,
        IKonumAnaliziService konumAnaliziService,
        ILogger<AnalysisController> logger)
    {
        _analysisService = analysisService;
        _konumAnaliziService = konumAnaliziService;
        _logger = logger;
    }

    /// <summary>
    /// Verilen poligonla kesişen envanterleri sayar ve listeler.
    /// Poligon veritabanına KAYDEDİLMEZ; sadece analiz için kullanılır.
    /// </summary>
    [HttpPost("intersect")]
    [YetkiGerekli(Yetkiler.AnalizCalistirma)]
    public async Task<ActionResult<AnalysisResultDto>> Intersect([FromBody] AnalysisRequestDto request)
    {
        // Ödev 5 / Madde 1: standart hata yönetimi kalıbı
        try
        {
            return Ok(await _analysisService.KesisimAnaliziAsync(request));
        }
        catch (WktFormatException ex)
        {
            _logger.LogWarning(ex, "Geçersiz analiz alanı");
            return BadRequest(new { message = ex.Message });
        }
        catch (GeoServerErisimException ex)
        {
            // Ödev 8: kesişim sorgusu da GeoServer'a (CQL INTERSECTS) gidiyor.
            _logger.LogError(ex, "GeoServer erişim hatası");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analiz sırasında beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Analiz yapılamadı. Lütfen tekrar deneyin." });
        }
    }

    /// <summary>
    /// KONUM ANALİZİ (Ödev 14).
    ///
    /// Hedef bölge (il listesi ya da çizilen poligon) ve 2–5 ağırlıklı kritere
    /// göre uygunluk yüzeyi üretir. Cevap bir ızgara: alanın her hücresi için
    /// 0–1 arası bir skor. Haritadaki renkli katman doğrudan bundan çiziliyor.
    ///
    /// YETKİ: <see cref="Yetkiler.AnalizCalistirma"/>. Bu yetki seed'de
    /// "Kullanıcı" rolüne de veriliyor — ödev metni analiz panelinin
    /// "Kullanıcı (User) rolünün de erişebileceği" olmasını istiyor ve o
    /// koşul zaten sağlanmış durumda (bkz. DatabaseSeeder.BaslangicRolleri).
    /// Ayrı bir yetki açmak, var olan rolleri tekrar düzenlemeyi gerektirir
    /// ve "analiz" adında iki farklı yetki bırakırdı.
    /// </summary>
    [HttpPost("konum")]
    [YetkiGerekli(Yetkiler.AnalizCalistirma)]
    public async Task<ActionResult<KonumAnaliziSonucuDto>> Konum(
        [FromBody] KonumAnaliziRequestDto request)
    {
        // Ödev 5 / Madde 1 kalıbı — Intersect ucundakiyle aynı.
        try
        {
            return Ok(await _konumAnaliziService.CalistirAsync(request));
        }
        catch (WktFormatException ex)
        {
            _logger.LogWarning(ex, "Geçersiz analiz alanı");
            return BadRequest(new { message = ex.Message });
        }
        catch (IsKuraliException ex)
        {
            // Kriter sayısı, ağırlık toplamı, alan seçimi — hepsi kullanıcının
            // düzeltebileceği hatalar. Mesaj doğrudan panelde gösteriliyor.
            _logger.LogWarning(ex, "Konum analizi kural ihlali");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Konum analizi sırasında beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Konum analizi yapılamadı. Lütfen tekrar deneyin." });
        }
    }
}
