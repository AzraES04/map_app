using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.API.Authorization;
using StajProject.API.Controllers;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// YAPISAL DENETİM: hiçbir yazma ucu yetki sisteminin dışında kalamaz.
///
/// NEDEN BÖYLE BİR TEST?
/// Ödev 6'da dinamik yetkilendirme kurulurken <c>LocationsController</c>
/// atlanmıştı. Sonuç, aylarca fark edilmeyen bir açıktı: hiçbir yetkisi
/// olmayan bir kullanıcı <c>POST /api/locations</c> ile kayıt ekleyebiliyor,
/// <c>DELETE</c> ile — orada silme HARD DELETE olduğu için — kaydı kalıcı
/// olarak yok edebiliyordu.
///
/// Tek tek "şu uç 403 dönüyor mu?" testleri bu hatayı YAKALAYAMAZDI: kimse
/// unuttuğu controller için test yazmaz. Bu yüzden test tek bir uca değil
/// KURALA bakıyor:
///
///     Her POST / PUT / DELETE ucu ya bir yetki özniteliği taşır,
///     ya da aşağıdaki İSTİSNA listesinde gerekçesiyle yazılıdır.
///
/// Yeni bir controller eklenip yetkisi unutulduğunda test kırmızı yanıyor ve
/// geliştirici iki şeyden birini yapmak zorunda kalıyor: yetkiyi eklemek ya
/// da istisnayı gerekçesiyle yazmak. İkisi de bilinçli bir karar.
/// </summary>
public class YetkilendirmeDenetimiTests
{
    /// <summary>
    /// Yetki özniteliği İSTEMEYEN yazma uçları — her biri gerekçeli.
    ///
    /// Biçim: "ControllerAdı.MetotAdı".
    ///
    /// Liste kısa tutulmalı: uzadıkça kuralın anlamı azalır. Şu an iki
    /// gerekçe var ve ikisi de projenin başka yerlerinde de geçerli olan
    /// bilinçli tasarım kararları.
    /// </summary>
    private static readonly Dictionary<string, string> Istisnalar = new()
    {
        ["TurController.MisafirYoklamaCevabi"] =
            "Kimliksiz uç: yetki özniteliği takılamaz çünkü cevap veren "
            + "misafirin hesabı yok. Yetkilendirmenin yerini KATILIM KODU "
            + "alıyor (kod geçersizse 404) ve uç veritabanına yazmıyor — "
            + "cevap yalnızca bellekteki yoklama sayacına gidiyor. "
            + "Gerekçenin uzunu AnonimUclar listesinde.",

        // Giriş ve kayıt: kimlik doğrulamadan ÖNCE çalışıyorlar, yetki
        // aramak mantıksız olurdu (henüz kim olduğu bilinmiyor). İkisi de
        // [EnableRateLimiting("giris")] ile korunuyor.
        ["AuthController.Login"] = "Kimlik doğrulamadan önce çalışır; hız sınırı ile korunuyor.",
        ["AuthController.Register"] = "Kayıt olma herkese açık; hesap yönetici onayı bekler.",

        // Eksik 5 — yenileme ve çıkış. İkisi de kimlik doğrulamadan ÖNCE
        // çalışıyor ve kanıt olarak YETKİ değil ANAHTARIN KENDİSİ kabul
        // ediliyor: onu bilen zaten oturumun sahibidir.
        //
        // Çıkışta [Authorize] bilerek yok — erişim token'ının süresi dolmuşken
        // de çıkabilmek gerekiyor; aksi hâlde tam da oturumu bırakmak isteyen
        // kullanıcı, bunu yapamadığı için yenileme anahtarını sunucuda açık
        // bırakırdı.
        ["AuthController.Refresh"] = "Anahtarın kendisi kanıt; hız sınırı ile korunuyor (AuthYenilemeTests).",
        ["AuthController.Logout"] = "Süresi dolmuş token'la da çıkılabilmeli; anahtarın kendisi kanıt (AuthYenilemeTests).",

        // İki adımlı doğrulama (TOTP). Dördü de KENDİ HESABININ ayarı —
        // yönetim işlemi değil, dolayısıyla bir "yetki" ile eşleşmiyor.
        // Kanıt zinciri her birinde farklı ve her biri kendi testinde:
        ["AuthController.IkinciAdim"] = "Giriş akışının 2. adımı; kanıt ara token + kod, hız sınırlı (IkinciAdimTests).",
        ["AuthController.TotpBaslat"] = "Kendi hesabının ayarı; [Authorize] yeterli (IkinciAdimTests).",
        ["AuthController.TotpDogrula"] = "Kendi hesabının ayarı; kanıt TOTP kodu, hız sınırlı (IkinciAdimTests).",
        ["AuthController.TotpKapat"] = "Kendi hesabının ayarı; kanıt ŞİFRE, hız sınırlı (IkinciAdimTests).",

        // Çöp kutusu: gereken yetki KAYDIN TÜRÜNE göre değişiyor (noktayı geri
        // almak "Kayıt Silme", rolü geri almak "Rol Yönetimi" istiyor). Tek
        // yetki adı alan [YetkiGerekli] bunu ifade edemiyor.
        //
        // Yeni bir "Çöp Kutusu Yönetimi" yetkisi UYDURULMADI: o yetkiye sahip
        // biri, silemeyeceği bir kaydı geri alabilir hâle gelirdi.
        ["CopKutusuController.GeriAl"] = "Yetki KAYIT TÜRÜNE göre değişiyor — kural serviste (CopKutusuTests).",
        ["CopKutusuController.KaliciSil"] = "Aynı gerekçe: yetki KAYIT TÜRÜNE göre değişiyor, kural serviste (CopKutusuTests).",

        // ---- Canlı tur oturumu ----
        //
        // Üçünde de gereken izin bir YETKİ değil, OTURUMLA KURULAN İLİŞKİ.
        // "Tur Yönetimi" yetkisine bağlasaydık tur açabilen herkes
        // BAŞKASININ grubunu yönlendirebilir ya da başkasının katılımını
        // sonlandırabilirdi — hata vermeyen, yalnızca yanlış davranan bir açık.
        //
        // Kurallar serviste (TurOturumServisi) ve testleri TurOturumTests'te:
        //   • Katilimci_turu_ilerletemiyor
        //   • Baskasinin_turunun_oturumu_devralinamiyor
        //   • Biten_turun_koduyla_katilinamiyor
        ["TurController.OturumaKatil"] = "Katılmak izleme işi; kanıt KATILIM KODU, ayrıca [Authorize] (TurOturumTests).",
        ["TurController.OturumGuncelle"] = "Yalnızca O OTURUMUN rehberi ilerletebilir — kural serviste (TurOturumTests).",
        ["TurController.OturumdanAyril"] = "Kendi katılımını sonlandırma; başkasınınkine dokunamıyor (TurOturumTests).",

        // POI ve durak GÜNCELLEME/SİLME: gereken yetki
        // "X Yönetimi" VEYA ("X Ekleme" + kaydın sahibi olmak).
        // Bu VEYA'yı tek yetki adı alan [YetkiGerekli] ifade edemiyor;
        // kural servis katmanında (PoiService.YetkiliMiAsync,
        // UlasimService.YetkiliMiAsync) ve testleri orada.
        ["PoiController.Update"] = "Sahiplik VEYA yönetim yetkisi — kural serviste (PoiTests).",
        ["PoiController.Delete"] = "Sahiplik VEYA yönetim yetkisi — kural serviste (PoiTests).",
        ["PoiController.SetActive"] = "Sahiplik VEYA yönetim yetkisi — kural serviste (PoiTests).",
        ["UlasimController.DurakGuncelle"] = "Sahiplik VEYA yönetim yetkisi — kural serviste (UlasimTests).",
        ["UlasimController.DurakSil"] = "Sahiplik VEYA yönetim yetkisi — kural serviste (UlasimTests).",

        // Ödev 18 — rota ÖNİZLEMESİ. POST olması "değiştiriyor" demek değil:
        // gövdesi olan bir SORGU. Ara nokta listesi (WKT noktaları) adres
        // satırına sığdırılamayacağı için GET yerine POST seçildi.
        //
        // Hiçbir şey yazmıyor; girdisi olan güzergah ve durak verisi zaten
        // yetkisiz okunabiliyor. KALICI adım ayrı bir uç: RotaOlustur ve o
        // "Güzergah Yönetimi" istiyor.
        ["UlasimController.RotaOnizleme"] = "Yazmıyor — gövdesi olan bir sorgu; kalıcı adım RotaOlustur (RotaAlternatifTests).",
    };

