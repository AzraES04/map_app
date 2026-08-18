using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// Rol tablosunun veri erişim sözleşmesi (Ödev 6 / Madde 1: Rol Listesi ekranı).
/// </summary>
public interface IRoleRepository
{
    /// <summary>Tüm roller — yetkileriyle birlikte (tek sorgu, N+1 yok).</summary>
    Task<List<Role>> GetAllAsync();

    Task<Role?> GetByIdAsync(int id);

    /// <summary>Ada göre arar. Seed ve "bu ad zaten var mı?" kontrolü için.</summary>
    Task<Role?> GetByNameAsync(string name);

    Task<Role> AddAsync(Role role);

    /// <summary>Ad / açıklama / aktiflik günceller; kayıt yoksa null döner.</summary>
    Task<Role?> UpdateAsync(Role role);

    /// <summary>
    /// Soft delete. Rol kaybolmaz, is_deleted işaretlenir; user_roles satırları
    /// yerinde kalır ama sorgu filtresi sayesinde artık yetki üretmezler.
    /// </summary>
    Task<bool> SoftDeleteAsync(int id);

    /// <summary>Rolün yetkilerini verilen listeyle değiştirir (ekleme+çıkarma birlikte).</summary>
    Task SetPermissionsAsync(int roleId, IReadOnlyCollection<int> permissionIds);

    /// <summary>
    /// Rol id → o role sahip kullanıcı sayısı. Liste ekranında "3 kullanıcı"
    /// yazabilmek için; her rol için ayrı COUNT sorgusu atmak yerine tek gruplama.
    /// </summary>
    Task<Dictionary<int, int>> GetUserCountsAsync();
}
