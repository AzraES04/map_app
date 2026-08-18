using StajProject.Business.Services;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Başlangıç verisi kuralları:
///   - Kullanıcı yoksa demo admin oluşturulur
///   - Geometri tabloları BOŞSA örnek envanter yüklenir
///   - Dolu tabloya DOKUNULMAZ (kullanıcının verisi korunur)
/// Bu üçüncü kural olmasaydı uygulama her açılışta aynı kayıtları tekrar ekler,
/// veritabanı kopyalarla şişerdi.
/// </summary>
public class DatabaseSeederTests
{
    private static (DatabaseSeeder seeder,
                    FakeUserRepository kullanicilar,
                    FakeGeometryRepository<PointEntity> noktalar,
                    FakeGeometryRepository<LineEntity> cizgiler,
                    FakeGeometryRepository<PolygonEntity> poligonlar) Kur()
        => Kur(new SahteVeritabani());

    /// <summary>
    /// Ödev 6 ile birlikte seed, kullanıcıların yanında rol ve yetki de kuruyor.
    /// Bu yüzden üç repository de AYNI sahte veritabanını paylaşmalı; rol/yetki
    /// tarafını inceleyen testler bu aşırı yükleme ile kendi veritabanını verir.
    /// </summary>
    private static (DatabaseSeeder seeder,
                    FakeUserRepository kullanicilar,
                    FakeGeometryRepository<PointEntity> noktalar,
                    FakeGeometryRepository<LineEntity> cizgiler,
                    FakeGeometryRepository<PolygonEntity> poligonlar) Kur(SahteVeritabani db)
    {
        var kullanicilar = new FakeUserRepository(db);
        var noktalar = new FakeGeometryRepository<PointEntity>();
        var cizgiler = new FakeGeometryRepository<LineEntity>();
        var poligonlar = new FakeGeometryRepository<PolygonEntity>();

        var seeder = new DatabaseSeeder(
            kullanicilar,
            new FakeRoleRepository(db),
            new FakePermissionRepository(db),
            noktalar, cizgiler, poligonlar);

        return (seeder, kullanicilar, noktalar, cizgiler, poligonlar);
    }

    [Fact]
    public async Task BosVeritabani_DemoEnvanteriYuklenir()
    {
        var (seeder, kullanicilar, noktalar, cizgiler, poligonlar) = Kur();

        await seeder.SeedAsync();

        Assert.True(await kullanicilar.AnyAsync());                 // admin oluştu
        Assert.NotEmpty(await noktalar.GetAllAsync());
        Assert.NotEmpty(await cizgiler.GetAllAsync());
        Assert.NotEmpty(await poligonlar.GetAllAsync());
    }

    [Fact]
    public async Task YuklenenKayitlar_Ad_Renk_ve_Gecerli_Geometri_Icerir()
    {
        var (seeder, _, noktalar, _, poligonlar) = Kur();

        await seeder.SeedAsync();

        foreach (var kayit in await noktalar.GetAllAsync())
        {
            Assert.False(string.IsNullOrWhiteSpace(kayit.Name));
            Assert.Matches("^#[0-9a-f]{6}$", kayit.Color!);          // Ödev 4 / Görev 2
            Assert.Equal(4326, kayit.Geometry.SRID);                 // Ödev 3 / Görev 3
            Assert.True(kayit.Geometry.IsValid);
        }

        // Poligonların halkası kapalı olmalı (WKT kuralı)
        foreach (var kayit in await poligonlar.GetAllAsync())
        {
            Assert.Equal("Polygon", kayit.Geometry.GeometryType);
            Assert.True(((NetTopologySuite.Geometries.Polygon)kayit.Geometry).Shell.IsClosed);
        }
    }

    [Fact]
    public async Task IkinciCalistirma_KayitlariCogaltmaz()
    {
        var (seeder, _, noktalar, cizgiler, poligonlar) = Kur();

        await seeder.SeedAsync();
        var ilkSayilar = (
            (await noktalar.GetAllAsync()).Count,
            (await cizgiler.GetAllAsync()).Count,
            (await poligonlar.GetAllAsync()).Count);

        await seeder.SeedAsync();   // uygulama yeniden başlatıldı
        var ikinciSayilar = (
            (await noktalar.GetAllAsync()).Count,
            (await cizgiler.GetAllAsync()).Count,
            (await poligonlar.GetAllAsync()).Count);

        Assert.Equal(ilkSayilar, ikinciSayilar);
    }

    [Fact]
    public async Task KullanicininKendiVerisiVarsa_GenelDemoSetiEklenmez()
    {
        var (seeder, _, noktalar, cizgiler, _) = Kur();

        // Kullanıcı kendi noktasını çizmiş
        await noktalar.AddAsync(new PointEntity
        {
            Name = "Kullanıcının noktası",
            Geom = new NetTopologySuite.Geometries.Point(32.85, 39.93) { SRID = 4326 },
        });

        await seeder.SeedAsync();

        var kalan = await noktalar.GetAllAsync();

        // Kullanıcının kaydı duruyor
        Assert.Contains(kalan, k => k.Name == "Kullanıcının noktası");

        // Genel demo seti (admin'in kayıtları) EKLENMEDİ — tablo boş değildi
        Assert.DoesNotContain(kalan, k => k.Name == "Anıtkabir");
        Assert.DoesNotContain(kalan, k => k.Name == "Ayasofya");

        // Boş olan çizgi tablosu doldu
        Assert.NotEmpty(await cizgiler.GetAllAsync());
    }

    [Fact]
    public async Task IkinciKullanici_KendiKayitlariylaOlusturulur()
    {
        // Ödev 5 / Madde 3'ün gösterilebilmesi için ikinci kullanıcı ve
        // ona ait ayrı bir veri seti oluşturulmalı.
        var (seeder, kullanicilar, noktalar, _, poligonlar) = Kur();

        await seeder.SeedAsync();

        var ayse = await kullanicilar.GetByUsernameAsync("ayse");
        Assert.NotNull(ayse);

        var ayseninNoktalari = await noktalar.GetAllAsync(ayse!.Id);
        var ayseninAlanlari = await poligonlar.GetAllAsync(ayse.Id);

        Assert.NotEmpty(ayseninNoktalari);
        Assert.NotEmpty(ayseninAlanlari);
        Assert.All(ayseninNoktalari, k => Assert.Equal(ayse.Id, k.InsertedUserId));

        // İki kullanıcının verisi ayrışmalı: admin'in seti ayse'de görünmemeli
        Assert.DoesNotContain(ayseninNoktalari, k => k.Name == "Anıtkabir");
    }

    [Fact]
    public async Task IkinciCalistirma_IkinciKullanicininKayitlariniCogaltmaz()
    {
        var (seeder, kullanicilar, noktalar, _, _) = Kur();

        await seeder.SeedAsync();
        var ayse = (await kullanicilar.GetByUsernameAsync("ayse"))!;
        var ilkSayi = (await noktalar.GetAllAsync(ayse.Id)).Count;

        await seeder.SeedAsync();   // uygulama yeniden başlatıldı

        Assert.Equal(ilkSayi, (await noktalar.GetAllAsync(ayse.Id)).Count);
    }
}
