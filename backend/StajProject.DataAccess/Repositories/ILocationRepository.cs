using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

public interface ILocationRepository
{
    Task<List<Location>> GetAllAsync();
    Task<Location?> GetByIdAsync(int id);
    Task<Location> AddAsync(Location location);
    Task<bool> DeleteAsync(int id);
}
