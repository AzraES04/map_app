using NetTopologySuite.Geometries;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.Business.Ulasim;
using StajProject.Business.Validation;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 19 — araç simülasyonu.
///
/// ---- BURADAKİ ASIL SORULAR ----
///
/// 1. Araç DOĞRU YERDE mi? Yüzde 50'deyken hattın ortasında olmalı — ama
///    "orta" hangi ölçüyle? Boylam düzeltmesi unutulursa araç doğu-batı
///    giden hatlarda yavaşlar, kuzey-güney gidenlerde hızlanır ve hiçbir
///    hata mesajı çıkmaz.
///
/// 2. İlerleme ZAMANDAN mı türetiliyor? Tik biriktiren bir sayaç, sunucu
///    yük altındayken simülasyonu sürüklerdi.
///
/// 3. Biten simülasyon SON KEZ yayınlanıp defterden düşüyor mu? Düşmezse
///    araç sonsuza kadar son durakta durur; son mesaj gönderilmezse
///    takipçilerin ekranında asılı kalır.
///
/// 4. Yüzde kaç hesabı DURAKLARLA tutarlı mı? Bilgi kutucuğu "3. duraktan
///    çıktı, 4'e gidiyor" yazıyor; bu bilgi konumla çelişirse kullanıcı
///    hangisine inanacağını bilemez.
/// </summary>
public class SimulasyonTests
{
    private const int Operator = 1;

    // ---- Ankara'da doğu-batı uzanan basit bir hat ----
    private static readonly Coordinate Batikent = new(32.72, 39.97);
    private static readonly Coordinate Kizilay = new(32.8541, 39.9208);
    private static readonly Coordinate Ulus = new(32.852, 39.942);

    // ==================================================================
    //  1) Saf çekirdek: SimulasyonMotoru
    // ==================================================================

    [Fact]
    public void Kumulatif_ilk_eleman_sifir_son_eleman_toplam_uzunluk()
    {
        var yol = new List<Coordinate> { new(0, 0), new(0, 1), new(0, 3) };

        var kumulatif = SimulasyonMotoru.Kumulatif(yol);

        Assert.Equal(0, kumulatif[0]);
        Assert.Equal(1, kumulatif[1], 6);
        Assert.Equal(3, kumulatif[2], 6);
    }

    [Fact]
    public void Kumulatif_tek_noktali_yolu_reddeder()
    {
        // Tek noktalı bir "hat" üzerinde hareket tanımsız. Sessizce sıfır
        // uzunluk döndürseydik araç ilk karede hedefe varmış sayılırdı.
        Assert.Throws<ArgumentException>(
            () => SimulasyonMotoru.Kumulatif(new List<Coordinate> { new(0, 0) }));
    }

    [Fact]
    public void Konum_yolun_yarisinda_ortada_durur()
    {
        var yol = new List<Coordinate> { new(0, 39), new(0, 41) };

        var orta = SimulasyonMotoru.Konum(yol, SimulasyonMotoru.Kumulatif(yol), 0.5);

        Assert.Equal(0, orta.X, 6);
        Assert.Equal(40, orta.Y, 6);
    }

    [Fact]
    public void Konum_orani_kirpiyor_arac_hattan_tasmiyor()
    {
        // Zamanlayıcı bir tik gecikirse oran 1'i aşabilir. Kırpmasaydık
        // araç hattın ucundan taşar, haritada boşlukta ilerlerdi.
        var yol = new List<Coordinate> { new(0, 0), new(0, 2) };
        var kumulatif = SimulasyonMotoru.Kumulatif(yol);

        Assert.Equal(2, SimulasyonMotoru.Konum(yol, kumulatif, 1.4).Y, 6);
        Assert.Equal(0, SimulasyonMotoru.Konum(yol, kumulatif, -0.3).Y, 6);
    }

    [Fact]
    public void ParcaUzunlugu_boylami_enleme_gore_daraltir()
    {
        // Ankara enleminde (~40°) bir boylam derecesi, bir enlem derecesinin
        // yaklaşık %77'si kadar. Düzeltme olmasaydı ikisi eşit çıkardı ve
        // araç yönüne göre hızlanıp yavaşlardı.
        var enlemDerecesi = SimulasyonMotoru.ParcaUzunlugu(new Coordinate(33, 39.5), new Coordinate(33, 40.5));
        var boylamDerecesi = SimulasyonMotoru.ParcaUzunlugu(new Coordinate(32.5, 40), new Coordinate(33.5, 40));

        Assert.Equal(1.0, enlemDerecesi, 3);
        Assert.InRange(boylamDerecesi, 0.75, 0.78);
    }

