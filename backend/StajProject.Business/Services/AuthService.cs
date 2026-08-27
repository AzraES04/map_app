using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Validation;
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
    
        // 2.5) Hesap durumu uygun mu? (Ödev 3 / Görev 1)
        // Not: Bilerek "hesabınız pasif" gibi ayrı bir mesaj DÖNMÜYORUZ. Öyle yapsaydık
        // saldırgana "bu kullanıcı adı sistemde var" bilgisini doğrulamış olurduk
        // (user enumeration). Dışarıya tek tip "kullanıcı adı veya şifre hatalı" gider.
        if (user.IsDeleted || !user.IsActive)
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

        // 4.5) Hesap yönetici onayından geçti mi? (Ödev 10)
        //
        // Bu kontrol ŞİFRE DOĞRULANDIKTAN SONRA yapılıyor — sırası önemli.
        // Önce yapsaydık, şifreyi bilmeyen biri de "onay bekliyor" cevabını
        // alır ve o kullanıcı adının sistemde var olduğunu öğrenirdi
        // (user enumeration). Şimdi bu bilgiyi yalnızca şifreyi zaten bilen,
        // yani hesabın gerçek sahibi görüyor — ve onun bilmeye hakkı var:
        // aksi hâlde "kayıt oldum ama giremiyorum" diye sebepsiz beklerdi.
        if (!user.IsApproved)
        {
            throw new IsKuraliException(
                "Hesabınız henüz yönetici onayından geçmedi. Onaylandığında giriş yapabilirsiniz.");
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

    /// <summary>
    /// Yeni hesap açar (Ödev 10 — giriş ekranındaki "Kayıt Ol").
    ///
    /// NEDEN ONAY GEREKİYOR? Kayıt ucu herkese açık. Onay olmasaydı internetten
    /// gelen herkes anında giriş yapıp haritayı ve envanteri görürdü. Onayla
    /// birlikte akış şu oluyor: kullanıcı kaydolur → yönetici panelde "onay
    /// bekliyor" rozetini görür → onaylar ve rol atar → kullanıcı girebilir.
    ///
    /// Kayıt olan kullanıcıya ROL VERİLMİYOR: yetkisiz bir hesap giriş yapsa
    /// bile hiçbir şey ekleyip silemez. Yetki dağıtımı yöneticinin işi (Ödev 6).
    /// </summary>
    public async Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request)
    {
        var kullaniciAdi = request.Username.Trim();

        if (kullaniciAdi.Length == 0)
        {
            throw new IsKuraliException("Kullanıcı adı boş olamaz.");
        }

        // Kullanıcı adı dolu mu? Burada "bu ad alınmış" demek zorundayız —
        // aksi hâlde kullanıcı neden kaydolamadığını anlayamaz. Kayıt
        // ekranında bu bilgi kaçınılmaz; bu yüzden uç hız sınırına tabi.
        if (await _userRepository.GetByUsernameAsync(kullaniciAdi) is not null)
        {
            throw new IsKuraliException($"\"{kullaniciAdi}\" kullanıcı adı zaten alınmış.");
        }

        var yeni = new User
        {
            Username = kullaniciAdi,
            IsActive = true,
            IsApproved = false,   // asıl kural burada
        };
        yeni.PasswordHash = _passwordHasher.HashPassword(yeni, request.Password);

        await _userRepository.AddAsync(yeni);

        return new RegisterResponseDto
        {
            Username = kullaniciAdi,
            Message = "Kaydınız alındı. Yönetici onayından sonra giriş yapabilirsiniz.",
        };
    }
}
