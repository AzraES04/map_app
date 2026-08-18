namespace StajProject.Entities;

// ============================================================================
//  Ödev 6 / Madde 2: DİNAMİK YETKİLENDİRME
//
//  Klasik çözüm, rolü kullanıcının üstüne bir metin olarak yazmaktır
//  ("admin", "editor"). O zaman yeni bir yetki eklemek KOD değişikliği ister:
//  if (user.Role == "admin") ... satırlarını tek tek güncellemek gerekir.
//
//  Burada yetkiler VERİ olarak duruyor: yeni bir yetki eklemek tabloya satır
//  eklemekten ibaret. Üç tablo yetiyor:
//
//      roles           ── kim olduğun          (Yönetici, Editör…)
//      permissions     ── ne yapabildiğin      (Point Ekleme, Rol Yönetimi…)
//      + üç bağlantı tablosu (user_roles, role_permissions, user_permissions)
//
//  Yetki İKİ yoldan gelebilir:
//      1) Rol üzerinden  → role_permissions   (toplu, sürdürülebilir)
//      2) Doğrudan       → user_permissions   (istisna; "şu kişiye bir de bu")
//
//  Kullanıcının GERÇEK yetkisi bu ikisinin BİRLEŞİMİdir. Rolden gelen bir yetki
//  ayrıca kullanıcıya işaretlenmez — arayüzde "rolden geliyor" diye gösterilir.
// ============================================================================

/// <summary>
/// roles ve permissions tablolarının ORTAK iskeleti.
///
/// İkisi de "id + ad + açıklama + durum kolonları"ndan ibaret; tek fark
/// hangi ilişkinin ucunda durdukları. Ortak alanları burada topluyoruz —
/// geometri tablolarındaki <see cref="GeometryEntityBase"/> ile aynı gerekçe.
/// Ayrıca DbContext'teki yapılandırma da tek bir generic metotla yazılabiliyor.
/// </summary>
public abstract class AuthorizationEntityBase : IAuditableEntity
{
    public int Id { get; set; }

    /// <summary>Görünen ad. Silinmemiş kayıtlar arasında benzersizdir.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Kaydın ne işe yaradığı; yönetim ekranında listede gösterilir.</summary>
    public string? Description { get; set; }

    /// <summary>Kaydın oluşturulma anı (UTC) — geometri tablolarıyla aynı izleme deseni.</summary>
    public DateTime InsertedDate { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public DateTime? ModifiedDate { get; set; }
}

/// <summary>
/// Rol: yetki demeti. Kullanıcıya tek tek yetki dağıtmak yerine
/// "Editör" dersin, o rolün yetkileri kullanıcıya akar.
/// </summary>
public class Role : AuthorizationEntityBase
{
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

/// <summary>
/// Yetki: sistemde yapılabilecek TEK bir iş ("Point Ekleme", "Rol Yönetimi").
///
/// Ödevin istediği <c>name</c> ve <c>description</c> alanları taban sınıftan geliyor.
/// Yetkiler hem role hem de doğrudan kullanıcıya atanabildiği için
/// ORTAK bir sözlük görevi görür — iki ayrı yetki listesi tutulmaz.
/// </summary>
public class Permission : AuthorizationEntityBase
{
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
}

// ---------------------------------------------------------------------------
//  Bağlantı (join) tabloları
//
//  Üçü de "çoka çok" ilişkiyi taşır ve BİLEŞİK anahtar kullanır
//  (örn. user_id + role_id). Ayrı bir Id kolonu koymuyoruz: bileşik anahtar
//  aynı çiftin iki kez eklenmesini veritabanı seviyesinde imkânsız kılar,
//  yani "aynı rolü iki kez atama" hatası kod yazmadan engellenmiş olur.
//
//  Bu satırlar soft delete edilmez; atama kaldırıldığında satır gerçekten
//  silinir. Sebebi: burada saklanacak bir "geçmiş" yok, sadece güncel durum var.
// ---------------------------------------------------------------------------

/// <summary>Kullanıcı ↔ Rol ataması (user_roles).</summary>
public class UserRole
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public DateTime InsertedDate { get; set; } = DateTime.UtcNow;
}

/// <summary>Rol ↔ Yetki ataması (role_permissions).</summary>
public class RolePermission
{
    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public int PermissionId { get; set; }
    public Permission? Permission { get; set; }

    public DateTime InsertedDate { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Kullanıcı ↔ Yetki ataması (user_permissions) — rolden BAĞIMSIZ, doğrudan yetki.
///
/// Buraya yalnızca kullanıcının rollerinde OLMAYAN yetkiler yazılır.
/// Rolde zaten varsa ikinci kez işaretlemek anlamsız olurdu: yetki zaten
/// geçerli, ayrıca rol değiştiğinde bu kopya satır arkada kalıp
/// "neden hâlâ yetkisi var?" sorusuna yol açardı.
/// </summary>
public class UserPermission
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public int PermissionId { get; set; }
    public Permission? Permission { get; set; }

    public DateTime InsertedDate { get; set; } = DateTime.UtcNow;
}
