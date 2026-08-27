using System.ComponentModel.DataAnnotations;

namespace StajProject.Business.DTOs;

// ============================================================================
//  Ödev 7 / Madde 2 — Coğrafi yetki DTO'ları
//
//  Geometri dışarıya yine WKT metni olarak taşınıyor: projede geometri
//  transferinin tek dili bu (bkz. GeometryDto). Böylece admin panelindeki
//  harita, çizim ekranıyla aynı dönüşüm yolunu (geo.js) kullanabiliyor.
// ============================================================================

/// <summary>İstemciye giden coğrafi yetki tanımı.</summary>
public class GeoPermissionDto
{
    public int Id { get; set; }

    /// <summary>Etiket — "Ankara ve çevresi".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Alan bir kullanıcıya aitse dolu.</summary>
    public int? UserId { get; set; }
    public string? Username { get; set; }

    /// <summary>Alan bir role aitse dolu.</summary>
    public int? RoleId { get; set; }
    public string? RoleName { get; set; }

    /// <summary>İzinli alan, EPSG:4326 WKT olarak.</summary>
    public string Wkt { get; set; } = string.Empty;

    public DateTime InsertedDate { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Yeni coğrafi yetki isteği.
///
/// İKİ AYRI "yalnızca biri" kuralı var:
///   • SAHİP  : <see cref="UserId"/> ya da <see cref="RoleId"/>
///   • ALANIN TANIMI (Ödev 10): <see cref="Wkt"/>, <see cref="IlPlakalari"/>
///     ya da <see cref="Bolge"/>
///
/// Alanın nasıl tanımlandığı SAKLANMIYOR; üçü de sonuçta aynı şeye —
/// bir geometriye — dönüşüyor ve <c>geo_permissions.geom</c> kolonuna aynı
/// biçimde yazılıyor. Böylece kuralı uygulayan kod (DogrulaAsync) alanın
/// nereden geldiğini hiç bilmek zorunda kalmıyor.
/// </summary>
public class GeoPermissionCreateDto
{
    [Required(ErrorMessage = "Alan adı zorunludur.")]
    [MaxLength(200, ErrorMessage = "Alan adı en fazla 200 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    public int? UserId { get; set; }

    public int? RoleId { get; set; }

    /// <summary>
    /// ELLE ÇİZİM: haritada çizilen poligonun WKT karşılığı (EPSG:4326).
    /// İl/bölge seçildiyse boş bırakılır.
    /// </summary>
    public string? Wkt { get; set; }

    /// <summary>
    /// İL SEÇİMİ (Ödev 10): seçilen illerin plaka kodları. Birden fazla il
    /// seçilebilir; alan hepsinin birleşimi olur.
    /// </summary>
    public List<int>? IlPlakalari { get; set; }

    /// <summary>
    /// BÖLGE SEÇİMİ (Ödev 10 · Ödev 11'de çoklu): "Ege", "Karadeniz"...
    /// Alan, seçilen bölgelerdeki TÜM illerin birleşimi olur.
    ///
    /// Ödev 11'de tekil <c>Bolge</c> alanı listeye çevrildi. Tek elemanlı
    /// liste eski davranışın aynısı; çağıran taraf için kırılma yok.
    /// </summary>
    public List<string>? Bolgeler { get; set; }

    /// <summary>
    /// KAYITLI ALAN SEÇİMİ (Ödev 11): <c>tbl_polygon</c>'daki mevcut
    /// çizimlerin id'leri. Alan, seçilenlerin birleşimi olur.
    ///
    /// Neden faydalı? Yönetici zaten "Ankara metropol alanı" diye bir poligon
    /// çizmişse, aynı sınırı yetki tanımlarken yeniden çizmesi hem zaman kaybı
    /// hem de iki sınırın birbirinden kayması demekti.
    /// </summary>
    public List<int>? PoligonIdleri { get; set; }
}

/// <summary>
/// Giriş yapan kullanıcının ÇALIŞMA ALANI — harita ekranı bunu çizip
/// kullanıcıya nerede çizim yapabileceğini gösteriyor.
/// </summary>
public class CalismaAlaniDto
{
    /// <summary>
    /// Kullanıcı coğrafi olarak kısıtlı mı? Hiç tanım yoksa false döner ve
    /// kullanıcı her yere çizebilir — kısıt tanımlanmamış olması "hiçbir yere
    /// çizemez" değil, "sınır yok" demektir.
    /// </summary>
    public bool Kisitli { get; set; }

    /// <summary>İzinli alanlar (birden fazla olabilir; birleşimleri geçerlidir).</summary>
    public List<CalismaAlaniParcasiDto> Alanlar { get; set; } = new();
}

/// <summary>Tek bir izinli alan parçası.</summary>
public class CalismaAlaniParcasiDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Wkt { get; set; } = string.Empty;

    /// <summary>Alan rolden mi geliyor yoksa doğrudan kullanıcıya mı verilmiş?</summary>
    public string Kaynak { get; set; } = string.Empty;
}
