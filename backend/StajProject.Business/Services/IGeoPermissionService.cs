using NetTopologySuite.Geometries;
using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// Coğrafi yetki servisi (Ödev 7 / Madde 2).
///
/// İki iş yapar:
///   1) Yönetim: kullanıcıya/role izinli alan tanımlama, listeleme, kaldırma
///   2) Uygulama: "bu geometri kullanıcının izinli alanı içinde mi?"
///
/// İkinci madde bu modülün asıl varlık sebebi: kural tek yerde yaşasın ki
/// nokta, çizgi ve poligon uçları aynı kontrolden geçsin.
/// </summary>
public interface IGeoPermissionService
{
    Task<List<GeoPermissionDto>> GetAllAsync();
    Task<List<GeoPermissionDto>> GetForUserAsync(int userId);
    Task<List<GeoPermissionDto>> GetForRoleAsync(int roleId);

    Task<GeoPermissionDto> CreateAsync(GeoPermissionCreateDto dto);
    Task<bool> DeleteAsync(int id);

    /// <summary>Giriş yapan kullanıcının çalışma alanı (harita ekranı için).</summary>
    Task<CalismaAlaniDto> GetCalismaAlanimAsync();

    /// <summary>
    /// Verilen geometri, kullanıcının izinli alanının İÇİNDE mi?
    /// Kullanıcıya hiç alan tanımlanmamışsa kısıt yoktur → true.
    /// Alan dışındaysa <see cref="Validation.IsKuraliException"/> fırlatır;
    /// böylece çağıran her uç aynı, açıklayıcı hata mesajını üretir.
    /// </summary>
    Task DogrulaAsync(int userId, Geometry geometri);
}
