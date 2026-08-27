using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

public interface IAuthService
{
    /// <summary>
    /// Kullanıcı adı/şifre doğruysa erişim token'ı + yenileme anahtarı üretir,
    /// yanlışsa null döner.
    /// </summary>
    Task<LoginResponseDto?> LoginAsync(LoginRequestDto request);

    /// <summary>
    /// Yeni hesap açar (Ödev 10). Hesap YÖNETİCİ ONAYI bekler; token dönmez.
    /// Kullanıcı adı doluysa <see cref="Validation.IsKuraliException"/> fırlatır.
    /// </summary>
    Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request);

    /// <summary>
    /// Eksik 5 — oturumu yeniler: geçerli bir yenileme anahtarı karşılığında
    /// YENİ erişim token'ı ve YENİ yenileme anahtarı verir (döndürme).
    ///
    /// Anahtar geçersiz, süresi dolmuş, iptal edilmiş ya da hesabı artık
    /// uygun değilse <c>null</c> döner. İptal edilmiş bir anahtar TEKRAR
    /// sunulursa bu bir hırsızlık işareti sayılır ve kullanıcının bütün
    /// oturumları kapatılır.
    /// </summary>
    Task<LoginResponseDto?> RefreshAsync(string refreshToken);

    /// <summary>
    /// Eksik 5 — çıkış: yenileme anahtarını iptal eder.
    /// Geçersiz anahtar için de sessizce başarılı döner.
    /// </summary>
    Task LogoutAsync(string refreshToken);

    // ---------- İki adımlı doğrulama (TOTP) ----------

    /// <summary>
    /// İkinci adımı tamamlar: ara token + 6 haneli kod karşılığında gerçek
    /// oturum verir. Ara token ya da kod geçersizse <c>null</c>.
    /// </summary>
    Task<LoginResponseDto?> IkinciAdimGirisAsync(IkinciAdimGirisDto request);

    /// <summary>
    /// Kurulumu başlatır: gizli anahtar üretip kaydeder ama HENÜZ AÇMAZ.
    /// Zaten açıksa <see cref="Validation.IsKuraliException"/> fırlatır.
    /// </summary>
    Task<TotpKurulumDto> TotpBaslatAsync();

    /// <summary>
    /// Kurulumu tamamlar: kod doğruysa korumayı açar. Kod yanlışsa
    /// <see cref="Validation.IsKuraliException"/> fırlatır.
    /// </summary>
    Task TotpDogrulaVeAcAsync(TotpDogrulaDto request);

    /// <summary>
    /// Korumayı kapatır ve gizli anahtarı siler. ŞİFRE ister — kapatma,
    /// güvenliği azaltan bir işlem ve açık kalmış bir oturumla yapılamamalı.
    /// </summary>
    Task TotpKapatAsync(TotpKapatDto request);

    /// <summary>Giriş yapmış kullanıcının iki adımlı doğrulama durumu.</summary>
    Task<TotpDurumDto> TotpDurumAsync();
}
