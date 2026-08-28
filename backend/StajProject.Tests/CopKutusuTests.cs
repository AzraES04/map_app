using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using StajProject.Business.Auth;
using StajProject.Business.Services;
using StajProject.Business.Validation;
using StajProject.DataAccess.Context;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// ÇÖP KUTUSU — silinen kayıtların geri getirilmesi.
///
/// ---- BURADAKİ ASIL SORU ----
///
/// "Geri alma çalışıyor mu?" değil — o zaten elle bir kez denendiğinde
/// görülür. Asıl soru şu: <b>çöp kutusu, yetki sisteminde bir arka kapı
/// açıyor mu?</b>
///
/// Tehlike somut: silme uçları yetki istiyor ama geri alma tek bir ekrandan
/// yapılıyor. Yetki kuralı orada gevşek olsaydı, bir noktayı silemeyen
/// kullanıcı onu geri alabilir — yani yetki sistemi kendi içinde tutarsız
/// hâle gelirdi.
///
/// İkinci tehlike: sorgu filtreleri. Bütün depolar silinmiş kaydı GÖRMÜYOR;
/// çöp kutusu bunu bilerek aşıyor. O aşımın bu dosyanın dışına SIZMAMASI
/// gerekiyor — sızsaydı silinmiş kayıtlar normal listelerde belirirdi.
/// </summary>
public class CopKutusuTests
{
    private const int Kullanici = 1;

    private static AppDbContext YeniContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed record Ortam(AppDbContext Db, CopKutusuService Servis, SahteVeritabani Sahte);

    /// <summary>
    /// PermissionService gerçek EF context'iyle çalışmıyor (sahte depoya
    /// bağlı), o yüzden yetkiler SahteVeritabani üzerinden veriliyor;
    /// kayıtlar ise gerçek DbContext'te. İkisi ayrı sorulara bakıyor:
    /// biri "yetkin var mı", diğeri "kayıt silinmiş mi".
    /// </summary>
    private static Ortam OrtamKur(params string[] yetkiler)
    {
        var db = YeniContext();
        var sahte = new SahteVeritabani();

        sahte.Kullanicilar.Add(new User { Id = Kullanici, Username = "azra" });
        foreach (var ad in yetkiler)
        {
            var yetki = sahte.Yetkiler.FirstOrDefault(y => y.Name == ad) ?? sahte.YetkiEkle(ad);
            sahte.KullaniciYetkileri.Add(new UserPermission
            {
                UserId = Kullanici,
                PermissionId = yetki.Id,
            });
        }

        var oturum = new FakeCurrentUserService(Kullanici);
        var izinler = new PermissionService(
            new FakePermissionRepository(sahte), new FakeUserRepository(sahte), oturum);

        return new Ortam(db, new CopKutusuService(
            new CopKutusuRepository(db), izinler, oturum), sahte);
    }

    // ---------------------------------------------------------------- veri

    private static async Task<PointEntity> SilinmisNoktaAsync(AppDbContext db, string ad = "Silinen nokta")
    {
        var nokta = new PointEntity
        {
            Name = ad,
            Description = "açıklama",
            Geom = new Point(32.85, 39.92) { SRID = 4326 },
            IsDeleted = true,
            IsActive = false,
            ModifiedDate = DateTime.UtcNow,
        };
        db.Points.Add(nokta);
        await db.SaveChangesAsync();
        return nokta;
    }

    private static async Task<Guzergah> SilinmisGuzergahAsync(AppDbContext db, string ad = "Silinen hat")
    {
        var hat = new Guzergah
        {
            Ad = ad,
            Renk = "#d64550",
            IsDeleted = true,
            IsActive = false,
            ModifiedDate = DateTime.UtcNow,
        };
        db.Guzergahlar.Add(hat);
        await db.SaveChangesAsync();
        return hat;
    }

    // ==================================================================
    //  1) Listeleme
    // ==================================================================

    [Fact]
    public async Task SILINMEMISKayitlarListedeYOK()
    {
        var o = OrtamKur();

        o.Db.Points.Add(new PointEntity
        {
            Name = "Duran nokta",
            Geom = new Point(1, 1) { SRID = 4326 },
        });
        await o.Db.SaveChangesAsync();

        // Çöp kutusu YALNIZCA silinmişleri gösteriyor. Göstermeseydi liste
        // bütün veritabanı olurdu ve "geri al" düğmesi anlamsızlaşırdı.
        Assert.Empty((await o.Servis.GetirAsync()).Ogeler);
    }

    [Fact]
    public async Task SilinmisKayitListedeVAR()
    {
        var o = OrtamKur();
        await SilinmisNoktaAsync(o.Db, "Kayıp nokta");

        var oge = Assert.Single((await o.Servis.GetirAsync()).Ogeler);

        Assert.Equal("nokta", oge.Tur);
        Assert.Equal("Nokta", oge.TurAdi);
        Assert.Equal("Kayıp nokta", oge.Ad);
        Assert.NotNull(oge.SilinmeZamani);
    }

