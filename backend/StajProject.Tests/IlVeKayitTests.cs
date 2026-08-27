using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using WktConverter = StajProject.Business.Geo.WktConverter;
using StajProject.Business.Services;
using StajProject.Business.Validation;
using StajProject.DataAccess.Context;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 10 testleri: il/bölge bazlı coğrafi yetki ve kayıt olma + yönetici onayı.
/// </summary>
public class IlVeKayitTests
{
    // ==================================================================
    //  1) Bölge eşlemesi
    // ==================================================================

    [Fact]
    public void Bolgeler_SeksenBirIlinTamamiEslenmis()
    {
        // Eksik bir plaka, o ilin hiçbir bölge seçiminde görünmemesi demek —
        // ve bunu ancak o ili elle arayan biri fark ederdi.
        Assert.Equal(81, Bolgeler.TumEslesme.Count);

        for (var plaka = 1; plaka <= 81; plaka++)
        {
            Assert.True(Bolgeler.TumEslesme.ContainsKey(plaka), $"{plaka} plakası eşlenmemiş");
        }
    }

    [Fact]
    public void Bolgeler_HerIlTEKBirBolgede()
    {
        // Bir il iki bölgeye yazılsaydı bölge birleşimleri örtüşür,
        // "Ege" ile "Akdeniz" aynı ili iki kez sayardı.
        var toplam = Bolgeler.Tumu.Sum(b => Bolgeler.TumEslesme.Count(e => e.Value == b));

        Assert.Equal(81, toplam);
    }

    [Theory]
    [InlineData(6, "İç Anadolu")]     // Ankara
    [InlineData(34, "Marmara")]       // İstanbul
    [InlineData(35, "Ege")]           // İzmir
    [InlineData(61, "Karadeniz")]     // Trabzon
    [InlineData(65, "Doğu Anadolu")]  // Van
    [InlineData(63, "Güneydoğu Anadolu")] // Şanlıurfa
    [InlineData(7, "Akdeniz")]        // Antalya
    public void Bolgeler_BilinenIllerDogruBolgede(int plaka, string beklenen)
        => Assert.Equal(beklenen, Bolgeler.BolgeBul(plaka));

