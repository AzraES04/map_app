using NetTopologySuite.Geometries;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// İl sınırları veri erişimi (Ödev 10).
///
/// Bu tablo REFERANS veridir: yazma yalnızca başlangıç yüklemesinde olur,
/// gerisi okuma. Bu yüzden ekleme/silme/güncelleme sözleşmesi yok —
/// yalnızca <see cref="EkleAsync"/> ve okuma metotları.
/// </summary>
public interface IIlRepository
{
    /// <summary>Tablo boş mu? Başlangıç yüklemesi buna bakıyor.</summary>
    Task<int> SayAsync();

    /// <summary>Başlangıç yüklemesi — 81 ili tek seferde yazar.</summary>
    Task EkleAsync(IEnumerable<Il> iller);

    /// <summary>
    /// İllerin listesi, GEOMETRİSİZ (id, ad, bölge).
    ///
    /// Geometri bilerek dışarıda: yönetim ekranındaki il listesi için 81 ilin
    /// sınırlarını taşımanın anlamı yok — dosyanın tamamı ~240 KB.
    /// </summary>
    Task<List<Il>> OzetGetirAsync();

    /// <summary>Sınırlarıyla birlikte tüm iller — haritada seçim katmanı için.</summary>
    Task<List<Il>> SinirlariGetirAsync();

    /// <summary>
    /// Verilen plakalara ait illerin sınırlarının BİRLEŞİMİ.
    /// Hiçbiri bulunamazsa null döner.
    /// </summary>
    Task<Geometry?> IllerinBirlesimiAsync(IReadOnlyCollection<int> plakalar);

    /// <summary>Bir bölgedeki tüm illerin birleşimi. Bölge boşsa null.</summary>
    Task<Geometry?> BolgeGeometrisiAsync(string bolge);
}
