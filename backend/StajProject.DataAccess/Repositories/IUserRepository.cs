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
}
