using NetTopologySuite.Geometries;

namespace StajProject.Entities;

/// <summary>
/// PostGIS geometry (Point) alanı içeren örnek konum entity'si.
/// </summary>
public class Location
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// PostGIS "geometry(Point, 4326)" kolonu — WGS84 koordinat sistemi.
    /// </summary>
    public Point Geom { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
