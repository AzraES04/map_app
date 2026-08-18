using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Tests.Fakes;

/// <summary>Bellekte çalışan sahte rol repository'si (Ödev 6).</summary>
public class FakeRoleRepository : IRoleRepository
{
    private readonly SahteVeritabani _db;

    public FakeRoleRepository(SahteVeritabani db)
    {
        _db = db;
    }

    public Task<List<Role>> GetAllAsync()
        => Task.FromResult(_db.Roller
            .Where(r => !r.IsDeleted)
            .OrderBy(r => r.Name)
            .Select(r => _db.RolüYetkileriyleGetir(r.Id)!)
            .ToList());

    public Task<Role?> GetByIdAsync(int id)
        => Task.FromResult(_db.RolüYetkileriyleGetir(id));

    public Task<Role?> GetByNameAsync(string name)
    {
        var rol = _db.Roller.FirstOrDefault(r => r.Name == name && !r.IsDeleted);
        return Task.FromResult(rol is null ? null : _db.RolüYetkileriyleGetir(rol.Id));
    }

    public Task<Role> AddAsync(Role role)
    {
        role.Id = _db.SonrakiRolId();
        _db.Roller.Add(role);
        return Task.FromResult(role);
    }

    public Task<Role?> UpdateAsync(Role role)
    {
        var mevcut = _db.Roller.FirstOrDefault(r => r.Id == role.Id && !r.IsDeleted);
        if (mevcut is null) return Task.FromResult<Role?>(null);

        mevcut.Name = role.Name;
        mevcut.Description = role.Description;
        mevcut.IsActive = role.IsActive;
        mevcut.ModifiedDate = DateTime.UtcNow;
        return Task.FromResult<Role?>(mevcut);
    }

    public Task<bool> SoftDeleteAsync(int id)
    {
        var rol = _db.Roller.FirstOrDefault(r => r.Id == id && !r.IsDeleted);
        if (rol is null) return Task.FromResult(false);

        rol.IsDeleted = true;
        rol.IsActive = false;
        rol.ModifiedDate = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    public Task SetPermissionsAsync(int roleId, IReadOnlyCollection<int> permissionIds)
    {
        _db.RolYetkileri.RemoveAll(ry => ry.RoleId == roleId);
        foreach (var permissionId in permissionIds.Distinct())
        {
            _db.RolYetkileri.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        }
        return Task.CompletedTask;
    }

    public Task<Dictionary<int, int>> GetUserCountsAsync()
        => Task.FromResult(_db.KullaniciRolleri
            .GroupBy(kr => kr.RoleId)
            .ToDictionary(g => g.Key, g => g.Count()));
}
