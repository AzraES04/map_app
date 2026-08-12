using NetTopologySuite.Geometries;
using StajProject.Business.DTOs;
using StajProject.DataAccess.Repositories;
using Location = StajProject.Entities.Location;

namespace StajProject.Business.Services;

public class LocationService : ILocationService
{
    private readonly ILocationRepository _repository;

    // SRID 4326 = WGS84 (GPS koordinat sistemi)
    private static readonly GeometryFactory GeometryFactory =
        NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

    public LocationService(ILocationRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<LocationDto>> GetAllAsync()
    {
        var entities = await _repository.GetAllAsync();
        return entities.Select(MapToDto).ToList();
    }

    public async Task<LocationDto?> GetByIdAsync(int id)
    {
        var entity = await _repository.GetByIdAsync(id);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<LocationDto> CreateAsync(LocationCreateDto dto)
    {
        var entity = new Location
        {
            Name = dto.Name,
            Description = dto.Description,
            // Dikkat: Point(x, y) => x = longitude, y = latitude
            Geom = GeometryFactory.CreatePoint(new Coordinate(dto.Longitude, dto.Latitude)),
            CreatedAt = DateTime.UtcNow
        };

        var created = await _repository.AddAsync(entity);
        return MapToDto(created);
    }

    public Task<bool> DeleteAsync(int id) => _repository.DeleteAsync(id);

    private static LocationDto MapToDto(Location entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Description = entity.Description,
        Longitude = entity.Geom.X,
        Latitude = entity.Geom.Y,
        CreatedAt = entity.CreatedAt
    };
}
