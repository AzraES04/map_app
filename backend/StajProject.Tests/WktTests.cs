using NetTopologySuite.Geometries;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Services;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 3 / Görev 3 testleri: WKT çözümleme, tip doğrulama ve SRID yönetimi.
/// </summary>
public class WktTests
{
    // ---------------- Okuma (WKT → geometri) ----------------

    [Fact]
    public void Read_Point_KoordinatlariDogruSiradaOkur()
    {
        var point = WktConverter.Read<Point>("POINT (32.8597 39.9334)");

        // WKT'de sıra "X Y" = "boylam enlem". Ankara: boylam 32.85, enlem 39.93.
        Assert.Equal(32.8597, point.X, precision: 6);   // X = boylam (longitude)
        Assert.Equal(39.9334, point.Y, precision: 6);   // Y = enlem  (latitude)
    }

    [Fact]
    public void Read_Point_SridDaimao4326Olur()
    {
        // WKT metninin İÇİNDE SRID bilgisi yoktur; okurken biz atıyoruz.
        var point = WktConverter.Read<Point>("POINT (32.8597 39.9334)");

        Assert.Equal(4326, point.SRID);
    }

    [Fact]
    public void Read_LineString_TumNoktalariOkur()
    {
        var line = WktConverter.Read<LineString>(
            "LINESTRING (32.85 39.93, 32.86 39.94, 32.87 39.92)");

        Assert.Equal(3, line.NumPoints);
        Assert.Equal(32.85, line.Coordinates[0].X, precision: 6);
        Assert.Equal(39.92, line.Coordinates[2].Y, precision: 6);
    }

    [Fact]
    public void Read_Polygon_KapaliHalkaOkunur()
    {
        var polygon = WktConverter.Read<Polygon>(
            "POLYGON ((32.85 39.93, 32.87 39.93, 32.87 39.95, 32.85 39.95, 32.85 39.93))");

        // Kapalı halka: ilk ve son koordinat aynı olduğu için 5 nokta = 4 köşe
        Assert.Equal(5, polygon.ExteriorRing.NumPoints);
        Assert.True(polygon.Shell.IsClosed);
        Assert.Equal(4326, polygon.SRID);
    }

    // ---------------- Tip ve format doğrulaması ----------------

    [Fact]
    public void Read_YanlisTip_WktFormatExceptionFirlatir()
    {
        // tbl_point endpoint'ine poligon gönderilmiş senaryosu
        var ex = Assert.Throws<WktFormatException>(
            () => WktConverter.Read<Point>("POLYGON ((0 0, 1 0, 1 1, 0 0))"));

        Assert.Contains("Point", ex.Message);
    }

    [Fact]
    public void Read_BozukMetin_WktFormatExceptionFirlatir()
    {
        Assert.Throws<WktFormatException>(() => WktConverter.Read<Point>("bu bir wkt degil"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Read_BosMetin_WktFormatExceptionFirlatir(string? wkt)
    {
        Assert.Throws<WktFormatException>(() => WktConverter.Read<Point>(wkt));
    }

    [Fact]
    public void Read_KapanmamisPoligon_Reddedilir()
    {
        // Son koordinat ilkine eşit değil → geçersiz halka
        Assert.Throws<WktFormatException>(
            () => WktConverter.Read<Polygon>("POLYGON ((0 0, 1 0, 1 1, 0 1))"));
    }

    // ---------------- Gidiş-dönüş (round-trip) ----------------

    [Fact]
    public void GidisDonus_KoordinatKaybiOlmaz()
    {
        const string original = "LINESTRING (32.850001 39.930002, 32.860003 39.940004)";

        var geometry = WktConverter.Read<LineString>(original);
        var tekrar = WktConverter.Write(geometry);
        var ikinciOkuma = WktConverter.Read<LineString>(tekrar);

        Assert.Equal(geometry.Coordinates[0].X, ikinciOkuma.Coordinates[0].X, precision: 6);
        Assert.Equal(geometry.Coordinates[1].Y, ikinciOkuma.Coordinates[1].Y, precision: 6);
    }

    // ---------------- Servis katmanı ----------------

    private static GeometryService<PointEntity, Point> CreatePointService()
        => new(new FakeGeometryRepository<PointEntity>());

    [Fact]
    public async Task CreateAsync_WktKaydeder_VeWktOlarakGeriDoner()
    {
        var service = CreatePointService();

        var created = await service.CreateAsync(new GeometryCreateDto
        {
            Name = "Anıtkabir",
            Description = "Ankara",
            Wkt = "POINT (32.8597 39.9334)"
        });

        Assert.True(created.Id > 0);
        Assert.Equal("Point", created.GeometryType);
        Assert.Contains("32.8597", created.Wkt);
        Assert.Contains("39.9334", created.Wkt);
        Assert.Null(created.ModifiedDate);   // yeni kayıt, henüz güncellenmedi
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task CreateAsync_YanlisGeometriTipi_Reddedilir()
    {
        var service = CreatePointService();

        await Assert.ThrowsAsync<WktFormatException>(() => service.CreateAsync(new GeometryCreateDto
        {
            Name = "Hatalı",
            Wkt = "LINESTRING (0 0, 1 1)"     // nokta servisine çizgi gönderiliyor
        }));
    }

    [Fact]
    public async Task DeleteAsync_SoftDelete_KayitListedenDuser()
    {
        var service = CreatePointService();
        var created = await service.CreateAsync(new GeometryCreateDto
        {
            Name = "Silinecek",
            Wkt = "POINT (30 40)"
        });

        var silindi = await service.DeleteAsync(created.Id);

        Assert.True(silindi);
        Assert.Empty(await service.GetAllAsync());
        Assert.False(await service.DeleteAsync(created.Id));   // ikinci kez silinemez
    }

    [Fact]
    public async Task UpdateAsync_GeometriBosBirakilirsa_SadeceAdDegisir()
    {
        var service = CreatePointService();
        var created = await service.CreateAsync(new GeometryCreateDto
        {
            Name = "Eski ad",
            Wkt = "POINT (30 40)"
        });

        var updated = await service.UpdateAsync(created.Id, new GeometryUpdateDto
        {
            Name = "Yeni ad",
            Wkt = null            // geometriye dokunma
        });

        Assert.NotNull(updated);
        Assert.Equal("Yeni ad", updated!.Name);
        Assert.Equal(created.Wkt, updated.Wkt);   // geometri aynı kaldı
    }
}