    [Fact]
    public void NoktaninOrani_yol_ustunde_olmayan_duragi_dik_izdusurur()
    {
        // Durak, OSRM rotasının tam üstünde durmuyor: rota yola oturuyor,
        // durak kullanıcının tıkladığı yerde. Dik izdüşüm olmasaydı durak
        // oranları kayar, "kaçıncı duraktayız" bilgisi yanlış olurdu.
        var yol = new List<Coordinate> { new(0, 40), new(2, 40) };
        var kumulatif = SimulasyonMotoru.Kumulatif(yol);

        // Çizginin 1 birim sağında ama 0.5 derece KUZEYİNDE duran nokta
        var oran = SimulasyonMotoru.NoktaninOrani(yol, kumulatif, new Coordinate(1, 40.5));

        Assert.Equal(0.5, oran, 2);
    }

    // ==================================================================
    //  2) Defter: SimulasyonServisi
    // ==================================================================

    private static SimulasyonKaynak KaynakKur(double sureSaniye = 60)
        => new()
        {
            GuzergahId = 7,
            GuzergahAdi = "M1 Test",
            Renk = "#d64550",
            Yol = new List<Coordinate> { new(0, 40), new(2, 40) },
            Duraklar = new List<SimulasyonDurak>
            {
                new(1, "Başlangıç", 0),
                new(2, "Orta", 0.5),
                new(3, "Son", 1),
            },
            ToplamMetre = 10_000,
            ToplamSaniye = sureSaniye,
            BaslatanKullaniciId = Operator,
            BaslatanKullanici = "kemal",
        };

    [Fact]
    public void Baslat_ilk_durum_sifir_yuzde_ve_ilk_durakta()
    {
        var servis = new SimulasyonServisi();
        var an = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        var durum = servis.Baslat(KaynakKur(), an);

        Assert.Equal(0, durum.Yuzde);
        Assert.Equal("Başlangıç", durum.OncekiDurakAdi);
        Assert.Equal("Orta", durum.SonrakiDurakAdi);
        Assert.False(durum.Tamamlandi);
        Assert.Equal("kemal", durum.BaslatanKullanici);
    }

    [Fact]
    public void Ilerleme_ZAMANDAN_turetiliyor_tik_sayisindan_degil()
    {
        // Tik biriktiren bir sayaç yazsaydık, gecikmiş ya da atlanmış tikler
        // simülasyonu sürükler; sunucu yük altındayken araç geride kalırdı.
        // Burada tek bir "tik" atmadan, sadece saati ileri alarak yüzde 50'ye
        // varıyoruz — ilerlemenin kaynağının zaman olduğunun kanıtı.
        var servis = new SimulasyonServisi();
        var an = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        servis.Baslat(KaynakKur(sureSaniye: 60), an);

        var durum = servis.Durum(7, an.AddSeconds(30))!;

        Assert.Equal(50, durum.Yuzde, 1);
        Assert.Equal(1, durum.Lon, 3);              // hattın tam ortası
        Assert.Equal(5000, durum.AlinanMetre);
    }

    [Fact]
    public void Yuzde_ile_durak_bilgisi_birbiriyle_tutarli()
    {
        var servis = new SimulasyonServisi();
        var an = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        servis.Baslat(KaynakKur(), an);

        // %60: ortadaki durak geçildi, son durağa gidiliyor
        var durum = servis.Durum(7, an.AddSeconds(36))!;

        Assert.Equal(60, durum.Yuzde, 1);
        Assert.Equal(2, durum.OncekiDurakSira);
        Assert.Equal(3, durum.SonrakiDurakSira);
    }

    [Fact]
    public void Biten_simulasyon_SON_KEZ_yayinlanip_defterden_dusuyor()
    {
        var servis = new SimulasyonServisi();
        var an = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        servis.Baslat(KaynakKur(sureSaniye: 10), an);

        var son = servis.Ilerlet(an.AddSeconds(11));

        // Son mesaj gönderiliyor: istemci aracı bu mesajla haritadan siliyor.
        var durum = Assert.Single(son);
        Assert.True(durum.Tamamlandi);
        Assert.Equal(100, durum.Yuzde);
        Assert.Null(durum.SonrakiDurakAdi);        // vardı, sonraki durak yok

        // Ve artık defterde yok: bir sonraki tik boş dönüyor.
        Assert.Empty(servis.Ilerlet(an.AddSeconds(12)));
        Assert.Null(servis.Durum(7, an.AddSeconds(12)));
    }

    [Fact]
    public void Durdur_calisan_simulasyonu_siler_yoksa_false_doner()
    {
        var servis = new SimulasyonServisi();
        servis.Baslat(KaynakKur());

        Assert.True(servis.Durdur(7));
        Assert.False(servis.Durdur(7));
        Assert.Null(servis.Durum(7));
    }

    [Fact]
    public void Ayni_hatta_ikinci_baslat_bastan_aliyor()
    {
        // Yanlışlıkla iki kez tıklayan kullanıcıya hata vermek yerine
        // simülasyonu baştan alıyoruz.
        var servis = new SimulasyonServisi();
        var an = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        servis.Baslat(KaynakKur(), an);

        servis.Baslat(KaynakKur(), an.AddSeconds(30));

        Assert.Equal(0, servis.Durum(7, an.AddSeconds(30))!.Yuzde);
    }