    [Fact]
    public async Task FARKLITURLERBirlikteListeleniyor()
    {
        var o = OrtamKur();
        await SilinmisNoktaAsync(o.Db);
        await SilinmisGuzergahAsync(o.Db);

        var cop = await o.Servis.GetirAsync();

        // Kullanıcının sorusu tek: "ne sildim?". Yedi ayrı liste yerine tek
        // liste; tür bir kolon.
        Assert.Equal(2, cop.Ogeler.Count);
        Assert.Contains(cop.Ogeler, x => x.Tur == "nokta");
        Assert.Contains(cop.Ogeler, x => x.Tur == "guzergah");
    }

    [Fact]
    public async Task ENYENISilinenENUSTTE()
    {
        var o = OrtamKur();

        var eski = await SilinmisNoktaAsync(o.Db, "Eski");
        eski.ModifiedDate = DateTime.UtcNow.AddDays(-3);
        await o.Db.SaveChangesAsync();

        await SilinmisGuzergahAsync(o.Db, "Yeni");

        // Kullanıcı çoğu zaman AZ ÖNCE sildiğini arıyor.
        var ogeler = (await o.Servis.GetirAsync()).Ogeler;
        Assert.Equal("Yeni", ogeler[0].Ad);
        Assert.Equal("Eski", ogeler[1].Ad);
    }

    [Fact]
    public async Task ISIMSIZKayitBOSSatirBirakmiyor()
    {
        var o = OrtamKur();
        await SilinmisNoktaAsync(o.Db, "   ");

        // Boş bir ad, listede tıklanamaz bir boşluk gibi durur.
        Assert.Equal("(isimsiz)", (await o.Servis.GetirAsync()).Ogeler[0].Ad);
    }

    [Fact]
    public async Task Ozet_TurBasinaSayiyor()
    {
        var o = OrtamKur();
        await SilinmisNoktaAsync(o.Db, "A");
        await SilinmisNoktaAsync(o.Db, "B");
        await SilinmisGuzergahAsync(o.Db);

        var cop = await o.Servis.GetirAsync();

        Assert.Equal(2, cop.Ozet.Single(x => x.Tur == "nokta").Adet);
        Assert.Equal(1, cop.Ozet.Single(x => x.Tur == "guzergah").Adet);

        // Özet LİSTEDEN türetiliyor; ayrı bir sayım sorgusu olsaydı "özet 3
        // diyor ama listede 2 var" gibi bir tutarsızlık mümkün olurdu.
        Assert.Equal(cop.Ogeler.Count, cop.Ozet.Sum(x => x.Adet));
    }

    // ==================================================================
    //  2) Geri alma
    // ==================================================================

    [Fact]
    public async Task GeriAl_KaydiGERIGETIRIYOR()
    {
        var o = OrtamKur(Yetkiler.KayitSilme);
        var nokta = await SilinmisNoktaAsync(o.Db);

        Assert.True(await o.Servis.GeriAlAsync("nokta", nokta.Id));

        var geri = await o.Db.Points.IgnoreQueryFilters().SingleAsync(x => x.Id == nokta.Id);
        Assert.False(geri.IsDeleted);

        // is_active DA açılıyor: silme ikisini birden kapatıyordu. Yalnızca
        // is_deleted'ı geri alsaydık kayıt "geri geldi ama hâlâ pasif" gibi
        // yarım bir durumda kalır, kullanıcı geri almanın işe yaramadığını
        // sanırdı.
        Assert.True(geri.IsActive);
    }

    [Fact]
    public async Task GeriAlinanKayitListedenCIKIYOR()
    {
        var o = OrtamKur(Yetkiler.KayitSilme);
        var nokta = await SilinmisNoktaAsync(o.Db);

        await o.Servis.GeriAlAsync("nokta", nokta.Id);

        Assert.Empty((await o.Servis.GetirAsync()).Ogeler);
    }

    [Fact]
    public async Task GeriAl_OLMAYANKayit_FalseDonuyor()
    {
        var o = OrtamKur(Yetkiler.KayitSilme);
        Assert.False(await o.Servis.GeriAlAsync("nokta", 9999));
    }

    [Fact]
    public async Task GeriAl_ZATENSilinmemisKayit_FalseDonuyor()
    {
        var o = OrtamKur(Yetkiler.KayitSilme);

        o.Db.Points.Add(new PointEntity { Name = "Duran", Geom = new Point(1, 1) { SRID = 4326 } });
        await o.Db.SaveChangesAsync();
        var id = (await o.Db.Points.SingleAsync()).Id;

        // İki kez geri almak zararsız olmalı ama "başarılı" da dememeli:
        // arayüz "geri alındı" derse kullanıcı yapmadığı bir işi yaptı sanır.
        Assert.False(await o.Servis.GeriAlAsync("nokta", id));
    }

