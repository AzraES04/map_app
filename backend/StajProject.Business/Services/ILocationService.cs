using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

public interface ILocationService
{
    Task<List<LocationDto>> GetAllAsync();
    Task<LocationDto?> GetByIdAsync(int id);
    Task<LocationDto> CreateAsync(LocationCreateDto dto);
    Task<bool> DeleteAsync(int id);
}
