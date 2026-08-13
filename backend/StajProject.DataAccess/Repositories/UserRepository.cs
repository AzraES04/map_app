using Microsoft.EntityFrameworkCore;
using StajProject.DataAccess.Context;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<User> AddAsync(User user)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Tabloda HERHANGİ bir kullanıcı var mı? Global query filter bilinçli olarak
    /// devre dışı: soft delete ile silinmiş kullanıcılar da sayılmalı, yoksa seed
    /// her açılışta yeni bir admin yaratır ve silme işlemi boşa gider.
    /// </summary>
    public Task<bool> AnyAsync() => _context.Users.IgnoreQueryFilters().AnyAsync();

    // ---------- Ödev 3 / Görev 1: durum yönetimi ----------

    public async Task<bool> SoftDeleteAsync(int id)
    {
        // DİKKAT: FindAsync yerine FirstOrDefaultAsync kullanıyoruz.
        // FindAsync global query filter'ı UYGULAMAZ (primary key ile doğrudan gider),
        // FirstOrDefaultAsync uygular. Yani zaten silinmiş bir kaydı ikinci kez silmeye
        // çalışırsak burada null döner ve false alırız — istediğimiz davranış bu.
        // Ayrıca AsNoTracking YOK: değişikliği kaydedeceğimiz için EF'in nesneyi takip etmesi gerek.
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return false;
        }

        user.IsDeleted = true;
        user.IsActive = false; // silinen hesap aynı zamanda giriş yapamamalı

        // ModifiedDate'i burada ELLE yazmıyoruz — AppDbContext.ApplyAuditRules() otomatik dolduruyor.
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetActiveAsync(int id, bool isActive)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return false;
        }

        user.IsActive = isActive;
        await _context.SaveChangesAsync();
        return true;
    }
}
