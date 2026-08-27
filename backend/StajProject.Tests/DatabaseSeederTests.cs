using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using StajProject.Business.Auth;
using StajProject.Business.Geo;
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
        => Kur(db, new FakeIlRepository());

    /// <summary>
    /// İl deposunu DIŞARIDAN alan aşırı yükleme (Ödev 14).
    ///
    /// Analiz veri seti il sınırlarından üretiliyor; "üretilen POI gerçekten
    /// ilin içinde mi?" sorusunu sınayan test, seed'in doldurduğu il listesini
    /// görebilmeli. Depoyu içeride kurup dışarı vermeseydik testin
    /// karşılaştıracağı bir sınır olmazdı.
    /// </summary>
    private static (DatabaseSeeder seeder,
                    FakeUserRepository kullanicilar,
                    FakeGeometryRepository<PointEntity> noktalar,
                    FakeGeometryRepository<LineEntity> cizgiler,
                    FakeGeometryRepository<PolygonEntity> poligonlar) Kur(
        SahteVeritabani db, FakeIlRepository iller)
    {
        var kullanicilar = new FakeUserRepository(db);
        var noktalar = new FakeGeometryRepository<PointEntity>();
        var cizgiler = new FakeGeometryRepository<LineEntity>();
        var poligonlar = new FakeGeometryRepository<PolygonEntity>();

        var seeder = new DatabaseSeeder(
            kullanicilar,
            new FakeRoleRepository(db),
            new FakePermissionRepository(db),
            new FakeGeoPermissionRepository(db),   // Ödev 7: çalışma alanı tanımları
            iller,                                 // Ödev 10: il sınırları
            new FakePoiRepository(db),             // Ödev 12: POI'ler
            new FakePoiCategoryRepository(db),     // Ödev 12: kategori sözlüğü
            noktalar, cizgiler, poligonlar,
            // Ödev 13: seed sonunda POI stilleri GeoServer'a yazılıyor.
            // Sahte istemciyle veriyoruz — testin GeoServer'a ihtiyacı olmasın
            // ama seed'in o adımı da gerçekten koşsun.
            new PoiStyleService(
                new FakePoiCategoryRepository(db),
                new FakeGeoServerClient(),
                NullLogger<PoiStyleService>.Instance));

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

    // ---------- Ödev 12: rol adları, POI kategorileri ve örnek POI'ler ----------

    [Fact]
    public async Task BosVeritabani_UcTemelRolAdiylaOlusur()
    {
        // Ödev 12 / Madde 1: Admin, Operatör, Kullanıcı.
        var db = new SahteVeritabani();
        var (seeder, _, _, _, _) = Kur(db);

        await seeder.SeedAsync();

        Assert.Contains(db.Roller, r => r.Name == "Admin");
        Assert.Contains(db.Roller, r => r.Name == "Operatör");
        Assert.Contains(db.Roller, r => r.Name == "Kullanıcı");
    }

    [Fact]
    public async Task MevcutRoller_YenidenAdlandirilirVeAtamalarKorunur()
    {
        // Eski adlarla kurulmuş bir veritabanı: yeni rol AÇILMAMALI, var olanın
        // ADI değişmeli. Yeni rol açılsaydı kullanıcılar eski (artık boş) rolde
        // kalır, yetkilerini bir sabah kaybederlerdi.
        var db = new SahteVeritabani();
        var yonetici = db.RolEkle("Yönetici");
        var editor = db.RolEkle("Editör");
        db.RolEkle("Görüntüleyici");

        var kullanici = new User { Id = db.SonrakiKullaniciId(), Username = "eski" };
        db.Kullanicilar.Add(kullanici);
        db.KullaniciRolleri.Add(new UserRole { UserId = kullanici.Id, RoleId = editor.Id });

        var (seeder, _, _, _, _) = Kur(db);
        await seeder.SeedAsync();

        Assert.DoesNotContain(db.Roller, r => r.Name == "Yönetici");
        Assert.DoesNotContain(db.Roller, r => r.Name == "Editör");

        // Aynı SATIR, yeni ad: id değişmediği için atamalar da yerinde.
        Assert.Equal("Admin", db.Roller.Single(r => r.Id == yonetici.Id).Name);
        Assert.Equal("Operatör", db.Roller.Single(r => r.Id == editor.Id).Name);
        Assert.Contains(db.KullaniciRolleri, kr => kr.UserId == kullanici.Id && kr.RoleId == editor.Id);
    }

    [Fact]
    public async Task MevcutOperatorRolu_PoiEklemeYetkisiniSonradanAlir()
    {
        // Bu testin var oluş sebebi gerçek bir hata: rol zaten var olduğu için
        // seed yetkilerine dokunmuyordu ve POI modülü, çalışan bir kurulumda
        // hiç kimsenin erişemediği bir özellik olarak kalıyordu.
        var db = new SahteVeritabani();
        var editor = db.RolEkle("Editör");

        var (seeder, _, _, _, _) = Kur(db);
        await seeder.SeedAsync();

        var poiEkleme = db.Yetkiler.Single(y => y.Name == Yetkiler.PoiEkleme);
        Assert.Contains(db.RolYetkileri, ry => ry.RoleId == editor.Id && ry.PermissionId == poiEkleme.Id);

        // Kategori ağacı operatörün işi DEĞİL: "POI Yönetimi" Admin'de kalmalı.
        var poiYonetimi = db.Yetkiler.Single(y => y.Name == Yetkiler.PoiYonetimi);
        Assert.DoesNotContain(db.RolYetkileri, ry => ry.RoleId == editor.Id && ry.PermissionId == poiYonetimi.Id);
    }

    [Fact]
    public async Task BosVeritabani_HiyerarsikKategorilerVeOrnekPoilerYuklenir()
    {
        var db = new SahteVeritabani();
        var (seeder, kullanicilar, _, _, _) = Kur(db);

        await seeder.SeedAsync();

        // Ödev metnindeki örnek: Yeme-İçme → Restoran, Kafe
        var yeme = db.PoiKategorileri.Single(k => k.Ad == "Yeme-İçme");
        Assert.Null(yeme.ParentId);
        Assert.Contains(db.PoiKategorileri, k => k.Ad == "Restoran" && k.ParentId == yeme.Id);
        Assert.Contains(db.PoiKategorileri, k => k.Ad == "Kafe" && k.ParentId == yeme.Id);

        // ÖRNEK POI'ler operatör kullanıcıya bağlı: admin panelindeki "ekleyen"
        // sütunu ilk açılışta anlamlı olsun diye.
        //
        // Ödev 14'ten sonra tabloda ikinci bir küme daha var: analiz veri seti.
        // O küme SAHİPSİZ (user_id = null) çünkü kimsenin girdiği bir kayıt
        // değil, üretilmiş referans envanteri. İki kümeyi burada sahipliğine
        // göre ayırıyoruz — ayrım zaten tasarımın kendisi.
        var ayse = (await kullanicilar.GetByUsernameAsync("ayse"))!;
        var ornekPoiler = db.Poiler.Where(p => p.UserId is not null).ToList();

        Assert.NotEmpty(ornekPoiler);
        Assert.All(ornekPoiler, poi =>
        {
            Assert.Equal(ayse.Id, poi.UserId);
            Assert.NotEqual(0, poi.KategoriId);
            Assert.Equal(4326, poi.Geom.SRID);
        });
    }

    [Fact]
    public async Task OrnekPoiler_OperatorunCografiYetkiAlaniIcindedir()
    {
        // Seed verisi kendi kuralıyla tutarlı olmalı: POI ekleme de coğrafi
        // yetki kontrolünden geçiyor, alan dışında bir örnek koysaydık
        // "seed'in ürettiği kayıt elle girilemiyor" gibi bir çelişki olurdu.
        var db = new SahteVeritabani();
        var (seeder, _, _, _, _) = Kur(db);

        await seeder.SeedAsync();

        var rolAlani = db.CografiYetkiler.Single(g => g.RoleId is not null).Geom;

        // Yalnızca operatöre AİT örnekler: analiz veri seti (Ödev 14) sahipsiz
        // ve ülke geneline yayılıyor, bir kullanıcının çalışma alanına
        // sığması beklenmiyor — o küme kimsenin adına girilmiş sayılmadığı
        // için coğrafi yetki kuralının konusu da değil.
        var ornekPoiler = db.Poiler.Where(p => p.UserId is not null).ToList();

        Assert.NotEmpty(ornekPoiler);
        Assert.All(ornekPoiler, poi => Assert.True(
            rolAlani.Covers(poi.Geom),
            $"{poi.Isim} çalışma alanının dışında: {poi.Geom.AsText()}"));
    }

    // ---------- Ödev 14: analiz veri seti ----------

    /// <summary>
    /// Üretilen POI'ler ait oldukları ilin SINIRI İÇİNDE olmalı.
    ///
    /// Bu, üreticinin en kolay bozulacak yeri: odak çevresine Gauss dağılımıyla
    /// saçılan bir nokta sınırın dışına taşabiliyor ve kabul-ret döngüsü
    /// olmasaydı POI'ler denize düşerdi. Ekranda "bir kısmı Ege'de yüzüyor"
    /// diye fark edilene kadar sessiz kalırdı.
    /// </summary>
    [Fact]
    public async Task AnalizVeriSeti_IllerinIcindeVeSahipsizUretilir()
    {
        var db = new SahteVeritabani();
        var iller = new FakeIlRepository();
        var (seeder, _, _, _, _) = Kur(db, iller);

        await seeder.SeedAsync();

        var uretilenler = db.Poiler.Where(p => p.UserId is null).ToList();

        // Ödev metni "ne kadar çok veri olursa o kadar iyi" diyor; eşiğin
        // üstünde olması hem analizin anlamlı çalışacağını hem de üretimin
        // ikinci açılışta tekrar etmeyeceğini garanti ediyor.
        Assert.True(uretilenler.Count > 2000,
            $"Analiz için üretilen POI sayısı beklenenden az: {uretilenler.Count}");

        // Türkiye'nin kabaca sınırladığı kutu. Tek tek il sınırlarıyla
        // karşılaştırmak daha kesin olurdu ama bu test asıl olarak "nokta
        // sınırın dışına kaçtı mı?" sorusunu cevaplıyor ve kaçan nokta
        // (üreticide bir hata olsa) ülke kutusunun da dışına düşerdi.
        Assert.All(uretilenler, poi =>
        {
            Assert.Equal(4326, poi.Geom.SRID);
            Assert.InRange(poi.Geom.X, 25.0, 45.5);
            Assert.InRange(poi.Geom.Y, 35.5, 42.5);
        });

        // Her POI gerçekten bir ilin içinde mi? İlleri tek tek gezmek yerine
        // hepsinin birleşimini alıp tek kontrol yapıyoruz.
        // Hazırlanmış (prepared) geometri: binlerce noktayı on binlerce köşeli
        // bir birleşimle karşılaştırıyoruz, kenarların bir kez indekslenmesi
        // testin süresini saniyelerden milisaniyelere indiriyor.
        var turkiye = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(
            NetTopologySuite.Operation.Union.UnaryUnionOp.Union(
                (await iller.SinirlariGetirAsync()).Select(i => i.Geom).ToList()));

        Assert.All(uretilenler, poi => Assert.True(
            turkiye.Intersects(poi.Geom),
            $"{poi.Isim} il sınırlarının dışında: {poi.Geom.AsText()}"));
    }

    /// <summary>
    /// Üretim TEKRARLANABİLİR olmalı: aynı tohum, aynı veri. Jüri sunumunda
    /// gösterilen ısı haritasının başka bir makinede farklı çıkmaması buna
    /// bağlı.
    /// </summary>
    [Fact]
    public async Task AnalizVeriSeti_HerKurulumdaAyniUretilir()
    {
        var birinci = new SahteVeritabani();
        var ikinci = new SahteVeritabani();

        await Kur(birinci).seeder.SeedAsync();
        await Kur(ikinci).seeder.SeedAsync();

        var a = birinci.Poiler.Where(p => p.UserId is null).Select(p => (p.Isim, p.Geom.X, p.Geom.Y)).ToList();
        var b = ikinci.Poiler.Where(p => p.UserId is null).Select(p => (p.Isim, p.Geom.X, p.Geom.Y)).ToList();

        Assert.Equal(a, b);
    }

    [Fact]
    public async Task IkinciCalistirma_PoiVeKategorileriCogaltmaz()
    {
        var db = new SahteVeritabani();
        var (seeder, _, _, _, _) = Kur(db);

        await seeder.SeedAsync();
        var ilk = (db.PoiKategorileri.Count, db.Poiler.Count);

        await seeder.SeedAsync();   // uygulama yeniden başlatıldı

        Assert.Equal(ilk, (db.PoiKategorileri.Count, db.Poiler.Count));
    }

    // ---------- Ödev 7: coğrafi yetki tohumlaması ----------

    [Fact]
    public async Task BosVeritabani_IkiCografiYetkiTanimiYuklenir()
    {
        // Bu kural VERİ olmadan gösterilemez: tanım yoksa "kısıt yok" demektir.
        // Seed'e konmasının sebebi bu — yeni kurulan makinede Ödev 7 sessizce
        // görünmez hâle geliyordu.
        var db = new SahteVeritabani();
        var (seeder, _, _, _, _) = Kur(db);

        await seeder.SeedAsync();

        Assert.Equal(2, db.CografiYetkiler.Count);
    }

    [Fact]
    public async Task CografiYetki_BiriKullaniciyaBiriRoleTanimlanir()
    {
        // geo_permissions tablosunda "YA kullanıcı YA rol" CHECK kısıtı var.
        // İkisini birden tohumlamak hem kısıtın iki dalını örnekliyor hem de
        // çalışma alanının BİRLEŞİM olduğunu gösterilebilir kılıyor.
        var db = new SahteVeritabani();
        var (seeder, kullanicilar, _, _, _) = Kur(db);

        await seeder.SeedAsync();

        var ayse = (await kullanicilar.GetByUsernameAsync("ayse"))!;
        // Ödev 12: rol adı "Editör" iken "Operatör" oldu; alan ROLÜN ID'sine
        // bağlı olduğu için yeniden adlandırma tanımı bozmuyor.
        var operatorRolu = db.Roller.Single(r => r.Name == "Operatör");

        var kullaniciAlani = Assert.Single(db.CografiYetkiler, g => g.UserId is not null);
        var rolAlani = Assert.Single(db.CografiYetkiler, g => g.RoleId is not null);

        Assert.Equal(ayse.Id, kullaniciAlani.UserId);
        Assert.Null(kullaniciAlani.RoleId);

        Assert.Equal(operatorRolu.Id, rolAlani.RoleId);
        Assert.Null(rolAlani.UserId);
    }

    [Fact]
    public async Task CografiYetki_KullaniciAlaniOnunKendiVerisiniKapsar()
    {
        // Alan rastgele bir kutu değil: ikinci kullanıcının örnek verisini
        // kapsayacak şekilde seçildi. Kapsamasaydı kullanıcı kendi kayıtlarını
        // güncelleyemezdi — güncelleme de alan kontrolünden geçiyor.
        var db = new SahteVeritabani();
        var (seeder, kullanicilar, noktalar, _, _) = Kur(db);

        await seeder.SeedAsync();

        var ayse = (await kullanicilar.GetByUsernameAsync("ayse"))!;
        var alan = db.CografiYetkiler.Single(g => g.UserId == ayse.Id).Geom;

        var ayseninNoktalari = await noktalar.GetAllAsync(ayse.Id);
        Assert.NotEmpty(ayseninNoktalari);
        Assert.All(ayseninNoktalari, n => Assert.True(
            alan.Covers(n.Geom),
            $"{n.Name} izinli alanın dışında kalıyor: {n.Geom.AsText()}"));
    }

    [Fact]
    public async Task MevcutCografiYetkiVarsa_SeedDokunmaz()
    {
        // Panelden tanımlanmış bir kuralı her açılışta yeniden üretmek ya da
        // ezmek kabul edilemez — kullanıcının işi geri alınmış olurdu.
        var db = new SahteVeritabani();
        db.CografiYetkiler.Add(new GeoPermission
        {
            Id = db.SonrakiCografiYetkiId(),
            Name = "Elle tanımlanmış alan",
            UserId = 1,
            Geom = WktConverter.Read<Polygon>(
                "POLYGON ((30 39, 31 39, 31 40, 30 40, 30 39))"),
        });

        var (seeder, _, _, _, _) = Kur(db);
        await seeder.SeedAsync();

        var kalan = Assert.Single(db.CografiYetkiler);
        Assert.Equal("Elle tanımlanmış alan", kalan.Name);
    }
}
