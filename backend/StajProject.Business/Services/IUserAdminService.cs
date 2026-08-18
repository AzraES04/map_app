using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// Kullanıcı yönetimi (Ödev 6 / Madde 1: "Kullanıcı Listesi — Ekle/Çıkar/Güncelle").
///
/// Neden AuthService'e eklemedik? AuthService'in işi KİMLİK DOĞRULAMA: token
/// üretir, şifre doğrular. Buradaki işler ise YÖNETİM: liste, rol atama, yetki
/// dağıtma. İkisini tek sınıfta toplamak, giriş akışını yönetim ekranı her
/// değiştiğinde riske atmak olurdu.
/// </summary>
public interface IUserAdminService
{
    Task<List<UserDto>> GetAllAsync();
    Task<UserDto?> GetByIdAsync(int id);
    Task<UserDto> CreateAsync(UserCreateDto dto);
    Task<UserDto?> UpdateAsync(int id, UserUpdateDto dto);

    /// <summary>Soft delete. Kayıt yoksa false.</summary>
    Task<bool> DeleteAsync(int id);

    /// <summary>
    /// Kullanıcının DOĞRUDAN yetkilerini günceller ve güncel matrisi döner.
    /// Rolden gelen yetkiler bu listeye yazılmaz — zaten geçerliler.
    /// </summary>
    Task<UserPermissionsDto?> SetPermissionsAsync(int id, SetUserPermissionsDto dto);
}
