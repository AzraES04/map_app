using NetTopologySuite.Geometries;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Services;
using StajProject.Business.Validation;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 7 / Madde 2 testleri: COĞRAFİ YETKİ.
///
/// Kural: kullanıcıya (veya rolüne) bir alan tanımlandıysa, çizimleri o alanın
/// dışına taşamaz. Hiç tanım yoksa kısıt da yoktur.
///
/// Testlerdeki koordinatlar sadeliği için 0–10 aralığında tutuldu; gerçek
/// coğrafya (Ankara vs.) yerine küçük kareler kullanmak, hangi noktanın nerede
/// olduğunu okurken hesap yapmayı gerektirmiyor.
/// </summary>
public class CografiYetkiTests
{
    private const int Ayse = 1;

    /// <summary>(0,0)-(10,10) karesi — "izinli bölge".</summary>
    private const string BuyukKare = "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))";

    /// <summary>(20,20)-(30,30) karesi — büyük kareyle hiç kesişmeyen uzak bölge.</summary>
    private const string UzakKare = "POLYGON ((20 20, 30 20, 30 30, 20 30, 20 20))";

    private static (SahteVeritabani db, GeoPermissionService servis) Kur(int? girisYapan = Ayse)
        => KurIllerle(new FakeIlRepository(), girisYapan);

    /// <summary>
    /// Ödev 10: il/bölge seçimini sınayan testler kendi il listesini veriyor.
    /// Diğer testler boş bir liste alıyor — o yolu hiç kullanmıyorlar.
    /// </summary>
    private static (SahteVeritabani db, GeoPermissionService servis) KurIllerle(
        FakeIlRepository iller, int? girisYapan = Ayse)
    {
        var db = new SahteVeritabani();
        var oturum = new FakeCurrentUserService(girisYapan);

        var servis = new GeoPermissionService(
            new FakeGeoPermissionRepository(db),
            new FakeUserRepository(db),
            new FakeRoleRepository(db),
            iller,
            new FakeGeometryRepository<PolygonEntity>(),
            oturum);

        return (db, servis);
    }

    /// <summary>Veritabanına doğrudan alan ekler (panelden tanımlanmış gibi).</summary>
    private static GeoPermission AlanEkle(SahteVeritabani db, string wkt, int? userId = null, int? roleId = null)
    {
        var kayit = new GeoPermission
        {
            Id = db.SonrakiCografiYetkiId(),
            Name = "Test alanı",
            UserId = userId,
            RoleId = roleId,
            Geom = WktConverter.Read<Polygon>(wkt),
        };
        db.CografiYetkiler.Add(kayit);
        return kayit;
    }

    private static Geometry Nokta(double x, double y) => WktConverter.Read<Point>($"POINT ({x} {y})");

    // ------------------------------------------------------------------
    //  Temel kural
    // ------------------------------------------------------------------

    [Fact]
    public async Task TanimYoksa_HerYereCizilebilir()
    {
        // Kısıtlama, konması gereken bir kuraldır. Tersini seçseydik modül
        // eklendiği anda bütün kullanıcılar çizim yapamaz hâle gelirdi.
        var (_, servis) = Kur();

        await servis.DogrulaAsync(Ayse, Nokta(999, 999));   // fırlatmıyorsa geçti
    }

    [Fact]
    public async Task AlanIcindekiCizim_KabulEdilir()
    {
        var (db, servis) = Kur();
        AlanEkle(db, BuyukKare, userId: Ayse);

        await servis.DogrulaAsync(Ayse, Nokta(5, 5));
    }

    [Fact]
    public async Task AlanDisindakiCizim_Reddedilir()
    {
        var (db, servis) = Kur();
        AlanEkle(db, BuyukKare, userId: Ayse);

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => servis.DogrulaAsync(Ayse, Nokta(50, 50)));

