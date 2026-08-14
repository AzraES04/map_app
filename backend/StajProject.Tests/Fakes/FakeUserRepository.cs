using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Bellekte çalışan sahte kullanıcı repository'si.
/// Not: AnyAsync() gerçek repository'de query filter'ı yok sayar (silinmişleri
/// de sayar); burada zaten tüm kayıtlara erişimimiz olduğu için davranış aynı.
/// </summary>
public class FakeUserRepository : IUserRepository
{
    private readonly List<User> _store = new();
    private int _nextId = 1;

    public Task<User?> GetByUsernameAsync(string username)
        => Task.FromResult(_store.FirstOrDefault(u => u.Username == username && !u.IsDeleted));

    public Task<User> AddAsync(User user)
    {
        user.Id = _nextId++;
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
}
