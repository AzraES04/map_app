using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// Üç geometri tablosunun ortak veri erişim sözleşmesi.
///
/// Generic (jenerik) olmasının sebebi: tbl_point, tbl_line ve tbl_polygon için
/// yapılacak işlemler birebir aynı (listele, id ile getir, ekle, güncelle, soft delete).
/// Üç ayrı repository yazmak ~150 satır kopyala-yapıştır demek olurdu; kopyalanan kodda
/// bir hata düzeltmek istediğinde üç yeri birden düzeltmen gerekirdi.
/// </summary>
/// <typeparam name="TEntity">PointEntity, LineEntity veya PolygonEntity.</typeparam>
public interface IGeometryRepository<TEntity> where TEntity : GeometryEntityBase
{
    Task<List<TEntity>> GetAllAsync();
    Task<TEntity?> GetByIdAsync(int id);
    Task<TEntity> AddAsync(TEntity entity);
    Task<TEntity?> UpdateAsync(TEntity entity);

    /// <summary>Fiziksel silme değil; is_deleted bayrağını kaldırır.</summary>
    Task<bool> SoftDeleteAsync(int id);

    /// <summary>
    /// Soft delete edilmiş kaydı geri getirir. Soft delete'in asıl faydası budur:
    /// veri hâlâ tabloda durduğu için silme işlemi tek UPDATE ile geri alınabilir.
    /// </summary>
    Task<bool> RestoreAsync(int id);
}
