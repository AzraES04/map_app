using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// Rol yönetimi (Ödev 6 / Madde 1: "Rol Listesi — Ekle/Çıkar/Sil" ekranı).
/// Rolün yetkileri de bu servis üzerinden düzenlenir; rol ile yetkileri
/// tek işlemde kaydedilir, arayüz iki ayrı istek atmak zorunda kalmaz.
/// </summary>
public interface IRoleService
{
    Task<List<RoleDto>> GetAllAsync();
    Task<RoleDto?> GetByIdAsync(int id);
    Task<RoleDto> CreateAsync(RoleSaveDto dto);
    Task<RoleDto?> UpdateAsync(int id, RoleSaveDto dto);

    /// <summary>Soft delete. Kayıt yoksa false.</summary>
    Task<bool> DeleteAsync(int id);
}
