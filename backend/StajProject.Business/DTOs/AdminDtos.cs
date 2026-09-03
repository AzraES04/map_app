using System.ComponentModel.DataAnnotations;

namespace StajProject.Business.DTOs;

// ============================================================================
//  Ödev 6 — yönetim paneli DTO'ları
//
//  Entity'leri doğrudan istemciye göndermiyoruz: User.PasswordHash gibi
//  alanlar dışarı sızardı ve navigasyonlar döngüsel JSON üretirdi
//  (User → UserRole → User → ...). DTO, dışarıya AÇILAN yüzeyin sözleşmesidir.
// ============================================================================

/// <summary>Tek bir yetki — "Point Ekleme" gibi.</summary>
public class PermissionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>Listelerde rolü kısaca göstermek için (id + ad).</summary>
public class RoleOzetDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>İstemciye giden rol kaydı — yetkileri ve kullanıcı sayısıyla.</summary>
public class RoleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime InsertedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    /// <summary>Bu role bağlı yetkiler (role_permissions).</summary>
    public List<PermissionDto> Permissions { get; set; } = new();

    /// <summary>Bu role sahip kullanıcı sayısı — silmeden önce uyarı verebilmek için.</summary>
    public int UserCount { get; set; }
}

/// <summary>Rol ekleme/güncelleme isteği. Yetki listesi de aynı istekte gelir.</summary>
public class RoleSaveDto
{
    [Required(ErrorMessage = "Rol adı zorunludur.")]
    [MaxLength(100, ErrorMessage = "Rol adı en fazla 100 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Açıklama en fazla 500 karakter olabilir.")]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Rolün yetkileri. Arayüz işaret kutularının SON hâlini gönderir;
    /// "ekle/çıkar" değil "şu an bunlar geçerli" sözleşmesi.
    /// </summary>
    public List<int> PermissionIds { get; set; } = new();
}

/// <summary>İstemciye giden kullanıcı kaydı. Şifre hash'i asla dahil edilmez.</summary>
public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    /// <summary>
    /// Yönetici onayından geçti mi? (Ödev 10) Kendi kaydını açan kullanıcılar
    /// <c>false</c> gelir ve listede "onay bekliyor" olarak işaretlenir.
    /// </summary>
    public bool IsApproved { get; set; }

    /// <summary>
    /// Kullanıcının iki adımlı doğrulaması açık mı?
    ///
    /// Yalnızca DURUM taşınıyor — gizli anahtar ASLA. Anahtarı bilen,
    /// kullanıcının bütün gelecek kodlarını üretebilir; yöneticinin de onu
    /// görmesi için hiçbir sebep yok. Yönetici yalnızca "açık mı?" bilgisine
    /// ihtiyaç duyuyor: kilitlenen kullanıcıyı sıfırlayıp sıfırlamayacağına
    /// karar verebilmek için.
    /// </summary>
    public bool IkiAdimliEtkin { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public List<RoleOzetDto> Roles { get; set; } = new();

    /// <summary>Rolden bağımsız, doğrudan verilmiş yetki sayısı.</summary>
    public int DirectPermissionCount { get; set; }

    /// <summary>Rol + doğrudan yetkilerin BİRLEŞİMİ; kullanıcının gerçek yetki sayısı.</summary>
    public int EffectivePermissionCount { get; set; }

    /// <summary>
    /// Bu kullanıcının BAĞLI OLDUĞU adminin kullanıcı adı; kendi başına
    /// kaydolduysa (davet kodu girmediyse) null.
    /// </summary>
    public string? ParentAdminUsername { get; set; }

    /// <summary>
    /// Telefon numarası; girilmemişse null. Tur rehberi olan kullanıcılarda
    /// bu numara, CANLI turun misafirlerine "Rehberi ara" düğmesi olarak
    /// açılıyor (bkz. <c>User.PhoneNumber</c>).
    /// </summary>
    public string? PhoneNumber { get; set; }
}

/// <summary>Bir kullanıcının davet kodu ve ona bağlı kaç kişi olduğu.</summary>
public class DavetKoduDto
{
    public string Kod { get; set; } = string.Empty;

