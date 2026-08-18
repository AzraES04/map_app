using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.Business.Validation;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 6 / Madde 2 testleri: DİNAMİK YETKİLENDİRME.
///
/// Ödevin kritik cümlesi şu: "bir yetki kullanıcının rolünde zaten tanımlıysa
/// kullanıcı sekmesinde tekrar seçim yaptırmayın, yetkinin rolden geldiğini
/// arayüzde belirtin." Bunun iki ayağı var ve ikisi de burada sınanıyor:
///   1) Matris, her yetkinin KAYNAĞINI söyleyebilmeli (rolden mi, doğrudan mı)
///   2) Rolden gelen bir yetki, kullanıcıya doğrudan da yazılmamalı
/// </summary>
public class YetkiTests
{
    /// <summary>
    /// Ortak kurulum: sahte veritabanı + üç repository + iki servis.
    /// <paramref name="girisYapan"/> "giriş yapmış kullanıcı"yı belirler;
    /// kendini kilitleme korumaları buna bakıyor.
    /// </summary>
    private static (SahteVeritabani db, UserAdminService kullaniciServisi,
                    PermissionService yetkiServisi, RoleService rolServisi)
        Kur(int? girisYapan = 99)
    {
        var db = new SahteVeritabani();

        var kullanicilar = new FakeUserRepository(db);
        var roller = new FakeRoleRepository(db);
        var yetkiler = new FakePermissionRepository(db);
        var oturum = new FakeCurrentUserService(girisYapan);

        var yetkiServisi = new PermissionService(yetkiler, kullanicilar, oturum);
        var kullaniciServisi = new UserAdminService(kullanicilar, roller, yetkiler, yetkiServisi, oturum);
        var rolServisi = new RoleService(roller, yetkiler);

        return (db, kullaniciServisi, yetkiServisi, rolServisi);
    }

    /// <summary>Sekiz standart yetkiyi ekler ve adlarına göre sözlük döner.</summary>
    private static Dictionary<string, int> YetkileriKur(SahteVeritabani db)
        => Yetkiler.Tumu.ToDictionary(y => y.Ad, y => db.YetkiEkle(y.Ad, y.Aciklama).Id);

    // ------------------------------------------------------------------
    //  Madde 2: yetki iki yoldan gelir, kaynağı belli olmalı
    // ------------------------------------------------------------------

    [Fact]
    public async Task RoldenGelenYetki_MatristeRolKaynagiylaIsaretlenir()
    {
        var (db, kullaniciServisi, yetkiServisi, _) = Kur();
        var yetki = YetkileriKur(db);
        var editor = db.RolEkle("Editör", yetki[Yetkiler.NoktaEkleme], yetki[Yetkiler.CizgiEkleme]);

        var kullanici = await kullaniciServisi.CreateAsync(new UserCreateDto
        {
            Username = "mehmet",
            Password = "deneme123",
            RoleIds = new List<int> { editor.Id },
        });

        var matris = await yetkiServisi.GetForUserAsync(kullanici.Id);
        var nokta = matris!.Permissions.First(y => y.Name == Yetkiler.NoktaEkleme);

        Assert.True(nokta.FromRole);                    // rolden geliyor
        Assert.False(nokta.Direct);                     // doğrudan verilmemiş
        Assert.True(nokta.Granted);                     // ama sonuçta yetkili
        Assert.Equal(new[] { "Editör" }, nokta.RoleNames);

        // Rolde olmayan yetki hiçbir yoldan gelmiyor
        var silme = matris.Permissions.First(y => y.Name == Yetkiler.KayitSilme);
        Assert.False(silme.Granted);
    }