    /// <summary>Değiştirme etkisi olan HTTP metotları.</summary>
    private static readonly Type[] YazmaMetotlari =
    {
        typeof(HttpPostAttribute), typeof(HttpPutAttribute),
        typeof(HttpDeleteAttribute), typeof(HttpPatchAttribute),
    };

    private static IEnumerable<(Type Controller, MethodInfo Metot)> YazmaUclari()
    {
        // API derlemesindeki bütün controller'lar. Tek tek listelemiyoruz:
        // testin bütün değeri, YENİ eklenen controller'ı da kendiliğinden
        // görmesinden geliyor.
        var derleme = typeof(LocationsController).Assembly;

        var controllerlar = derleme.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var controller in controllerlar)
        {
            var metotlar = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var metot in metotlar)
            {
                var yazma = metot.GetCustomAttributes()
                    .Any(a => YazmaMetotlari.Contains(a.GetType()));

                if (yazma) yield return (controller, metot);
            }
        }
    }

    /// <summary>Metotta ya da tanımlandığı controller'da yetki özniteliği var mı?</summary>
    private static bool YetkiVar(Type controller, MethodInfo metot)
    {
        bool Tasiyor(IEnumerable<Attribute> oznitelikler)
            => oznitelikler.Any(a => a is YetkiGerekliAttribute or EklemeYetkisiGerekliAttribute);

        if (Tasiyor(metot.GetCustomAttributes())) return true;

        // Sınıf düzeyinde verilmiş olabilir (ve taban sınıftan miras da gelebilir).
        return Tasiyor(controller.GetCustomAttributes(inherit: true).Cast<Attribute>());
    }

    // ==================================================================

    /// <summary>
    /// TESTİN KALBİ: yetkisiz kalmış bir yazma ucu var mı?
    ///
    /// Hata mesajı doğrudan yapılacak işi söylüyor — [YetkiGerekli] ekle ya
    /// da istisnayı gerekçesiyle yaz.
    /// </summary>
    [Fact]
    public void HicbirYazmaUcuYetkisizKalmaz()
    {
        var korumasizlar = YazmaUclari()
            .Where(x => !YetkiVar(x.Controller, x.Metot))
            .Select(x => $"{x.Controller.Name}.{x.Metot.Name}")
            .Where(ad => !Istisnalar.ContainsKey(ad))
            .OrderBy(ad => ad)
            .ToList();

        Assert.True(korumasizlar.Count == 0,
            "Şu yazma uçlarında yetki özniteliği yok:\n  "
            + string.Join("\n  ", korumasizlar)
            + "\n\nYa [YetkiGerekli(...)] ekleyin, ya da servis katmanında "
            + "kontrol ediliyorsa Istisnalar sözlüğüne GEREKÇESİYLE yazın.");
    }

    /// <summary>
    /// İstisna listesi ÇÜRÜMESİN: adı değişen ya da silinen bir uç listede
    /// kalırsa, o satır artık hiçbir şeyi açıklamıyor demektir — ve bir
    /// sonraki geliştirici onu geçerli bir muafiyet sanabilir.
    /// </summary>
    [Fact]
    public void IstisnaListesindeOlmayanUcKalmaz()
    {
        var gercekUclar = YazmaUclari()
            .Select(x => $"{x.Controller.Name}.{x.Metot.Name}")
            .ToHashSet();

        var karsiligiYoklar = Istisnalar.Keys
            .Where(ad => !gercekUclar.Contains(ad))
            .OrderBy(ad => ad)
            .ToList();

        Assert.True(karsiligiYoklar.Count == 0,
            "İstisna listesinde artık var olmayan uçlar var:\n  "
            + string.Join("\n  ", karsiligiYoklar)
            + "\n\nSilinmiş ya da yeniden adlandırılmış olabilirler; listeden çıkarın.");
    }

    /// <summary>
    /// Her istisnanın BOŞ OLMAYAN bir gerekçesi olmalı. Gerekçe alanı, listeye
    /// düşüncesizce satır eklenmesini zorlaştırmak için var.
    /// </summary>
    [Fact]
    public void HerIstisnaGerekceTasir()
        => Assert.All(Istisnalar, x => Assert.False(string.IsNullOrWhiteSpace(x.Value), x.Key));

    // ------------------------------------------------------------------
    //  KİMLİKSİZ (anonim) uçlar
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>[AllowAnonymous]</c> taşıyan uçlar ve gerekçeleri.
    ///
    /// ---- BU LİSTE NEDEN SONRADAN EKLENDİ? ----
    /// Denetim yalnızca SINIF düzeyindeki <c>[Authorize]</c>'a bakıyordu.
    /// Metot düzeyinde <c>[AllowAnonymous]</c> onu eziyor ve testler bunu
    /// görmüyordu — nitekim misafir tur ucu eklendiğinde denetim hiç
    /// kırılmadan geçti. Kimliksiz bir uç, güvenlik yüzeyinin en dikkat
    /// isteyen parçası; sessizce eklenebiliyor olması başlı başına bir
    /// açıktı.
    /// </summary>
    private static readonly Dictionary<string, string> AnonimUclar = new()
    {
        // AuthController BURADA YOK ve olmamalı: onun uçları [AllowAnonymous]
        // taşımıyor, sınıfın kendisi [Authorize] altında değil. O durumu
        // ButunControllerlarAuthorizeAltinda testi ayrıca kayda geçiriyor.
        // Aynı istisnayı iki listede tutmak, birini güncelleyip diğerini
        // unutmaya davetiye olurdu.
        ["TurController.MisafirYoklamaCevabi"] =
            "Yoklama cevabı: cevap verecek kişinin hesabı yok, katılım kodu "
            + "o gruba ait olduğunun tek kanıtı. Yazdığı şey KAPALI bir "
            + "kümeden ('Buradayim'|'Degilim'|'Acil') ve yalnızca bellekteki "
            + "sayaca gidiyor — veritabanına hiçbir şey yazılmıyor. Serbest "
            + "metin alsaydık uç, kimliksiz bir mesaj kutusuna dönerdi.",

        ["TurController.Misafir"] =
            "Paylaşılan tur bağlantısı: kullanıcı hesabı olmayan misafir, "
            + "yalnızca katılım koduyla turu GÖRÜNTÜLÜYOR. Uç salt okuma; "
            + "cevapta katılımcı adları, id'ler ve katılım kodu yok "
            + "(bkz. MisafirTurDto). Kaba kuvvete karşı IP başına dakikada "
            + "60 istek sınırı var.",
    };

    /// <summary>Sınıf ya da metot düzeyinde <c>[AllowAnonymous]</c> taşıyan uçlar.</summary>
    private static IEnumerable<(Type Controller, MethodInfo Metot)> AnonimUcAdaylari()
        => typeof(LocationsController).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes(inherit: true).OfType<AllowAnonymousAttribute>().Any())
                .Select(m => (Controller: t, Metot: m)));

    /// <summary>
    /// Kimliksiz her uç, gerekçesiyle birlikte listede olmalı.
    ///
    /// Yeni bir <c>[AllowAnonymous]</c> eklemek artık bilinçli bir karar:
    /// test kırılıyor ve düzeltmenin yolu, o ucun neden herkese açık
    /// olduğunu yazmaktan geçiyor.
    /// </summary>
    [Fact]
    public void HicbirAnonimUc_GEREKCESIZ_kalmaz()
    {
        var yazilmamislar = AnonimUcAdaylari()
            .Select(x => $"{x.Controller.Name}.{x.Metot.Name}")
            .Where(ad => !AnonimUclar.ContainsKey(ad))
            .Distinct()
            .OrderBy(ad => ad)
            .ToList();

        Assert.True(yazilmamislar.Count == 0,
            "Şu uçlar [AllowAnonymous] taşıyor ama gerekçesi yazılmamış: "
            + string.Join(", ", yazilmamislar)
            + " — kimliksiz bir uç güvenlik yüzeyini genişletir. Gerçekten "
            + "gerekiyorsa AnonimUclar sözlüğüne GEREKÇESİYLE yazın; "
            + "gerekmiyorsa [AllowAnonymous] özniteliğini kaldırın.");
    }

    /// <summary>Anonim uç listesi de çürümesin (yazma uçlarındaki kuralın aynısı).</summary>
    [Fact]
    public void AnonimUcListesinde_karsiligi_olmayan_kalmaz()
    {
        var gercek = AnonimUcAdaylari()
            .Select(x => $"{x.Controller.Name}.{x.Metot.Name}")
            .ToHashSet();

        var artiklar = AnonimUclar.Keys.Where(ad => !gercek.Contains(ad)).OrderBy(a => a).ToList();

        Assert.True(artiklar.Count == 0,
            "Anonim uç listesinde artık var olmayan uçlar var: "
            + string.Join(", ", artiklar));
    }

    [Fact]
    public void HerAnonimUcGerekceTasir()
        => Assert.All(AnonimUclar, x => Assert.False(string.IsNullOrWhiteSpace(x.Value), x.Key));

    /// <summary>
    /// Bütün controller'lar <c>[Authorize]</c> altında olmalı — yetki
    /// özniteliği ayrı bir katman, ama önce KİMLİK doğrulanmalı.
    /// AuthController tek istisna: giriş ucunun kendisi anonim olmak zorunda.
    /// </summary>
    [Fact]
    public void ButunControllerlarAuthorizeAltinda()
    {
        var derleme = typeof(LocationsController).Assembly;

        var korumasizlar = derleme.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .Where(t => t != typeof(AuthController))
            .Where(t => !t.GetCustomAttributes(inherit: true).OfType<AuthorizeAttribute>().Any())
            .Select(t => t.Name)
            .OrderBy(ad => ad)
            .ToList();

        Assert.True(korumasizlar.Count == 0,
            "Şu controller'larda [Authorize] yok: " + string.Join(", ", korumasizlar));
    }

    /// <summary>
    /// Açığın KENDİSİNİ kilitleyen test: locations uçları artık yetki istiyor.
    ///
    /// Yukarıdaki genel kural bunu zaten kapsıyor; bu test o kuralın hangi
    /// somut olaydan doğduğunu koda yazıyor. Biri yarın "bu öznitelik ne
    /// işe yarıyor" diye silmek isterse, kırılan testin adı cevabı veriyor.
    /// </summary>
    [Fact]
    public void Locations_EklemeVeSilmeYetkiIster()
    {
        var controller = typeof(LocationsController);

        var ekle = controller.GetMethod(nameof(LocationsController.Create))!;
        var sil = controller.GetMethod(nameof(LocationsController.Delete))!;

        Assert.Equal(
            Business.Auth.Yetkiler.NoktaEkleme,
            ekle.GetCustomAttribute<YetkiGerekliAttribute>()?.YetkiAdi);

        Assert.Equal(
            Business.Auth.Yetkiler.KayitSilme,
            sil.GetCustomAttribute<YetkiGerekliAttribute>()?.YetkiAdi);
    }
}
