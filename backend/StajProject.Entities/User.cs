namespace StajProject.Entities;

/// <summary>
/// Giriş yapabilen kullanıcı. Şifre asla düz metin saklanmaz, hash'i saklanır.
/// </summary>
public class User : IAuditableEntity
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ---------- Ödev 3 / Görev 1: Sistem durum takibi kolonları ----------

    /// <summary>
    /// Soft delete (yumuşak silme) bayrağı. Kayıt fiziksel olarak silinmez,
    /// sadece bu alan true yapılır; böylece geçmiş veri ve ilişkiler korunur.
    /// </summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// Hesap aktif mi? Pasif hesap silinmemiştir ama giriş yapamaz
    /// (örn. askıya alınmış kullanıcı).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Kaydın en son güncellendiği an (UTC). Kayıt hiç güncellenmediyse null kalır.
    /// </summary>
    public DateTime? ModifiedDate { get; set; }

    /// <summary>
    /// Hesap YÖNETİCİ ONAYINDAN geçti mi? (Ödev 10 — kayıt olma)
    ///
    /// Giriş ekranından kendi kaydını açan kullanıcı bu alan <c>false</c> ile
    /// oluşur ve onaylanana kadar giriş yapamaz.
    ///
    /// NEDEN <see cref="IsActive"/> yetmedi? İkisi farklı şey söylüyor:
    /// <c>IsActive=false</c> "bu hesap askıya alındı" (bir zamanlar
    /// çalışıyordu), <c>IsApproved=false</c> ise "bu hesap hiç onaylanmadı".
    /// Aynı kolona bindirseydik yönetici listede yeni kaydı askıya alınmış
    /// eski bir kullanıcıdan ayırt edemezdi.
    ///
    /// Seed'le gelen ve yöneticinin panelden açtığı kullanıcılar doğrudan
    /// onaylı sayılır — onları zaten bir yönetici oluşturmuş oluyor.
    /// </summary>
    public bool IsApproved { get; set; } = true;

    // ---------- Ödev 6 / Madde 2: dinamik yetkilendirme ----------

    /// <summary>Kullanıcının rolleri (user_roles). Yetkilerin ASIL kaynağı budur.</summary>
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    /// <summary>
    /// Rolden bağımsız, DOĞRUDAN verilmiş yetkiler (user_permissions).
    /// Rolde zaten olan bir yetki buraya yazılmaz; ayrıntı için
    /// <see cref="UserPermission"/> açıklamasına bak.
    /// </summary>
    public ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
}
