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
/// Ödev 16 testleri: AKILLI ULAŞIM MODÜLÜ.
///
/// Dört kural kümesi sınanıyor:
///   1. 1-N ilişki — durak güzergahsız olamaz, pasif hatta eklenemez
///   2. SIRA — yeni durak sona gelir, silme boşluk bırakmaz, taşıma çalışır
///   3. Sürükle-bırak sıralaması — eksik/yabancı liste reddedilir
///   4. Yetki — ulaşım rolleri POI ve çizim EKLEYEMEZ (ödev notunun kendisi)
///
/// Dördüncüsü bu ödevin en kolay gözden kaçacak maddesi: rol tablosuna
/// bakmadan "eklemedim herhâlde" demek yetmez, teste bağlanması gerekiyor.
///
/// Koordinatlar 0–10 aralığında (PoiTests ve CografiYetkiTests ile aynı
/// yaklaşım): hangi noktanın nerede olduğu okurken hesap gerektirmiyor.
/// </summary>
public class UlasimTests
{
    private const int Operator = 1;    // Durak Ekleme + Güzergah Yönetimi
    private const int Baskasi = 2;     // yalnızca Durak Ekleme

    private sealed record Ortam(SahteVeritabani Db, UlasimService Servis, FakeGeoPermissionService Geo);

    /// <summary>
    /// İki kullanıcılı bir dünya kurar. Yetkiler DOĞRUDAN veriliyor; rol
    /// üzerinden vermek de aynı sonucu üretirdi (PermissionService ikisini
    /// birleştiriyor) ama testin kurulumunu gereksiz uzatırdı.
    /// </summary>
    private static Ortam Kur(int? girisYapan = Operator)
    {
        var db = new SahteVeritabani();

        db.Kullanicilar.Add(new User { Id = db.SonrakiKullaniciId(), Username = "kemal" });
        db.Kullanicilar.Add(new User { Id = db.SonrakiKullaniciId(), Username = "baskasi" });

        YetkiVer(db, Operator, Yetkiler.DurakEkleme, Yetkiler.GuzergahYonetimi);
        YetkiVer(db, Baskasi, Yetkiler.DurakEkleme);

        var oturum = new FakeCurrentUserService(girisYapan);
        var izinler = new PermissionService(
            new FakePermissionRepository(db), new FakeUserRepository(db), oturum);

        var geo = new FakeGeoPermissionService();

        return new Ortam(db, new UlasimService(new FakeUlasimRepository(db), oturum, izinler, geo), geo);
    }

    private static void YetkiVer(SahteVeritabani db, int kullaniciId, params string[] adlar)
    {
        foreach (var ad in adlar)
        {
            var yetki = db.Yetkiler.FirstOrDefault(y => y.Name == ad) ?? db.YetkiEkle(ad);
            db.KullaniciYetkileri.Add(new UserPermission { UserId = kullaniciId, PermissionId = yetki.Id });
        }
    }

    private static GuzergahSaveDto Hat(string ad, string renk = "#2d7dd2", bool aktif = true)
        => new() { Ad = ad, Renk = renk, IsActive = aktif };

