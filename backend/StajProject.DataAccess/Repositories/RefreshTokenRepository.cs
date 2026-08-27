using Microsoft.EntityFrameworkCore;
using StajProject.DataAccess.Context;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _context;

    public RefreshTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<RefreshToken> AddAsync(RefreshToken token)
    {
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync();
        return token;
    }

    /// <summary>
    /// Özete göre anahtarı, kullanıcısıyla birlikte getirir.
    ///
    /// AsNoTracking KULLANILMIYOR — bilerek. Bu satır okunduktan hemen sonra
    /// döndürme sırasında iptal edilecek (RevokedAt yazılacak); izlenmeyen bir
    /// nesnede yapılan değişiklik SaveChanges'e hiç ulaşmaz ve eski anahtar
    /// sessizce geçerli kalırdı. Depodaki diğer okumalar salt okunur olduğu
    /// için orada AsNoTracking doğru; burada değil.
    /// </summary>
    public async Task<RefreshToken?> GetByHashAsync(string tokenHash)
    {
        return await _context.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
    }

    public Task SaveAsync() => _context.SaveChangesAsync();

    public async Task<int> RevokeAllForUserAsync(int userId, string sebep)
    {
        var an = DateTime.UtcNow;

        // Yalnızca AÇIK olanlar: zaten iptal edilmiş satırların sebebini
        // ezmek, hırsızlık kaydının üstüne yazmak olurdu.
        var acikOlanlar = await _context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync();

        foreach (var anahtar in acikOlanlar)
        {
            anahtar.RevokedAt = an;
            anahtar.RevokedReason = sebep;
        }

        await _context.SaveChangesAsync();
        return acikOlanlar.Count;
    }
}
