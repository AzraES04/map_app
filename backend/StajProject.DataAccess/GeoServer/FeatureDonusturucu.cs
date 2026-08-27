using StajProject.Entities;

namespace StajProject.DataAccess.GeoServer;

/// <summary>
/// WFS kaydını (<see cref="GeoServerFeature"/>) geometri entity'sine çevirir.
///
/// Ayrı bir sınıf çünkü iki yerden kullanılıyor: listeleme yapan
/// <c>GeoServerGeometryRepository</c> ve kesişim analizi yapan
/// <c>GeoServerAnalysisRepository</c>. Aynı 15 satırı iki kez yazmak yerine
/// tek yerde tutuyoruz — kolon adlarından biri değişirse tek dosya güncellenir.
/// </summary>
public static class FeatureDonusturucu
{
    /// <summary>
    /// Kolon adları veritabanındakiyle aynıdır (snake_case): GeoServer tabloyu
    /// olduğu gibi yayınlar, EF'in C# property adlarından haberi yoktur.
    /// </summary>
    /// <param name="katman">Hata mesajlarında görünsün diye — örn. "tbl_point".</param>
    public static TEntity Cevir<TEntity>(GeoServerFeature feature, string katman)
        where TEntity : GeometryEntityBase, new()
    {
        if (feature.Geometry is null)
        {
            throw new GeoServerErisimException(
                $"{katman} katmanında id={feature.Id} kaydının geometrisi boş geldi.");
        }

        var entity = new TEntity
        {
            Id = feature.Id,
            Name = feature.Metin("name") ?? string.Empty,
            Description = feature.Metin("description"),
            ImageUrl = feature.Metin("image_url"),
            Color = feature.Metin("color"),
            InsertedDate = feature.Tarih("inserted_date") ?? default,
            InsertedUserId = feature.Tamsayi("inserted_user_id"),
            ModifiedDate = feature.Tarih("modified_date"),
            IsDeleted = feature.Mantiksal("is_deleted"),
            IsActive = feature.Mantiksal("is_active", varsayilan: true),
        };

        try
        {
            // GeometryEntityBase.Geometry setter'ı somut tipe çeviriyor
            // (örn. (Point)value). Katman/tablo eşlemesi bozuksa burada patlar.
            entity.Geometry = feature.Geometry;
        }
        catch (InvalidCastException ex)
        {
            throw new GeoServerErisimException(
                $"{katman} katmanından beklenmeyen geometri tipi geldi: " +
                $"{feature.Geometry.GeometryType}. GeoServer katmanı doğru tabloya mı bağlı?", ex);
        }

        return entity;
    }
}
