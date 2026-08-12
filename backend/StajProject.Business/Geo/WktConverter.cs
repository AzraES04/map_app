using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace StajProject.Business.Geo;

/// <summary>
/// WKT (Well-Known Text) metni ile NetTopologySuite geometrisi arasındaki çeviri katmanı.
///
/// WKT, OGC'nin (Open Geospatial Consortium) tanımladığı, geometriyi İNSAN OKUYABİLİR
/// metin olarak ifade eden standarttır:
///
///   POINT (32.8597 39.9334)
///   LINESTRING (32.85 39.93, 32.86 39.94, 32.87 39.92)
///   POLYGON ((32.85 39.93, 32.86 39.93, 32.86 39.94, 32.85 39.93))
///
/// Koordinat sırası her zaman "X Y" yani "BOYLAM ENLEM"dir (longitude latitude).
/// Google Maps'in "enlem, boylam" sırasıyla karıştırmak en klasik hatadır.
/// </summary>
public static class WktConverter
{
    /// <summary>
    /// Projemizin veritabanı koordinat sistemi: EPSG:4326 (WGS84).
    /// Birim: derece. GPS'in, WKT'nin ve PostGIS kolonlarımızın ortak dili.
    /// </summary>
    public const int Srid = 4326;

    /// <summary>
    /// SRID'si baştan 4326 olarak ayarlanmış geometri fabrikası.
    /// Fabrika üzerinden üretilen her geometri bu SRID'yi miras alır.
    /// </summary>
    private static readonly GeometryFactory Factory =
        NtsGeometryServices.Instance.CreateGeometryFactory(Srid);

    /// <summary>
    /// WKT metnini istenen geometri tipine çevirir.
    /// Tip uyuşmazlığı veya bozuk metin durumunda <see cref="WktFormatException"/> fırlatır.
    /// </summary>
    /// <typeparam name="TGeometry">Beklenen tip: Point, LineString veya Polygon.</typeparam>
    public static TGeometry Read<TGeometry>(string? wkt) where TGeometry : Geometry
    {
        if (string.IsNullOrWhiteSpace(wkt))
        {
            throw new WktFormatException("WKT metni boş olamaz.");
        }

        Geometry parsed;
        try
        {
            // WKTReader'ı 4326'lık fabrikayla kuruyoruz ki ürettiği geometrinin SRID'si dolu olsun.
            parsed = new WKTReader(Factory.GeometryServices).Read(wkt);
        }
        catch (Exception ex)
        {
            throw new WktFormatException($"WKT metni çözümlenemedi: {ex.Message}", ex);
        }

        if (parsed is null || parsed.IsEmpty)
        {
            throw new WktFormatException("WKT metni boş bir geometri üretti.");
        }

        if (parsed is not TGeometry typed)
        {
            throw new WktFormatException(
                $"Beklenen geometri tipi {typeof(TGeometry).Name}, gelen ise {parsed.GeometryType}. " +
                "Her çizim tipi kendi endpoint'ine gönderilmelidir.");
        }

        // Güvence: WKT metninin içinde SRID bilgisi YOKTUR (o EWKT'nin işi).
        // PostGIS, geometry(Point, 4326) kolonuna SRID'si 0 olan bir geometri yazılmasına izin vermez.
        typed.SRID = Srid;

        if (!typed.IsValid)
        {
            throw new WktFormatException(
                "Geometri geçersiz (örn. kendini kesen poligon veya kapanmamış halka).");
        }

        return typed;
    }

    /// <summary>
    /// Geometriyi WKT metnine çevirir. Veritabanından okunan geometriyi
    /// istemciye göndermeden önce bu biçime dönüştürüyoruz.
    /// </summary>
    public static string Write(Geometry geometry) => geometry.AsText();
}

/// <summary>
/// WKT çözümleme/doğrulama hatası. Controller katmanı bunu yakalayıp
/// istemciye 400 Bad Request döner — 500 Internal Server Error DEĞİL,
/// çünkü hata sunucuda değil, gönderilen veridedir.
/// </summary>
public class WktFormatException : Exception
{
    public WktFormatException(string message) : base(message) { }
    public WktFormatException(string message, Exception inner) : base(message, inner) { }
}
