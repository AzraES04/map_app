using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using StajProject.API.Authorization;
using StajProject.API.Controllers;
using StajProject.Business.Services;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 17 / Madde 2 — "Ulaşım rolündeki kullanıcılar (Operatör ve Kullanıcı)
/// POI verilerini SADECE GÖRÜNTÜLEME yetkisine sahip olmalıdır."
///
/// ---- BU DOSYA NEDEN VAR? ----
///
/// Aynı kural Ödev 16'da da vardı ve <c>UlasimTests</c> içinde test edilmişti.
/// Ama o test ROL TANIMINA bakıyor: "Ulaşım Operatörü'nün yetki listesinde
/// PoiEkleme yok." Bu, elle yazılmış bir yasaklı-yetki listesine bağlı:
///
///     var yasakli = new[] { PoiEkleme, PoiYonetimi, NoktaEkleme, ... };
///
/// Sorun şu: yarın POI'ye yeni bir yazma ucu ve yeni bir yetki eklenirse
/// (örneğin "POI Onaylama"), o liste güncellenmedikçe test yeşil kalır —
/// ulaşım rolüne o yetki verilmiş olsa bile.
///
/// Buradaki test listeyi ELLE TUTMUYOR: POI controller'ının YAZMA uçlarını
/// yansımayla okuyup hangi yetkileri istediklerini kendisi buluyor, sonra
/// ulaşım rollerinin hiçbirinde o yetkilerden bulunmadığını doğruluyor.
/// Yeni bir uç eklendiğinde test onu kendiliğinden hesaba katıyor.
///
/// ---- "GÖRÜNTÜLEME" TARAFI NASIL SAĞLANIYOR? ----
///
/// Bir yetki EKLEYEREK değil, hiçbir şey eklemeyerek: <c>GET /api/poi</c>
/// bilinçli olarak yetkisiz, çünkü POI ortak referans verisi. Aşağıdaki
/// <see cref="PoiOkumaUclari_YetkiISTEMEZ"/> tam olarak bunu koruyor —
/// biri okuma ucuna yetki özniteliği eklerse ulaşım rolleri POI'leri
/// göremez hâle gelir ve ödevin şartı sessizce bozulur.
/// </summary>
public class UlasimPoiYetkiTests
{
    private static readonly string[] UlasimRolleri =
    {
        "Ulaşım Operatörü",
        "Ulaşım Kullanıcısı",
    };

    /// <summary>Değiştirme etkisi olan HTTP metotları.</summary>
    private static readonly Type[] YazmaMetotlari =
    {
        typeof(HttpPostAttribute), typeof(HttpPutAttribute),
        typeof(HttpDeleteAttribute), typeof(HttpPatchAttribute),
    };

    private static IEnumerable<MethodInfo> Uclar(Type controller)
        => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static bool YazmaUcuMu(MethodInfo m)
        => YazmaMetotlari.Any(t => m.GetCustomAttributes(t, true).Length > 0);

    /// <summary>
    /// POI ile ilgili controller'ların YAZMA uçlarının istediği yetki adları.
    ///
    /// İki controller birden: <c>PoiController</c> (POI kayıtları) ve
    /// <c>AdminPoiCategoriesController</c> (kategori ağacı). İkincisini
    /// atlasaydık, ulaşım rolüne "POI Yönetimi" verilmesi bu testten
    /// kaçardı — oysa o yetki kategori ağacını değiştirmeye yetiyor.
    /// </summary>
    private static HashSet<string> PoiYazmaYetkileri()
    {
        var yetkiler = new HashSet<string>(StringComparer.Ordinal);

        foreach (var controller in PoiControllerlari())
        {
            // Sınıf düzeyindeki öznitelik (örn. AdminPoiCategoriesController
            // tamamen "POI Yönetimi" altında olabilir).
            foreach (var oznitelik in controller.GetCustomAttributes<YetkiGerekliAttribute>(true))
            {
                yetkiler.Add(oznitelik.YetkiAdi);
            }

            foreach (var metot in Uclar(controller).Where(YazmaUcuMu))
            {
                foreach (var oznitelik in metot.GetCustomAttributes<YetkiGerekliAttribute>(true))
                {
                    yetkiler.Add(oznitelik.YetkiAdi);
                }
            }
        }

        return yetkiler;
    }

    private static List<Type> PoiControllerlari()
        => typeof(PoiController).Assembly
            .GetTypes()
            .Where(t => t.Name.Contains("Poi", StringComparison.OrdinalIgnoreCase)
                     && typeof(ControllerBase).IsAssignableFrom(t)
                     && !t.IsAbstract)
            .ToList();

    // ==================================================================

