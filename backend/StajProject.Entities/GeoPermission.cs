using NetTopologySuite.Geometries;

namespace StajProject.Entities;

/// <summary>
/// COĞRAFİ YETKİ — bir kullanıcının veya rolün çizim yapabileceği alan.
///
/// Ödev 7 / Madde 2: "Kullanıcı/Rol için haritada bir poligon alan çizildikten
/// sonra, ilgili kullanıcı sistemde bu tanımlı alanın dışına çizim yapamasın."
///
/// Buradaki poligon bir ÇİZİM DEĞİL, bir KURALDIR. tbl_polygon'daki kayıtlarla
/// karıştırılmaması için ayrı tabloda duruyor: oradakiler kullanıcının ürettiği
/// veri, buradaki ise o veriyi sınırlayan yetki tanımı. Aynı tabloya koysaydık
/// haritada envanter olarak listelenir, analizlerde sayılır ve silinebilirdi.
///
/// SAHİP: ya bir kullanıcı ya bir rol — ikisi birden değil.
/// Aynı yetki mantığının coğrafi karşılığı: alan role verilirse o roldeki
/// herkese, kullanıcıya verilirse yalnızca ona uygulanır. Kullanıcının GERÇEK
/// çalışma alanı ikisinin BİRLEŞİMİdir (bkz. GeoPermissionService).
/// </summary>
public class GeoPermission : IAuditableEntity
{
    public int Id { get; set; }

    /// <summary>İnsan okuyabilir etiket — "Ankara ve çevresi" gibi.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Alan bir KULLANICIYA verildiyse dolu; role verildiyse null.</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Alan bir ROLE verildiyse dolu; kullanıcıya verildiyse null.</summary>
    public int? RoleId { get; set; }
    public Role? Role { get; set; }

    /// <summary>
    /// İzinli alan (EPSG:4326).
    ///
    /// Tip <see cref="Geometry"/>, <see cref="Polygon"/> DEĞİL. Ödev 10'a kadar
    /// alan yalnızca elle çizilen tek parça bir poligondu ve Polygon yetiyordu.
    /// Artık il ve bölge seçimiyle de tanımlanabiliyor: seçilen illerin
    /// birleşimi çoğu zaman <b>MultiPolygon</b> oluyor (bitişik olmayan iller,
    /// adalar). Polygon dayatsaydık "Ege + İç Anadolu" gibi bir seçim
    /// kaydedilemezdi.
    /// </summary>
    public Geometry Geom { get; set; } = default!;

    /// <summary>Kaydın oluşturulma anı (UTC).</summary>
    public DateTime InsertedDate { get; set; } = DateTime.UtcNow;

    /// <summary>Alanı tanımlayan yöneticinin id'si — "bu kuralı kim koydu?" izi.</summary>
    public int? InsertedUserId { get; set; }

    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// Pasif alan kısıtlama UYGULAMAZ. Kuralı silmeden geçici olarak kaldırmak
    /// istendiğinde kullanılır; rollerdeki is_active ile aynı mantık.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime? ModifiedDate { get; set; }
}
