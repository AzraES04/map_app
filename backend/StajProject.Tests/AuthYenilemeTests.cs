using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.DataAccess.Context;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Eksik 5 — yenileme anahtarı (refresh token) testleri.
///
/// Buradaki testlerin çoğu "çalışıyor mu?" değil "KÖTÜYE KULLANILAMIYOR MU?"
/// sorusunu soruyor. Sebep basit: mutlu yol zaten elle denendiğinde görülür
/// (kullanıcı 10 dakika sonra giriş ekranına düşmüyor). Görülmeyen taraf,
/// iptal edilmiş bir anahtarın hâlâ işe yarayıp yaramadığı — ve bu ancak
/// testle bilinir.
/// </summary>
public class AuthYenilemeTests
{
    private const string Sifre = "staj123";

    private static AppDbContext YeniContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AuthService Servis(AppDbContext db, int erisimDakika = 10, int yenilemeGun = 7)
        => new(
            new UserRepository(db),
            new RefreshTokenRepository(db),
            new FakeCurrentUserService(),
            Options.Create(new JwtSettings
            {
                Key = "StajProject-Test-Imza-Anahtari-En-Az-32-Karakter!",
                Issuer = "test",
                Audience = "test",
                ExpiryMinutes = erisimDakika,
                RefreshExpiryDays = yenilemeGun,
            }));

    private static async Task<User> KullaniciEkleAsync(AppDbContext db, string ad = "azra")
    {
        var user = new User { Username = ad };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Sifre);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static Task<LoginResponseDto?> GirisAsync(AuthService servis, string ad = "azra")
        => servis.LoginAsync(new LoginRequestDto { Username = ad, Password = Sifre });

    // ==================================================================
    //  1) Giriş artık yenileme anahtarı da veriyor
    // ==================================================================