    [Fact]
    public void PoiControllerlari_Bulunabiliyor()
    {
        // Testin kendisini koruyan test: sınıflar yeniden adlandırılırsa
        // yukarıdaki arama boş küme döner ve asıl testler "yasaklı yetki yok"
        // diyerek SESSİZCE geçerdi. Kırılamayan bir koruma, koruma değildir.
        var controllerlar = PoiControllerlari();

        Assert.Contains(controllerlar, t => t == typeof(PoiController));
        Assert.True(controllerlar.Count >= 2,
            $"POI controller'ı beklenenden az bulundu: {controllerlar.Count}");
    }

    [Fact]
    public void PoiYazmaUclari_YetkiIstiyor()
    {
        // Bu da testin dayanağını koruyor: uçların hiçbiri yetki istemiyorsa
        // aşağıdaki "ulaşım rolünde bu yetkiler yok" testi boş bir kümeyle
        // çalışır ve hiçbir şey ispatlamaz.
        var yetkiler = PoiYazmaYetkileri();

        Assert.NotEmpty(yetkiler);
        Assert.Contains(Business.Auth.Yetkiler.PoiEkleme, yetkiler);
    }

    [Theory]
    [InlineData("Ulaşım Operatörü")]
    [InlineData("Ulaşım Kullanıcısı")]
    public void UlasimRolleri_HicbirPoiYAZMAYetkisiTasimaz(string rolAdi)
    {
        var rol = DatabaseSeeder.BaslangicRolleriTest.Single(r => r.Ad == rolAdi);
        var yazmaYetkileri = PoiYazmaYetkileri();

        var ihlaller = rol.Yetkiler.Where(yazmaYetkileri.Contains).ToList();

        Assert.True(ihlaller.Count == 0,
            $"\"{rolAdi}\" rolü POI'yi DEĞİŞTİRMEYE yeten yetki(ler) taşıyor: "
            + string.Join(", ", ihlaller)
            + "\n\nÖdev 17 / Madde 2: ulaşım rolleri POI verilerini yalnızca "
            + "GÖRÜNTÜLEYEBİLİR.");
    }

    [Fact]
    public void PoiOkumaUclari_YetkiISTEMEZ()
    {
        // "Görüntüleyebilsinler" koşulunun karşılığı: okuma ucuna yetki
        // KOYMAMAK. Biri buraya bir öznitelik eklerse "Ulaşım Kullanıcısı"
        // (hiç yetkisi olmayan rol) POI'leri göremez hâle gelir — ve bu,
        // rol tanımına bakan testlerin GÖREMEYECEĞİ bir bozulma.
        var okumaUclari = Uclar(typeof(PoiController))
            .Where(m => m.GetCustomAttributes(typeof(HttpGetAttribute), true).Length > 0)
            .ToList();

        Assert.NotEmpty(okumaUclari);

        foreach (var uc in okumaUclari)
        {
            var oznitelik = uc.GetCustomAttributes<YetkiGerekliAttribute>(true).FirstOrDefault();

            Assert.True(oznitelik is null,
                $"PoiController.{uc.Name} okuma ucu \"{oznitelik?.YetkiAdi}\" yetkisi istiyor. "
                + "POI ortak referans verisidir; okuma yetkisiz olmalı, aksi hâlde "
                + "hiçbir yetkisi olmayan \"Ulaşım Kullanıcısı\" POI'leri göremez.");
        }
    }

    [Fact]
    public void UlasimRolleri_KendiIslerineYetiyor()
    {
        // Kısıtlamanın aşırıya kaçmadığının kontrolü: operatör hâlâ kendi
        // modülünü yönetebilmeli. Yalnızca "şu yetkiler YOK" diye test
        // etseydik, bütün yetkileri silen bir değişiklik de yeşil kalırdı.
        var operatorRolu = DatabaseSeeder.BaslangicRolleriTest.Single(r => r.Ad == "Ulaşım Operatörü");

        Assert.Contains(Business.Auth.Yetkiler.DurakEkleme, operatorRolu.Yetkiler);
        Assert.Contains(Business.Auth.Yetkiler.GuzergahYonetimi, operatorRolu.Yetkiler);
    }

    [Fact]
    public void UlasimRolleri_SeedListesindeGercektenVar()
    {
        // Rol adları metin olarak yazılı; biri yeniden adlandırırsa
        // yukarıdaki Single(...) çağrıları patlar. Bu test hatayı ANLAŞILIR
        // bir mesajla veriyor.
        foreach (var ad in UlasimRolleri)
        {
            Assert.True(
                DatabaseSeeder.BaslangicRolleriTest.Any(r => r.Ad == ad),
                $"\"{ad}\" rolü başlangıç rolleri arasında yok. Yeniden adlandırıldıysa "
                + "bu testteki adlar da güncellenmeli.");
        }
    }
}
