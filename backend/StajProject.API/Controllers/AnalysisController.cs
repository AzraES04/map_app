using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.Business.DTOs;
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

    public AnalysisController(IAnalysisService analysisService)
    {
        _analysisService = analysisService;
    }

    /// <summary>
    /// Verilen poligonla kesişen envanterleri sayar ve listeler.
    /// Poligon veritabanına KAYDEDİLMEZ; sadece analiz için kullanılır.
    /// </summary>
    [HttpPost("intersect")]
    public async Task<ActionResult<AnalysisResultDto>> Intersect([FromBody] AnalysisRequestDto request)
    {
        return Ok(await _analysisService.KesisimAnaliziAsync(request));
    }
}
