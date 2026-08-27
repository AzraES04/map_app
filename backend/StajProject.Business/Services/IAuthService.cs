using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

public interface IAuthService
{
    /// <summary>Kullanıcı adı/şifre doğruysa JWT üretir, yanlışsa null döner.</summary>
    Task<LoginResponseDto?> LoginAsync(LoginRequestDto request);

    /// <summary>
    /// Yeni hesap açar (Ödev 10). Hesap YÖNETİCİ ONAYI bekler; token dönmez.
    /// Kullanıcı adı doluysa <see cref="Validation.IsKuraliException"/> fırlatır.
    /// </summary>
    Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request);
}
