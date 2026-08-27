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

    // ---------- İki adımlı doğrulama ----------

    /// <summary>
    /// Şifre doğru ama HİZMET HENÜZ VERİLMEDİ: kullanıcının iki adımlı
    /// doğrulaması açık, altı haneli kod bekleniyor.
    ///
    /// Bu true iken <see cref="Token"/> ve <see cref="RefreshToken"/> BOŞ
    /// gelir — yarım bir oturum diye bir şey yok. İstemcinin tek yapacağı
    /// <see cref="AraToken"/> ile ikinci adıma gitmek.
    /// </summary>
    public bool IkinciAdimGerekli { get; set; }

    /// <summary>
    /// İkinci adım için kısa ömürlü ara token (yalnızca
    /// <see cref="IkinciAdimGerekli"/> true iken dolu).
    ///
    /// Normal erişim token'ı YERİNE GEÇEMEZ: farklı bir audience ile
    /// imzalanıyor ve API'nin token doğrulaması onu reddediyor
    /// (bkz. JwtSettings.IkinciAdimAudience).
    /// </summary>
    public string? AraToken { get; set; }
}
