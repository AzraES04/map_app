using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username);
    Task<User> AddAsync(User user);
    Task<bool> AnyAsync();

    // ---------- Ödev 3 / Görev 1: durum yönetimi ----------

    /// <summary>Kaydı fiziksel olarak silmez; is_deleted bayrağını kaldırır (soft delete).</summary>
    Task<bool> SoftDeleteAsync(int id);

    /// <summary>Hesabı askıya alır veya yeniden aktif eder.</summary>
    Task<bool> SetActiveAsync(int id, bool isActive);

    // ---------- Ödev 6: yönetim paneli ----------

    /// <summary>
    /// Tüm kullanıcılar — rolleri ve DOĞRUDAN yetkileriyle birlikte.
    /// Yönetim listesinde her satırda rolleri göstereceğimiz için ilişkiler
    /// tek sorguda yükleniyor (aksi hâlde N+1 sorgu problemi oluşurdu).
    /// </summary>
    Task<List<User>> GetAllAsync();

    /// <summary>Tek kullanıcı — rolleri ve doğrudan yetkileriyle.</summary>
    Task<User?> GetByIdAsync(int id);

    /// <summary>
    /// Kullanıcı adı / şifre hash'i / aktiflik günceller.
    /// Kayıt bulunamazsa null döner. ModifiedDate'i AppDbContext otomatik basar.
    /// </summary>
    Task<User?> UpdateAsync(User user);

    /// <summary>
    /// Kullanıcının rollerini verilen listeyle DEĞİŞTİRİR (ekleme+çıkarma birlikte).
    /// "Ekle" ve "çıkar" uçlarını ayırmak yerine tek "şu an bu roller geçerli"
    /// çağrısı: arayüz de zaten işaret kutularının son hâlini gönderiyor.
    /// </summary>
    Task SetRolesAsync(int userId, IReadOnlyCollection<int> roleIds);

    /// <summary>
    /// Kullanıcının DOĞRUDAN yetkilerini verilen listeyle değiştirir.
    /// Rolden gelen yetkiler buraya YAZILMAZ; onlar role_permissions'ta durur.
    /// </summary>
    Task SetPermissionsAsync(int userId, IReadOnlyCollection<int> permissionIds);
}
