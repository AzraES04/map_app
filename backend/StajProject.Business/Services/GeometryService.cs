using NetTopologySuite.Geometries;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

/// <summary>
/// Üç geometri tipinin ortak iş mantığı.
///
/// İki tip parametresi var:
///   TEntity   → hangi tabloya yazılacağı (PointEntity / LineEntity / PolygonEntity)
///   TGeometry → o tablonun kabul ettiği NTS geometri tipi (Point / LineString / Polygon)
///
/// TGeometry sayesinde "tbl_point'e POLYGON gönderilmesi" gibi hataları çevirinin
/// tam o anında yakalayıp anlamlı bir hata mesajı üretebiliyoruz.
/// </summary>
public class GeometryService<TEntity, TGeometry> : IGeometryService<TEntity>
    where TEntity : GeometryEntityBase, new()
    where TGeometry : Geometry
{
    private readonly IGeometryRepository<TEntity> _repository;

    public GeometryService(IGeometryRepository<TEntity> repository)
    {
        _repository = repository;
    }

    public async Task<List<GeometryDto>> GetAllAsync()
    {
        var entities = await _repository.GetAllAsync();
        return entities.Select(MapToDto).ToList();
    }

    public async Task<GeometryDto?> GetByIdAsync(int id)
    {
        var entity = await _repository.GetByIdAsync(id);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<GeometryDto> CreateAsync(GeometryCreateDto dto)
    {
        // 1) WKT metnini geometriye çevir + tipini doğrula + SRID'sini 4326 yap.
        //    Hatalıysa WktFormatException fırlar, controller onu 400'e çevirir.
        var geometry = WktConverter.Read<TGeometry>(dto.Wkt);

        // 2) Entity'yi kur. new() kısıtı sayesinde generic tipten nesne üretebiliyoruz.
        var entity = new TEntity
        {
            Name = dto.Name,
            Description = dto.Description,
            Geometry = geometry,
            CreatedAt = DateTime.UtcNow
        };

        var created = await _repository.AddAsync(entity);
        return MapToDto(created);
    }

    public async Task<GeometryDto?> UpdateAsync(int id, GeometryUpdateDto dto)
    {
        var existing = await _repository.GetByIdAsync(id);
        if (existing is null)
        {
            return null;
        }

        existing.Name = dto.Name;
        existing.Description = dto.Description;

        // Wkt boş gönderildiyse geometriye dokunmuyoruz — sadece ad/açıklama güncellenir.
        if (!string.IsNullOrWhiteSpace(dto.Wkt))
        {
            existing.Geometry = WktConverter.Read<TGeometry>(dto.Wkt);
        }

        var updated = await _repository.UpdateAsync(existing);
        return updated is null ? null : MapToDto(updated);
    }

    public Task<bool> DeleteAsync(int id) => _repository.SoftDeleteAsync(id);

    /// <summary>Entity → DTO. Geometri burada WKT metnine dönüşür.</summary>
    private static GeometryDto MapToDto(TEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Description = entity.Description,
        Wkt = WktConverter.Write(entity.Geometry),
        GeometryType = entity.Geometry.GeometryType,
        CreatedAt = entity.CreatedAt,
        ModifiedDate = entity.ModifiedDate,
        IsActive = entity.IsActive
    };
}
