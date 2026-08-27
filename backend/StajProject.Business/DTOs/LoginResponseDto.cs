namespace StajProject.Business.DTOs;

public class LoginResponseDto
{
    /// <summary>
    /// Kısa ömürlü erişim token'ı (JWT). Her API isteğinde
    /// <c>Authorization: Bearer ...</c> başlığında gider.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Token'ın geçersiz olacağı an (UTC). Frontend otomatik çıkış için kullanır.</summary>
    public DateTime ExpiresAt { get; set; }

    public string Username { get; set; } = string.Empty;

    // ---------- Eksik 5: yenileme anahtarı ----------

    /// <summary>
    /// Uzun ömürlü yenileme anahtarı. Yalnızca <c>POST /api/auth/refresh</c>
    /// ucuna gönderilir, başka hiçbir isteğe eklenmez.
    ///
    /// Sunucu bu değeri BİR KEZ, burada gösterir; veritabanında yalnızca
    /// SHA-256 özeti durur. Kaybolursa geri getirilemez, yenisi alınır.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Yenileme anahtarının son kullanma anı (UTC) — oturumun MUTLAK sonu.
    ///
    /// Frontend'in otomatik çıkış zamanlayıcısı artık <see cref="ExpiresAt"/>'e
    /// değil buna bakıyor: erişim token'ı 10 dakikada bir sessizce yenilendiği
    /// için kullanıcı ancak bu an geldiğinde giriş ekranına döner.
    /// </summary>
    public DateTime RefreshTokenExpiresAt { get; set; }
}