    /// <summary>Bu koda kayıt olmuş (ParentAdminId = ben) kullanıcı sayısı.</summary>
    public int BagliKullaniciSayisi { get; set; }
}

/// <summary>Yeni kullanıcı isteği.</summary>
public class UserCreateDto
{
    [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
    [MaxLength(100, ErrorMessage = "Kullanıcı adı en fazla 100 karakter olabilir.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [MinLength(6, ErrorMessage = "Şifre en az 6 karakter olmalıdır.")]
    public string Password { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// İsteğe bağlı telefon numarası. Biçim doğrulaması yok, yalnızca uzunluk
    /// sınırı — gerekçe <c>User.PhoneNumber</c> açıklamasında.
    /// </summary>
    [MaxLength(32, ErrorMessage = "Telefon numarası en fazla 32 karakter olabilir.")]
    public string? PhoneNumber { get; set; }

    public List<int> RoleIds { get; set; } = new();
}

/// <summary>Kullanıcı güncelleme isteği.</summary>
public class UserUpdateDto
{
    [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
    [MaxLength(100, ErrorMessage = "Kullanıcı adı en fazla 100 karakter olabilir.")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Boş bırakılırsa şifre DEĞİŞMEZ. Güncelleme ekranında şifre alanını
    /// zorunlu tutmak, adı düzeltmek isteyen yöneticiyi şifre uydurmaya iterdi.
    /// </summary>
    [MinLength(6, ErrorMessage = "Şifre en az 6 karakter olmalıdır.")]
    public string? Password { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Telefon numarası. Şifreden FARKLI olarak boş göndermek numarayı
    /// SİLER: form alanını temizleyip kaydetmek, "artık numaramı paylaşmak
    /// istemiyorum" demenin doğal yolu. Şifrede aynı davranış hesabı
    /// kilitlerdi; burada en kötü ihtimalle bir düğme kaybolur.
    /// </summary>
    [MaxLength(32, ErrorMessage = "Telefon numarası en fazla 32 karakter olabilir.")]
    public string? PhoneNumber { get; set; }

    public List<int> RoleIds { get; set; } = new();
}

/// <summary>
/// Bir kullanıcının TEK bir yetki üzerindeki durumu (Ödev 6 / Madde 2'nin kalbi).
///
/// Aynı yetki iki farklı yoldan gelebildiği için "var/yok" tek başına yetmiyor;
/// arayüzün "bu yetki rolden geliyor, dokunamazsın" diyebilmesi için
/// KAYNAK bilgisi de taşınıyor.
/// </summary>
public class EffectivePermissionDto
{
    public int PermissionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Kullanıcının rollerinden geliyor mu?</summary>
    public bool FromRole { get; set; }

    /// <summary>Hangi rol(ler)den geldiği — arayüzde "Yönetici rolünden" yazabilmek için.</summary>
    public List<string> RoleNames { get; set; } = new();

    /// <summary>Kullanıcıya DOĞRUDAN atanmış mı? (user_permissions satırı var mı)</summary>
    public bool Direct { get; set; }

    /// <summary>Sonuç: kullanıcı bu yetkiye sahip mi? (rol VEYA doğrudan)</summary>
    public bool Granted => FromRole || Direct;
}

/// <summary>Kullanıcının yetki matrisi: tüm yetkiler + her birinin kaynağı.</summary>
public class UserPermissionsDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>Kullanıcının rolleri — matrisin "rolden geliyor" kısmını açıklar.</summary>
    public List<RoleOzetDto> Roles { get; set; } = new();

    /// <summary>Sistemdeki TÜM yetkiler; sahip olunmayanlar da gelir (işaretsiz kutu).</summary>
    public List<EffectivePermissionDto> Permissions { get; set; } = new();
}

/// <summary>Kullanıcının DOĞRUDAN yetkilerini güncelleme isteği.</summary>
public class SetUserPermissionsDto
{
    /// <summary>
    /// Doğrudan verilecek yetkiler. Rolden gelenleri buraya koymaya gerek yok;
    /// konsa bile servis onları eler (bkz. UserAdminService.SetPermissionsAsync).
    /// </summary>
    public List<int> PermissionIds { get; set; } = new();
}
