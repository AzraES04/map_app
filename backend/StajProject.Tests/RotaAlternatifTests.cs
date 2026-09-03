using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.Business.Ulasim;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 18 — bir durağa GİDEN yolların alternatifleri.
///
/// ---- BURADAKİ ASIL SORULAR ----
///
/// 1. Doğru BACAK mı soruluyor? Kullanıcı bir durağa tıklıyor; alternatifler
///    "bir önceki duraktan bu durağa" olmalı. Yanlış bacağı sorsaydık liste
///    dolu gelir, hiçbir hata alınmaz ve kullanıcı ilgisiz yolları seçerdi.
///
/// 2. "En iyi" gerçekten OTOMATİK mi seçiliyor? Ödevin açık isteği bu.
///
/// 3. Seçim KALICI mı? Kullanıcı bir alternatif seçtiğinde kaydedilen rota
///    gerçekten o yoldan mı geçiyor, yoksa OSRM yine kendi bildiğini mi
///    okuyor?
///
/// 4. Alternatif YOKKEN ne oluyor? Üç ayrı sebep var (ilk durak / OSRM kapalı
///    / tek makul yol) ve üçü kullanıcı için farklı şeyler.
/// </summary>
public class RotaAlternatifTests
{
    private const int Operator = 1;

    private sealed record Ortam(SahteVeritabani Db, UlasimService Servis, FakeOsrmClient Osrm);

    private static Ortam OrtamKur()
    {
        var db = new SahteVeritabani();
        db.Kullanicilar.Add(new User { Id = db.SonrakiKullaniciId(), Username = "kemal" });

        foreach (var ad in new[] { Yetkiler.DurakEkleme, Yetkiler.GuzergahYonetimi })
        {
            var yetki = db.Yetkiler.FirstOrDefault(y => y.Name == ad) ?? db.YetkiEkle(ad);
            db.KullaniciYetkileri.Add(new UserPermission { UserId = Operator, PermissionId = yetki.Id });
        }

        var oturum = new FakeCurrentUserService(Operator);
        var izinler = new PermissionService(
            new FakePermissionRepository(db), new FakeUserRepository(db), oturum);

        var osrm = new FakeOsrmClient();
        // Ödev 19: simülasyon defteri ve ayarları GERÇEK nesneyle veriliyor.
        // Sahte yazmadık çünkü ikisi de bellekte çalışan, dış dünyaya
        // dokunmayan sınıflar — taklidi aslından karmaşık olurdu.
        return new Ortam(db, new UlasimService(
            new FakeUlasimRepository(db), oturum, izinler,
            new FakeGeoPermissionService(), osrm,
            new SimulasyonServisi(), new SimulasyonAyarlari()), osrm);
    }

    private static async Task<GuzergahDto> HatKurAsync(Ortam o, params (double Lon, double Lat)[] duraklar)
    {
        var hat = await o.Servis.GuzergahEkleAsync(new GuzergahSaveDto
        {
            Ad = "M1 Test",
            Renk = "#d64550",
        });

        var sira = 1;
        foreach (var (lon, lat) in duraklar)
        {
            await o.Servis.DurakEkleAsync(new DurakCreateDto
            {
                Ad = $"Durak {sira++}",
                GuzergahId = hat.Id,
                Wkt = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"POINT ({lon} {lat})"),
            });
        }

