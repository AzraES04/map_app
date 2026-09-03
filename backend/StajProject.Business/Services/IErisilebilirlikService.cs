using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// Toplu taşıma erişilebilirlik analizinin iş katmanı.
/// Gerekçe ve yöntem <see cref="Analiz.TopluTasimaErisilebilirligi"/> başlığında.
/// </summary>
public interface IErisilebilirlikService
{
    Task<ErisilebilirlikSonucuDto> CalistirAsync(ErisilebilirlikRequestDto istek);
}
