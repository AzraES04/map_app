using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Services;
using StajProject.Business.Validation;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 12 testleri: POI ve HİYERARŞİK KATEGORİ.
///
/// İki ayrı kural kümesi sınanıyor:
///   1. Kategori ağacı — ata seçimi, döngü engeli, derinlik, silme koşulları
///   2. POI — kategori zorunluluğu, coğrafi yetki, sahiplik ve ortak görünürlük
///
/// Koordinatlar sadelik için 0–10 aralığında; hangi noktanın nerede olduğunu
/// okurken hesap yapmayı gerektirmiyor (CografiYetkiTests ile aynı yaklaşım).
/// </summary>
public class PoiTests
{
    private const int Ayse = 1;      // Operatör: POI Ekleme yetkisi var
    private const int Admin = 2;     // POI Yönetimi yetkisi var

    private sealed record Ortam(
        SahteVeritabani Db,
        PoiService Poi,
        PoiCategoryService Kategori,
        FakeGeoPermissionService Geo);

    /// <summary>
    /// İki kullanıcılı bir dünya kurar: "ayse" (POI Ekleme) ve "admin"
    /// (POI Yönetimi). Yetkiler DOĞRUDAN veriliyor; rol üzerinden vermek de
    /// aynı sonucu üretirdi (PermissionService ikisini birleştiriyor) ama
    /// testin kurulumunu gereksiz uzatırdı.
    /// </summary>
    private static Ortam Kur(int? girisYapan = Ayse)
    {
        var db = new SahteVeritabani();

        db.Kullanicilar.Add(new User { Id = db.SonrakiKullaniciId(), Username = "ayse" });
        db.Kullanicilar.Add(new User { Id = db.SonrakiKullaniciId(), Username = "admin" });

        YetkiVer(db, Ayse, Yetkiler.PoiEkleme);
        YetkiVer(db, Admin, Yetkiler.PoiYonetimi);

        var oturum = new FakeCurrentUserService(girisYapan);
        var poiRepo = new FakePoiRepository(db);
        var kategoriRepo = new FakePoiCategoryRepository(db);

        // Gerçek PermissionService kullanılıyor: "POI Yönetimi varsa her kayda
        // dokunabilir" kuralının doğruluğu yetki birleştirmesine bağlı, onu
        // sahte bir sınıfla atlarsak test asıl riski hiç sınamamış olur.
        var izinler = new PermissionService(
            new FakePermissionRepository(db), new FakeUserRepository(db), oturum);

        var geo = new FakeGeoPermissionService();

        return new Ortam(
            db,
            new PoiService(poiRepo, kategoriRepo, oturum, izinler, geo),
            new PoiCategoryService(kategoriRepo, poiRepo),
            geo);
    }

    private static void YetkiVer(SahteVeritabani db, int kullaniciId, params string[] adlar)
    {
        foreach (var ad in adlar)
        {
            var yetki = db.Yetkiler.FirstOrDefault(y => y.Name == ad) ?? db.YetkiEkle(ad);
            db.KullaniciYetkileri.Add(new UserPermission
            {
                UserId = kullaniciId,
                PermissionId = yetki.Id,
            });
        }
    }

    private static PoiCreateDto YeniPoi(int kategoriId, string isim = "Test POI",
                                        string wkt = "POINT (5 5)", string? mesai = "09:00 - 18:00")
        => new() { Isim = isim, KategoriId = kategoriId, Wkt = wkt, MesaiSaatleri = mesai };

    // ==================================================================
    //  KATEGORİ — hiyerarşi
    // ==================================================================

    [Fact]
    public async Task Kategori_AltKategoriyle_AgacOlarakDoner()
    {
        var o = Kur();

        var kok = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Yeme-İçme" });
        await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran", ParentId = kok.Id });
        await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Kafe", ParentId = kok.Id });

        var agac = await o.Kategori.GetTreeAsync();

        // Ödev metnindeki örnek: tek kök, altında iki yaprak.
        var tekKok = Assert.Single(agac);
        Assert.Equal("Yeme-İçme", tekKok.Ad);
        Assert.Equal(2, tekKok.Cocuklar.Count);
        Assert.Equal(0, tekKok.Seviye);

        var restoran = tekKok.Cocuklar.Single(c => c.Ad == "Restoran");
        Assert.Equal(1, restoran.Seviye);
        Assert.Equal($"Yeme-İçme{PoiCategoryService.YolAyraci}Restoran", restoran.TamYol);
    }

