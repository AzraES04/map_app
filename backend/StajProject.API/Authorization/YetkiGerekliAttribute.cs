using Microsoft.AspNetCore.Mvc.Filters;

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
///
/// Sınıfa yazılırsa o controller'ın TÜM uçlarına, metoda yazılırsa yalnızca
/// o uca uygulanır. Taban sınıftaki bir metoda yazıldığında ondan türeyen
/// bütün controller'lar için geçerli olur — üç geometri controller'ında
/// bunu böyle kullanıyoruz.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class YetkiGerekliAttribute : Attribute, IAsyncAuthorizationFilter
{
    /// <summary>
    /// Aranan yetkinin adı.
    ///
    /// Salt okunur ve PUBLIC: yapısal denetim testi
    /// (<c>YetkilendirmeDenetimiTests</c>) yansımayla "bu uç hangi yetkiyi
    /// istiyor?" diye soruyor. Alan private kalsaydı test yalnızca
    /// özniteliğin VARLIĞINI görebilir, DOĞRU yetkiyi istediğini
    /// doğrulayamazdı.
    /// </summary>
    public string YetkiAdi { get; }

    public YetkiGerekliAttribute(string yetkiAdi)
    {
        YetkiAdi = yetkiAdi;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Sonuç doldurulursa boru hattı burada kesilir, controller hiç çalışmaz.
        context.Result = await YetkiKontrolu.DogrulaAsync(context.HttpContext, YetkiAdi)
                         ?? context.Result;
    }
}
