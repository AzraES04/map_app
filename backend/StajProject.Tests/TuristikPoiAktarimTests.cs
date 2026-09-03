using Microsoft.Extensions.Logging.Abstractions;
using StajProject.Business.Services;
using StajProject.Business.Tur;
using StajProject.Business.Validation;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

// ============================================================================
//  ŞEHİR MERKEZİ ve TURİSTİK POI AKTARIMI
//
//  İki konu tek dosyada çünkü ikisi de AYNI hatanın sonucu: tur önerisi
//  Ankara için Polatlı'daki bir müzeyi getiriyordu.
//
//    Sebep 1 — arama merkezi il sınırının geometrik iç noktasıydı
//              (Ankara: 35 km güneybatı, İstanbul: Çatalca civarı).
//    Sebep 2 — POI tablosunda turistik kategori hiç yoktu.
//
//  İkisi de "hata vermeyen" cinsten: ekranda çalışıyor görünüyor, sadece
//  yanlış yerleri öneriyor. Testleri bu yüzden var.
// ============================================================================

public class TuristikPoiAktarimTests
{
    private const string AnkaraKaresi =
        "POLYGON ((32.6 39.8, 33.1 39.8, 33.1 40.1, 32.6 40.1, 32.6 39.8))";

    // ------------------------------------------------------------------
    //  Şehir merkezleri
    // ------------------------------------------------------------------

    [Fact]
    public void Seksen_bir_ilin_merkezi_tanimli()
    {
        Assert.Equal(81, SehirMerkezleri.Plakalar.Count);

        for (var plaka = 1; plaka <= 81; plaka++)
        {
            Assert.NotNull(SehirMerkezleri.Bul(plaka));
        }
    }

    [Theory]
    [InlineData(6, 39.93, 32.86)]    // Ankara — Kızılay
    [InlineData(34, 41.01, 28.98)]   // İstanbul — tarihi yarımada
    [InlineData(35, 38.42, 27.14)]   // İzmir — Konak
    public void Buyuk_sehirlerin_merkezi_gercekten_sehir_merkezi(int plaka, double lat, double lon)
    {
        var merkez = SehirMerkezleri.Bul(plaka);

        Assert.NotNull(merkez);
        // 5 km'lik tolerans: amaç kesin nokta değil, ŞEHRİN İÇİNDE olmak.
        Assert.Equal(lat, merkez!.Value.Lat, 1);
        Assert.Equal(lon, merkez.Value.Lon, 1);
    }

    [Fact]
    public void Tanimsiz_plaka_null_donuyor()
    {
        // Null bir hata değil: çağıran taraf il sınırının iç noktasına düşüyor.
        Assert.Null(SehirMerkezleri.Bul(0));
        Assert.Null(SehirMerkezleri.Bul(82));
    }

    // ------------------------------------------------------------------
    //  Aktarım
    // ------------------------------------------------------------------

    private static (TuristikPoiAktarici aktarici, FakePlacesClient osm,
                    FakePoiRepository poiler, FakePoiCategoryRepository kategoriler)
        Kur()
    {
        var osm = new FakePlacesClient();
        var poiler = new FakePoiRepository();
        var kategoriler = new FakePoiCategoryRepository();
        var iller = new FakeIlRepository().Ekle(6, "Ankara", AnkaraKaresi);

        return (
            new TuristikPoiAktarici(osm, poiler, kategoriler, iller,
                NullLogger<TuristikPoiAktarici>.Instance),
            osm, poiler, kategoriler);
    }

    /// <summary>Havuza gerçekçi birkaç turistik mekan koyar.</summary>
    private static void MekanEkle(FakePlacesClient osm)
    {
        // Anıtkabir OSM'de "tourist_attraction" süzgeciyle bulunuyor ama
        // etiketleri onu "monument" diye tanıtıyor — gerçek davranışın taklidi.
        osm.Ekle("osm:node/1", "Anıtkabir", 39.925, 32.836, null, 0, "monument",
            onem: 0.9, aramaTipi: "tourist_attraction");
        osm.Ekle("osm:node/2", "Etnografya Müzesi", 39.934, 32.855, null, 0, "museum", onem: 0.7);
        osm.Ekle("osm:way/3", "Gençlik Parkı", 39.941, 32.851, null, 0, "park", onem: 0.5);
    }

