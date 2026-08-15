using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using StajProject.Business.Geo;
using StajProject.Business.Services;

namespace StajProject.API.Services;

/// <summary>
/// <see cref="ICurrentUserService"/> arayüzünün HTTP tarafındaki gerçeklemesi.
/// JWT içindeki "sub" claim'ini okur — AuthService token üretirken oraya
/// kullanıcının id'sini yazıyor.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserService(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public int? UserId
    {
        get
        {
            var kullanici = _accessor.HttpContext?.User;
            if (kullanici?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            // ASP.NET, JWT'deki "sub" claim'ini varsayılan olarak NameIdentifier'a
            // eşler; ikisini de deniyoruz ki eşleme davranışı değişirse kırılmasın.
            var ham = kullanici.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? kullanici.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            return int.TryParse(ham, out var id) ? id : null;
        }
    }

    public int RequireUserId()
        => UserId ?? throw new WktFormatException("Bu işlem için giriş yapmış olmanız gerekiyor.");
}