    [Fact]
    public async Task RoldenGelenYetki_KullaniciyaIkinciKezYAZILMAZ()
    {
        // Ödevin istediği davranışın veri tarafındaki karşılığı:
        // arayüz kutuyu kilitliyor, servis de aynı kuralı kendi başına uyguluyor.
        var (db, kullaniciServisi, _, _) = Kur();
        var yetki = YetkileriKur(db);
        var editor = db.RolEkle("Editör", yetki[Yetkiler.NoktaEkleme]);

        var kullanici = await kullaniciServisi.CreateAsync(new UserCreateDto
        {
            Username = "mehmet",
            Password = "deneme123",
            RoleIds = new List<int> { editor.Id },
        });

        // Arayüz bozulsa/atlansa bile: rolden gelen "Point Ekleme" ile
        // rolde OLMAYAN "Kayıt Silme" birlikte gönderiliyor.
        var matris = await kullaniciServisi.SetPermissionsAsync(kullanici.Id, new SetUserPermissionsDto
        {
            PermissionIds = new List<int> { yetki[Yetkiler.NoktaEkleme], yetki[Yetkiler.KayitSilme] },
        });

        // user_permissions tablosuna YALNIZCA rolde olmayan yetki yazılmalı
        var dogrudanKayitlar = db.KullaniciYetkileri.Where(ky => ky.UserId == kullanici.Id).ToList();
        Assert.Single(dogrudanKayitlar);
        Assert.Equal(yetki[Yetkiler.KayitSilme], dogrudanKayitlar[0].PermissionId);

        // Matriste ikisi de "verili" ama kaynakları farklı
        var nokta = matris!.Permissions.First(y => y.Name == Yetkiler.NoktaEkleme);
        var silme = matris.Permissions.First(y => y.Name == Yetkiler.KayitSilme);

        Assert.True(nokta.FromRole);
        Assert.False(nokta.Direct);
        Assert.False(silme.FromRole);
        Assert.True(silme.Direct);
        Assert.True(silme.Granted);
    }

    [Fact]
    public async Task AyniYetkiIkiRoldenGelirse_HerIkiRolDeGosterilir()
    {
        var (db, kullaniciServisi, yetkiServisi, _) = Kur();
        var yetki = YetkileriKur(db);
        var editor = db.RolEkle("Editör", yetki[Yetkiler.NoktaEkleme]);
        var saha = db.RolEkle("Saha Ekibi", yetki[Yetkiler.NoktaEkleme], yetki[Yetkiler.CizgiEkleme]);

        var kullanici = await kullaniciServisi.CreateAsync(new UserCreateDto
        {
            Username = "mehmet",
            Password = "deneme123",
            RoleIds = new List<int> { editor.Id, saha.Id },
        });

        var matris = await yetkiServisi.GetForUserAsync(kullanici.Id);
        var nokta = matris!.Permissions.First(y => y.Name == Yetkiler.NoktaEkleme);

        Assert.Equal(2, nokta.RoleNames.Count);
        Assert.Contains("Editör", nokta.RoleNames);
        Assert.Contains("Saha Ekibi", nokta.RoleNames);
    }

    [Fact]
    public async Task PasifRol_YetkiUretmez()
    {
        // Rolü askıya almanın anlamı "şu an geçerli değil"; yetkilerini
        // dağıtmaya devam etmesi bu anlamla çelişirdi.
        var (db, kullaniciServisi, yetkiServisi, _) = Kur();
        var yetki = YetkileriKur(db);
        var rol = db.RolEkle("Editör", yetki[Yetkiler.NoktaEkleme]);
        rol.IsActive = false;

        var kullanici = await kullaniciServisi.CreateAsync(new UserCreateDto
        {
            Username = "mehmet",
            Password = "deneme123",
            RoleIds = new List<int> { rol.Id },
        });

        var matris = await yetkiServisi.GetForUserAsync(kullanici.Id);

        Assert.All(matris!.Permissions, y => Assert.False(y.Granted));
        // Rol yine de kullanıcının üstünde görünüyor — atama silinmedi
        Assert.Single(matris.Roles);
    }

    [Fact]
    public async Task RolunYetkisiDegisince_KullanicininYetkisiAninaGuncellenir()
    {
        // Dinamik yetkilendirmenin can alıcı noktası: yetki token'a yazılmıyor,
        // her sorguda veritabanından okunuyor. Yeniden giriş gerekmiyor.
        var (db, kullaniciServisi, yetkiServisi, rolServisi) = Kur();
        var yetki = YetkileriKur(db);
        var rol = db.RolEkle("Editör", yetki[Yetkiler.NoktaEkleme]);

        var kullanici = await kullaniciServisi.CreateAsync(new UserCreateDto
        {
            Username = "mehmet",
            Password = "deneme123",
            RoleIds = new List<int> { rol.Id },
        });

        Assert.False(await yetkiServisi.HasPermissionAsync(kullanici.Id, Yetkiler.KayitSilme));

        // Yönetici, rol ekranından "Kayıt Silme" yetkisini ekliyor
        await rolServisi.UpdateAsync(rol.Id, new RoleSaveDto
        {
            Name = "Editör",
            PermissionIds = new List<int> { yetki[Yetkiler.NoktaEkleme], yetki[Yetkiler.KayitSilme] },
        });

        Assert.True(await yetkiServisi.HasPermissionAsync(kullanici.Id, Yetkiler.KayitSilme));
    }

