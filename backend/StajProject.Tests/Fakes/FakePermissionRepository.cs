using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Tests.Fakes;

/// <summary>Bellekte çalışan sahte yetki repository'si (Ödev 6).</summary>
public class FakePermissionRepository : IPermissionRepository
{
    private readonly SahteVeritabani _db;

    public FakePermissionRepository(SahteVeritabani db)
    {
        _db = db;
    }

    public Task<List<Permission>> GetAllAsync()
        => Task.FromResult(_db.Yetkiler.Where(y => !y.IsDeleted).OrderBy(y => y.Id).ToList());

    public Task<List<int>> GetExistingIdsAsync(IReadOnlyCollection<int> ids)
        => Task.FromResult(_db.Yetkiler
            .Where(y => !y.IsDeleted && ids.Contains(y.Id))
            .Select(y => y.Id)
            .ToList());

    public Task<Permission?> GetByNameAsync(string name)
        => Task.FromResult(_db.Yetkiler.FirstOrDefault(y => y.Name == name && !y.IsDeleted));

    public Task<Permission> AddAsync(Permission permission)
    {
        permission.Id = _db.SonrakiYetkiId();
        _db.Yetkiler.Add(permission);
        return Task.FromResult(permission);
    }
}
