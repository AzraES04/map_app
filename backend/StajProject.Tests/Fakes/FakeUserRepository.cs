using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Bellekte çalışan sahte kullanıcı repository'si.
/// Not: AnyAsync() gerçek repository'de query filter'ı yok sayar (silinmişleri
/// de sayar); burada zaten tüm kayıtlara erişimimiz olduğu için davranış aynı.
///
/// Ödev 6 ile birlikte kullanıcı tablosu tek başına yetmiyor (rol ve yetki
/// atamaları da lazım), o yüzden ortak <see cref="SahteVeritabani"/> üzerinden
/// çalışıyor. Parametresiz kurulum eski testler için korunuyor.
/// </summary>
public class FakeUserRepository : IUserRepository
{
    private readonly SahteVeritabani _db;

    public FakeUserRepository() : this(new SahteVeritabani()) { }

    public FakeUserRepository(SahteVeritabani db)
    {
        _db = db;
    }

    private List<User> _store => _db.Kullanicilar;

    public Task<User?> GetByUsernameAsync(string username)
        => Task.FromResult(_store.FirstOrDefault(u => u.Username == username && !u.IsDeleted));

    public Task<User> AddAsync(User user)
    {
        user.Id = _db.SonrakiKullaniciId();
        _store.Add(user);
        return Task.FromResult(user);
    }

    public Task<bool> AnyAsync() => Task.FromResult(_store.Count > 0);

    public Task<bool> SoftDeleteAsync(int id)
    {
        var user = _store.FirstOrDefault(u => u.Id == id && !u.IsDeleted);
        if (user is null) return Task.FromResult(false);

        user.IsDeleted = true;
        user.IsActive = false;
        user.ModifiedDate = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    public Task<bool> SetActiveAsync(int id, bool isActive)
    {
        var user = _store.FirstOrDefault(u => u.Id == id && !u.IsDeleted);
        if (user is null) return Task.FromResult(false);

        user.IsActive = isActive;
        user.ModifiedDate = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    // ---------- Ödev 6: yönetim paneli ----------

    public Task<List<User>> GetAllAsync()
        => Task.FromResult(_store
            .Where(u => !u.IsDeleted)
            .OrderBy(u => u.Username)
            .Select(u => _db.KullaniciyiIliskileriyleGetir(u.Id)!)
            .ToList());

    public Task<User?> GetByIdAsync(int id)
        => Task.FromResult(_db.KullaniciyiIliskileriyleGetir(id));

    public Task<User?> UpdateAsync(User user)
    {
        var mevcut = _store.FirstOrDefault(u => u.Id == user.Id && !u.IsDeleted);
        if (mevcut is null) return Task.FromResult<User?>(null);

        mevcut.Username = user.Username;
        mevcut.PasswordHash = user.PasswordHash;
        mevcut.IsActive = user.IsActive;
        mevcut.ModifiedDate = DateTime.UtcNow;
        return Task.FromResult<User?>(mevcut);
    }

    public Task SetRolesAsync(int userId, IReadOnlyCollection<int> roleIds)
    {
        _db.KullaniciRolleri.RemoveAll(kr => kr.UserId == userId);
        foreach (var roleId in roleIds.Distinct())
        {
            _db.KullaniciRolleri.Add(new UserRole { UserId = userId, RoleId = roleId });
        }
        return Task.CompletedTask;
    }

    public Task SetPermissionsAsync(int userId, IReadOnlyCollection<int> permissionIds)
    {
        _db.KullaniciYetkileri.RemoveAll(ky => ky.UserId == userId);
        foreach (var permissionId in permissionIds.Distinct())
        {
            _db.KullaniciYetkileri.Add(new UserPermission { UserId = userId, PermissionId = permissionId });
        }
        return Task.CompletedTask;
    }
}
