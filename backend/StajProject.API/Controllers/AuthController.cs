using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.Business.Validation;

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
        catch (IsKuraliException ex)
        {
            // Ödev 10: şifre doğru ama hesap onay bekliyor. Bu mesajı
            // göstermek gerekiyor — kullanıcı aksi hâlde neden giremediğini
            // bilemezdi. 403: kimlik doğru, erişim henüz açık değil.
            _logger.LogInformation(ex, "Onaylanmamış hesap giriş denedi");
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Giriş sırasında beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Giriş yapılamadı. Lütfen tekrar deneyin." });
        }
    }

    /// <summary>
    /// Yeni hesap açar (Ödev 10). Hesap yönetici onayına düşer, token DÖNMEZ.
    /// Giriş ucuyla aynı hız sınırına tabidir: kayıt ucu da kullanıcı adı
    /// taraması için kötüye kullanılabilir.
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting("giris")]
    public async Task<ActionResult<RegisterResponseDto>> Register([FromBody] RegisterRequestDto request)
    {
        try
        {
            return Ok(await _authService.RegisterAsync(request));
        }
        catch (IsKuraliException ex)
        {
            _logger.LogWarning(ex, "Kayıt reddedildi");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kayıt sırasında beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Kayıt yapılamadı. Lütfen tekrar deneyin." });
        }
    }
}
