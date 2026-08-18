using Microsoft.EntityFrameworkCore;
using StajProject.DataAccess.Context;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

public class PermissionRepository : IPermissionRepository
{
    private readonly AppDbContext _context;

    public PermissionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Permission>> GetAllAsync()
        => await _context.Permissions
            .AsNoTracking()
            .OrderBy(p => p.Id)      // seed sırası = mantıklı okuma sırası (önce çizim, sonra yönetim)
            .ToListAsync();

    public async Task<List<int>> GetExistingIdsAsync(IReadOnlyCollection<int> ids)
        => await _context.Permissions
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync();

    public async Task<Permission?> GetByNameAsync(string name)
        => await _context.Permissions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == name);

    public async Task<Permission> AddAsync(Permission permission)
    {
        _context.Permissions.Add(permission);
        await _context.SaveChangesAsync();
        return permission;
    }
}
