using Microsoft.EntityFrameworkCore;
using StajProject.DataAccess.Context;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

public class LocationRepository : ILocationRepository
{
    private readonly AppDbContext _context;

    public LocationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Location>> GetAllAsync()
    {
        return await _context.Locations
            .AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task<Location?> GetByIdAsync(int id)
    {
        return await _context.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id);
    }

    public async Task<Location> AddAsync(Location location)
    {
        _context.Locations.Add(location);
        await _context.SaveChangesAsync();
        return location;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _context.Locations.FindAsync(id);
        if (entity is null)
        {
            return false;
        }

        _context.Locations.Remove(entity);
        await _context.SaveChangesAsync();
        return true;
    }
}
