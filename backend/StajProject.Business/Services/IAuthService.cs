using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

public interface IAuthService
{
    /// <summary>Kullanıcı adı/şifre doğruysa JWT üretir, yanlışsa null döner.</summary>
    Task<LoginResponseDto?> LoginAsync(LoginRequestDto request);
}