    [Fact]
    public async Task SilinenRol_YetkiUretmeyiBirakir()
    {
        var (db, kullaniciServisi, yetkiServisi, rolServisi) = Kur();
        var yetki = YetkileriKur(db);
        var rol = db.RolEkle("Editör", yetki[Yetkiler.NoktaEkleme]);

        var kullanici = await kullaniciServisi.CreateAsync(new UserCreateDto
        {
            Username = "mehmet",
            Password = "deneme123",
            RoleIds = new List<int> { rol.Id },
        });

        Assert.True(await yetkiServisi.HasPermissionAsync(kullanici.Id, Yetkiler.NoktaEkleme));

        Assert.True(await rolServisi.DeleteAsync(rol.Id));

        Assert.False(await yetkiServisi.HasPermissionAsync(kullanici.Id, Yetkiler.NoktaEkleme));
    }

    // ------------------------------------------------------------------
    //  Madde 1: kullanıcı ekle / güncelle / çıkar kuralları
    // ------------------------------------------------------------------

    [Fact]
    public async Task AyniKullaniciAdi_IkinciKezEklenemez()
    {
        var (_, kullaniciServisi, _, _) = Kur();
        await kullaniciServisi.CreateAsync(new UserCreateDto { Username = "mehmet", Password = "deneme123" });

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() =>
            kullaniciServisi.CreateAsync(new UserCreateDto { Username = "mehmet", Password = "baska123" }));