    [Fact]
    public async Task Kategori_AyniAtaAltinda_AyniAdIkiKezOlamaz()
    {
        var o = Kur();
        var kok = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Yeme-İçme" });
        await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran", ParentId = kok.Id });

        await Assert.ThrowsAsync<IsKuraliException>(() =>
            o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran", ParentId = kok.Id }));
    }

    [Fact]
    public async Task Kategori_FarkliAtalarAltinda_AyniAdSerbesttir()
    {
        // "Yeme-İçme › Diğer" ile "Konaklama › Diğer" birbirini engellememeli:
        // benzersizlik KARDEŞLER arasında tanımlı, ağacın tamamında değil.
        var o = Kur();
        var yeme = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Yeme-İçme" });
        var konaklama = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Konaklama" });

        await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Diğer", ParentId = yeme.Id });
        var ikinci = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Diğer", ParentId = konaklama.Id });

        Assert.Equal($"Konaklama{PoiCategoryService.YolAyraci}Diğer", ikinci.TamYol);
    }

    [Fact]
    public async Task Kategori_KendisininAtasiOlamaz()
    {
        var o = Kur();
        var kok = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Yeme-İçme" });

        await Assert.ThrowsAsync<IsKuraliException>(() =>
            o.Kategori.UpdateAsync(kok.Id, new PoiKategoriSaveDto { Ad = "Yeme-İçme", ParentId = kok.Id }));
    }

    [Fact]
    public async Task Kategori_KendiTorununAltinaTasinamaz()
    {
        // Döngünün asıl tehlikeli hâli: A → B → A. Yol hesaplayan her döngü
        // sonsuza kadar döner; veritabanının yabancı anahtarı bunu göremez.
        var o = Kur();
        var kok = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Yeme-İçme" });
        var cocuk = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran", ParentId = kok.Id });

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() =>
            o.Kategori.UpdateAsync(kok.Id, new PoiKategoriSaveDto { Ad = "Yeme-İçme", ParentId = cocuk.Id }));

        Assert.Contains("döngü", hata.Message);
    }

    [Fact]
    public async Task Kategori_OlmayanAtayaBaglanamaz()
    {
        var o = Kur();

        await Assert.ThrowsAsync<IsKuraliException>(() =>
            o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran", ParentId = 999 }));
    }

    [Fact]
    public async Task Kategori_DerinlikSiniriAsilamaz()
    {
        var o = Kur();
        var a = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Yeme-İçme" });
        var b = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran", ParentId = a.Id });
        var c = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Kebapçı", ParentId = b.Id });

        // Üç seviye serbest, dördüncü değil.
        Assert.Equal(2, c.Seviye);
        await Assert.ThrowsAsync<IsKuraliException>(() =>
            o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Adana", ParentId = c.Id }));
    }

    [Fact]
    public async Task Kategori_AltKategorisiVarsa_Silinemez()
    {
        var o = Kur();
        var kok = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Yeme-İçme" });
        await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran", ParentId = kok.Id });

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() => o.Kategori.DeleteAsync(kok.Id));
        Assert.Contains("alt kategorileri", hata.Message);
    }

    [Fact]
    public async Task Kategori_BagliPoiVarsa_Silinemez()
    {
        var o = Kur();
        var kategori = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran" });
        await o.Poi.CreateAsync(YeniPoi(kategori.Id));

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() => o.Kategori.DeleteAsync(kategori.Id));
        Assert.Contains("POI var", hata.Message);
    }

    [Fact]
    public async Task Kategori_BosKategori_SoftDeleteEdilir()
    {
        var o = Kur();
        var kategori = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran" });

        Assert.True(await o.Kategori.DeleteAsync(kategori.Id));

        // Fiziksel silme YOK: satır tabloda, sadece bayrak kalktı.
        Assert.Empty(await o.Kategori.GetTreeAsync());
        Assert.True(o.Db.PoiKategorileri.Single().IsDeleted);
    }