    [Fact]
    public async Task GeriAl_BILINMEYENTur_Reddediliyor()
    {
        var o = OrtamKur(Yetkiler.KayitSilme);

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.GeriAlAsync("uydurma", 1));

        Assert.Contains("Bilinmeyen kayıt türü", hata.Message);
    }

    // ==================================================================
    //  3) YETKİ — çöp kutusu bir arka kapı mı?
    // ==================================================================

    [Fact]
    public async Task YetkiSIZKullanici_GeriALAMAZ()
    {
        var o = OrtamKur();   // hiç yetki yok
        var nokta = await SilinmisNoktaAsync(o.Db);

        // ASIL İDDİA: silme yetkisi olmayan biri, silinmiş bir kaydı geri
        // getiremiyor. Getirebilseydi çöp kutusu, yetki sisteminde açılmış
        // bir arka kapı olurdu.
        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.GeriAlAsync("nokta", nokta.Id));

        Assert.Contains(Yetkiler.KayitSilme, hata.Message);

        // Ve kayıt gerçekten SİLİNMİŞ kalmalı.
        Assert.True((await o.Db.Points.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == nokta.Id)).IsDeleted);
    }

    [Fact]
    public async Task BASKATURUNYetkisiIseYaramiyor()
    {
        // Güzergah yetkisi var ama NOKTA geri alınmaya çalışılıyor.
        var o = OrtamKur(Yetkiler.GuzergahYonetimi);
        var nokta = await SilinmisNoktaAsync(o.Db);

        // Tek bir "çöp kutusu yetkisi" olsaydı bu geçerdi. Tür başına yetki,
        // silme kurallarını birebir yansıtıyor.
        await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.GeriAlAsync("nokta", nokta.Id));
    }

    [Fact]
    public async Task DOGRUYetkiIseCalisiyor()
    {
        var o = OrtamKur(Yetkiler.GuzergahYonetimi);
        var hat = await SilinmisGuzergahAsync(o.Db);

        // Kısıtlamanın aşırıya kaçmadığının kontrolü: yalnızca "reddediyor"
        // diye test etseydik, her şeyi reddeden bir kod da yeşil kalırdı.
        Assert.True(await o.Servis.GeriAlAsync("guzergah", hat.Id));
    }

    [Fact]
    public async Task Liste_YETKISIZKullaniciyaDaGeliyor_AmaDugmeKapali()
    {
        var o = OrtamKur();   // hiç yetki yok
        await SilinmisNoktaAsync(o.Db);

        var oge = Assert.Single((await o.Servis.GetirAsync()).Ogeler);

        // Liste görünüyor: silinmiş bir kaydın adı, silinmeden önce zaten
        // herkesin görebildiği bir bilgiydi.
        //
        // Ama geriAlinabilir FALSE: arayüz düğmeyi göstermiyor. Yetkiyi
        // istemcide hesaplatsaydık, yetki kurallarını ikinci kez (ve er geç
        // yanlış) uygulamış olurduk.
        Assert.False(oge.GeriAlinabilir);
    }

    [Fact]
    public async Task Liste_YetkiliKullanicidaDugmeAcik()
    {
        var o = OrtamKur(Yetkiler.KayitSilme);
        await SilinmisNoktaAsync(o.Db);

        Assert.True((await o.Servis.GetirAsync()).Ogeler[0].GeriAlinabilir);
    }

    [Fact]
    public async Task GeriAlinabilir_TURBASINAAyriHesaplaniyor()
    {
        var o = OrtamKur(Yetkiler.KayitSilme);   // yalnızca geometri yetkisi
        await SilinmisNoktaAsync(o.Db);
        await SilinmisGuzergahAsync(o.Db);

        var ogeler = (await o.Servis.GetirAsync()).Ogeler;

        Assert.True(ogeler.Single(x => x.Tur == "nokta").GeriAlinabilir);
        Assert.False(ogeler.Single(x => x.Tur == "guzergah").GeriAlinabilir);
    }

    // ==================================================================
    //  4) Sorgu filtresi sızıntısı
    // ==================================================================

    [Fact]
    public async Task SilinmisKayit_NORMALSorgudaGORUNMUYOR()
    {
        var o = OrtamKur(Yetkiler.KayitSilme);
        await SilinmisNoktaAsync(o.Db);

        // Çöp kutusu IgnoreQueryFilters kullanıyor. O aşımın kendi dosyasının
        // DIŞINA sızmadığının kontrolü: normal sorgu hâlâ silinmişleri
        // görmemeli. Sızsaydı silinmiş kayıtlar haritada ve listelerde
        // belirirdi.
        Assert.Empty(await o.Db.Points.ToListAsync());
        Assert.Single(await o.Db.Points.IgnoreQueryFilters().ToListAsync());
    }
}
