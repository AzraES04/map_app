using System.ComponentModel.DataAnnotations;

namespace StajProject.Business.DTOs;

/// <summary>Analiz isteği: EPSG:4326 WKT biçiminde bir poligon.</summary>
public class AnalysisRequestDto
{
    [Required(ErrorMessage = "Analiz alanı (WKT) zorunludur.")]
    public string Wkt { get; set; } = string.Empty;

    /// <summary>
    /// Kaydedilmiş bir poligonun analizi yapılıyorsa kendi id'si.
    /// Kendisini "kesişen envanter" olarak saymamak için hariç tutulur.
    /// </summary>
    public int? HaricTutulanPolygonId { get; set; }
}

/// <summary>Analiz sonucunda bulunan tek bir envanter kaydı.</summary>
public class AnalysisItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>"Point" | "LineString" | "Polygon"</summary>
    public string GeometryType { get; set; } = string.Empty;

    public string Wkt { get; set; } = string.Empty;
    public string? Color { get; set; }
}

/// <summary>Analiz sonucu: tip bazında sayılar + bulunan kayıtlar.</summary>
public class AnalysisResultDto
{
    public int PointCount { get; set; }
    public int LineCount { get; set; }
    public int PolygonCount { get; set; }

    /// <summary>Üç tipin toplamı — kullanıcıya gösterilen ana sayı.</summary>
    public int Total => PointCount + LineCount + PolygonCount;

    public List<AnalysisItemDto> Items { get; set; } = new();
}
