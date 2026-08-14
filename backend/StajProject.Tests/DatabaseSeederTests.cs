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
    {
        var kullanicilar = new FakeUserRepository();
        var noktalar = new FakeGeometryRepository<PointEntity>();
        var cizgiler = new FakeGeometryRepository<LineEntity>();
        var poligonlar = new FakeGeometryRepository<PolygonEntity>();

        return (new DatabaseSeeder(kullanicilar, noktalar, cizgiler, poligonlar),
                kullanicilar, noktalar, cizgiler, poligonlar);
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
    public async Task KullanicininKendiVerisiVarsa_OTabloyaDokunulmaz()
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
        Assert.Single(kalan);                                  // örnek nokta EKLENMEDİ
        Assert.Equal("Kullanıcının noktası", kalan[0].Name);
        Assert.NotEmpty(await cizgiler.GetAllAsync());          // boş olan tablo doldu
    }
}