        Assert.Contains("tanımlı alanın dışında", hata.Message);
    }

    [Fact]
    public async Task AlanaTasanCizgi_Reddedilir()
    {
        // Ödevin şartı "alanın dışına çizim yapamasın" — bir ucu içeride,
        // diğeri dışarıda olan çizgi de alanın dışına taşmış sayılır.
        var (db, servis) = Kur();
        AlanEkle(db, BuyukKare, userId: Ayse);

        var tasanCizgi = WktConverter.Read<LineString>("LINESTRING (5 5, 50 50)");

        await Assert.ThrowsAsync<IsKuraliException>(() => servis.DogrulaAsync(Ayse, tasanCizgi));
    }

    [Fact]
    public async Task SinirUzerindekiNokta_KabulEdilir()
    {
        // Covers kullanıyoruz (Contains değil): tam kenara konan noktayı
        // reddetmek kullanıcıya açıklanması zor, keyfî bir davranış olurdu.
        var (db, servis) = Kur();
        AlanEkle(db, BuyukKare, userId: Ayse);

        await servis.DogrulaAsync(Ayse, Nokta(10, 5));
    }

    // ------------------------------------------------------------------
    //  Alanın kaynağı: kullanıcı / rol / birleşim
    // ------------------------------------------------------------------

    [Fact]
    public async Task RoldenGelenAlan_KullaniciyaUygulanir()
    {
        var (db, servis) = Kur();
        var rol = db.RolEkle("Saha Ekibi");
        db.KullaniciRolleri.Add(new UserRole { UserId = Ayse, RoleId = rol.Id });
        AlanEkle(db, BuyukKare, roleId: rol.Id);

        await servis.DogrulaAsync(Ayse, Nokta(5, 5));                       // içeride
        await Assert.ThrowsAsync<IsKuraliException>(
            () => servis.DogrulaAsync(Ayse, Nokta(50, 50)));                 // dışarıda
    }

    [Fact]
    public async Task PasifRoldenGelenAlan_KisitlamaUygulamaz()
    {
        // Pasif rol yetki üretmiyorsa coğrafi kısıt da üretmemeli;
        // iki kuralın tutarlı olması gerekiyor (bkz. PermissionService).
        var (db, servis) = Kur();
        var rol = db.RolEkle("Askıya alınmış");
        rol.IsActive = false;
        db.KullaniciRolleri.Add(new UserRole { UserId = Ayse, RoleId = rol.Id });
        AlanEkle(db, BuyukKare, roleId: rol.Id);

        await servis.DogrulaAsync(Ayse, Nokta(999, 999));   // kısıt yok sayılır
    }

    [Fact]
    public async Task BirdenFazlaAlan_BIRLESIMOlarakDegerlendirilir()
    {
        // İki ayrı bölge tanımlıysa kullanıcı ikisinde de çizebilmeli.
        var (db, servis) = Kur();
        var rol = db.RolEkle("Editör");
        db.KullaniciRolleri.Add(new UserRole { UserId = Ayse, RoleId = rol.Id });

        AlanEkle(db, BuyukKare, userId: Ayse);   // doğrudan
        AlanEkle(db, UzakKare, roleId: rol.Id);  // rolden

        await servis.DogrulaAsync(Ayse, Nokta(5, 5));     // birinci alanda
        await servis.DogrulaAsync(Ayse, Nokta(25, 25));   // ikinci alanda

        await Assert.ThrowsAsync<IsKuraliException>(
            () => servis.DogrulaAsync(Ayse, Nokta(15, 15)));   // ikisinin de dışında
    }

    [Fact]
    public async Task PasifAlanTanimi_KisitUygulamaz()
    {
        var (db, servis) = Kur();
        var alan = AlanEkle(db, BuyukKare, userId: Ayse);
        alan.IsActive = false;   // kural geçici olarak askıya alındı

        await servis.DogrulaAsync(Ayse, Nokta(999, 999));
    }

    [Fact]
    public async Task BaskaKullanicininAlani_BeniBaglamaz()
    {
        var (db, servis) = Kur();
        AlanEkle(db, BuyukKare, userId: 42);   // başka kullanıcıya tanımlı

        await servis.DogrulaAsync(Ayse, Nokta(999, 999));
    }

    // ------------------------------------------------------------------
    //  Yönetim tarafı doğrulamaları
    // ------------------------------------------------------------------

    [Fact]
    public async Task Tanimlarken_HemKullaniciHemRolVerilemez()
    {
        var (db, servis) = Kur();
        db.Kullanicilar.Add(new User { Id = Ayse, Username = "ayse" });
        var rol = db.RolEkle("Editör");

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() =>
            servis.CreateAsync(new GeoPermissionCreateDto
            {
                Name = "Karışık",
                UserId = Ayse,
                RoleId = rol.Id,
                Wkt = BuyukKare,
            }));

        Assert.Contains("ikisi birden olamaz", hata.Message);
    }

    [Fact]
    public async Task Tanimlarken_SahipsizAlanEklenemez()
    {
        var (_, servis) = Kur();

        await Assert.ThrowsAsync<IsKuraliException>(() =>
            servis.CreateAsync(new GeoPermissionCreateDto { Name = "Sahipsiz", Wkt = BuyukKare }));
    }

    [Fact]
    public async Task Tanimlarken_OlmayanKullaniciReddedilir()
    {
        var (_, servis) = Kur();

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() =>
            servis.CreateAsync(new GeoPermissionCreateDto
            {
                Name = "Hayalet",
                UserId = 999,
                Wkt = BuyukKare,
            }));

        Assert.Contains("kullanıcı bulunamadı", hata.Message);
    }

    [Fact]
    public async Task TanimlananAlan_AninaGecerliOlur()
    {
        // Dinamik yetkilendirmenin coğrafi karşılığı: yönetici alanı tanımlar
        // tanımlamaz kural işler, kullanıcının yeniden giriş yapması gerekmez.
        var (db, servis) = Kur();
        db.Kullanicilar.Add(new User { Id = Ayse, Username = "ayse" });

        await servis.DogrulaAsync(Ayse, Nokta(999, 999));   // önce serbest

        await servis.CreateAsync(new GeoPermissionCreateDto
        {
            Name = "Ankara ve çevresi",
            UserId = Ayse,
            Wkt = BuyukKare,
        });

        await Assert.ThrowsAsync<IsKuraliException>(
            () => servis.DogrulaAsync(Ayse, Nokta(999, 999)));   // artık kısıtlı
    }

    [Fact]
    public async Task CalismaAlanim_KisitYoksaBosDoner()
    {
        var (_, servis) = Kur();

        var alan = await servis.GetCalismaAlanimAsync();

        Assert.False(alan.Kisitli);
        Assert.Empty(alan.Alanlar);
    }

    [Fact]
    public async Task CalismaAlanim_KaynagiBelirtir()
    {
        var (db, servis) = Kur();
        var rol = db.RolEkle("Editör");
        db.KullaniciRolleri.Add(new UserRole { UserId = Ayse, RoleId = rol.Id });
        AlanEkle(db, BuyukKare, roleId: rol.Id);
        AlanEkle(db, UzakKare, userId: Ayse);

        var alan = await servis.GetCalismaAlanimAsync();

        Assert.True(alan.Kisitli);
        Assert.Equal(2, alan.Alanlar.Count);
        Assert.Contains(alan.Alanlar, a => a.Kaynak == "rol");
        Assert.Contains(alan.Alanlar, a => a.Kaynak == "doğrudan");
    }

    // ------------------------------------------------------------------
    //  Çizim uçlarıyla bütünleşme
    // ------------------------------------------------------------------

    [Fact]
    public async Task GeometryService_AlanDisiKaydiReddeder()
    {
        // Kontrolün gerçekten çizim yolunda olduğunu kanıtlar: servis
        // coğrafi yetkiye sormasaydı bu kayıt sorunsuz eklenirdi.
        var geo = new FakeGeoPermissionService
        {
            IzinliAlan = WktConverter.Read<Polygon>(BuyukKare),
        };
        var repo = new FakeGeometryRepository<PointEntity>();
        var servis = new GeometryService<PointEntity, Point>(
            repo, new FakeCurrentUserService(Ayse), geo);

        await servis.CreateAsync(new GeometryCreateDto
        {
            Name = "İçeride",
            Wkt = "POINT (5 5)",
        });

        await Assert.ThrowsAsync<IsKuraliException>(() => servis.CreateAsync(new GeometryCreateDto
        {
            Name = "Dışarıda",
            Wkt = "POINT (50 50)",
        }));

        Assert.Single(await servis.GetAllAsync());   // yalnızca içerideki kaydedildi
        Assert.Equal(2, geo.DogrulamaSayisi);        // her iki denemede de kontrol çalıştı
    }

    [Fact]
    public async Task GeometryService_GuncellemeyleAlanDisinaTasinamaz()
    {
        // Yalnızca eklemeyi kontrol etseydik, kullanıcı alan içine çizip
        // sonra kaydı sürükleyerek dışarı taşıyabilirdi.
        var geo = new FakeGeoPermissionService
        {
            IzinliAlan = WktConverter.Read<Polygon>(BuyukKare),
        };
        var repo = new FakeGeometryRepository<PointEntity>();
        var servis = new GeometryService<PointEntity, Point>(
            repo, new FakeCurrentUserService(Ayse), geo);

        var kayit = await servis.CreateAsync(new GeometryCreateDto { Name = "İçeride", Wkt = "POINT (5 5)" });

        await Assert.ThrowsAsync<IsKuraliException>(() => servis.UpdateAsync(kayit.Id, new GeometryUpdateDto
        {
            Name = "Kaçırılmaya çalışıldı",
            Wkt = "POINT (50 50)",
        }));

        var guncel = await servis.GetByIdAsync(kayit.Id);
        Assert.Contains("5 5", guncel!.Wkt);   // kayıt yerinde kaldı
    }
}
