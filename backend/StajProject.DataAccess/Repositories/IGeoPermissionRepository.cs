using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// Coğrafi yetki (izinli çizim alanı) veri erişimi — Ödev 7 / Madde 2.
/// </summary>
public interface IGeoPermissionRepository
{
    /// <summary>Tüm tanımlar — sahip (kullanıcı/rol) bilgisiyle birlikte.</summary>
    Task<List<GeoPermission>> GetAllAsync();

    /// <summary>Belirli bir kullanıcıya DOĞRUDAN verilmiş alanlar.</summary>
    Task<List<GeoPermission>> GetByUserAsync(int userId);

    /// <summary>Belirli bir role verilmiş alanlar.</summary>
    Task<List<GeoPermission>> GetByRoleAsync(int roleId);

    /// <summary>
    /// Bir kullanıcı için GEÇERLİ tüm alanlar: kendi tanımları + AKTİF rollerinin
    /// tanımları. Kullanıcının çalışma alanı bunların birleşimidir.
    ///
    /// Tek sorguda toplanıyor: "önce rolleri çek, sonra her rol için alanları çek"
    /// yolu N+1 sorgu üretirdi.
    /// </summary>
    Task<List<GeoPermission>> GetEffectiveForUserAsync(int userId);

    Task<GeoPermission?> GetByIdAsync(int id);

    Task<GeoPermission> AddAsync(GeoPermission kayit);

    /// <summary>Soft delete — kural kaldırılır ama kim ne zaman tanımlamıştı izi kalır.</summary>
    Task<bool> SoftDeleteAsync(int id);
}
