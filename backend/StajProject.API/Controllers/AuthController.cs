using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StajProject.Business.DTOs;
using StajProject.Business.Services;

namespace StajProject.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Kullanıcı adı ve şifre ile giriş yapar, başarılıysa JWT döner.
    /// IP başına dakikada 5 deneme ile sınırlıdır (kaba kuvvet koruması).
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("giris")]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
    {
        // Ödev 5 / Madde 1: standart hata yönetimi kalıbı
        try
        {
            var response = await _authService.LoginAsync(request);
            if (response is null)
            {
                // Kullanıcı yok, şifre yanlış, hesap pasif veya silinmiş —
                // hepsine AYNI cevap. Farklı mesaj vermek saldırgana
                // "bu kullanıcı adı var" bilgisini doğrulardı.
                return Unauthorized(new { message = "Kullanıcı adı veya şifre hatalı." });
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Giriş sırasında beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Giriş yapılamadı. Lütfen tekrar deneyin." });
        }
    }
}
