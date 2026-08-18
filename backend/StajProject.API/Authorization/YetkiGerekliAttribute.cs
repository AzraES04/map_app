using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using StajProject.Business.Services;

namespace StajProject.API.Authorization;

/// <summary>
/// Bir ucu (endpoint) belirli bir YETKİYE bağlar (Ödev 6 / Madde 2).
///
/// <code>[YetkiGerekli(Yetkiler.KullaniciYonetimi)]</code>
///
/// Neden ASP.NET'in hazır [Authorize(Roles = "...")] mekanizmasını kullanmıyoruz?
/// Çünkü o, rol adlarını TOKEN'a yazar ve kodun içine sabitler. Bizim yetkiler
/// çalışma anında veritabanından değişiyor: yönetici panelden "Editör" rolüne
/// yeni bir yetki eklediğinde, kullanıcının yeniden giriş yapmasını beklemeden
/// etkili olmalı. Bu yüzden kontrol her istekte veritabanındaki GÜNCEL duruma
/// bakar — dinamik yetkilendirmenin anlamı da bu.
///
/// Kimlik doğrulama işi bu filtrenin değil: uçlar ayrıca [Authorize] taşır,
/// token yoksa istek buraya gelmeden 401 ile döner.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class YetkiGerekliAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _yetkiAdi;

    public YetkiGerekliAttribute(string yetkiAdi)
    {
        _yetkiAdi = yetkiAdi;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Filtreler DI konteynerinden nesne alamaz (öznitelikler derleme zamanı
        // sabitleridir), bu yüzden servisi istek kapsamından çözüyoruz.
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserService>();
        var permissionService = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();

        var kullaniciId = currentUser.UserId;
        if (kullaniciId is null)
        {
            // [Authorize] normalde bunu zaten yakalar; yine de tek başına güvenli olsun.
            context.Result = new UnauthorizedObjectResult(
                new { message = "Bu işlem için giriş yapmış olmanız gerekiyor." });
            return;
        }

        if (!await permissionService.HasPermissionAsync(kullaniciId.Value, _yetkiAdi))
        {
            // 403: "kim olduğunu biliyoruz ama bu iş senin yetkinde değil".
            // 404 döndürüp varlığı gizlemek burada gereksiz — yönetim uçlarının
            // var olduğu zaten sır değil, eksik olan yalnızca yetki.
            context.Result = new ObjectResult(
                new { message = $"Bu işlem için \"{_yetkiAdi}\" yetkisine sahip olmanız gerekiyor." })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
    }
}
