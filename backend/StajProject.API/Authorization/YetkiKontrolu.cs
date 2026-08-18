using Microsoft.AspNetCore.Mvc;
using StajProject.Business.Services;

namespace StajProject.API.Authorization;

/// <summary>
/// Yetki filtrelerinin ORTAK gövdesi.
///
/// İki filtremiz var — biri yetki adını sabit alır (<see cref="YetkiGerekliAttribute"/>),
/// diğeri controller'dan okur (<see cref="EklemeYetkisiGerekliAttribute"/>) — ve
/// ikisi boru hattının farklı noktalarında çalışıyor. Ortak olan tek şey
/// "kullanıcıyı bul, yetkisine bak, yoksa hata sonucu üret" kısmı; kopyalamak
/// yerine burada topluyoruz.
///
/// Metot sonucu KENDİSİ atamıyor, geri döndürüyor: iki filtre tipinin
/// <c>Result</c> özelliği ayrı sınıflarda tanımlı olduğu için ortak bir
/// parametre tipi yok. Sonucu döndürüp atamayı çağırana bırakmak,
/// aynı işi iki kez yazmaktan da yansımayla uğraşmaktan da temiz.
/// </summary>
internal static class YetkiKontrolu
{
    /// <summary>
    /// Giriş yapmış kullanıcının verilen yetkisi var mı?
    /// Varsa <c>null</c> (istek devam etsin), yoksa 401/403 sonucu döner.
    /// </summary>
    public static async Task<IActionResult?> DogrulaAsync(HttpContext httpContext, string yetkiAdi)
    {
        // Filtreler DI konteynerinden nesne alamaz (öznitelikler derleme zamanı
        // sabitleridir), bu yüzden servisleri istek kapsamından çözüyoruz.
        var currentUser = httpContext.RequestServices.GetRequiredService<ICurrentUserService>();
        var permissionService = httpContext.RequestServices.GetRequiredService<IPermissionService>();

        var kullaniciId = currentUser.UserId;
        if (kullaniciId is null)
        {
            // [Authorize] normalde bunu zaten yakalar; yine de tek başına güvenli olsun.
            return new UnauthorizedObjectResult(
                new { message = "Bu işlem için giriş yapmış olmanız gerekiyor." });
        }

        if (await permissionService.HasPermissionAsync(kullaniciId.Value, yetkiAdi))
        {
            return null;   // yetkili — istek yoluna devam etsin
        }

        // 403: "kim olduğunu biliyoruz ama bu iş senin yetkinde değil".
        // Mesaj hangi yetkinin eksik olduğunu söylüyor; kullanıcı yöneticiden
        // ne isteyeceğini bilsin diye.
        return new ObjectResult(
            new { message = $"Bu işlem için \"{yetkiAdi}\" yetkisine sahip olmanız gerekiyor." })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