    [Fact]
    public async Task Giris_ErisimVeYenilemeAnahtariniBirlikteDoner()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);

        var oturum = await GirisAsync(Servis(db));

        Assert.NotNull(oturum);
        Assert.False(string.IsNullOrWhiteSpace(oturum!.Token));
        Assert.False(string.IsNullOrWhiteSpace(oturum.RefreshToken));

        // Yenileme anahtarı erişim token'ından UZUN ömürlü olmalı; tersi
        // olsaydı yenileme hiç işe yaramaz, kullanıcı yine dışarı düşerdi.
        Assert.True(oturum.RefreshTokenExpiresAt > oturum.ExpiresAt);
    }

    [Fact]
    public async Task YenilemeAnahtari_VeritabaninaDUZMETINYazilmaz()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);

        var oturum = await GirisAsync(Servis(db));

        var satir = await db.RefreshTokens.SingleAsync();

        // Bu testin savunduğu şey: veritabanını okuyabilen biri (yedek
        // dosyası, SQL enjeksiyonu, bakış yetkisi olan bir çalışan) oradaki
        // değerle oturum devralamasın.
        Assert.NotEqual(oturum!.RefreshToken, satir.TokenHash);
        Assert.Equal(64, satir.TokenHash.Length);           // SHA-256 hex
        Assert.Equal(AuthService.Ozetle(oturum.RefreshToken), satir.TokenHash);
    }

    [Fact]
    public async Task HerGiris_FARKLIAnahtarUretir()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);
        var servis = Servis(db);

        var birinci = await GirisAsync(servis);
        var ikinci = await GirisAsync(servis);

        // Tahmin edilemezlik iddiasının en kaba kontrolü. Anahtar üretimi
        // sabit ya da sayaca bağlı olsaydı burada eşit çıkarlardı.
        Assert.NotEqual(birinci!.RefreshToken, ikinci!.RefreshToken);

        // İki oturum BİRLİKTE yaşayabilmeli: aynı kullanıcının iki cihazı.
        Assert.Equal(2, await db.RefreshTokens.CountAsync(t => t.RevokedAt == null));
    }

    // ==================================================================
    //  2) Yenileme — mutlu yol ve döndürme
    // ==================================================================

    [Fact]
    public async Task Yenileme_YeniErisimTokeniVerir()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);
        var servis = Servis(db);
        var oturum = await GirisAsync(servis);

        var yeni = await servis.RefreshAsync(oturum!.RefreshToken);

        Assert.NotNull(yeni);
        Assert.False(string.IsNullOrWhiteSpace(yeni!.Token));
        Assert.Equal("azra", yeni.Username);
    }

    [Fact]
    public async Task Yenileme_ANAHTARIDADEGISTIRIR_dondurme()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);
        var servis = Servis(db);
        var oturum = await GirisAsync(servis);

        var yeni = await servis.RefreshAsync(oturum!.RefreshToken);

        // Döndürmenin tanımı: aynı anahtar geri verilmez.
        Assert.NotEqual(oturum.RefreshToken, yeni!.RefreshToken);

        var eski = await db.RefreshTokens
            .SingleAsync(t => t.TokenHash == AuthService.Ozetle(oturum.RefreshToken));
        Assert.NotNull(eski.RevokedAt);
        Assert.Equal("dondurme", eski.RevokedReason);

        // Zincir kurulmuş: hangi anahtarın yerine ne geçtiği okunabiliyor.
        Assert.Equal(AuthService.Ozetle(yeni.RefreshToken), eski.ReplacedByHash);
    }

    [Fact]
    public async Task Yenileme_EskiAnahtarBirDahaCALISMAZ()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);
        var servis = Servis(db);
        var oturum = await GirisAsync(servis);

        await servis.RefreshAsync(oturum!.RefreshToken);          // 1. kullanım
        var ikinciDeneme = await servis.RefreshAsync(oturum.RefreshToken);   // aynı anahtar

        Assert.Null(ikinciDeneme);
    }

    [Fact]
    public async Task Yenileme_ZincirlemeCalisir()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);
        var servis = Servis(db);
        var oturum = await GirisAsync(servis);

        // Bir haftalık kullanımın kısaltılmış hâli: her yenileme bir
        // sonrakini beslemeli. Zincir kopsaydı kullanıcı ikinci yenilemede
        // dışarı düşerdi — ve bu, tek yenilemeyi test eden bir senaryoda
        // GÖRÜNMEZDİ.
        var anahtar = oturum!.RefreshToken;
        for (var i = 0; i < 5; i++)
        {
            var yeni = await servis.RefreshAsync(anahtar);
            Assert.NotNull(yeni);
            anahtar = yeni!.RefreshToken;
        }

        // Zincirdeki tek açık anahtar sonuncusu olmalı.
        Assert.Equal(1, await db.RefreshTokens.CountAsync(t => t.RevokedAt == null));
        Assert.Equal(6, await db.RefreshTokens.CountAsync());
    }

    // ==================================================================
    //  3) Yeniden kullanım tespiti — döndürmenin ASIL sebebi
    // ==================================================================

    [Fact]
    public async Task YenidenKullanim_TUMOTURUMLARIKapatir()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);
        var servis = Servis(db);

        // Kullanıcının iki cihazı var.
        var telefon = await GirisAsync(servis);
        var bilgisayar = await GirisAsync(servis);

        // Telefonun anahtarı çalındı; gerçek kullanıcı normal şekilde yeniledi.
        var telefonYeni = await servis.RefreshAsync(telefon!.RefreshToken);
        Assert.NotNull(telefonYeni);

        // Hırsız ELİNDEKİ KOPYAYI kullanmayı deniyor — artık iptal edilmiş.
        var hirsiz = await servis.RefreshAsync(telefon.RefreshToken);
        Assert.Null(hirsiz);

        // ASIL İDDİA: sadece hırsızın denemesi reddedilmiyor, kullanıcının
        // BÜTÜN oturumları kapatılıyor. Çünkü aynı anahtarın iki kopyası
        // dolaşıyorsa hangi tarafın hırsız olduğu bilinemez; güvenli olan
        // ikisini de dışarı atıp yeniden giriş istemek.
        Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.RevokedAt == null));

        // Hırsızın az önce ürettiği taze anahtar da öldü.
        Assert.Null(await servis.RefreshAsync(telefonYeni!.RefreshToken));

        // Masaüstü oturumu da kapandı — kullanıcı bunu fark edecek, ki
        // fark etmesi İSTENEN şey.
        Assert.Null(await servis.RefreshAsync(bilgisayar!.RefreshToken));
    }

    [Fact]
    public async Task YenidenKullanim_SebebiKAYITALTINAAlinir()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);
        var servis = Servis(db);
        var oturum = await GirisAsync(servis);

        await servis.RefreshAsync(oturum!.RefreshToken);
        await servis.RefreshAsync(oturum.RefreshToken);   // yeniden kullanım

        // Olay iz bırakmalı: "neden bu kullanıcının oturumu kapandı?"
        // sorusunun cevabı veritabanında yazılı olmalı.
        Assert.Contains(await db.RefreshTokens.ToListAsync(),
            t => t.RevokedReason == "yeniden-kullanim");
    }

    // ==================================================================
    //  4) Geçersiz anahtarlar
    // ==================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("uydurma-anahtar")]
    public async Task Yenileme_GecersizAnahtar_Reddedilir(string anahtar)
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);

        Assert.Null(await Servis(db).RefreshAsync(anahtar));
    }

    [Fact]
    public async Task Yenileme_SuresiDolmusAnahtar_Reddedilir()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);
        var servis = Servis(db);
        var oturum = await GirisAsync(servis);

        // Süreyi beklemek yerine satırı geriye alıyoruz: yedi gün bekleyen
        // bir test yazılamaz, Thread.Sleep ile taklit etmek de testi
        // yavaşlatır ve kırılgan yapardı.
        var satir = await db.RefreshTokens.SingleAsync();
        satir.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        Assert.Null(await servis.RefreshAsync(oturum!.RefreshToken));
    }

    [Fact]
    public async Task SuresiDolmusAnahtar_YenidenKullanimSAYILMAZ()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);
        var servis = Servis(db);
        var suresiDolan = await GirisAsync(servis);
        var digerCihaz = await GirisAsync(servis);

        var satir = await db.RefreshTokens
            .SingleAsync(t => t.TokenHash == AuthService.Ozetle(suresiDolan!.RefreshToken));
        satir.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        await servis.RefreshAsync(suresiDolan!.RefreshToken);

        // Süre dolması SIRADAN bir olay (tatilden dönen kullanıcı); hırsızlık
        // alarmı çalmamalı. Sıraları karıştırıp süre kontrolünü iptal
        // kontrolünden önce koysaydık, her tatil dönüşü bütün cihazları
        // dışarı atardık.
        Assert.NotNull(await servis.RefreshAsync(digerCihaz!.RefreshToken));
    }

    // ==================================================================
    //  5) Hesap durumu — yenileme, girişin sessiz tekrarıdır
    // ==================================================================

    [Fact]
    public async Task PasifeAlinanKullanici_YenileyEMEZ()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        var servis = Servis(db);
        var oturum = await GirisAsync(servis);

        // Yönetici hesabı askıya aldı.
        await new UserRepository(db).SetActiveAsync(user.Id, false);

        // Bu kontrol olmasaydı "hesabı kapattım" düğmesi hiçbir işe
        // yaramazdı: kullanıcı elindeki anahtarla yedi gün boyunca yeni
        // token üretmeye devam ederdi.
        Assert.Null(await servis.RefreshAsync(oturum!.RefreshToken));
        Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.RevokedAt == null));
    }

    [Fact]
    public async Task SoftDeleteEdilenKullanici_YenileyEMEZ()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        var servis = Servis(db);
        var oturum = await GirisAsync(servis);

        await new UserRepository(db).SoftDeleteAsync(user.Id);

        Assert.Null(await servis.RefreshAsync(oturum!.RefreshToken));
    }

    [Fact]
    public async Task OnayiGeriAlinanKullanici_YenileyEMEZ()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        var servis = Servis(db);
        var oturum = await GirisAsync(servis);

        (await db.Users.SingleAsync(u => u.Id == user.Id)).IsApproved = false;
        await db.SaveChangesAsync();

        Assert.Null(await servis.RefreshAsync(oturum!.RefreshToken));
    }

    // ==================================================================
    //  6) Çıkış
    // ==================================================================

    [Fact]
    public async Task Cikis_AnahtariIptalEder()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);
        var servis = Servis(db);
        var oturum = await GirisAsync(servis);

        await servis.LogoutAsync(oturum!.RefreshToken);

        Assert.Null(await servis.RefreshAsync(oturum.RefreshToken));
        Assert.Equal("cikis", (await db.RefreshTokens.SingleAsync()).RevokedReason);
    }

    [Fact]
    public async Task Cikis_YALNIZCAOOturumuKapatir()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);
        var servis = Servis(db);
        var telefon = await GirisAsync(servis);
        var bilgisayar = await GirisAsync(servis);

        await servis.LogoutAsync(telefon!.RefreshToken);

        // Bir cihazdan çıkmak diğerlerini düşürmemeli. Çıkışı
        // RevokeAllForUser ile yazsaydık — ki kolay olurdu — kullanıcı
        // telefondan çıktığında masaüstündeki çalışmasını da kaybederdi.
        Assert.NotNull(await servis.RefreshAsync(bilgisayar!.RefreshToken));
    }

    [Fact]
    public async Task Cikis_GecersizAnahtaraSessizceKatlanir()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);

        // İstisna fırlatmamalı: kullanıcı "çıkış yapılamadı" ekranında
        // kalmamalı, hem de elinde rastgele değer olan birine hangisinin
        // gerçek olduğu söylenmemeli.
        await Servis(db).LogoutAsync("uydurma");
        await Servis(db).LogoutAsync("");
    }

    [Fact]
    public async Task Cikis_HirsizlikKaydiniEZMEZ()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);
        var servis = Servis(db);
        var oturum = await GirisAsync(servis);

        await servis.RefreshAsync(oturum!.RefreshToken);
        await servis.RefreshAsync(oturum.RefreshToken);   // yeniden kullanım alarmı

        await servis.LogoutAsync(oturum.RefreshToken);

        // Zaten iptal edilmiş satırın sebebi "cikis" ile ezilirse, olayın
        // neden yaşandığı kaydı kaybolurdu.
        var satir = await db.RefreshTokens
            .SingleAsync(t => t.TokenHash == AuthService.Ozetle(oturum.RefreshToken));
        Assert.Equal("dondurme", satir.RevokedReason);
    }
}