    [Fact]
    public async Task Kategori_PasifKokunCocuklari_SecilebilirListedeCikmaz()
    {
        // Pasiflik daldan aşağı akar: kapatılmış bir kökün altından seçim
        // yaptırmak, kapatma işlemini anlamsız kılardı.
        var o = Kur();
        var kok = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Yeme-İçme" });
        await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran", ParentId = kok.Id });

        await o.Kategori.UpdateAsync(kok.Id, new PoiKategoriSaveDto
        {
            Ad = "Yeme-İçme",
            IsActive = false,
        });

        Assert.Empty(await o.Kategori.GetSelectableAsync());

        // Yönetim ekranı ise pasifleri GÖRMELİ; yoksa geri açılamazdı.
        Assert.Equal(2, await SayAsync(o));
    }

    private static async Task<int> SayAsync(Ortam o)
    {
        var agac = await o.Kategori.GetTreeAsync();
        int Say(List<PoiKategoriDto> dugumler)
            => dugumler.Sum(d => 1 + Say(d.Cocuklar));
        return Say(agac);
    }

    // ==================================================================
    //  POI
    // ==================================================================

    [Fact]
    public async Task Poi_Eklenince_EkleyenKullaniciyaBaglanir()
    {
        var o = Kur();
        var kok = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Yeme-İçme" });
        var restoran = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran", ParentId = kok.Id });

        var poi = await o.Poi.CreateAsync(YeniPoi(restoran.Id, "Kebapçı Ali"));

        Assert.Equal(Ayse, poi.UserId);
        Assert.Equal("ayse", poi.KullaniciAdi);
        Assert.Equal("Restoran", poi.KategoriAdi);
        Assert.Equal($"Yeme-İçme{PoiCategoryService.YolAyraci}Restoran", poi.KategoriYolu);
        Assert.Equal("09:00 - 18:00", poi.MesaiSaatleri);
        Assert.True(poi.IsActive);
    }

    [Fact]
    public async Task Poi_OlmayanKategoriyle_Eklenemez()
    {
        var o = Kur();

        await Assert.ThrowsAsync<IsKuraliException>(() => o.Poi.CreateAsync(YeniPoi(999)));
    }

