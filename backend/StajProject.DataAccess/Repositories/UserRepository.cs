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

    // ---------- Ödev 6: yönetim paneli ----------

    /// <summary>
    /// Kullanıcı sorgularının ortak gövdesi: roller, ROLLERİN YETKİLERİ ve
    /// doğrudan yetkiler tek sorguda yüklenir. Include olmasaydı her kullanıcı
    /// için ayrı sorgu atılırdı (N+1); 50 kullanıcılı listede 101 sorgu demek olurdu.
    ///
    /// Rolün yetkileri de burada yükleniyor çünkü kullanıcının GERÇEK yetkisi
    /// "rolden gelenler + doğrudan verilenler" birleşimi; ikisi olmadan
    /// PermissionService hesabı yapamaz.
    /// </summary>
    private IQueryable<User> IliskileriyleBirlikte()
        => _context.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role!).ThenInclude(r => r.RolePermissions)
            .Include(u => u.UserPermissions).ThenInclude(up => up.Permission);

    public async Task<List<User>> GetAllAsync()
        => await IliskileriyleBirlikte().OrderBy(u => u.Username).ToListAsync();

    public async Task<User?> GetByIdAsync(int id)
        => await IliskileriyleBirlikte().FirstOrDefaultAsync(u => u.Id == id);

    public async Task<User?> UpdateAsync(User user)
    {
        // Takip edilen (tracked) kopyayı çekiyoruz — GeometryRepository.UpdateAsync ile aynı desen.
        var mevcut = await _context.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        if (mevcut is null)
        {
            return null;
        }

        mevcut.Username = user.Username;
        mevcut.PasswordHash = user.PasswordHash;
        mevcut.IsActive = user.IsActive;
        mevcut.IsApproved = user.IsApproved;   // Ödev 10: yönetici onayı

        // ModifiedDate elle yazılmıyor — AppDbContext.ApplyAuditRules() basıyor.
        await _context.SaveChangesAsync();
        return mevcut;
    }

    public Task SetRolesAsync(int userId, IReadOnlyCollection<int> roleIds)
        => AtamalariEsitleAsync(
            _context.UserRoles.Where(ur => ur.UserId == userId),
            roleIds,
            mevcut => mevcut.RoleId,
            roleId => new UserRole { UserId = userId, RoleId = roleId });

    public Task SetPermissionsAsync(int userId, IReadOnlyCollection<int> permissionIds)
        => AtamalariEsitleAsync(
            _context.UserPermissions.Where(up => up.UserId == userId),
            permissionIds,
            mevcut => mevcut.PermissionId,
            permissionId => new UserPermission { UserId = userId, PermissionId = permissionId });

    /// <summary>
    /// Bir bağlantı tablosundaki atamaları istenen id listesiyle EŞİTLER:
    /// listede olmayanlar silinir, olmayan yeniler eklenir, ortaktakilere dokunulmaz.
    ///
    /// "Hepsini sil, hepsini yeniden ekle" daha kısa olurdu ama her kaydetmede
    /// bütün satırların inserted_date damgasını sıfırlardı; kim ne zaman
    /// yetkilendirildi bilgisi boşuna kaybolurdu.
    /// </summary>
    private async Task AtamalariEsitleAsync<TAtama>(
        IQueryable<TAtama> mevcutSorgu,
        IReadOnlyCollection<int> istenenIdler,
        Func<TAtama, int> idSec,
        Func<int, TAtama> uret)
        where TAtama : class
    {
        var mevcut = await mevcutSorgu.ToListAsync();
        var mevcutIdler = mevcut.Select(idSec).ToHashSet();

        _context.Set<TAtama>().RemoveRange(mevcut.Where(a => !istenenIdler.Contains(idSec(a))));

        foreach (var id in istenenIdler.Distinct().Where(id => !mevcutIdler.Contains(id)))
        {
            _context.Set<TAtama>().Add(uret(id));
        }

        await _context.SaveChangesAsync();
    }
}