    [Fact]
    public void Bolgeler_TanimsizPlakaHataVerir()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Bolgeler.BolgeBul(99));

    // ==================================================================
    //  2) Coğrafi yetki — üç tanımlama yolu
    // ==================================================================

    /// <summary>Yan yana iki kare il: birleşimleri tek bir dikdörtgen olur.</summary>
    private static FakeIlRepository IkiKomsuIl() => new FakeIlRepository()
        .Ekle(6, "Ankara", "POLYGON ((32 39, 33 39, 33 40, 32 40, 32 39))")
        .Ekle(42, "Konya", "POLYGON ((33 39, 34 39, 34 40, 33 40, 33 39))");

    private static (SahteVeritabani db, GeoPermissionService servis) Kur(FakeIlRepository iller)
    {
        var db = new SahteVeritabani();
        db.Kullanicilar.Add(new User { Id = 2, Username = "ayse" });
        db.RolEkle("Editör");

        var servis = new GeoPermissionService(
            new FakeGeoPermissionRepository(db),
            new FakeUserRepository(db),
            new FakeRoleRepository(db),
            iller,
            new FakeGeometryRepository<PolygonEntity>(),
            new FakeCurrentUserService(1));

        return (db, servis);
    }

    [Fact]
    public async Task IlSecimi_SecilenIllerinBirlesimiKaydedilir()
    {
        var (db, servis) = Kur(IkiKomsuIl());

        await servis.CreateAsync(new GeoPermissionCreateDto
        {
            Name = "Ankara + Konya",
            UserId = 2,
            IlPlakalari = new List<int> { 6, 42 },
        });

        var kayit = Assert.Single(db.CografiYetkiler);

        // İki bitişik kare birleşince tek bir dikdörtgen olmalı:
        // 32→34 boylam, 39→40 enlem.
        var sinir = kayit.Geom.EnvelopeInternal;
        Assert.Equal(32, sinir.MinX, precision: 6);
        Assert.Equal(34, sinir.MaxX, precision: 6);
        Assert.Equal(4326, kayit.Geom.SRID);
    }

    [Fact]
    public async Task BolgeSecimi_BolgedekiTumIllerBirlesir()
    {
        // 35 İzmir ve 45 Manisa ikisi de Ege; 6 Ankara İç Anadolu.
        // Ege seçilince Ankara DAHİL OLMAMALI.
        var iller = new FakeIlRepository()
            .Ekle(35, "İzmir", "POLYGON ((27 38, 28 38, 28 39, 27 39, 27 38))")
            .Ekle(45, "Manisa", "POLYGON ((28 38, 29 38, 29 39, 28 39, 28 38))")
            .Ekle(6, "Ankara", "POLYGON ((32 39, 33 39, 33 40, 32 40, 32 39))");

        var (db, servis) = Kur(iller);

        await servis.CreateAsync(new GeoPermissionCreateDto
        {
            Name = "Ege",
            UserId = 2,
            Bolgeler = new List<string> { "Ege" },
        });

        var sinir = Assert.Single(db.CografiYetkiler).Geom.EnvelopeInternal;

        Assert.Equal(27, sinir.MinX, precision: 6);
        Assert.Equal(29, sinir.MaxX, precision: 6);   // Ankara (32–33) dışarıda
    }

    [Fact]
    public async Task Cizim_EskiYolHalaCalisiyor()
    {
        // Ödev 7'den beri var olan yol bozulmamalı: il/bölge eklenmesi
        // elle çizimi geçersiz kılmıyor.
        var (db, servis) = Kur(new FakeIlRepository());

        await servis.CreateAsync(new GeoPermissionCreateDto
        {
            Name = "Elle çizim",
            UserId = 2,
            Wkt = "POLYGON ((30 39, 31 39, 31 40, 30 40, 30 39))",
        });

        Assert.Single(db.CografiYetkiler);
    }

    [Fact]
    public async Task AlanTanimiYoksa_Reddedilir()
    {
        var (_, servis) = Kur(IkiKomsuIl());

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() =>
            servis.CreateAsync(new GeoPermissionCreateDto { Name = "Boş", UserId = 2 }));

        Assert.Contains("Alan tanımlanmadı", hata.Message);
    }

    [Fact]
    public async Task IkiYolBirdenGelirse_Reddedilir()
    {
        // Belirsizliği kabul etmek yerine reddediyoruz: yönetici hem çizip
        // hem il seçtiyse hangisini istediği bilinemez.
        var (_, servis) = Kur(IkiKomsuIl());

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() =>
            servis.CreateAsync(new GeoPermissionCreateDto
            {
                Name = "Karışık",
                UserId = 2,
                Wkt = "POLYGON ((30 39, 31 39, 31 40, 30 40, 30 39))",
                Bolgeler = new List<string> { "Ege" },
            }));

        Assert.Contains("tek bir yolla", hata.Message);
    }

    [Fact]
    public async Task BilinmeyenBolge_Reddedilir()
    {
        var (_, servis) = Kur(IkiKomsuIl());

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() =>
            servis.CreateAsync(new GeoPermissionCreateDto
            {
                Name = "X", UserId = 2, Bolgeler = new List<string> { "Kuzey Kutbu" },
            }));

        Assert.Contains("Bilinmeyen bölge", hata.Message);
    }

    [Fact]
    public async Task IlSecimiyleTanimlananAlan_KuralOlarakUygulaniyor()
    {
        // Asıl mesele bu: il seçimiyle tanımlanan alan, çizimle tanımlanmış
        // gibi davranmalı. Aşağıdaki iki nokta aynı kuraldan geçiyor.
        var (_, servis) = Kur(IkiKomsuIl());

        await servis.CreateAsync(new GeoPermissionCreateDto
        {
            Name = "Ankara + Konya", UserId = 2, IlPlakalari = new List<int> { 6, 42 },
        });

        var fabrika = new GeometryFactory(new PrecisionModel(), 4326);

        // İçeride: hata fırlatmamalı
        await servis.DogrulaAsync(2, fabrika.CreatePoint(new Coordinate(32.5, 39.5)));

        // Dışarıda: reddedilmeli
        var hata = await Assert.ThrowsAsync<IsKuraliException>(() =>
            servis.DogrulaAsync(2, fabrika.CreatePoint(new Coordinate(40.0, 39.5))));

        Assert.Contains("tanımlı alanın dışında", hata.Message);
    }

    // ==================================================================
    //  2.5) Ödev 11 — çoklu bölge ve kayıtlı alan seçimi
    // ==================================================================

    [Fact]
    public async Task CokluBolge_HepsininBirlesimiKaydedilir()
    {
        // Ege (27–29) + İç Anadolu (32–33): ayrık iki bölge.
        // Birleşim MultiPolygon olmalı ve ikisini de kapsamalı.
        var iller = new FakeIlRepository()
            .Ekle(35, "İzmir", "POLYGON ((27 38, 28 38, 28 39, 27 39, 27 38))")
            .Ekle(45, "Manisa", "POLYGON ((28 38, 29 38, 29 39, 28 39, 28 38))")
            .Ekle(6, "Ankara", "POLYGON ((32 39, 33 39, 33 40, 32 40, 32 39))");

        var (db, servis) = Kur(iller);

        await servis.CreateAsync(new GeoPermissionCreateDto
        {
            Name = "Ege + İç Anadolu",
            UserId = 2,
            Bolgeler = new List<string> { "Ege", "İç Anadolu" },
        });

        var geom = Assert.Single(db.CografiYetkiler).Geom;
        var sinir = geom.EnvelopeInternal;

        Assert.Equal(27, sinir.MinX, precision: 6);
        Assert.Equal(33, sinir.MaxX, precision: 6);
        // Ayrık bölgeler → çok parçalı geometri
        Assert.True(geom.NumGeometries > 1, "Ayrık bölgeler tek parçaya indirgenmemeli");
    }

    [Fact]
    public async Task CokluBolge_AyniBolgeIkiKezGelirseSorunOlmaz()
    {
        var iller = new FakeIlRepository()
            .Ekle(35, "İzmir", "POLYGON ((27 38, 28 38, 28 39, 27 39, 27 38))");

        var (db, servis) = Kur(iller);

        await servis.CreateAsync(new GeoPermissionCreateDto
        {
            Name = "Ege x2", UserId = 2,
            Bolgeler = new List<string> { "Ege", "Ege" },
        });

        Assert.Single(db.CografiYetkiler);
    }

    /// <summary>Kayıtlı poligon deposu — iki komşu kare alan.</summary>
    private static FakeGeometryRepository<PolygonEntity> KayitliAlanlar()
    {
        var depo = new FakeGeometryRepository<PolygonEntity>();
        depo.AddAsync(new PolygonEntity
        {
            Name = "Alan A",
            Geom = WktConverter.Read<Polygon>("POLYGON ((10 10, 11 10, 11 11, 10 11, 10 10))"),
        }).GetAwaiter().GetResult();
        depo.AddAsync(new PolygonEntity
        {
            Name = "Alan B",
            Geom = WktConverter.Read<Polygon>("POLYGON ((11 10, 12 10, 12 11, 11 11, 11 10))"),
        }).GetAwaiter().GetResult();
        return depo;
    }

    [Fact]
    public async Task KayitliAlanSecimi_SecilenPoligonlarinBirlesimi()
    {
        var db = new SahteVeritabani();
        db.Kullanicilar.Add(new User { Id = 2, Username = "ayse" });

        var servis = new GeoPermissionService(
            new FakeGeoPermissionRepository(db),
            new FakeUserRepository(db),
            new FakeRoleRepository(db),
            new FakeIlRepository(),
            KayitliAlanlar(),
            new FakeCurrentUserService(1));

        await servis.CreateAsync(new GeoPermissionCreateDto
        {
            Name = "İki alan", UserId = 2, PoligonIdleri = new List<int> { 1, 2 },
        });

        var sinir = Assert.Single(db.CografiYetkiler).Geom.EnvelopeInternal;

        // İki bitişik kare → 10'dan 12'ye uzanan tek dikdörtgen
        Assert.Equal(10, sinir.MinX, precision: 6);
        Assert.Equal(12, sinir.MaxX, precision: 6);
    }

    [Fact]
    public async Task KayitliAlanSecimi_OlmayanIdReddedilir()
    {
        var db = new SahteVeritabani();
        db.Kullanicilar.Add(new User { Id = 2, Username = "ayse" });

        var servis = new GeoPermissionService(
            new FakeGeoPermissionRepository(db),
            new FakeUserRepository(db),
            new FakeRoleRepository(db),
            new FakeIlRepository(),
            KayitliAlanlar(),
            new FakeCurrentUserService(1));

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() =>
            servis.CreateAsync(new GeoPermissionCreateDto
            {
                Name = "Yok", UserId = 2, PoligonIdleri = new List<int> { 999 },
            }));

        Assert.Contains("bulunamadı", hata.Message);
    }

    [Fact]
    public async Task DortYoldanIkisiBirdenGelirse_Reddedilir()
    {
        var (_, servis) = Kur(IkiKomsuIl());

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() =>
            servis.CreateAsync(new GeoPermissionCreateDto
            {
                Name = "Karışık", UserId = 2,
                IlPlakalari = new List<int> { 6 },
                PoligonIdleri = new List<int> { 1 },
            }));

        Assert.Contains("tek bir yolla", hata.Message);
    }

    // ==================================================================
    //  3) Kayıt olma + yönetici onayı
    // ==================================================================

    private static AppDbContext YeniContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AuthService AuthServisi(AppDbContext db)
        => new(new UserRepository(db), new RefreshTokenRepository(db),
            new FakeCurrentUserService(), Options.Create(new JwtSettings
        {
            Key = "test-anahtari-en-az-32-karakter-olmali-1234",
            Issuer = "test",
            Audience = "test",
            ExpiryMinutes = 10,
        }));

    [Fact]
    public async Task Kayit_HesapOnayBekleyerekOlusur()
    {
        using var db = YeniContext();

        var sonuc = await AuthServisi(db).RegisterAsync(new RegisterRequestDto
        {
            Username = "yeni", Password = "deneme123",
        });

        var kullanici = await db.Users.SingleAsync(u => u.Username == "yeni");

        Assert.False(kullanici.IsApproved);   // asıl kural
        Assert.True(kullanici.IsActive);      // pasif DEĞİL — onaysız, ayrı şey
        Assert.Contains("onay", sonuc.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Kayit_SifreDuzMetinSaklanmaz()
    {
        using var db = YeniContext();

        await AuthServisi(db).RegisterAsync(new RegisterRequestDto
        {
            Username = "yeni", Password = "deneme123",
        });

        var kullanici = await db.Users.SingleAsync();
        Assert.DoesNotContain("deneme123", kullanici.PasswordHash);
    }

    [Fact]
    public async Task Kayit_AyniKullaniciAdiReddedilir()
    {
        using var db = YeniContext();
        var servis = AuthServisi(db);

        await servis.RegisterAsync(new RegisterRequestDto { Username = "ayse", Password = "deneme123" });

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() =>
            servis.RegisterAsync(new RegisterRequestDto { Username = "ayse", Password = "baska123" }));

        Assert.Contains("zaten alınmış", hata.Message);
    }

    [Fact]
    public async Task OnaysizHesap_DogruSifreyleBileGirisYapamaz()
    {
        using var db = YeniContext();
        var servis = AuthServisi(db);

        await servis.RegisterAsync(new RegisterRequestDto { Username = "yeni", Password = "deneme123" });

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() =>
            servis.LoginAsync(new LoginRequestDto { Username = "yeni", Password = "deneme123" }));

        Assert.Contains("onay", hata.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnaysizHesap_YANLIS_SifredeOnayBilgisiSizmaz()
    {
        // Sıra önemli: onay kontrolü ŞİFRE DOĞRULANDIKTAN SONRA yapılıyor.
        // Önce yapılsaydı, şifreyi bilmeyen biri de "onay bekliyor" cevabını
        // alır ve o kullanıcı adının var olduğunu öğrenirdi.
        using var db = YeniContext();
        var servis = AuthServisi(db);

        await servis.RegisterAsync(new RegisterRequestDto { Username = "yeni", Password = "deneme123" });

        var sonuc = await servis.LoginAsync(new LoginRequestDto
        {
            Username = "yeni", Password = "YANLIS-SIFRE",
        });

        Assert.Null(sonuc);   // istisna DEĞİL, sıradan "giriş başarısız"
    }

    [Fact]
    public async Task OnaylandiktanSonra_GirisYapabilir()
    {
        using var db = YeniContext();
        var servis = AuthServisi(db);

        await servis.RegisterAsync(new RegisterRequestDto { Username = "yeni", Password = "deneme123" });

        var kullanici = await db.Users.SingleAsync();
        kullanici.IsApproved = true;
        await db.SaveChangesAsync();

        var sonuc = await servis.LoginAsync(new LoginRequestDto
        {
            Username = "yeni", Password = "deneme123",
        });

        Assert.NotNull(sonuc);
        Assert.False(string.IsNullOrWhiteSpace(sonuc!.Token));
    }

    [Fact]
    public async Task PanelinActigiKullanici_DogrudanOnaylidir()
    {
        // Yöneticinin oluşturduğu hesabı bir de onaylatmak anlamsız olurdu:
        // onu zaten bir yönetici açtı. Varsayılan true bunu sağlıyor.
        using var db = YeniContext();

        db.Users.Add(new User { Username = "panelden", PasswordHash = "x" });
        await db.SaveChangesAsync();

        Assert.True((await db.Users.SingleAsync()).IsApproved);
    }
}
