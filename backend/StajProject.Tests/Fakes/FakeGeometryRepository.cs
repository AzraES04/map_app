using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Bellekte çalışan sahte geometri repository'si.
/// GeometryService'i veritabanından bağımsız test etmek için kullanılır.
/// </summary>
public class FakeGeometryRepository<TEntity> : IGeometryRepository<TEntity>
    where TEntity : GeometryEntityBase
{
    private readonly List<TEntity> _store = new();
    private int _nextId = 1;

    public Task<List<TEntity>> GetAllAsync()
        => Task.FromResult(_store.Where(e => !e.IsDeleted)
                                 .OrderByDescending(e => e.CreatedAt)
                                 .ToList());

    public Task<TEntity?> GetByIdAsync(int id)
        => Task.FromResult(_store.FirstOrDefault(e => e.Id == id && !e.IsDeleted));

    public Task<TEntity> AddAsync(TEntity entity)
    {
        entity.Id = _nextId++;
        _store.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<TEntity?> UpdateAsync(TEntity entity)
    {
        var current = _store.FirstOrDefault(e => e.Id == entity.Id && !e.IsDeleted);
        if (current is null) return Task.FromResult<TEntity?>(null);

        current.Name = entity.Name;
        current.Description = entity.Description;
        current.ImageUrl = entity.ImageUrl;
        current.Geometry = entity.Geometry;
        current.ModifiedDate = DateTime.UtcNow;   // gerçekte AppDbContext yapıyor
        return Task.FromResult<TEntity?>(current);
    }

    public Task<bool> SoftDeleteAsync(int id)
    {
        var entity = _store.FirstOrDefault(e => e.Id == id && !e.IsDeleted);
        if (entity is null) return Task.FromResult(false);

        entity.IsDeleted = true;
        entity.IsActive = false;
        return Task.FromResult(true);
    }

    public Task<bool> RestoreAsync(int id)
    {
        // Gerçek repository IgnoreQueryFilters() kullanıyor; burada zaten
        // tüm kayıtlara erişimimiz var, silinmiş olanı da buluyoruz.
        var entity = _store.FirstOrDefault(e => e.Id == id);
        if (entity is null || !entity.IsDeleted) return Task.FromResult(false);

        entity.IsDeleted = false;
        entity.IsActive = true;
        return Task.FromResult(true);
    }
}
