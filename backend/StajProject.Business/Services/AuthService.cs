using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

public class AuthService :IAuthService       // BOŞLUK 1
{
    private readonly IUserRepository _userRepository;   // BOŞLUK 2
    private readonly JwtSettings _jwtSettings;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(IUserRepository userRepository, IOptions<JwtSettings> jwtOptions)
    {
        _userRepository = userRepository;              // BOŞLUK 3
        _jwtSettings = jwtOptions.Value;      // BOŞLUK 4
    }

public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto request)
    {
        // 1) Kullanıcıyı kullanıcı adına göre bul
        var user = await _userRepository.GetByUsernameAsync(request.Username);

        // 2) Kullanıcı bulunamadıysa giriş başarısız
        if (user is null)
        {
            return null;
        }
    
    // 3) Şifreyi doğrula (hash karşılaştırması)
        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        // 4) Şifre yanlışsa giriş başarısız
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        // 5) Token ne zaman geçersiz olacak?
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes);

        // 6) Token'ın içine yazılacak bilgiler (claim = iddia/bilgi)
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // 7) İmza anahtarını hazırla
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // 8) Token'ı oluştur
        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires:expiresAt,
            signingCredentials: credentials);

        // 9) Cevabı döndür
        return new LoginResponseDto
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAt = expiresAt,
            Username = user.Username
        };
    }
}
