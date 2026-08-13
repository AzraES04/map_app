using Microsoft.EntityFrameworkCore;
using StajProject.DataAccess.Context;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// <see cref="IGeometryRepository{TEntity}"/> arayüzünün tek ve ortak gerçeklemesi.
/// DI konteynerinde üç kez, üç farklı tip argümanıyla kaydedilir:
///   IGeometryRepository&lt;PointEntity&gt;   → GeometryRepository&lt;PointEntity&gt;
///   IGeometryRepository&lt;LineEntity&gt;    → GeometryRepository&lt;LineEntity&gt;
///   IGeometryRepository&lt;PolygonEntity&gt; → GeometryRepository&lt;PolygonEntity&gt;
/// </summary>
public class GeometryRepository<TEntity> : IGeometryRepository<TEntity>
    where TEntity : GeometryEntityBase
{
    private readonly AppDbContext _context;

    public GeometryRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// _context.Set&lt;TEntity&gt;() → "bu tipin DbSet'ini ver".
    /// Böylece Points/Lines/Polygons özelliklerinden hangisi olduğunu bilmemize gerek kalmıyor.
    /// </summary>
    private DbSet<TEntity> Table => _context.Set<TEntity>();

    public async Task<List<TEntity>> GetAllAsync()
    {
        // Global query filter (!IsDeleted) burada otomatik uygulanır: silinenler gelmez.
        return await Table
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();
    }

    public async Task<TEntity?> GetByIdAsync(int id)
    {
        return await Table
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<TEntity> AddAsync(TEntity entity)
    {
        Table.Add(entity);
        await _context.SaveChangesAsync();
        return entity;   // SaveChanges sonrası entity.Id veritabanının verdiği değerle dolar
    }

    public async Task<TEntity?> UpdateAsync(TEntity entity)
    {
        // AsNoTracking YOK: değiştirip kaydedeceğimiz için EF'in nesneyi takip etmesi gerekiyor.
        var current = await Table.FirstOrDefaultAsync(e => e.Id == entity.Id);
        if (current is null)
        {
            return null;
        }

        current.Name = entity.Name;
        current.Description = entity.Description;
        current.ImageUrl = entity.ImageUrl;
        current.Color = entity.Color;
        current.Geometry = entity.Geometry;

        // ModifiedDate'i elle yazmıyoruz — AppDbContext.ApplyAuditRules() otomatik basıyor.
        await _context.SaveChangesAsync();
        return current;
    }

    public async Task<bool> SoftDeleteAsync(int id)
    {
        var entity = await Table.FirstOrDefaultAsync(e => e.Id == id);
        if (entity is null)
        {
            return false;   // yok, ya da zaten silinmiş (query filter gizliyor)
        }

        entity.IsDeleted = true;
        entity.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(int id)
    {
        // DİKKAT — buradaki IgnoreQueryFilters() olmazsa olmaz.
        // Global query filter (!IsDeleted) yüzünden normal sorgu silinmiş kaydı
        // GÖREMEZ; onu geri getirebilmek için filtreyi bilinçli olarak aşıyoruz.
        // Bu, filtrenin doğru kullanımına iyi bir örnek: kural varsayılan, istisna açık.
        var entity = await Table
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id);

        if (entity is null || !entity.IsDeleted)
        {
            return false;   // kayıt yok ya da zaten silinmemiş
        }

        entity.IsDeleted = false;
        entity.IsActive = true;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetActiveAsync(int id, bool isActive)
    {
        // Burada IgnoreQueryFilters YOK: silinmiş bir kaydın aktifliğini
        // değiştirmek anlamsız olurdu, önce geri alınması gerekir.
        var entity = await Table.FirstOrDefaultAsync(e => e.Id == id);
        if (entity is null)
        {
            return false;
        }

        entity.IsActive = isActive;
        await _context.SaveChangesAsync();   // ModifiedDate otomatik damgalanır
        return true;
    }
}
