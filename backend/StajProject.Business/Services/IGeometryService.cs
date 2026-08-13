using StajProject.Business.DTOs;
using StajProject.Entities;

namespace StajProject.Business.Services;

/// <summary>
/// Geometri iş katmanı sözleşmesi. Controller'lar entity görmez, sadece DTO görür;
/// WKT ↔ geometri çevirisi ve doğrulama bu katmanın sorumluluğudur.
/// </summary>
public interface IGeometryService<TEntity> where TEntity : GeometryEntityBase
{
    Task<List<GeometryDto>> GetAllAsync();
    Task<GeometryDto?> GetByIdAsync(int id);
    Task<GeometryDto> CreateAsync(GeometryCreateDto dto);
    Task<GeometryDto?> UpdateAsync(int id, GeometryUpdateDto dto);
    Task<bool> DeleteAsync(int id);

    /// <summary>Soft delete edilmiş kaydı geri getirir ("Geri al" işlemi).</summary>
    Task<bool> RestoreAsync(int id);
}