        return (await o.Servis.GuzergahGetirAsync(hat.Id))!;
    }

    private static readonly (double, double)[] UcDurak =
    {
        (32.8541, 39.9208),   // Kızılay
        (32.8520, 39.9420),   // Ulus
        (32.7200, 39.9700),   // Batıkent
    };

    // ==================================================================
    //  1) Doğru bacak
    // ==================================================================

    [Fact]
    public async Task IKINCIDuragaSorulunca_ONCEKIDuraktanHesapliyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[1].Id);

        Assert.NotNull(sonuc);
        Assert.Equal("Durak 1", sonuc!.OncekiDurakAdi);
        Assert.Equal("Durak 2", sonuc.DurakAdi);

        // OSRM'e giden noktalar: 1. durak → 2. durak. Yanlış bacağı sorsaydık
        // liste yine dolu gelirdi ve hiçbir hata alınmazdı.
        Assert.Equal(2, o.Osrm.SonNoktalar!.Count);
        Assert.Equal(32.8541, o.Osrm.SonNoktalar[0].X, 4);
        Assert.Equal(32.8520, o.Osrm.SonNoktalar[1].X, 4);
    }

    [Fact]
    public async Task UCUNCUDuragaSorulunca_IKINCIDENHesapliyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[2].Id);

        Assert.Equal("Durak 2", sonuc!.OncekiDurakAdi);
        Assert.Equal(32.8520, o.Osrm.SonNoktalar![0].X, 4);
        Assert.Equal(32.7200, o.Osrm.SonNoktalar[1].X, 4);
    }

    // ==================================================================
    //  2) "En iyi" otomatik seçiliyor
    // ==================================================================

    [Fact]
    public async Task ILKALTERNATIF_EnIyiIsaretli()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[1].Id);

        Assert.True(sonuc!.Alternatifler.Count > 1);

        // ÖDEVİN AÇIK İSTEĞİ: en iyi alternatif otomatik seçili gelmeli.
        Assert.True(sonuc.Alternatifler[0].EnIyi);
        Assert.All(sonuc.Alternatifler.Skip(1), a => Assert.False(a.EnIyi));
    }

    [Fact]
    public async Task SIRAOSRMdenGeldigiGibiKORUNUYOR()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[1].Id);
        var sureler = sonuc!.Alternatifler.Select(a => a.SureSaniye).ToList();

        // Kendi sıralamamızı YAPMIYORUZ. "En iyi" tanımını yol ağını bilen
        // tarafa bırakmak, mesafeye göre kendi sıralamamızı yapmaktan daha
        // doğru: en KISA yol, en HIZLI yol değildir.
        //
        // Sahte istemci alternatifleri artan maliyetle üretiyor; sıra
        // bozulsaydı burada görünürdü.
        Assert.Equal(sureler.OrderBy(x => x), sureler);
        Assert.Equal(0, sonuc.Alternatifler[0].Sira);
    }

    [Fact]
    public async Task SUREFARKI_EnIyiyeGoreHesaplaniyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[1].Id);

        // En iyide fark sıfır; diğerlerinde pozitif. Kullanıcı "bu yolu
        // seçersem ne kaybederim?" sorusunun cevabını görmeli — mutlak
        // süreleri yan yana koymak aynı bilgiyi vermiyor.
        Assert.Equal(0, sonuc!.Alternatifler[0].SureFarkiSaniye);
        Assert.All(sonuc.Alternatifler.Skip(1), a => Assert.True(a.SureFarkiSaniye > 0));
    }

    [Fact]
    public async Task HerAlternatif_VIANOKTASITasiyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[1].Id);

        // Via noktası, seçimi KALICI kılan şey. Olmasaydı kullanıcı bir
        // alternatife tıklar, kaydedince OSRM yine kendi bildiğini okurdu.
        Assert.All(sonuc!.Alternatifler, a =>
        {
            Assert.StartsWith("POINT", a.ViaWkt);
            Assert.StartsWith("LINESTRING", a.Wkt);
        });

        // Via noktaları BİRBİRİNDEN FARKLI olmalı: aynı olsalardı hepsi aynı
        // rotayı zorlar, seçim anlamsızlaşırdı.
        var vialar = sonuc.Alternatifler.Select(a => a.ViaWkt).ToList();
        Assert.Equal(vialar.Count, vialar.Distinct().Count());
    }

    // ==================================================================
    //  3) Seçim KALICI mı?
    // ==================================================================

    [Fact]
    public async Task SecilenAlternatif_ROTAYAYANSIYOR()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[1].Id);
        var secilen = sonuc!.Alternatifler[1];       // en iyi DEĞİL, ikincisi

        await o.Servis.RotaHesaplaAsync(
            hat.Id,
            new[] { new RotaViaDto { DurakId = hat.Duraklar[1].Id, Wkt = secilen.ViaWkt } });

        // OSRM'e giden nokta listesi ARA NOKTAYI da içermeli: üç durak +
        // bir via = dört nokta.
        Assert.Equal(4, o.Osrm.SonNoktalar!.Count);
    }

    [Fact]
    public async Task VIANOKTASIDogruBacaginICINEGiriyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        // 2. durağa giden bacak = 1. ile 2. durak arası. Via oraya girmeli.
        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[1].Id);
        await o.Servis.RotaHesaplaAsync(
            hat.Id,
            new[]
            {
                new RotaViaDto
                {
                    DurakId = hat.Duraklar[1].Id,
                    Wkt = sonuc!.Alternatifler[1].ViaWkt,
                },
            });

        var noktalar = o.Osrm.SonNoktalar!;

        // Sıra: durak1, VIA, durak2, durak3
        //
        // Sona eklenseydi rota önce bütün durakları dolaşır, sonra ara
        // noktaya giderdi — yani seçilen alternatif değil, saçma bir zikzak.
        Assert.Equal(32.8541, noktalar[0].X, 4);   // durak 1
        Assert.Equal(32.8520, noktalar[2].X, 4);   // durak 2
        Assert.Equal(32.7200, noktalar[3].X, 4);   // durak 3
    }

    /// <summary>
    /// CANLIDA YAKALANAN HATANIN TESTİ.
    ///
    /// İlk sürümde via'nın hangi bacağa gireceğini geometriden tahmin
    /// ediyordum: "iki durağa uzaklıklarının toplamı en küçük olan bacak".
    /// Ankara verisinde bu tahmin YANILDI —
    ///
    ///   via (32.8001, 39.9562) için
    ///     bacak Batıkent→Kızılay : 7014 + 6061 = 13075 m
    ///     bacak Kızılay→Ulus     : 6061 + 4706 = 10767 m   ← kazanıyor
    ///
    /// — çünkü uzaklık toplamı "doğru parçasına yakınlık" ölçmüyor: uçları
    /// birbirine yakın kısa bir bacak, via ondan uzakta olsa bile küçük
    /// toplam veriyor. Sonuç: rota seçilen alternatifi izlemek yerine
    /// yaklaşık 10 km dolanıyordu ve HİÇBİR TEST KIRILMIYORDU.
    ///
    /// Bu test o dizilimi birebir kuruyor. DurakId ile açık söylemek yerine
    /// tahmine dönersek burası kırmızı olur.
    /// </summary>
    [Fact]
    public async Task VIA_KOMSUBACAKDAHAYAKINGORUNSEBILE_DOGRUBACAGAGiriyor()
    {
        var o = OrtamKur();

        // Canlı veriden alınmış gerçek dizilim: uzun bir bacağın ardından
        // birbirine çok yakın iki durak.
        var hat = await HatKurAsync(
            o,
            (32.7200, 39.9700),    // Batıkent
            (32.8541, 39.9208),    // Kızılay
            (32.8520, 39.9420));   // Ulus

        // Via, BATIKENT→KIZILAY bacağından alınmış bir nokta.
        await o.Servis.RotaHesaplaAsync(
            hat.Id,
            new[]
            {
                new RotaViaDto
                {
                    DurakId = hat.Duraklar[1].Id,          // Kızılay
                    Wkt = "POINT (32.800104 39.956182)",
                },
            });

        var noktalar = o.Osrm.SonNoktalar!;

        Assert.Equal(4, noktalar.Count);
        Assert.Equal(32.7200, noktalar[0].X, 4);     // Batıkent
        Assert.Equal(32.800104, noktalar[1].X, 5);   // VIA — burada olmalı
        Assert.Equal(32.8541, noktalar[2].X, 4);     // Kızılay
        Assert.Equal(32.8520, noktalar[3].X, 4);     // Ulus
    }

    /// <summary>
    /// Hattın İLK durağı için via anlamsız: öncesinde bacak yok. Sessizce
    /// atlanıyor — istisna fırlatmak, kullanıcının hiç göremediği bir ayrıntı
    /// yüzünden bütün rota hesabını iptal etmek olurdu.
    /// </summary>
    [Fact]
    public async Task ILKDURAGAViaVerilirse_SESSIZCEAtlaniyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        var guncel = await o.Servis.RotaHesaplaAsync(
            hat.Id,
            new[]
            {
                new RotaViaDto
                {
                    DurakId = hat.Duraklar[0].Id,          // ilk durak
                    Wkt = "POINT (32.8530 39.9300)",
                },
            });

        Assert.NotNull(guncel);
        Assert.Equal(3, o.Osrm.SonNoktalar!.Count);   // yalnızca duraklar
    }

    [Fact]
    public async Task VIASIZCagri_ESKIDAVRANISIKoruyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        await o.Servis.RotaHesaplaAsync(hat.Id);

        // Ara nokta verilmezse OSRM serbest: yalnızca duraklar gidiyor.
        // Bu, Ödev 17'nin davranışı ve bozulmamalı.
        Assert.Equal(3, o.Osrm.SonNoktalar!.Count);
    }

    [Fact]
    public async Task BOZUKViaNoktasi_ROTAYIBOZMUYOR()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        // Bozuk WKT sessizce atlanıyor: bir ara noktanın biçimi bozuk diye
        // bütün rota hesabını reddetmek, kullanıcının işini dış bir hataya
        // kurban etmek olurdu.
        var guncel = await o.Servis.RotaHesaplaAsync(
            hat.Id,
            new[] { new RotaViaDto { DurakId = hat.Duraklar[1].Id, Wkt = "BOZUK WKT" } });

        Assert.NotNull(guncel);
        Assert.NotNull(guncel!.RotaWkt);
        Assert.Equal(3, o.Osrm.SonNoktalar!.Count);
    }

    // ==================================================================
    //  4) Önizleme — gösteriyor ama KAYDETMİYOR
    // ==================================================================

    /// <summary>
    /// Kullanıcı haritada bir alternatife tıkladığında hattın TAMAMINI o
    /// yoldan görüyor. Önizleme, seçilen ara noktayı içeren tam rotayı
    /// döndürmeli.
    /// </summary>
    [Fact]
    public async Task ONIZLEME_TAMROTAYIAraNoktayla_Donduruyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[1].Id);

        var onizleme = await o.Servis.RotaOnizleAsync(
            hat.Id,
            new[]
            {
                new RotaViaDto
                {
                    DurakId = hat.Duraklar[1].Id,
                    Wkt = sonuc!.Alternatifler[1].ViaWkt,
                },
            });

        Assert.NotNull(onizleme);
        Assert.Equal(hat.Id, onizleme!.GuzergahId);
        Assert.StartsWith("LINESTRING", onizleme.Wkt);

        // Üç durak + bir ara nokta: önizleme de gerçek hesapla aynı noktaları
        // gönderiyor. Farklı gönderseydi kullanıcı bir şey görüp başka bir
        // şey kaydetmiş olurdu.
        Assert.Equal(4, o.Osrm.SonNoktalar!.Count);
    }

    /// <summary>
    /// EN KRİTİK TEST. Önizleme bir GEZİNME hareketi: kullanıcı alternatifler
    /// arasında dolaşırken hiçbir şey değişmemeli. Kaydetseydi, sadece bakmak
    /// isteyen kullanıcı farkında olmadan hattın rotasını değiştirirdi.
    /// </summary>
    [Fact]
    public async Task ONIZLEME_VERITABANINADOKUNMUYOR()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        // Önce gerçek bir rota yazdır; karşılaştıracağımız "önceki hâl" bu.
        await o.Servis.RotaHesaplaAsync(hat.Id);
        var oncesi = await o.Servis.GuzergahGetirAsync(hat.Id);

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[1].Id);
        await o.Servis.RotaOnizleAsync(
            hat.Id,
            new[]
            {
                new RotaViaDto
                {
                    DurakId = hat.Duraklar[1].Id,
                    Wkt = sonuc!.Alternatifler[1].ViaWkt,
                },
            });

        var sonrasi = await o.Servis.GuzergahGetirAsync(hat.Id);

        Assert.Equal(oncesi!.RotaWkt, sonrasi!.RotaWkt);
        Assert.Equal(oncesi.RotaMesafeMetre, sonrasi.RotaMesafeMetre);
    }

    /// <summary>Olmayan güzergah için null — 404'e dönüşüyor.</summary>
    [Fact]
    public async Task ONIZLEME_OLMAYANGUZERGAH_NullDonuyor()
    {
        var o = OrtamKur();
        Assert.Null(await o.Servis.RotaOnizleAsync(99999));
    }

    /// <summary>
    /// Ara nokta verilmezse önizleme, hattın OSRM'e göre en iyi hâlini
    /// gösteriyor — "alternatif seçimini geri al" hareketinin karşılığı.
    /// </summary>
    [Fact]
    public async Task ONIZLEME_VIASIZ_YALNIZCADURAKLARIGonderiyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        var onizleme = await o.Servis.RotaOnizleAsync(hat.Id);

        Assert.NotNull(onizleme);
        Assert.Equal(3, o.Osrm.SonNoktalar!.Count);
    }

    [Fact]
    public async Task SIRADEGISINCE_AlternatifSecimiKORUNMUYOR()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[1].Id);
        await o.Servis.RotaHesaplaAsync(
            hat.Id,
            new[]
            {
                new RotaViaDto
                {
                    DurakId = hat.Duraklar[1].Id,
                    Wkt = sonuc!.Alternatifler[1].ViaWkt,
                },
            });

        // Sıra değişti → otomatik yenileme devreye giriyor.
        await o.Servis.SiralamaGuncelleAsync(hat.Id, new DurakSiralamaDto
        {
            DurakIdleri = hat.Duraklar.Select(d => d.Id).Reverse().ToList(),
        });

        // BİLİNÇLİ KARAR: seçim korunmuyor. Alternatif, O ANKİ durak dizilimi
        // için anlamlıydı; dizilim başkalaştığında eski ara noktayı zorlamak,
        // artık ilgisi kalmamış bir yerden geçen tuhaf bir rota üretirdi.
        // Doğru varsayılana dönmek, sessizce yanlış bir rotayı korumaktan iyi.
        Assert.Equal(3, o.Osrm.SonNoktalar!.Count);
    }

    // ==================================================================
    //  4) Alternatif yokken — üç farklı sebep
    // ==================================================================

    [Fact]
    public async Task ILKDURAK_SebebiniSoyluyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[0].Id);

        // İlk durağın "öncesi" yok. Boş liste dönüp susmak, bunu "bir şey
        // olmadı"ya indirgerdi.
        Assert.Empty(sonuc!.Alternatifler);
        Assert.Contains("ilk durağı", sonuc.Mesaj);
        Assert.Null(sonuc.OncekiDurakAdi);
    }

    [Fact]
    public async Task OSRMKAPALI_SebebiniSoyluyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);
        o.Osrm.Etkin = false;

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[1].Id);

        Assert.Empty(sonuc!.Alternatifler);
        Assert.Contains("OSRM", sonuc.Mesaj);
    }

    [Fact]
    public async Task TEKYOLVARSA_SoyluyorAmaHataDEGIL()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);
        o.Osrm.UretilecekAlternatif = 1;

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[1].Id);

        // İki nokta arasında gerçekten tek makul yol olabiliyor. Söylemezsek
        // kullanıcı "alternatifler nerede?" diye düğmeye tekrar tekrar basar.
        Assert.Single(sonuc!.Alternatifler);
        Assert.Contains("tek makul yol", sonuc.Mesaj);
    }

    [Fact]
    public async Task OSRMYOLBULAMAZSA_SebebiniSoyluyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);
        o.Osrm.BasarisizOl = true;

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[1].Id);

        Assert.Empty(sonuc!.Alternatifler);
        Assert.Contains("yol bulamadı", sonuc.Mesaj);
    }

    [Fact]
    public async Task OLMAYANDurak_NullDonuyor()
    {
        var o = OrtamKur();
        Assert.Null(await o.Servis.DurakAlternatifleriAsync(9999));
    }

    [Fact]
    public async Task TEKDURAKLIHat_SebebiniSoyluyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, (32.8541, 39.9208));

        var sonuc = await o.Servis.DurakAlternatifleriAsync(hat.Duraklar[0].Id);

        Assert.Empty(sonuc!.Alternatifler);
        Assert.Contains("en az iki durak", sonuc.Mesaj);
    }
}