    // ==================================================================
    //  3) Servis + veritabanı: UlasimService.SimulasyonBaslatAsync
    // ==================================================================

    private sealed record Ortam(SahteVeritabani Db, UlasimService Servis, ISimulasyonServisi Simulasyon);

    private static Ortam OrtamKur()
    {
        var db = new SahteVeritabani();
        db.Kullanicilar.Add(new User { Id = db.SonrakiKullaniciId(), Username = "ayse" });

        foreach (var ad in new[]
                 {
                     Yetkiler.DurakEkleme, Yetkiler.GuzergahYonetimi, Yetkiler.SimulasyonBaslatma,
                 })
        {
            var yetki = db.Yetkiler.FirstOrDefault(y => y.Name == ad) ?? db.YetkiEkle(ad);
            db.KullaniciYetkileri.Add(new UserPermission { UserId = Operator, PermissionId = yetki.Id });
        }

        var oturum = new FakeCurrentUserService(Operator) { UserName = "ayse" };
        var izinler = new PermissionService(
            new FakePermissionRepository(db), new FakeUserRepository(db), oturum);

        var simulasyon = new SimulasyonServisi();
        var servis = new UlasimService(
            new FakeUlasimRepository(db), oturum, izinler,
            new FakeGeoPermissionService(), new FakeOsrmClient(),
            simulasyon, new SimulasyonAyarlari());

        return new Ortam(db, servis, simulasyon);
    }

    private static async Task<GuzergahDto> HatKurAsync(Ortam o, params Coordinate[] duraklar)
    {
        var hat = await o.Servis.GuzergahEkleAsync(new GuzergahSaveDto { Ad = "M1 Test", Renk = "#d64550" });

        var sira = 1;
        foreach (var nokta in duraklar)
        {
            await o.Servis.DurakEkleAsync(new DurakCreateDto
            {
                Ad = $"Durak {sira++}",
                GuzergahId = hat.Id,
                Wkt = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"POINT ({nokta.X} {nokta.Y})"),
            });
        }

        return (await o.Servis.GuzergahGetirAsync(hat.Id))!;
    }

    [Fact]
    public async Task SimulasyonBaslat_duraklari_ORANLARIYLA_kaydediyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, Batikent, Kizilay, Ulus);

        var durum = await o.Servis.SimulasyonBaslatAsync(hat.Id);

        Assert.NotNull(durum);
        Assert.Equal(0, durum!.Yuzde);
        Assert.Equal(3, durum.DurakSayisi);
        Assert.Equal("Durak 1", durum.OncekiDurakAdi);      // ilk durakta duruyor
        Assert.Equal("ayse", durum.BaslatanKullanici);
        Assert.Equal(hat.Renk, durum.Renk);                 // araç hattın renginde
    }

    [Fact]
    public async Task SimulasyonBaslat_tek_durakli_hatta_is_kurali_hatasi()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, Kizilay);

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.SimulasyonBaslatAsync(hat.Id));

        Assert.Contains("en az 2 durak", hata.Message);
    }

    [Fact]
    public async Task SimulasyonBaslat_olmayan_hatta_null_doner()
    {
        var o = OrtamKur();

        Assert.Null(await o.Servis.SimulasyonBaslatAsync(9999));
    }

    [Fact]
    public async Task Baslatilan_simulasyon_AKTIF_listesinde_gorunuyor()
    {
        // Haritayı yeni açan istemci bu listeden "hangi hatlarda araç var?"
        // öğreniyor; SignalR yalnızca bundan sonraki güncellemeleri gönderir.
        var o = OrtamKur();
        var hat = await HatKurAsync(o, Batikent, Kizilay, Ulus);

        await o.Servis.SimulasyonBaslatAsync(hat.Id);

        var aktifler = o.Servis.AktifSimulasyonlar();
        Assert.Equal(hat.Id, Assert.Single(aktifler).GuzergahId);

        Assert.True(o.Servis.SimulasyonDurdur(hat.Id));
        Assert.Empty(o.Servis.AktifSimulasyonlar());
    }

    [Fact]
    public void Simulasyon_yetkisi_Admin_ve_Operator_rollerinde()
    {
        // Ödev metni: "Sadece Admin ve Operatör başlatabilsin."
        // Rol adı koda gömülmedi; yetki DAĞITIMI seed'de tanımlı ve testi bu.
        var roller = DatabaseSeeder.BaslangicRolleriTest;

        foreach (var rolAdi in new[] { "Admin", "Operatör" })
        {
            var rol = roller.Single(r => r.Ad == rolAdi);
            Assert.Contains(Yetkiler.SimulasyonBaslatma, rol.Yetkiler);
        }

        foreach (var rolAdi in new[] { "Kullanıcı", "Ulaşım Kullanıcısı" })
        {
            var rol = roller.Single(r => r.Ad == rolAdi);
            Assert.DoesNotContain(Yetkiler.SimulasyonBaslatma, rol.Yetkiler);
        }
    }
}
