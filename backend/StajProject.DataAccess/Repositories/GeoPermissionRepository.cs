using Microsoft.EntityFrameworkCore;
using StajProject.DataAccess.Context;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

public class GeoPermissionRepository : IGeoPermissionRepository
{
    private readonly AppDbContext _context;

    public GeoPermissionRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>Sahip bilgisi listede gösterileceği için ilişkiler dâhil ediliyor.</summary>
    private IQueryable<GeoPermission> SahibiyleBirlikte()
        => _context.GeoPermissions
            .AsNoTracking()
            .Include(g => g.User)
            .Include(g => g.Role);

    public async Task<List<GeoPermission>> GetAllAsync()
        => await SahibiyleBirlikte().OrderBy(g => g.Id).ToListAsync();

    public async Task<List<GeoPermission>> GetByUserAsync(int userId)
        => await SahibiyleBirlikte().Where(g => g.UserId == userId).OrderBy(g => g.Id).ToListAsync();

    public async Task<List<GeoPermission>> GetByRoleAsync(int roleId)
        => await SahibiyleBirlikte().Where(g => g.RoleId == roleId).OrderBy(g => g.Id).ToListAsync();

    public async Task<List<GeoPermission>> GetEffectiveForUserAsync(int userId)
    {
        // Kullanıcının AKTİF rollerinin id'leri.
        // user_roles'un sorgu filtresi silinmiş kullanıcı/rolleri zaten eliyor;
        // pasif rolü de burada ayıklıyoruz — pasif rol yetki üretmiyorsa
        // coğrafi kısıt da üretmemeli, iki kural tutarlı olmalı.
        var rolIdleri = _context.UserRoles
            .Where(ur => ur.UserId == userId && ur.Role!.IsActive)
            .Select(ur => ur.RoleId);

        return await _context.GeoPermissions
            .AsNoTracking()
            .Where(g => g.IsActive &&
                        (g.UserId == userId || (g.RoleId != null && rolIdleri.Contains(g.RoleId.Value))))
            .OrderBy(g => g.Id)
            .ToListAsync();
    }

    public async Task<GeoPermission?> GetByIdAsync(int id)
        => await SahibiyleBirlikte().FirstOrDefaultAsync(g => g.Id == id);

    public async Task<GeoPermission> AddAsync(GeoPermission kayit)
    {
        _context.GeoPermissions.Add(kayit);
        await _context.SaveChangesAsync();
        return kayit;
    }

    public async Task<bool> SoftDeleteAsync(int id)
    {
        var kayit = await _context.GeoPermissions.FirstOrDefaultAsync(g => g.Id == id);
        if (kayit is null)
        {
            return false;
        }

        kayit.IsDeleted = true;
        kayit.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }
}