    [Fact]
    public async Task Aktarim_kategorileri_olusturup_poi_yaziyor()
    {
        var (aktarici, osm, poiler, kategoriler) = Kur();
        MekanEkle(osm);

        var sonuc = await aktarici.AktarAsync(new[] { 6 });

        Assert.Equal(3, sonuc.Eklenen);
        Assert.Equal(3, (await poiler.GetAllAsync()).Count);

        // Kök + beş alt kategori (Müze, Tarihi Yer, Park, İbadet Yeri).
        var adlar = (await kategoriler.GetAllAsync()).Select(k => k.Ad).ToList();
        Assert.Contains("Gezilecek Yer", adlar);
        Assert.Contains("Müze", adlar);
        Assert.Contains("Tarihi Yer", adlar);
        Assert.Contains("Park", adlar);

        // Sonuç sayıları şehir ve kategori bazında raporlanıyor: "başarılı"
        // demek, hiçbir şey eklemeyen bir çalıştırmayı da başarılı gösterirdi.
        Assert.Equal(3, sonuc.Sehirler["Ankara"]);
        Assert.Equal(1, sonuc.KategoriBazinda["Müze"]);
    }

    [Fact]
    public async Task Ikinci_aktarim_kayitlari_ikiye_katlamiyor()
    {
        var (aktarici, osm, poiler, _) = Kur();
        MekanEkle(osm);

        await aktarici.AktarAsync(new[] { 6 });
        var ikinci = await aktarici.AktarAsync(new[] { 6 });

        // Aynı adla yakında duran kayıt atlanıyor: aktarım tekrar
        // çalıştırılabilir olmalı, yoksa "acaba daha önce çalıştırdım mı?"
        // sorusu her seferinde veriyi bozma riski taşırdı.
        Assert.Equal(0, ikinci.Eklenen);
        Assert.Equal(3, ikinci.Atlanan);
        Assert.Equal(3, (await poiler.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Aktarilan_poi_kaynagini_belirtiyor()
    {
        var (aktarici, osm, poiler, _) = Kur();
        MekanEkle(osm);

        await aktarici.AktarAsync(new[] { 6 });

        // ODbL lisansı kaynak göstermeyi şart koşuyor; metin POI popup'ında
        // görünüyor.
        Assert.All(await poiler.GetAllAsync(),
            p => Assert.Contains("OpenStreetMap", p.MesaiSaatleri!));
    }

    [Fact]
    public async Task Aktarim_sehir_merkezinden_ariyor()
    {
        var (aktarici, osm, _, _) = Kur();
        MekanEkle(osm);

        await aktarici.AktarAsync(new[] { 6 });

        // İL SINIRININ ORTASI DEĞİL: kare ilin iç noktası (32.85, 39.95)
        // olurdu; beklenen Kızılay (32.86, 39.93). Bu ayrım, Polatlı'daki
        // müzenin Ankara turuna girmesini engelleyen şey.
        Assert.All(osm.Aramalar, a =>
        {
            Assert.Equal(39.9334, a.Lat, 3);
            Assert.Equal(32.8597, a.Lon, 3);
        });
    }

    // ------------------------------------------------------------------
    //  Yeme-içme süzgeci — canlı denemenin sonucu
    // ------------------------------------------------------------------

    /// <summary>Etiketli bir yeme-içme mekanı ekler.</summary>
    private static void YemeIcmeEkle(
        FakePlacesClient osm, string id, string ad, string tip,
        Dictionary<string, string>? etiketler = null)
    {
        osm.Ekle(id, ad, 39.93, 32.86, null, 0, tip, onem: 0.4, etiketler: etiketler);
    }

    [Fact]
    public async Task Zincir_subeleri_aktarilmiyor()
    {
        var (aktarici, osm, poiler, _) = Kur();

        // Canlıda öneriye sürekli Starbucks düşüyordu: şehir merkezinde
        // onlarca şubesi var ve her biri ayrı kayıt.
        YemeIcmeEkle(osm, "z1", "Starbucks Kızılay", "cafe",
            new() { ["brand:wikidata"] = "Q37158", ["cuisine"] = "coffee_shop" });
        YemeIcmeEkle(osm, "z2", "Burger King Tunalı", "restaurant",
            new() { ["cuisine"] = "burger" });

        // Marka etiketi girilmemiş bir şube: ad listesi ikinci hat.
        YemeIcmeEkle(osm, "z3", "Domino's Pizza Çankaya", "restaurant",
            new() { ["cuisine"] = "pizza" });

        var sonuc = await aktarici.AktarAsync(new[] { 6 });

        Assert.Equal(0, sonuc.Eklenen);
        Assert.Empty(await poiler.GetAllAsync());
    }

    [Fact]
    public async Task Yoresel_lezzetler_aktariliyor()
    {
        var (aktarici, osm, poiler, _) = Kur();

        YemeIcmeEkle(osm, "y1", "Kebapçı Halil Usta", "restaurant",
            new() { ["cuisine"] = "kebab" });
        YemeIcmeEkle(osm, "y2", "Tarihi Çiğdem Pastanesi", "cafe",
            new() { ["wikidata"] = "Q123456" });

        var sonuc = await aktarici.AktarAsync(new[] { 6 });

        Assert.Equal(2, sonuc.Eklenen);

        // Gerçek mekanlar seed'in SENTETİK "Restoran"/"Kafe" kategorilerine
        // karışmıyor; kullanıcı hangisinin gerçek olduğunu ayırt edebilmeli.
        var adlar = (await poiler.GetAllAsync()).Select(p => p.Isim).ToList();
        Assert.Contains("Kebapçı Halil Usta", adlar);
        Assert.Equal(1, sonuc.KategoriBazinda["Yöresel Lezzet"]);
        Assert.Equal(1, sonuc.KategoriBazinda["Kahve & Tatlı"]);
    }

    [Fact]
    public async Task Sinyalsiz_yeme_icme_mekani_aktarilmiyor()
    {
        var (aktarici, osm, poiler, _) = Kur();

        // Ne mutfağı yazılmış ne ansiklopedi maddesi var: şehirdeki her büfeyi
        // POI yapmak listeyi işe yaramaz hâle getirirdi.
        YemeIcmeEkle(osm, "b1", "Büfe", "restaurant");

        var sonuc = await aktarici.AktarAsync(new[] { 6 });

        Assert.Equal(0, sonuc.Eklenen);
        Assert.Equal(1, sonuc.Atlanan);
    }

    [Fact]
    public async Task Turistik_yerlere_kalite_suzgeci_UYGULANMIYOR()
    {
        var (aktarici, osm, _, _) = Kur();

        // Müze/anıt/parkta etiket aranmıyor: adı olan her kayıt alınıyor.
        osm.Ekle("m9", "Küçük Mahalle Müzesi", 39.93, 32.86, null, 0, "museum", onem: 0.3);

        var sonuc = await aktarici.AktarAsync(new[] { 6 });

        Assert.Equal(1, sonuc.Eklenen);
    }

    [Fact]
    public async Task Sonuc_alinamayan_sehir_uyari_uretiyor()
    {
        var (aktarici, osm, _, _) = Kur();
        osm.BosDon = true;

        var sonuc = await aktarici.AktarAsync(new[] { 6 });

        Assert.Equal(0, sonuc.Eklenen);
        Assert.Contains(sonuc.Uyarilar, u => u.Contains("Ankara"));
    }

    [Fact]
    public async Task Kaynak_kapaliysa_anlamli_hata_veriyor()
    {
        var (aktarici, osm, _, _) = Kur();
        osm.Etkin = false;

        var hata = await Assert.ThrowsAsync<DisServisException>(
            () => aktarici.AktarAsync(new[] { 6 }));

        Assert.Contains("OpenStreetMap", hata.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(82)]
    public async Task Gecersiz_plaka_reddediliyor(int plaka)
    {
        var (aktarici, osm, _, _) = Kur();
        MekanEkle(osm);

        await Assert.ThrowsAsync<IsKuraliException>(() => aktarici.AktarAsync(new[] { plaka }));
    }

    [Fact]
    public async Task Bos_liste_reddediliyor()
    {
        var (aktarici, _, _, _) = Kur();

        await Assert.ThrowsAsync<IsKuraliException>(() => aktarici.AktarAsync(Array.Empty<int>()));
    }
}
