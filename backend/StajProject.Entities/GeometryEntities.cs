using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace StajProject.Entities;

/// <summary>
/// Üç geometri tablosunun ortak sözleşmesi.
///
/// Neden arayüz? Repository / Service katmanlarını üç kez kopyalamak yerine
/// tek bir generic (jenerik) yapı yazacağız. O yapının "elindeki entity'nin
/// Id'si, adı ve durum bayrakları var" diyebilmesi için ortak bir tip lazım.
/// </summary>
public interface IGeometryEntity : IAuditableEntity
{
    int Id { get; set; }
    string Name { get; set; }
    string? Description { get; set; }
    DateTime CreatedAt { get; set; }

    // Durum kolonları (is_deleted / is_active / modified_date) IAuditableEntity'den geliyor.
}

/// <summary>
/// Ortak alanların tek yerde tanımı. Üç entity de bundan türüyor,
/// böylece aynı 7 property'yi üç kez yazmıyoruz.
/// Not: Geometry property'si burada DEĞİL — çünkü her tabloda tipi farklı
/// (Point / LineString / Polygon) ve EF'in bunu somut tiple görmesi gerekiyor.
/// </summary>
public abstract class GeometryEntityBase : IGeometryEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public DateTime? ModifiedDate { get; set; }

    /// <summary>
    /// Somut tipteki Geom alanına, tipini bilmeyen generic kodun erişebilmesi için köprü.
    /// [NotMapped] → EF bunu ayrı bir kolon sanmasın; asıl kolon her sınıftaki Geom.
    /// </summary>
    [NotMapped]
    public abstract Geometry Geometry { get; set; }
}

/// <summary>tbl_point → PostGIS geometry(Point, 4326)</summary>
public class PointEntity : GeometryEntityBase
{
    /// <summary>Tek bir konum: POINT(boylam enlem)</summary>
    public Point Geom { get; set; } = default!;

    public override Geometry Geometry
    {
        get => Geom;
        set => Geom = (Point)value;
    }
}

/// <summary>tbl_line → PostGIS geometry(LineString, 4326)</summary>
public class LineEntity : GeometryEntityBase
{
    /// <summary>
    /// Kırıklı çizgi: LINESTRING(b1 e1, b2 e2, ...). En az 2 nokta içermeli.
    /// Dikkat: OpenLayers ve OGC standardında tipin adı "Line" değil "LineString"tir;
    /// ödev metnindeki "Line" ile kastedilen budur, tablo adı ise tbl_line.
    /// </summary>
    public LineString Geom { get; set; } = default!;

    public override Geometry Geometry
    {
        get => Geom;
        set => Geom = (LineString)value;
    }
}

/// <summary>tbl_polygon → PostGIS geometry(Polygon, 4326)</summary>
public class PolygonEntity : GeometryEntityBase
{
    /// <summary>
    /// Kapalı alan: POLYGON((b1 e1, b2 e2, b3 e3, b1 e1)).
    /// İlk ve son koordinat AYNI olmak zorundadır (kapalı halka kuralı).
    /// </summary>
    public Polygon Geom { get; set; } = default!;

    public override Geometry Geometry
    {
        get => Geom;
        set => Geom = (Polygon)value;
    }
}
