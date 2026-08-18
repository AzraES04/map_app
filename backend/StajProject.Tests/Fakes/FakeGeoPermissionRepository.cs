using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Bellekte çalışan sahte coğrafi yetki repository'si (Ödev 7 / Madde 2).
///
/// Ortak <see cref="SahteVeritabani"/> üzerinden çalışıyor: "role alan tanımla,
/// o roldeki kullanıcının alanını sor" senaryosu ancak kullanıcı-rol
/// atamalarına da erişebilirsek test edilebilir.
/// </summary>
public class FakeGeoPermissionRepository : IGeoPermissionRepository
{
    private readonly SahteVeritabani _db;

    public FakeGeoPermissionRepository(SahteVeritabani db)
    {
        _db = db;
    }

    private List<GeoPermission> Yasayanlar
        => _db.CografiYetkiler.Where(g => !g.IsDeleted).ToList();

    public Task<List<GeoPermission>> GetAllAsync()
        => Task.FromResult(Yasayanlar.OrderBy(g => g.Id).ToList());

    public Task<List<GeoPermission>> GetByUserAsync(int userId)
        => Task.FromResult(Yasayanlar.Where(g => g.UserId == userId).ToList());

    public Task<List<GeoPermission>> GetByRoleAsync(int roleId)
        => Task.FromResult(Yasayanlar.Where(g => g.RoleId == roleId).ToList());

    public Task<List<GeoPermission>> GetEffectiveForUserAsync(int userId)
    {
        // Gerçek repository ile aynı kural: kullanıcının kendi alanları +
        // AKTİF rollerinin alanları; pasif alan tanımları sayılmaz.
        var aktifRolIdleri = _db.KullaniciRolleri
            .Where(kr => kr.UserId == userId)
            .Select(kr => kr.RoleId)
            .Where(rolId => _db.Roller.Any(r => r.Id == rolId && r.IsActive && !r.IsDeleted))
            .ToHashSet();

        return Task.FromResult(Yasayanlar
            .Where(g => g.IsActive &&
                        (g.UserId == userId || (g.RoleId is not null && aktifRolIdleri.Contains(g.RoleId.Value))))
            .OrderBy(g => g.Id)
            .ToList());
    }

    public Task<GeoPermission?> GetByIdAsync(int id)
        => Task.FromResult(Yasayanlar.FirstOrDefault(g => g.Id == id));

    public Task<GeoPermission> AddAsync(GeoPermission kayit)
    {
        kayit.Id = _db.SonrakiCografiYetkiId();
        _db.CografiYetkiler.Add(kayit);
        return Task.FromResult(kayit);
    }

    public Task<bool> SoftDeleteAsync(int id)
    {
        var kayit = Yasayanlar.FirstOrDefault(g => g.Id == id);
        if (kayit is null) return Task.FromResult(false);

        kayit.IsDeleted = true;
        kayit.IsActive = false;
        return Task.FromResult(true);
    }
}