    private static DurakCreateDto YeniDurak(int guzergahId, string ad, double x = 5, double y = 5)
        => new()
        {
            Ad = ad,
            GuzergahId = guzergahId,
            Wkt = $"POINT ({x.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                  $"{y.ToString(System.Globalization.CultureInfo.InvariantCulture)})",
        };

    // ==================================================================
    //  1) Güzergah
    // ==================================================================

    [Fact]
    public async Task Guzergah_AdVeRenkleOlusturulur()
    {
        var o = Kur();

        var olusan = await o.Servis.GuzergahEkleAsync(Hat("126 Ulus - Çayyolu", "#D64550"));

        Assert.Equal("126 Ulus - Çayyolu", olusan.Ad);
        // Renk küçük harfe sabitleniyor: arayüzdeki renk seçici küçük harf
        // üretiyor ve büyük harfli değer orada "seçili değil" görünürdü.
        Assert.Equal("#d64550", olusan.Renk);
        Assert.Empty(olusan.Duraklar);
        Assert.Equal(0, olusan.DurakSayisi);
    }

    [Theory]
    [InlineData("mavi")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("rgb(1,2,3)")]
    public async Task Guzergah_GecersizRenkReddedilir(string renk)
    {
        var o = Kur();

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.GuzergahEkleAsync(Hat("Hat", renk)));

        Assert.Contains("#rrggbb", hata.Message);
    }

    /// <summary>
    /// Aynı adda ikinci hat açılamaz: durak formundaki açılır listede hat
    /// ADIYLA seçiliyor, iki "126 Ulus" satırı arasında hangisinin doğru
    /// olduğunu kimse bilemez.
    /// </summary>
    [Fact]
    public async Task Guzergah_AyniAdIkiKezAcilamaz()
    {
        var o = Kur();
        await o.Servis.GuzergahEkleAsync(Hat("M1 Metro"));

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.GuzergahEkleAsync(Hat("m1 metro")));   // büyük/küçük harf fark etmez

        Assert.Contains("zaten var", hata.Message);
    }

    [Fact]
    public async Task Guzergah_GuncellenebilirVeAdKendisiyleCakismaz()
    {
        var o = Kur();
        var hat = await o.Servis.GuzergahEkleAsync(Hat("M1"));

        var guncel = await o.Servis.GuzergahGuncelleAsync(hat.Id, Hat("M1", "#2e9e63"));

        Assert.Equal("#2e9e63", guncel!.Renk);
    }

    /// <summary>
    /// Durağı olan güzergah silinemez. Şemada ilişki Restrict ama soft delete
    /// kullandığımız için veritabanı bunu göremiyor — kural serviste.
    /// </summary>
    [Fact]
    public async Task Guzergah_DuragiVarsaSilinemez()
    {
        var o = Kur();
        var hat = await o.Servis.GuzergahEkleAsync(Hat("M1"));
        await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "Kızılay"));

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.GuzergahSilAsync(hat.Id));

        Assert.Contains("1 durak", hata.Message);
    }

    [Fact]
    public async Task Guzergah_BosOlaninSilinmesiSerbest()
    {
        var o = Kur();
        var hat = await o.Servis.GuzergahEkleAsync(Hat("Boş Hat"));

        Assert.True(await o.Servis.GuzergahSilAsync(hat.Id));
        Assert.Empty(await o.Servis.GuzergahlariGetirAsync());
    }

    // ==================================================================
    //  2) 1-N ilişki ve sıra
    // ==================================================================

    [Fact]
    public async Task Durak_OlmayanGuzergahaEklenemez()
    {
        var o = Kur();

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.DurakEkleAsync(YeniDurak(999, "Kızılay")));

        Assert.Contains("bulunamadı", hata.Message);
    }

    [Fact]
    public async Task Durak_PasifGuzergahaEklenemez()
    {
        var o = Kur();
        var hat = await o.Servis.GuzergahEkleAsync(Hat("Kapalı Hat", aktif: false));

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "Kızılay")));

        Assert.Contains("pasif", hata.Message);
    }

    /// <summary>
    /// Yeni durak hattın SONUNA ekleniyor ve sıra 1'den başlıyor.
    /// Haritadan durak koyarken en doğal davranış bu.
    /// </summary>
    [Fact]
    public async Task Durak_SonaEklenirVeSiraBirdenBaslar()
    {
        var o = Kur();
        var hat = await o.Servis.GuzergahEkleAsync(Hat("M1"));

        var a = await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "A"));
        var b = await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "B"));
        var c = await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "C"));

        Assert.Equal(1, a.Sira);
        Assert.Equal(2, b.Sira);
        Assert.Equal(3, c.Sira);

        var guncel = await o.Servis.GuzergahGetirAsync(hat.Id);
        Assert.Equal(new[] { "A", "B", "C" }, guncel!.Duraklar.Select(d => d.Ad));
    }

    /// <summary>
    /// Ortadaki durak silinince kalanların sırası 1..N'e SIKIŞTIRILIYOR.
    /// Boşluk teknik olarak zararsız ama arayüz numarayı ekranda gösteriyor;
    /// "3. durak" yazan satırın listede ikinci sırada olması yanıltırdı.
    /// </summary>
    [Fact]
    public async Task Durak_SilinceSiraSikistirilir()
    {
        var o = Kur();
        var hat = await o.Servis.GuzergahEkleAsync(Hat("M1"));

        await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "A"));
        var b = await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "B"));
        await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "C"));

        Assert.True(await o.Servis.DurakSilAsync(b.Id));

        var guncel = await o.Servis.GuzergahGetirAsync(hat.Id);
        Assert.Equal(new[] { "A", "C" }, guncel!.Duraklar.Select(d => d.Ad));
        Assert.Equal(new[] { 1, 2 }, guncel.Duraklar.Select(d => d.Sira));
    }

    /// <summary>
    /// Durak BAŞKA bir hatta taşınırsa yeni hattın sonuna gidiyor ve eski
    /// hattaki boşluk kapanıyor. Eski sıra numarasını korusaydı yeni hatta
    /// başka bir durakla çakışırdı.
    /// </summary>
    [Fact]
    public async Task Durak_BaskaGuzergahaTasininca_IkiHattinSirasiDuzelir()
    {
        var o = Kur();
        var m1 = await o.Servis.GuzergahEkleAsync(Hat("M1"));
        var m2 = await o.Servis.GuzergahEkleAsync(Hat("M2"));

        await o.Servis.DurakEkleAsync(YeniDurak(m1.Id, "A"));
        var b = await o.Servis.DurakEkleAsync(YeniDurak(m1.Id, "B"));
        await o.Servis.DurakEkleAsync(YeniDurak(m1.Id, "C"));
        await o.Servis.DurakEkleAsync(YeniDurak(m2.Id, "X"));

        await o.Servis.DurakGuncelleAsync(b.Id, new DurakUpdateDto { Ad = "B", GuzergahId = m2.Id });

        var eski = await o.Servis.GuzergahGetirAsync(m1.Id);
        var yeni = await o.Servis.GuzergahGetirAsync(m2.Id);

        Assert.Equal(new[] { "A", "C" }, eski!.Duraklar.Select(d => d.Ad));
        Assert.Equal(new[] { 1, 2 }, eski.Duraklar.Select(d => d.Sira));

        Assert.Equal(new[] { "X", "B" }, yeni!.Duraklar.Select(d => d.Ad));
        Assert.Equal(new[] { 1, 2 }, yeni.Duraklar.Select(d => d.Sira));
    }

    [Fact]
    public async Task Durak_KonumBosBirakilirsaDegismez()
    {
        var o = Kur();
        var hat = await o.Servis.GuzergahEkleAsync(Hat("M1"));
        var a = await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "A", 3, 4));

        var guncel = await o.Servis.DurakGuncelleAsync(
            a.Id, new DurakUpdateDto { Ad = "A2", GuzergahId = hat.Id });

        Assert.Equal("A2", guncel!.Ad);
        Assert.Equal(a.Wkt, guncel.Wkt);
    }

    /// <summary>
    /// Durak da haritaya yapılan bir kayıt: coğrafi yetki alanının dışına
    /// konamıyor. Kontrol çizim ve POI uçlarıyla AYNI servise gidiyor —
    /// üç yolun ayrışmaması için.
    /// </summary>
    [Fact]
    public async Task Durak_CografiYetkiAlaniDisinaEklenemez()
    {
        var o = Kur();
        var hat = await o.Servis.GuzergahEkleAsync(Hat("M1"));

        // İzinli alan sol alt köşede; durak (5,5)'e konuyor — dışarıda.
        o.Geo.IzinliAlan = WktConverter.Read<NetTopologySuite.Geometries.Polygon>(
            "POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))");

        await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "Uzak Durak")));

        // Aynı alanın İÇİNDEKİ durak kabul edilmeli — kontrolün her şeyi
        // reddetmediğini de göstermek gerekiyor.
        var yakin = await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "Yakın Durak", 1, 1));
        Assert.Equal("Yakın Durak", yakin.Ad);
    }

    [Fact]
    public async Task Durak_GecersizWktReddedilir()
    {
        var o = Kur();
        var hat = await o.Servis.GuzergahEkleAsync(Hat("M1"));

        await Assert.ThrowsAsync<WktFormatException>(
            () => o.Servis.DurakEkleAsync(new DurakCreateDto
            {
                Ad = "Hatalı",
                GuzergahId = hat.Id,
                Wkt = "LINESTRING (0 0, 1 1)",   // POINT olmalı
            }));
    }

    // ==================================================================
    //  3) Sürükle-bırak sıralaması
    // ==================================================================

    [Fact]
    public async Task Siralama_YeniSirayiYazar()
    {
        var o = Kur();
        var hat = await o.Servis.GuzergahEkleAsync(Hat("M1"));

        var a = await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "A"));
        var b = await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "B"));
        var c = await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "C"));

        // C'yi başa taşı (sürükle-bırakın sunucudaki karşılığı)
        var guncel = await o.Servis.SiralamaGuncelleAsync(hat.Id, new DurakSiralamaDto
        {
            DurakIdleri = new List<int> { c.Id, a.Id, b.Id },
        });

        Assert.Equal(new[] { "C", "A", "B" }, guncel!.Duraklar.Select(d => d.Ad));
        Assert.Equal(new[] { 1, 2, 3 }, guncel.Duraklar.Select(d => d.Sira));
    }

    /// <summary>
    /// Liste EKSİK gelirse reddediliyor. Eksik listeyi kabul etseydik
    /// gönderilmeyen durakların sırası eski kalır ve iki durak aynı numarayı
    /// paylaşabilirdi.
    /// </summary>
    [Fact]
    public async Task Siralama_EksikListeReddedilir()
    {
        var o = Kur();
        var hat = await o.Servis.GuzergahEkleAsync(Hat("M1"));
        var a = await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "A"));
        await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "B"));

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.SiralamaGuncelleAsync(hat.Id, new DurakSiralamaDto
            {
                DurakIdleri = new List<int> { a.Id },
            }));

        Assert.Contains("BÜTÜN duraklarını", hata.Message);
    }

    /// <summary>
    /// Başka hattın durağının id'si gönderilirse reddediliyor. Repository
    /// yabancı id'yi zaten atlıyor ama "istek kabul edildi" cevabı
    /// yanıltıcı olurdu.
    /// </summary>
    [Fact]
    public async Task Siralama_YabanciDurakReddedilir()
    {
        var o = Kur();
        var m1 = await o.Servis.GuzergahEkleAsync(Hat("M1"));
        var m2 = await o.Servis.GuzergahEkleAsync(Hat("M2"));

        var a = await o.Servis.DurakEkleAsync(YeniDurak(m1.Id, "A"));
        var x = await o.Servis.DurakEkleAsync(YeniDurak(m2.Id, "X"));

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.SiralamaGuncelleAsync(m1.Id, new DurakSiralamaDto
            {
                DurakIdleri = new List<int> { a.Id, x.Id },
            }));

        Assert.Contains("bu güzergaha ait değil", hata.Message);
    }

    [Fact]
    public async Task Siralama_AyniDurakIkiKezVerilirseReddedilir()
    {
        var o = Kur();
        var hat = await o.Servis.GuzergahEkleAsync(Hat("M1"));
        var a = await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "A"));
        await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "B"));

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.SiralamaGuncelleAsync(hat.Id, new DurakSiralamaDto
            {
                DurakIdleri = new List<int> { a.Id, a.Id },
            }));

        Assert.Contains("birden fazla", hata.Message);
    }

    // ==================================================================
    //  4) Sahiplik
    // ==================================================================

    /// <summary>
    /// Yalnızca "Durak Ekleme" yetkisi olan biri BAŞKASININ durağına
    /// dokunamaz — POI'deki kuralın ulaşım karşılığı.
    /// </summary>
    [Fact]
    public async Task Durak_BaskasininKaydinaDokunulamaz()
    {
        var sahip = Kur(Operator);
        var hat = await sahip.Servis.GuzergahEkleAsync(Hat("M1"));
        var durak = await sahip.Servis.DurakEkleAsync(YeniDurak(hat.Id, "A"));

        // Aynı veritabanı, farklı kullanıcı: yalnızca Durak Ekleme yetkisi var.
        var oturum = new FakeCurrentUserService(Baskasi);
        var izinler = new PermissionService(
            new FakePermissionRepository(sahip.Db), new FakeUserRepository(sahip.Db), oturum);
        var digeri = new UlasimService(
            new FakeUlasimRepository(sahip.Db), oturum, izinler, new FakeGeoPermissionService());

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => digeri.DurakGuncelleAsync(durak.Id, new DurakUpdateDto { Ad = "X", GuzergahId = hat.Id }));

        Assert.Contains("başka bir kullanıcı", hata.Message);
    }

    /// <summary>
    /// "Güzergah Yönetimi" yetkisi olan (hat sorumlusu) HER durağa
    /// dokunabiliyor — sahibi olmasa bile.
    /// </summary>
    [Fact]
    public async Task Durak_GuzergahYoneticisiHerKaydaDokunabilir()
    {
        var o = Kur(Baskasi);   // durağı "baskasi" ekliyor
        var hatSahibi = Kur(Operator);
        _ = hatSahibi;

        // Hattı ve durağı "baskasi" oluşturuyor; ama hat açmak için Güzergah
        // Yönetimi gerekiyor, o yüzden hattı doğrudan veritabanına koyuyoruz.
        var hat = o.Db.GuzergahEkle("M1");
        var durak = await o.Servis.DurakEkleAsync(YeniDurak(hat.Id, "A"));

        // Şimdi Güzergah Yönetimi yetkisi olan kullanıcı aynı durağı düzenliyor.
        var oturum = new FakeCurrentUserService(Operator);
        var izinler = new PermissionService(
            new FakePermissionRepository(o.Db), new FakeUserRepository(o.Db), oturum);
        var yonetici = new UlasimService(
            new FakeUlasimRepository(o.Db), oturum, izinler, new FakeGeoPermissionService());

        var guncel = await yonetici.DurakGuncelleAsync(
            durak.Id, new DurakUpdateDto { Ad = "Kızılay", GuzergahId = hat.Id });

        Assert.Equal("Kızılay", guncel!.Ad);
    }

    // ==================================================================
    //  5) ÖDEV NOTU — ulaşım rolleri POI ve çizim EKLEYEMEZ
    // ==================================================================

    /// <summary>
    /// Ödev notunun teste bağlanmış hâli:
    ///
    ///   "Bu yeni rolleri eklerken daha önce yapılan POI ve Harita Çizim
    ///    araçlarını bu roldekiler ekleme işlemlerini yapmasınlar yetkileri
    ///    olmasın, eklenen POI leri görüntüleyebilsinler."
    ///
    /// Rol tanımına bakıp "eklemedim herhâlde" demek yetmez: yeni bir ödevde
    /// listeye bir yetki eklenirse kimse fark etmez. Bu test o kapıyı
    /// kapatıyor.
    /// </summary>
    [Theory]
    [InlineData("Ulaşım Operatörü")]
    [InlineData("Ulaşım Kullanıcısı")]
    public void UlasimRolleri_PoiVeCizimYetkisiIcermez(string rolAdi)
    {
        var rol = DatabaseSeeder.BaslangicRolleriTest.Single(r => r.Ad == rolAdi);

        var yasakli = new[]
        {
            Yetkiler.PoiEkleme,
            Yetkiler.PoiYonetimi,
            Yetkiler.NoktaEkleme,
            Yetkiler.CizgiEkleme,
            Yetkiler.PoligonEkleme,
        };

        foreach (var yetki in yasakli)
        {
            Assert.DoesNotContain(yetki, rol.Yetkiler);
        }
    }

    /// <summary>
    /// Ulaşım Operatörü kendi işini yapabilmeli — "hiçbir yetkisi yok" da
    /// yanlış olurdu.
    /// </summary>
    [Fact]
    public void UlasimOperatoru_KendiYetkileriniTasir()
    {
        var rol = DatabaseSeeder.BaslangicRolleriTest.Single(r => r.Ad == "Ulaşım Operatörü");

        Assert.Contains(Yetkiler.DurakEkleme, rol.Yetkiler);
        Assert.Contains(Yetkiler.GuzergahYonetimi, rol.Yetkiler);
    }

    /// <summary>
    /// Ulaşım Kullanıcısının HİÇ yetkisi yok — ve bu bir eksiklik değil,
    /// tanımın kendisi. Görüntüleme uçları yetki istemiyor.
    /// </summary>
    [Fact]
    public void UlasimKullanicisi_HicYetkiTasimaz()
    {
        var rol = DatabaseSeeder.BaslangicRolleriTest.Single(r => r.Ad == "Ulaşım Kullanıcısı");
        Assert.Empty(rol.Yetkiler);
    }

    /// <summary>
    /// Yeni iki yetki, seed'in tanıdığı yetki listesinde olmalı. Olmasaydı
    /// permissions tablosuna hiç yazılmaz, rollere de dağıtılamazdı.
    /// </summary>
    [Fact]
    public void YeniYetkiler_SeedListesindeTanimli()
    {
        var adlar = Yetkiler.Tumu.Select(y => y.Ad).ToList();

        Assert.Contains(Yetkiler.DurakEkleme, adlar);
        Assert.Contains(Yetkiler.GuzergahYonetimi, adlar);
    }
}
