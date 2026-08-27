using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.Business.Validation;
using StajProject.DataAccess.Context;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// İki adımlı doğrulama — AKIŞ testleri.
///
/// Algoritmanın kendisi <see cref="TotpTests"/>'te RFC vektörleriyle sınandı.
/// Buradaki soru farklı: <b>koruma gerçekten devrede mi, yoksa etrafından
/// dolaşılabiliyor mu?</b>
///
/// En kritik test <see cref="AraToken_ERISIMTOKENIYerineGECEMEZ"/>. İki adımlı
/// doğrulamanın klasik uygulama hatası şu: şifre doğrulandıktan sonra verilen
/// ara token, normal erişim token'ıyla aynı biçimde imzalanır ve istemci onu
/// doğrudan <c>Authorization</c> başlığına koyup ikinci adımı tamamen atlar.
/// Koruma görünürde vardır, gerçekte yoktur — ve hiçbir hata mesajı alınmaz.
/// </summary>
public class IkinciAdimTests
{
    private const string Sifre = "staj123";
    private const string Anahtar = "StajProject-Test-Imza-Anahtari-En-Az-32-Karakter!";
    private const string Yayinci = "test-issuer";
    private const string ErisimAudience = "test-erisim";
    private const string AraAudience = "test-ara";

    private static AppDbContext YeniContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static JwtSettings Ayarlar() => new()
    {
        Key = Anahtar,
        Issuer = Yayinci,
        Audience = ErisimAudience,
        IkinciAdimAudience = AraAudience,
        ExpiryMinutes = 10,
        RefreshExpiryDays = 7,
        IkinciAdimDakika = 5,
    };

    private static AuthService Servis(AppDbContext db, int? girisYapan = null)
        => new(
            new UserRepository(db),
            new RefreshTokenRepository(db),
            new FakeCurrentUserService(girisYapan),
            Options.Create(Ayarlar()));

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

    /// <summary>Kullanıcı için 2FA'yı kurup açar; anahtarı döner.</summary>
    private static async Task<string> IkiAdimliAcAsync(AppDbContext db, User user)
    {
        var servis = Servis(db, user.Id);
        var kurulum = await servis.TotpBaslatAsync();

        // Kurulum DTO'su okunur (gruplu) biçim veriyor; kodu üretmek için
        // ham anahtarı veritabanından alıyoruz.
        var ham = (await db.Users.SingleAsync(u => u.Id == user.Id)).TotpSecret!;

        await servis.TotpDogrulaVeAcAsync(new TotpDogrulaDto
        {
            Kod = Totp.KodUret(ham, DateTimeOffset.UtcNow),
        });

        Assert.NotEmpty(kurulum.KurulumAdresi);
        return ham;
    }

    // ==================================================================
    //  1) Giriş akışı
    // ==================================================================

