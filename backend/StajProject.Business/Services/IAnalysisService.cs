using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// Mekânsal analiz iş katmanı (Ödev 4 / Görev 3).
/// Controller yalnızca bu arayüzü çağırır; WKT çözümleme, doğrulama ve
/// sonucun DTO'ya dönüştürülmesi burada yapılır.
/// </summary>
public interface IAnalysisService
{
    /// <summary>
    /// Verilen poligonla kesişen envanterleri bulur.
    /// "Kesişim" en ufak teması da kapsar; tamamen içinde olma şartı yoktur.
    /// </summary>
    Task<AnalysisResultDto> KesisimAnaliziAsync(AnalysisRequestDto request);
}
