namespace StajProject.Entities;

/// <summary>
/// Giriş yapabilen kullanıcı. Şifre asla düz metin saklanmaz, hash'i saklanır.
/// </summary>
public class User
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
}