    [Fact]
    public async Task TotpKAPALIYKEN_GirisTekAdimda()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db);

        var oturum = await GirisAsync(Servis(db));

        Assert.NotNull(oturum);
        Assert.False(oturum!.IkinciAdimGerekli);
        Assert.False(string.IsNullOrWhiteSpace(oturum.Token));
    }

    [Fact]
    public async Task TotpACIKKEN_GirisOTURUMVERMIYOR()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        await IkiAdimliAcAsync(db, user);

        var cevap = await GirisAsync(Servis(db));

        // ASIL İDDİA: doğru şifre bile TEK BAŞINA oturum açmıyor.
        // "Yarım oturum" diye bir şey yok — token da yenileme anahtarı da boş.
        Assert.NotNull(cevap);
        Assert.True(cevap!.IkinciAdimGerekli);
        Assert.True(string.IsNullOrEmpty(cevap.Token));
        Assert.True(string.IsNullOrEmpty(cevap.RefreshToken));
        Assert.False(string.IsNullOrWhiteSpace(cevap.AraToken));
    }

    [Fact]
    public async Task TotpACIKKEN_GirisVERITABANINAOTURUMYAZMIYOR()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        await IkiAdimliAcAsync(db, user);

        await GirisAsync(Servis(db));

        // Yenileme anahtarı satırı OLUŞMAMALI. Oluşsaydı, ikinci adım hiç
        // tamamlanmasa bile kullanılabilir bir oturum ortada kalırdı.
        Assert.Equal(0, await db.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task DogruKodla_IkinciAdimOturumVeriyor()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        var totpAnahtari = await IkiAdimliAcAsync(db, user);

        var servis = Servis(db);
        var birinci = await GirisAsync(servis);

        var oturum = await servis.IkinciAdimGirisAsync(new IkinciAdimGirisDto
        {
            AraToken = birinci!.AraToken!,
            Kod = Totp.KodUret(totpAnahtari, DateTimeOffset.UtcNow),
        });

        Assert.NotNull(oturum);
        Assert.False(oturum!.IkinciAdimGerekli);
        Assert.False(string.IsNullOrWhiteSpace(oturum.Token));
        Assert.False(string.IsNullOrWhiteSpace(oturum.RefreshToken));
        Assert.Equal("azra", oturum.Username);
    }

    [Fact]
    public async Task YanlisKod_Reddediliyor()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        await IkiAdimliAcAsync(db, user);

        var servis = Servis(db);
        var birinci = await GirisAsync(servis);

        Assert.Null(await servis.IkinciAdimGirisAsync(new IkinciAdimGirisDto
        {
            AraToken = birinci!.AraToken!,
            Kod = "000000",
        }));
    }

    // ==================================================================
    //  2) ARA TOKEN — korumanın etrafından dolaşılabiliyor mu?
    // ==================================================================

    [Fact]
    public async Task AraToken_ERISIMTOKENIYerineGECEMEZ()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        await IkiAdimliAcAsync(db, user);

        var birinci = await GirisAsync(Servis(db));
        var araToken = birinci!.AraToken!;

        // API'nin token doğrulaması TAM OLARAK BU: Program.cs'teki
        // TokenValidationParameters ile aynı ayarlar.
        var dogrulama = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = Yayinci,
            ValidAudience = ErisimAudience,          // ← ERİŞİM audience'ı
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Anahtar)),
            ClockSkew = TimeSpan.Zero,
        };

        // İmza GEÇERLİ (aynı anahtarla imzalandı) ama audience farklı.
        // Bu satır kırmızıya dönerse iki adımlı doğrulama TAMAMEN atlanabilir
        // demektir: istemci ara token'ı Authorization başlığına koyup içeri
        // girer.
        Assert.Throws<SecurityTokenInvalidAudienceException>(
            () => new JwtSecurityTokenHandler().ValidateToken(araToken, dogrulama, out _));
    }

    [Fact]
    public async Task ERISIMTOKENI_AraTokenYerineGECEMEZ()
    {
        using var db = YeniContext();
        await KullaniciEkleAsync(db, "totpsuz");

        // 2FA kapalı bir kullanıcının NORMAL erişim token'ı.
        var servis = Servis(db);
        var normal = await GirisAsync(servis, "totpsuz");

        // Duvar İKİ YÖNLÜ olmalı. Tek yönlü olsaydı, geçerli bir erişim
        // token'ı olan biri onu ikinci adımda kullanıp başka bir hesabın
        // 2FA'sını geçebilirdi.
        Assert.Null(await servis.IkinciAdimGirisAsync(new IkinciAdimGirisDto
        {
            AraToken = normal!.Token,
            Kod = "123456",
        }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("uydurma.ara.token")]
    public async Task GecersizAraToken_Reddediliyor(string araToken)
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        var totpAnahtari = await IkiAdimliAcAsync(db, user);

        Assert.Null(await Servis(db).IkinciAdimGirisAsync(new IkinciAdimGirisDto
        {
            AraToken = araToken,
            Kod = Totp.KodUret(totpAnahtari, DateTimeOffset.UtcNow),
        }));
    }

    [Fact]
    public async Task BASKAANAHTARLAImzalanmisAraToken_Reddediliyor()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        var totpAnahtari = await IkiAdimliAcAsync(db, user);

        // Doğru audience, doğru issuer, doğru kullanıcı — ama saldırganın
        // kendi anahtarıyla imzalanmış. İmza doğrulanmasaydı bu token
        // geçerdi ve 2FA anlamsızlaşırdı.
        var sahte = new JwtSecurityToken(
            issuer: Yayinci,
            audience: AraAudience,
            claims: new[] { new System.Security.Claims.Claim(
                JwtRegisteredClaimNames.Sub, user.Id.ToString()) },
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                    "SALDIRGANIN-KENDI-ANAHTARI-EN-AZ-32-KARAKTER!")),
                SecurityAlgorithms.HmacSha256));

        Assert.Null(await Servis(db).IkinciAdimGirisAsync(new IkinciAdimGirisDto
        {
            AraToken = new JwtSecurityTokenHandler().WriteToken(sahte),
            Kod = Totp.KodUret(totpAnahtari, DateTimeOffset.UtcNow),
        }));
    }

    [Fact]
    public async Task SURESIDOLMUSAraToken_Reddediliyor()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        var totpAnahtari = await IkiAdimliAcAsync(db, user);

        var suresiDolmus = new JwtSecurityToken(
            issuer: Yayinci,
            audience: AraAudience,
            claims: new[] { new System.Security.Claims.Claim(
                JwtRegisteredClaimNames.Sub, user.Id.ToString()) },
            expires: DateTime.UtcNow.AddMinutes(-1),      // geçmişte
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Anahtar)),
                SecurityAlgorithms.HmacSha256));

        Assert.Null(await Servis(db).IkinciAdimGirisAsync(new IkinciAdimGirisDto
        {
            AraToken = new JwtSecurityTokenHandler().WriteToken(suresiDolmus),
            Kod = Totp.KodUret(totpAnahtari, DateTimeOffset.UtcNow),
        }));
    }

    [Fact]
    public async Task ARADAPasifeAlinanKullanici_IcerıGiremiyor()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        var totpAnahtari = await IkiAdimliAcAsync(db, user);

        var servis = Servis(db);
        var birinci = await GirisAsync(servis);

        // Birinci adım ile ikinci adım arasında beş dakika var; yönetici o
        // aralıkta hesabı askıya alabilir. Kontrol olmasaydı kapatılmış
        // hesap ara token'ı elinde tuttuğu için içeri girerdi.
        await new UserRepository(db).SetActiveAsync(user.Id, false);

        Assert.Null(await servis.IkinciAdimGirisAsync(new IkinciAdimGirisDto
        {
            AraToken = birinci!.AraToken!,
            Kod = Totp.KodUret(totpAnahtari, DateTimeOffset.UtcNow),
        }));
    }

    // ==================================================================
    //  3) Kurulum akışı
    // ==================================================================

    [Fact]
    public async Task Kurulum_AnahtarUretiyorAmaHENUZACMIYOR()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);

        var kurulum = await Servis(db, user.Id).TotpBaslatAsync();

        var kayit = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.NotNull(kayit.TotpSecret);

        // Kod üretebildiğini kanıtlamadan koruma devreye GİRMEMELİ. Girseydi,
        // QR'ı okutmayı yarıda bırakan kullanıcı bir daha hiç giriş yapamazdı.
        Assert.False(kayit.TotpEnabled);
        Assert.Contains("otpauth://totp/", kurulum.KurulumAdresi);
    }

    [Fact]
    public async Task Kurulum_YARIDAKALIRSAGirisBOZULMUYOR()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);

        await Servis(db, user.Id).TotpBaslatAsync();   // başlatıldı, bitirilmedi

        var oturum = await GirisAsync(Servis(db));

        // Bir önceki testin gerekçesinin DAVRANIŞ karşılığı.
        Assert.NotNull(oturum);
        Assert.False(oturum!.IkinciAdimGerekli);
        Assert.False(string.IsNullOrWhiteSpace(oturum.Token));
    }

    [Fact]
    public async Task Kurulum_YanlisKodlaTamamlanmiyor()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        var servis = Servis(db, user.Id);
        await servis.TotpBaslatAsync();

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => servis.TotpDogrulaVeAcAsync(new TotpDogrulaDto { Kod = "000000" }));

        Assert.Contains("doğrulanamadı", hata.Message);
        Assert.False((await db.Users.SingleAsync(u => u.Id == user.Id)).TotpEnabled);
    }

    [Fact]
    public async Task Kurulum_BASLATILMADANDogrulanamaz()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => Servis(db, user.Id).TotpDogrulaVeAcAsync(new TotpDogrulaDto { Kod = "123456" }));

        Assert.Contains("kurulumu başlatmalısınız", hata.Message);
    }

    [Fact]
    public async Task ZatenACIKKEN_YenidenKurulamaz()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        await IkiAdimliAcAsync(db, user);

        // Açıkken yeni anahtar üretmek, telefondaki kaydı SESSİZCE geçersiz
        // kılardı; kullanıcı bunu ancak giriş yapamayınca fark ederdi.
        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => Servis(db, user.Id).TotpBaslatAsync());

        Assert.Contains("zaten açık", hata.Message);
    }

    // ==================================================================
    //  4) Kapatma
    // ==================================================================

    [Fact]
    public async Task Kapatma_SIFREIster()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        await IkiAdimliAcAsync(db, user);

        // Kapatma, güvenliği AZALTAN ve tam da saldırganın yapmak isteyeceği
        // işlem. Açık kalmış bir oturumu ele geçiren biri, şifreyi bilmeden
        // korumayı kaldıramamalı.
        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => Servis(db, user.Id).TotpKapatAsync(new TotpKapatDto { Sifre = "yanlis" }));

        Assert.Contains("Şifre hatalı", hata.Message);
        Assert.True((await db.Users.SingleAsync(u => u.Id == user.Id)).TotpEnabled);
    }

    [Fact]
    public async Task Kapatma_ANAHTARIDASiliyor()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        await IkiAdimliAcAsync(db, user);

        await Servis(db, user.Id).TotpKapatAsync(new TotpKapatDto { Sifre = Sifre });

        var kayit = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.False(kayit.TotpEnabled);

        // Anahtar KALSAYDI, korumayı yeniden açan kullanıcı eski (belki de
        // sızmış) anahtarla devam ederdi.
        Assert.Null(kayit.TotpSecret);
    }

    [Fact]
    public async Task Kapatildiktan_SonraGirisTekAdima_Donuyor()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        await IkiAdimliAcAsync(db, user);
        await Servis(db, user.Id).TotpKapatAsync(new TotpKapatDto { Sifre = Sifre });

        var oturum = await GirisAsync(Servis(db));

        Assert.False(oturum!.IkinciAdimGerekli);
        Assert.False(string.IsNullOrWhiteSpace(oturum.Token));
    }

    [Fact]
    public async Task Durum_DogruBildiriliyor()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);

        Assert.False((await Servis(db, user.Id).TotpDurumAsync()).Etkin);

        await IkiAdimliAcAsync(db, user);

        Assert.True((await Servis(db, user.Id).TotpDurumAsync()).Etkin);
    }

    // ==================================================================
    //  5) Şifre hâlâ birinci kapı
    // ==================================================================

    [Fact]
    public async Task YanlisSifre_2FAAcikOlsaBileAraTokenVERMIYOR()
    {
        using var db = YeniContext();
        var user = await KullaniciEkleAsync(db);
        await IkiAdimliAcAsync(db, user);

        var cevap = await Servis(db).LoginAsync(new LoginRequestDto
        {
            Username = "azra",
            Password = "yanlis",
        });

        // İki adımlı doğrulama şifrenin YERİNE geçmiyor, ÜSTÜNE biniyor.
        // Ara token verseydik, şifreyi bilmeyen biri kod deneme hakkı
        // kazanırdı.
        Assert.Null(cevap);
    }
}
