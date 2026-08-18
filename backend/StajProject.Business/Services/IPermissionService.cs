using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// Yetki okuma servisi (Ödev 6 / Madde 2).
///
/// Yetkiler İKİ yoldan gelir — rol üzerinden ve doğrudan — ve kullanıcının
/// gerçek yetkisi bu ikisinin BİRLEŞİMİdir. Bu birleştirme işi tek bir yerde,
/// burada yapılır; hem yönetim ekranı hem de erişim kontrolü aynı hesabı kullanır.
/// İki ayrı yerde hesaplansaydı, biri güncellenip diğeri unutulduğunda
/// "ekranda yetkili görünüyor ama işlem reddediliyor" gibi hatalar çıkardı.
/// </summary>
public interface IPermissionService
{
    /// <summary>Sistemdeki tüm yetkiler (permissions tablosu).</summary>
    Task<List<PermissionDto>> GetAllAsync();

    /// <summary>
    /// Bir kullanıcının yetki matrisi: TÜM yetkiler + her birinin kaynağı
    /// (rolden mi, doğrudan mı, hiç yok mu). Kullanıcı yoksa null.
    /// </summary>
    Task<UserPermissionsDto?> GetForUserAsync(int userId);

    /// <summary>Giriş yapmış kullanıcının kendi yetki matrisi (arayüzün menüyü kısması için).</summary>
    Task<UserPermissionsDto?> GetForCurrentUserAsync();

    /// <summary>Kullanıcı bu yetkiye sahip mi? (rol VEYA doğrudan)</summary>
    Task<bool> HasPermissionAsync(int userId, string permissionName);
}