    [Fact]
    public async Task Poi_PasifKategoriye_Eklenemez()
    {
        var o = Kur();
        var kategori = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran", IsActive = false });

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() => o.Poi.CreateAsync(YeniPoi(kategori.Id)));
        Assert.Contains("pasif", hata.Message);
    }

    [Fact]
    public async Task Poi_NoktaDisiGeometri_Reddedilir()
    {
        // POI tanımı gereği bir NOKTA. Poligon gönderildiğinde hata WKT
        // katmanından gelir ve controller onu 400'e çevirir.
        var o = Kur();
        var kategori = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran" });

        await Assert.ThrowsAsync<WktFormatException>(() => o.Poi.CreateAsync(
            YeniPoi(kategori.Id, wkt: "POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))")));
    }

    [Fact]
    public async Task Poi_CografiYetkiAlaniDisina_Eklenemez()
    {
        // Ödev 7 kuralı POI'ye de uygulanıyor — çizim uçlarıyla AYNI servis
        // çağrılıyor, iki yol ayrışmasın diye.
        var o = Kur();
        o.Geo.IzinliAlan = WktConverter.Read<NetTopologySuite.Geometries.Polygon>(
            "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");

        var kategori = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran" });

        await o.Poi.CreateAsync(YeniPoi(kategori.Id, "İçeride", "POINT (5 5)"));

        await Assert.ThrowsAsync<IsKuraliException>(() =>
            o.Poi.CreateAsync(YeniPoi(kategori.Id, "Dışarıda", "POINT (25 25)")));
    }

    [Fact]
    public async Task Poi_Listesi_SahibineGoreSuzulmez()
    {
        // Çizimlerden BİLEREK farklı: POI ortak referans verisidir, giriş
        // yapan herkes hepsini görür. Süzseydik iki operatör aynı bölgeye
        // aynı restoranı ikinci kez girerdi.
        var o = Kur();
        var kategori = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran" });
        await o.Poi.CreateAsync(YeniPoi(kategori.Id, "Ayse'nin POI'si"));

        // Başkasının kaydını doğrudan tabloya koyuyoruz: ikinci bir oturum
        // kurmak testin anlattığı şeye bir şey katmazdı.
        o.Db.Poiler.Add(new Poi
        {
            Id = o.Db.SonrakiPoiId(),
            Isim = "Başkasının POI'si",
            KategoriId = kategori.Id,
            UserId = Admin,
            Geom = WktConverter.Read<NetTopologySuite.Geometries.Point>("POINT (6 6)"),
        });

        var liste = await o.Poi.GetAllAsync();

        Assert.Contains(liste, p => p.Isim == "Ayse'nin POI'si");
        Assert.Contains(liste, p => p.Isim == "Başkasının POI'si");
    }

    [Fact]
    public async Task Poi_BaskasininKaydini_OperatorDuzenleyemez()
    {
        var o = Kur();
        var kategori = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran" });

        o.Db.Poiler.Add(new Poi
        {
            Id = o.Db.SonrakiPoiId(),
            Isim = "Admin'in POI'si",
            KategoriId = kategori.Id,
            UserId = Admin,
            Geom = WktConverter.Read<NetTopologySuite.Geometries.Point>("POINT (6 6)"),
        });

        var hata = await Assert.ThrowsAsync<IsKuraliException>(() => o.Poi.UpdateAsync(1, new PoiUpdateDto
        {
            Isim = "Değiştirildi",
            KategoriId = kategori.Id,
        }));

        Assert.Contains("başka bir kullanıcı", hata.Message);
    }

    [Fact]
    public async Task Poi_YonetimYetkisiOlan_BaskasininKaydiniDuzenleyebilir()
    {
        // "POI Yönetimi" yetkisi sahiplik kuralını aşar: yönetici panelden
        // yanlış girilmiş bir kaydı düzeltebilmeli.
        var o = Kur(Admin);
        var kategori = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran" });

        o.Db.Poiler.Add(new Poi
        {
            Id = o.Db.SonrakiPoiId(),
            Isim = "Ayse'nin POI'si",
            KategoriId = kategori.Id,
            UserId = Ayse,
            Geom = WktConverter.Read<NetTopologySuite.Geometries.Point>("POINT (6 6)"),
        });

        var guncel = await o.Poi.UpdateAsync(1, new PoiUpdateDto
        {
            Isim = "Yönetici düzeltti",
            KategoriId = kategori.Id,
            MesaiSaatleri = "7/24",
        });

        Assert.NotNull(guncel);
        Assert.Equal("Yönetici düzeltti", guncel!.Isim);
        Assert.Equal("7/24", guncel.MesaiSaatleri);
    }

    [Fact]
    public async Task Poi_WktBosGonderilirse_KonumDegismez()
    {
        var o = Kur();
        var kategori = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran" });
        var poi = await o.Poi.CreateAsync(YeniPoi(kategori.Id, wkt: "POINT (3 4)"));

        var guncel = await o.Poi.UpdateAsync(poi.Id, new PoiUpdateDto
        {
            Isim = "Yeni ad",
            KategoriId = kategori.Id,
            Wkt = null,
        });

        Assert.Equal("POINT (3 4)", guncel!.Wkt);
    }

    [Fact]
    public async Task Poi_BosMesai_NullOlarakSaklanir()
    {
        // Boş metin yerine NULL: "değer yok"un veritabanındaki doğru karşılığı.
        var o = Kur();
        var kategori = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran" });

        var poi = await o.Poi.CreateAsync(YeniPoi(kategori.Id, mesai: "   "));

        Assert.Null(poi.MesaiSaatleri);
    }

    [Fact]
    public async Task Poi_Silme_SoftDeleteVeGeriAlinabilir()
    {
        var o = Kur();
        var kategori = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran" });
        var poi = await o.Poi.CreateAsync(YeniPoi(kategori.Id));

        Assert.True(await o.Poi.DeleteAsync(poi.Id));
        Assert.Empty(await o.Poi.GetAllAsync());

        // Satır hâlâ tabloda — geri alınabilmesinin tek sebebi bu.
        Assert.True(o.Db.Poiler.Single().IsDeleted);

        Assert.True(await o.Poi.RestoreAsync(poi.Id));
        Assert.Single(await o.Poi.GetAllAsync());
    }

    [Fact]
    public async Task Poi_PasifeAlinca_ListedeKalir()
    {
        // Pasif ≠ silinmiş. Kapalı bir işletme haritada görünmeye devam
        // etmeli, sadece "Pasif" işaretiyle.
        var o = Kur();
        var kategori = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Restoran" });
        var poi = await o.Poi.CreateAsync(YeniPoi(kategori.Id));

        Assert.True(await o.Poi.SetActiveAsync(poi.Id, false));

        var liste = await o.Poi.GetAllAsync();
        Assert.False(Assert.Single(liste).IsActive);
    }
}
