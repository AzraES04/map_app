using Microsoft.AspNetCore.Mvc.Filters;

namespace StajProject.API.Authorization;

/// <summary>
/// Yetki adını CONTROLLER'IN KENDİSİNDEN okuyan filtre.
///
/// Neden ayrı bir filtre gerekti? Üç geometri controller'ı (nokta / çizgi / poligon)
/// gövdesini tek bir taban sınıftan alıyor; "Create" ucu üçünde de AYNI metot.
/// Ama gereken yetki üçünde FARKLI: "Point Ekleme", "Line Ekleme", "Polygon Ekleme".
///
/// Öznitelik parametresi derleme zamanı sabiti olmak zorunda olduğu için
/// [YetkiGerekli("...")] ile bu üç durumu ayıramazdık. Çözüm: yetki adını
/// çalışma anında, isteği karşılayan controller nesnesinden sormak.
///
/// DİKKAT — bu bir AUTHORIZATION filtresi değil, ACTION filtresidir.
/// Sebebi teknik: yetkilendirme filtreleri boru hattında controller nesnesi
/// daha OLUŞTURULMADAN çalışır, dolayısıyla context.Controller orada yoktur.
/// Action filtresi ise nesne kurulduktan hemen sonra, metot gövdesi
/// çalışmadan ÖNCE devreye girer — bize gereken tam olarak bu an.
/// Kullanıcı açısından sonuç aynı: yetki yoksa metot hiç çalışmaz, 403 döner.
///
/// Alternatif, taban sınıftaki Create metodunu üç controller'da da override edip
/// her birine ayrı öznitelik yazmaktı — 3 kat kod tekrarı ve HTTP özniteliklerinin
/// (route, verb) de kopyalanması demekti.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class EklemeYetkisiGerekliAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // context.Controller: isteği karşılayan controller ÖRNEĞİ.
        // Arayüzü uygulamıyorsa bu filtre yanlış yere konmuş demektir; sessizce
        // geçmek yerine hatayı hemen görünür kılıyoruz.
        if (context.Controller is not IEklemeYetkisiTasiyan controller)
        {
            throw new InvalidOperationException(
                $"[EklemeYetkisiGerekli] yalnızca {nameof(IEklemeYetkisiTasiyan)} uygulayan " +
                $"controller'larda kullanılabilir. Gelen: {context.Controller.GetType().Name}");
        }

        context.Result = await YetkiKontrolu.DogrulaAsync(context.HttpContext, controller.EklemeYetkisi);

        // Yetki kontrolü sonucu doldurduysa (401/403) metodu HİÇ çalıştırma.
        if (context.Result is null)
        {
            await next();
        }
    }
}

/// <summary>
/// "Bu controller'da yeni kayıt oluşturmak hangi yetkiyi gerektirir?"
/// sorusunun cevabını taşıyan sözleşme.
/// </summary>
public interface IEklemeYetkisiTasiyan
{
    /// <summary>Örn. PointsController için "Point Ekleme".</summary>
    string EklemeYetkisi { get; }
}
