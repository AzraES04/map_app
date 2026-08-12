namespace StajProject.Entities;

/// <summary>
/// "Sistem durum takibi" yapılan her tablonun ortak sözleşmesi (Ödev 3 / Görev 1).
///
/// Bu arayüzü uygulayan her entity'de:
///   - is_deleted     → soft delete bayrağı
///   - is_active      → kayıt kullanılabilir durumda mı
///   - modified_date  → son güncelleme damgası (AppDbContext otomatik doldurur)
/// kolonları bulunur.
///
/// Arayüzün asıl faydası: AppDbContext.ApplyAuditRules() tek bir döngüde
/// "IAuditableEntity olan her şeyi" damgalayabiliyor — User, PointEntity,
/// LineEntity, PolygonEntity... Yarın 5. tabloyu eklediğimizde de kod değişmeyecek.
/// </summary>
public interface IAuditableEntity
{
    bool IsDeleted { get; set; }
    bool IsActive { get; set; }
    DateTime? ModifiedDate { get; set; }
}
