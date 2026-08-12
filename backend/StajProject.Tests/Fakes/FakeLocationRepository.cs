using StajProject.DataAccess.Repositories;
using Location = StajProject.Entities.Location;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Gerçek veritabanı yerine bellekte çalışan sahte repository.
/// Birim testlerinde Business katmanını izole test etmek için kullanılır.
/// </summary>
public class FakeLocationRepository : ILocationRepository
{
    private readonly List<Location> _store = new();
    private int _nextId = 1;

    public Task<List<Location>> GetAllAsync()
        => Task.FromResult(_store.OrderByDescending(l => l.CreatedAt).ToList());

    public Task<Location?> GetByIdAsync(int id)
        => Task.FromResult(_store.FirstOrDefault(l => l.Id == id));

    public Task<Location> AddAsync(Location location)
    {
        location.Id = _nextId++;
        _store.Add(location);
        return Task.FromResult(location);
    }

    public Task<bool> DeleteAsync(int id)
    {
        var entity = _store.FirstOrDefault(l => l.Id == id);
        if (entity is null) return Task.FromResult(false);
        _store.Remove(entity);
        return Task.FromResult(true);
    }
}
