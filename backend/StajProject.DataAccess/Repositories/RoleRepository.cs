using Microsoft.EntityFrameworkCore;
using StajProject.DataAccess.Context;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly AppDbContext _context;

    public RoleRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>Rol + yetkileri tek sorguda. Global query filter silinenleri zaten eliyor.</summary>
    private IQueryable<Role> YetkileriyleBirlikte()
        => _context.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission);

    public async Task<List<Role>> GetAllAsync()
        => await YetkileriyleBirlikte().OrderBy(r => r.Name).ToListAsync();

    public async Task<Role?> GetByIdAsync(int id)
        => await YetkileriyleBirlikte().FirstOrDefaultAsync(r => r.Id == id);

    public async Task<Role?> GetByNameAsync(string name)
        => await YetkileriyleBirlikte().FirstOrDefaultAsync(r => r.Name == name);

    public async Task<Role> AddAsync(Role role)
    {
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();
        return role;
    }

    public async Task<Role?> UpdateAsync(Role role)
    {
        var mevcut = await _context.Roles.FirstOrDefaultAsync(r => r.Id == role.Id);
        if (mevcut is null)
        {
            return null;
        }

        mevcut.Name = role.Name;
        mevcut.Description = role.Description;
        mevcut.IsActive = role.IsActive;

        await _context.SaveChangesAsync();   // ModifiedDate otomatik damgalanır
        return mevcut;
    }

    public async Task<bool> SoftDeleteAsync(int id)
    {
        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
        {
            return false;
        }

        role.IsDeleted = true;
        role.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task SetPermissionsAsync(int roleId, IReadOnlyCollection<int> permissionIds)
    {
        var mevcut = await _context.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync();

        // Listeden çıkarılanları sil
        _context.RolePermissions.RemoveRange(
            mevcut.Where(rp => !permissionIds.Contains(rp.PermissionId)));

        // Yeni işaretlenenleri ekle (zaten varsa dokunma: inserted_date korunsun)
        var mevcutIdler = mevcut.Select(rp => rp.PermissionId).ToHashSet();
        foreach (var permissionId in permissionIds.Distinct().Where(id => !mevcutIdler.Contains(id)))
        {
            _context.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId,
            });
        }

        await _context.SaveChangesAsync();
    }

    public async Task<Dictionary<int, int>> GetUserCountsAsync()
    {
        // Tek GROUP BY sorgusu: her rol için ayrı COUNT atmaktan kaçınıyoruz.
        // user_roles'un sorgu filtresi silinmiş kullanıcı/rolleri zaten dışarıda bırakıyor.
        return await _context.UserRoles
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Sayi = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Sayi);
    }
}
