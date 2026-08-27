namespace StajProject.Business.DTOs;

// ============================================================================
//  Ödev 10 — il ve bölge DTO'ları
//
//  Coğrafi yetki artık üç yoldan tanımlanabiliyor: elle çizim, il seçimi,
//  bölge seçimi. Son ikisi için arayüzün illeri tanıması gerekiyor.
//
//  Geometri yine WKT metni olarak taşınıyor — projede geometri transferinin
//  tek dili bu (bkz. GeometryDto, GeoPermissionDto). Böylece harita tarafı
//  aynı dönüşüm yolunu (geo.js) kullanabiliyor.
// ============================================================================

/// <summary>Geometrisiz il bilgisi — listeler ve seçim kutuları için.</summary>
public class IlOzetDto
{
    /// <summary>Plaka kodu (1–81). Aynı zamanda birincil anahtar.</summary>
    public int Plaka { get; set; }

    public string Ad { get; set; } = string.Empty;

    /// <summary>Coğrafi bölge — "Karadeniz", "İç Anadolu"...</summary>
    public string Bolge { get; set; } = string.Empty;
}

/// <summary>
/// Sınır geometrisiyle birlikte il. Harita üzerinde tıklanabilir seçim
/// katmanını bu besliyor.
/// </summary>
public class IlSinirDto : IlOzetDto
{
    /// <summary>İl sınırı, EPSG:4326 WKT. Çoğu POLYGON, 17'si MULTIPOLYGON.</summary>
    public string Wkt { get; set; } = string.Empty;
}

/// <summary>Bölge listesi — kaç il içerdiğiyle birlikte.</summary>
public class BolgeOzetDto
{
    public string Ad { get; set; } = string.Empty;

    /// <summary>Bu bölgedeki il sayısı; arayüzde "Ege · 8 il" diye gösteriliyor.</summary>
    public int IlSayisi { get; set; }
}
