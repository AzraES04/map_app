using NetTopologySuite.Geometries;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Services;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 5 / Madde 3 testleri: harita yalnızca GİRİŞ YAPAN kullanıcının
/// çizimlerini listelemeli; başkasının kaydına erişim, güncelleme ve silme
/// mümkün olmamalı.
///
/// Yetkisiz erişimde bilerek 404 ("bulunamadı") davranışı üretiyoruz, 403 değil:
/// 403 dönmek saldırgana "bu id'de bir kayıt VAR ama senin değil" bilgisini
/// doğrulardı. Kaynağın varlığını sızdırmamak daha güvenli.
/// </summary>
public class SahiplikTests
{
    private const int Ayse = 1;
    private const int Mehmet = 2;

    private static (GeometryService<PointEntity, Point> servis, FakeCurrentUserService kullanici)
        Kur(FakeGeometryRepository<PointEntity> repo, int userId)
    {
        var kullanici = new FakeCurrentUserService(userId);
        return (new GeometryService<PointEntity, Point>(repo, kullanici), kullanici);
    }

    private static GeometryCreateDto Nokta(string ad, double x = 32.85, double y = 39.93)
        => new() { Name = ad, Color = "#2e8fa8", Wkt = $"POINT ({x} {y})" };

    [Fact]
    public async Task OlusturulanKayit_GirisYapanKullaniciyaBaglanir()
    {
        var repo = new FakeGeometryRepository<PointEntity>();
        var (servis, _) = Kur(repo, Ayse);

        var olusan = await servis.CreateAsync(Nokta("Ayşe'nin noktası"));

        Assert.Equal(Ayse, olusan.InsertedUserId);
        Assert.NotEqual(default, olusan.InsertedDate);
    }

    [Fact]
    public async Task Listeleme_YalnizcaKendiKayitlariniDoner()
    {
        var repo = new FakeGeometryRepository<PointEntity>();

        var (ayseServis, _) = Kur(repo, Ayse);
        await ayseServis.CreateAsync(Nokta("Ayşe 1"));
        await ayseServis.CreateAsync(Nokta("Ayşe 2", 33.0));

        var (mehmetServis, _) = Kur(repo, Mehmet);
        await mehmetServis.CreateAsync(Nokta("Mehmet 1", 29.0, 41.0));

        var ayseninListesi = await ayseServis.GetAllAsync();
        var mehmedinListesi = await mehmetServis.GetAllAsync();

        Assert.Equal(2, ayseninListesi.Count);
        Assert.All(ayseninListesi, k => Assert.StartsWith("Ayşe", k.Name));

        Assert.Single(mehmedinListesi);
        Assert.Equal("Mehmet 1", mehmedinListesi[0].Name);
    }

    [Fact]
    public async Task BaskasininKaydi_TekilGetirmede_Gorunmez()
    {
        var repo = new FakeGeometryRepository<PointEntity>();
        var (ayseServis, _) = Kur(repo, Ayse);
        var ayseninKaydi = await ayseServis.CreateAsync(Nokta("Ayşe'nin noktası"));

        var (mehmetServis, _) = Kur(repo, Mehmet);

        Assert.Null(await mehmetServis.GetByIdAsync(ayseninKaydi.Id));
        Assert.NotNull(await ayseServis.GetByIdAsync(ayseninKaydi.Id));   // sahibi görebiliyor
    }

    [Fact]
    public async Task BaskasininKaydi_Guncellenemez()
    {
        var repo = new FakeGeometryRepository<PointEntity>();
        var (ayseServis, _) = Kur(repo, Ayse);
        var kayit = await ayseServis.CreateAsync(Nokta("Ayşe'nin noktası"));

        var (mehmetServis, _) = Kur(repo, Mehmet);
        var sonuc = await mehmetServis.UpdateAsync(kayit.Id, new GeometryUpdateDto
        {
            Name = "Mehmet ele geçirdi",
            Color = "#d9553f",
        });

        Assert.Null(sonuc);

        // Kayıt bozulmamış olmalı
        var guncel = await ayseServis.GetByIdAsync(kayit.Id);
        Assert.Equal("Ayşe'nin noktası", guncel!.Name);
    }

    [Fact]
    public async Task BaskasininKaydi_Silinemez()
    {
        var repo = new FakeGeometryRepository<PointEntity>();
        var (ayseServis, _) = Kur(repo, Ayse);
        var kayit = await ayseServis.CreateAsync(Nokta("Ayşe'nin noktası"));

        var (mehmetServis, _) = Kur(repo, Mehmet);

        Assert.False(await mehmetServis.DeleteAsync(kayit.Id));
        Assert.Single(await ayseServis.GetAllAsync());     // hâlâ duruyor

        Assert.True(await ayseServis.DeleteAsync(kayit.Id));   // sahibi silebiliyor
        Assert.Empty(await ayseServis.GetAllAsync());
    }

    [Fact]
    public async Task GirisYapilmamissa_IslemReddedilir()
    {
        var repo = new FakeGeometryRepository<PointEntity>();
        var servis = new GeometryService<PointEntity, Point>(
            repo, new FakeCurrentUserService(userId: null));

        await Assert.ThrowsAsync<WktFormatException>(() => servis.GetAllAsync());
        await Assert.ThrowsAsync<WktFormatException>(() => servis.CreateAsync(Nokta("Sahipsiz")));
    }

    [Fact]
    public async Task Guncelleme_GeometriyiDeDegistirebilir()
    {
        // Ödev 5 / Madde 4: detay ekranından konum da güncellenebilmeli
        var repo = new FakeGeometryRepository<PointEntity>();
        var (servis, _) = Kur(repo, Ayse);
        var kayit = await servis.CreateAsync(Nokta("Taşınacak", 32.85, 39.93));

        var guncel = await servis.UpdateAsync(kayit.Id, new GeometryUpdateDto
        {
            Name = "Taşındı",
            Color = "#57a05a",
            Wkt = "POINT (28.9784 41.0082)",     // İstanbul'a taşındı
        });

        Assert.NotNull(guncel);
        Assert.Equal("Taşındı", guncel!.Name);
        Assert.Equal("#57a05a", guncel.Color);
        Assert.Contains("28.9784", guncel.Wkt);
        Assert.NotNull(guncel.ModifiedDate);      // audit damgası basıldı
    }
}
