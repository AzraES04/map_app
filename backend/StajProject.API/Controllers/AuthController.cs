using Microsoft.AspNetCore.Mvc;
using StajProject.Business.DTOs;
using StajProject.Business.Services;

namespace StajProject.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>Kullanıcı adı ve şifre ile giriş yapar, başarılıysa JWT döner.</summary>
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
    {
        var response = await _authService.LoginAsync(request);
        if (response is null)
        {
            return Unauthorized(new { message = "Kullanıcı adı veya şifre hatalı." });
        }

        return Ok(response);
    }
}
