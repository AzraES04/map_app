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
    /// Kendi kaydını açmış bir kullanıcıyı ONAYLAR (Ödev 10).
    /// Zaten onaylıysa ya da kayıt yoksa null döner.
    ///
    /// Onayı geri alma ucu YOK: bir hesabı kapatmanın yolu "pasife al"
    /// (is_active) ya da silmektir. Onay, bir kereye mahsus bir kapıdır;
    /// geri alınabilir olsaydı iki ayrı "kapalı" durumu doğar ve hangisinin
    /// geçerli olduğu belirsizleşirdi.
    /// </summary>
    Task<UserDto?> OnaylaAsync(int id);

    /// <summary>
    /// Bir kullanıcının iki adımlı doğrulamasını SIFIRLAR (yönetici işlemi).
    ///
    /// ---- NEDEN GEREKLİ? ----
    ///
    /// TOTP'de gizli anahtar yalnızca kullanıcının telefonunda ve sunucuda
    /// duruyor. Telefon kaybolur, bozulur ya da uygulama silinirse kullanıcı
    /// KALICI OLARAK kilitlenir: şifresini bilse bile ikinci adımı geçemez ve
    /// korumayı kapatmak da giriş yapmayı gerektirir.
    ///
    /// Kurtarma kodları da bir seçenekti; yönetici sıfırlaması seçildi çünkü
    /// bu sistemde zaten bir yönetici var (hesapları o onaylıyor, rolleri o
    /// veriyor) ve kimliği doğrulayacak insan da o. Kurtarma kodları
    /// kullanıcının onları güvenli bir yere kaydetmiş olmasına bel bağlar —
    /// pratikte çoğu kişi kaydetmez.
    ///
    /// "Kullanıcı Yönetimi" yetkisi ister; kullanıcı yoksa null döner.
    /// </summary>
    Task<UserDto?> IkiAdimliSifirlaAsync(int id);

    /// <summary>
    /// Kullanıcının DOĞRUDAN yetkilerini günceller ve güncel matrisi döner.
    /// Rolden gelen yetkiler bu listeye yazılmaz — zaten geçerliler.
    /// </summary>
    Task<UserPermissionsDto?> SetPermissionsAsync(int id, SetUserPermissionsDto dto);
}