        Assert.Contains("zaten kullanılıyor", hata.Message);
    }

    [Fact]
    public async Task Guncellemede_SifreBosBirakilirsa_MevcutSifreKorunur()
    {
        var (db, kullaniciServisi, _, _) = Kur();
        var olusan = await kullaniciServisi.CreateAsync(new UserCreateDto
        {
            Username = "mehmet",
            Password = "deneme123",
        });

        var ilkHash = db.Kullanicilar.First(k => k.Id == olusan.Id).PasswordHash;

        await kullaniciServisi.UpdateAsync(olusan.Id, new UserUpdateDto
        {
            Username = "mehmet-yeni",
            Password = null,          // dokunma
            IsActive = true,
        });

        var kullanici = db.Kullanicilar.First(k => k.Id == olusan.Id);
        Assert.Equal("mehmet-yeni", kullanici.Username);
        Assert.Equal(ilkHash, kullanici.PasswordHash);
    }

    [Fact]
    public async Task AyniAdliRol_IkinciKezEklenemez()
    {
        var (_, _, _, rolServisi) = Kur();
        await rolServisi.CreateAsync(new RoleSaveDto { Name = "Editör" });

        await Assert.ThrowsAsync<IsKuraliException>(() =>
            rolServisi.CreateAsync(new RoleSaveDto { Name = "Editör" }));
    }

    [Fact]
    public async Task OlmayanYetkiId_Reddedilir()
    {
        // Doğrulama olmasaydı veritabanı yabancı anahtar hatası fırlatır,
        // kullanıcı da anlaşılmaz bir 500 görürdü.
        var (db, _, _, rolServisi) = Kur();
        YetkileriKur(db);

        await Assert.ThrowsAsync<IsKuraliException>(() =>
            rolServisi.CreateAsync(new RoleSaveDto
            {
                Name = "Hayalet",
                PermissionIds = new List<int> { 9999 },
            }));
    }

    // ------------------------------------------------------------------
    //  Kendi ayağına sıkma korumaları
    // ------------------------------------------------------------------

    [Fact]
    public async Task KullaniciKendiHesabiniSilemez_BaskasiniSilebilir()
    {
        var db = new SahteVeritabani();
        var ben = new User { Id = db.SonrakiKullaniciId(), Username = "yonetici" };
        var digeri = new User { Id = db.SonrakiKullaniciId(), Username = "baskasi" };
        db.Kullanicilar.Add(ben);
        db.Kullanicilar.Add(digeri);

        // "yonetici" olarak giriş yapılmış gibi
        var servis = ServisKur(db, girisYapan: ben.Id);

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() => servis.DeleteAsync(ben.Id));
        Assert.Contains("Kendi hesabınızı", hata.Message);

        Assert.True(await servis.DeleteAsync(digeri.Id));   // başkasını silmek serbest
        Assert.True(db.Kullanicilar.First(k => k.Id == digeri.Id).IsDeleted);
    }

    [Fact]
    public async Task KullaniciKendiHesabiniPasifeAlamaz()
    {
        var db = new SahteVeritabani();
        var ben = new User { Id = db.SonrakiKullaniciId(), Username = "yonetici" };
        db.Kullanicilar.Add(ben);

        var servis = ServisKur(db, girisYapan: ben.Id);

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() =>
            servis.UpdateAsync(ben.Id, new UserUpdateDto { Username = "yonetici", IsActive = false }));

        Assert.Contains("pasife alamazsınız", hata.Message);
    }

    [Fact]
    public async Task KullaniciKendiYonetimYetkisiniKaldiramaz()
    {
        // Tek yöneticili kurulumda bu kural olmasaydı, yanlış bir tık paneli
        // kimsenin açamayacağı hâle getirirdi.
        var db = new SahteVeritabani();
        var yetki = Yetkiler.Tumu.ToDictionary(y => y.Ad, y => db.YetkiEkle(y.Ad, y.Aciklama).Id);
        var yoneticiRol = db.RolEkle("Yönetici", yetki[Yetkiler.KullaniciYonetimi], yetki[Yetkiler.RolYonetimi]);
        var sadeRol = db.RolEkle("Görüntüleyici", yetki[Yetkiler.AnalizCalistirma]);

        var ben = new User { Id = db.SonrakiKullaniciId(), Username = "yonetici" };
        db.Kullanicilar.Add(ben);
        db.KullaniciRolleri.Add(new UserRole { UserId = ben.Id, RoleId = yoneticiRol.Id });

        var servis = ServisKur(db, girisYapan: ben.Id);

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() =>
            servis.UpdateAsync(ben.Id, new UserUpdateDto
            {
                Username = "yonetici",
                IsActive = true,
                RoleIds = new List<int> { sadeRol.Id },     // yönetici rolünü bırakıyor
            }));

        Assert.Contains("Kullanıcı Yönetimi", hata.Message);

        // BAŞKA bir kullanıcının rolünü aynı şekilde değiştirmek serbest
        var digeri = new User { Id = db.SonrakiKullaniciId(), Username = "baskasi" };
        db.Kullanicilar.Add(digeri);
        db.KullaniciRolleri.Add(new UserRole { UserId = digeri.Id, RoleId = yoneticiRol.Id });

        var sonuc = await servis.UpdateAsync(digeri.Id, new UserUpdateDto
        {
            Username = "baskasi",
            IsActive = true,
            RoleIds = new List<int> { sadeRol.Id },
        });

        Assert.NotNull(sonuc);
        Assert.Equal(new[] { "Görüntüleyici" }, sonuc!.Roles.Select(r => r.Name));
    }

    // ------------------------------------------------------------------
    //  Kurulum yardımcıları
    // ------------------------------------------------------------------

    /// <summary>Hazır bir veritabanı üzerine kullanıcı yönetim servisi kurar.</summary>
    private static UserAdminService ServisKur(SahteVeritabani db, int girisYapan)
    {
        var kullanicilar = new FakeUserRepository(db);
        var roller = new FakeRoleRepository(db);
        var yetkiler = new FakePermissionRepository(db);
        var oturum = new FakeCurrentUserService(girisYapan);
        var yetkiServisi = new PermissionService(yetkiler, kullanicilar, oturum);

        return new UserAdminService(kullanicilar, roller, yetkiler, yetkiServisi, oturum);
    }
}
