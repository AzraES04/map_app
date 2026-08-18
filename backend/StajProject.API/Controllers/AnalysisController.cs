using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.API.Authorization;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Services;

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
    private readonly ILogger<AnalysisController> _logger;

    public AnalysisController(IAnalysisService analysisService, ILogger<AnalysisController> logger)
    {
        _analysisService = analysisService;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analiz sırasında beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Analiz yapılamadı. Lütfen tekrar deneyin." });
        }
    }
}
