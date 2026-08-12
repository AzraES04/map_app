using System.ComponentModel.DataAnnotations;

namespace StajProject.Business.DTOs;

/// <summary>
/// İstemciye GİDEN geometri kaydı.
///
/// Dikkat: Geometri burada Longitude/Latitude çifti olarak DEĞİL, tek bir WKT metni
/// olarak taşınıyor. Sebep: nokta iki sayıyla ifade edilebilir ama çizgi ve poligon
/// değişken sayıda koordinat içerir. WKT üç tipi de tek bir string alanında,
/// standart ve okunabilir biçimde taşır.
/// </summary>
public class GeometryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Örn: "POINT (32.8597 39.9334)" — koordinatlar EPSG:4326.</summary>
    public string Wkt { get; set; } = string.Empty;

    /// <summary>Geometri tipi: "Point" | "LineString" | "Polygon".</summary>
    public string GeometryType { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>İstemciden GELEN yeni geometri isteği.</summary>
public class GeometryCreateDto
{
    [Required(ErrorMessage = "Ad alanı zorunludur.")]
    [MaxLength(200, ErrorMessage = "Ad en fazla 200 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000, ErrorMessage = "Açıklama en fazla 1000 karakter olabilir.")]
    public string? Description { get; set; }

    /// <summary>
    /// EPSG:4326 koordinatlarıyla WKT metni.
    /// Frontend haritadan (EPSG:3857) aldığı geometriyi bu formata çevirip gönderir.
    /// </summary>
    [Required(ErrorMessage = "WKT alanı zorunludur.")]
    public string Wkt { get; set; } = string.Empty;
}

/// <summary>Var olan bir geometrinin adı/açıklaması güncellenirken kullanılır.</summary>
public class GeometryUpdateDto
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Boş bırakılırsa geometri değişmez, sadece ad/açıklama güncellenir.</summary>
    public string? Wkt { get; set; }
}
