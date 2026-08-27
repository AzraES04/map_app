using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Mesai;
using StajProject.Business.Poiler;
using StajProject.Business.Services;
using StajProject.Business.Validation;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 13 testleri.
///
/// Dört madde, üç sınanabilir alan:
///   Madde 2 → POI arama (PoiService.AraAsync)
///   Madde 3 → gün gün mesai planı + resmî tatiller (MesaiPlani, ResmiTatiller)
///   Madde 4 → yer türü/adından kategori önerisi (KategoriEsleme, KategoriOnerAsync)
///
/// Madde 1 (GeoServer katmanı ve SLD stilleri) burada YOK: karşılığı bir
/// GeoServer yapılandırmasıdır, birim testiyle değil ayakta bir GeoServer'la
/// doğrulanır (gs-yapilandir.ps1 sonunda WFS ile kendini denetliyor). Bu
/// dosyada sınanabilen kısmı — POI okumasının repository arayüzü arkasında
/// kalması — GeoServerTests'teki desenle zaten korunuyor.
/// </summary>
public class Odev13Tests
{
    private const int Ayse = 1;

    private sealed record Ortam(
        SahteVeritabani Db,
        PoiService Poi,
        PoiCategoryService Kategori);

    private static Ortam Kur()
    {
        var db = new SahteVeritabani();
        db.Kullanicilar.Add(new User { Id = db.SonrakiKullaniciId(), Username = "ayse" });

        var yetki = db.Yetkiler.FirstOrDefault(y => y.Name == Yetkiler.PoiEkleme)
                    ?? db.YetkiEkle(Yetkiler.PoiEkleme);
        db.KullaniciYetkileri.Add(new UserPermission { UserId = Ayse, PermissionId = yetki.Id });

        var oturum = new FakeCurrentUserService(Ayse);
        var poiRepo = new FakePoiRepository(db);
        var kategoriRepo = new FakePoiCategoryRepository(db);
        var izinler = new PermissionService(
            new FakePermissionRepository(db), new FakeUserRepository(db), oturum);

        return new Ortam(
            db,
            new PoiService(poiRepo, kategoriRepo, oturum, izinler, new FakeGeoPermissionService()),
            new PoiCategoryService(kategoriRepo, poiRepo));
    }

    /// <summary>Seed'dekine benzer bir ağaç kurar: Eğitim › Kütüphane, Sağlık › Eczane.</summary>
    private static async Task<(int Kutuphane, int Eczane, int Egitim)> AgacKur(Ortam o)
    {
        var egitim = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Eğitim" });
        var kutuphane = await o.Kategori.CreateAsync(
            new PoiKategoriSaveDto { Ad = "Kütüphane", ParentId = egitim.Id });

        var saglik = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Sağlık" });
        var eczane = await o.Kategori.CreateAsync(
            new PoiKategoriSaveDto { Ad = "Eczane", ParentId = saglik.Id });

        return (kutuphane.Id, eczane.Id, egitim.Id);
    }

    // ==================================================================
    //  Madde 3 — mesai planı
    // ==================================================================

    [Fact]
    public void MesaiPlani_EksikGunler_KapaliOlarakTamamlaniyor()
    {
        // İstemci yalnızca iki gün göndermiş olsun. Sonradan okuyan hiçbir
        // kodun "acaba yedi gün var mı?" diye kontrol etmesi gerekmemeli.
        var plan = new MesaiPlani
        {
            Tip = MesaiTipi.Haftalik,
            Gunler = new List<MesaiGunu>
            {
                new() { Gun = 1, Acik = true, Acilis = "09:00", Kapanis = "18:00" },
                new() { Gun = 6, Acik = true, Acilis = "10:00", Kapanis = "14:00" },
            },
        }.Dogrula();

        Assert.Equal(7, plan.Gunler.Count);
        Assert.All(plan.Gunler.Select((g, i) => (g, i)), x => Assert.Equal(x.i + 1, x.g.Gun));
        Assert.True(plan.Gunler[0].Acik);
        Assert.False(plan.Gunler[1].Acik);      // salı gönderilmedi → kapalı
        Assert.Null(plan.Gunler[1].Acilis);     // kapalı günde saat taşınmıyor
    }

    [Fact]
    public void MesaiPlani_KapanisAcilistanOnceyse_Reddediliyor()
    {
        var plan = new MesaiPlani
        {
            Tip = MesaiTipi.Haftalik,
            Gunler = new List<MesaiGunu>
            {
                new() { Gun = 3, Acik = true, Acilis = "18:00", Kapanis = "09:00" },
            },
        };

        var hata = Assert.Throws<IsKuraliException>(() => plan.Dogrula());
        Assert.Contains("Çar", hata.Message);
    }

    [Fact]
    public void MesaiPlani_AcikGunde_SaatBossaReddediliyor()
    {
        var plan = new MesaiPlani
        {
            Tip = MesaiTipi.Haftalik,
            Gunler = new List<MesaiGunu> { new() { Gun = 1, Acik = true } },
        };

        Assert.Throws<IsKuraliException>(() => plan.Dogrula());
    }

    [Fact]
    public void MesaiPlani_BilinmeyenTip_Reddediliyor()
    {
        var plan = new MesaiPlani { Tip = "her-zaman" };
        Assert.Throws<IsKuraliException>(() => plan.Dogrula());
    }

    [Fact]
    public void MesaiPlani_Saat_TekHaneliGirilse_BicimeOturtuluyor()
    {
        var plan = new MesaiPlani
        {
            Tip = MesaiTipi.Haftalik,
            Gunler = new List<MesaiGunu>
            {
                new() { Gun = 1, Acik = true, Acilis = "9:05", Kapanis = "18:00" },
            },
        }.Dogrula();

        Assert.Equal("09:05", plan.Gunler[0].Acilis);
    }

    [Fact]
    public void OzetMetin_ArdisikAyniGunleri_TekGruptaTopluyor()
    {
        var plan = MesaiPlani.Varsayilan().Dogrula();   // Pzt–Cum 09:00–18:00, hafta sonu kapalı

        // Yedi günü tek tek yazmak yerine aralık: liste okunur kalsın.
        Assert.Equal("Pzt-Cum 09:00-18:00 · Cmt-Paz kapalı", plan.OzetMetin());
    }

    [Fact]
    public void OzetMetin_HaftaSonuFarkliysa_AyriGrupOluyor()
    {
        var plan = MesaiPlani.Varsayilan();
        plan.Gunler[5] = new MesaiGunu { Gun = 6, Acik = true, Acilis = "10:00", Kapanis = "14:00" };

        Assert.Equal(
            "Pzt-Cum 09:00-18:00 · Cmt 10:00-14:00 · Paz kapalı",
            plan.Dogrula().OzetMetin());
    }

    [Fact]
    public void OzetMetin_TumGunlerKapaliysa_Kapali()
    {
        var plan = new MesaiPlani { Tip = MesaiTipi.Haftalik }.Dogrula();
        Assert.Equal("Kapalı", plan.OzetMetin());
    }

    [Fact]
    public void ResmiKurumKipi_TatilKapaliyi_ZorunluKiliyor()
    {
        // Kullanıcı bayrağı kapatmaya çalışsa bile kip onu geri açıyor:
        // "resmî tatilde açık resmî kurum" diye bir şey yok.
        var plan = new MesaiPlani
        {
            Tip = MesaiTipi.ResmiKurum,
            ResmiTatilKapali = false,
            Gunler = MesaiPlani.ResmiKurum().Gunler,
        }.Dogrula();

        Assert.True(plan.ResmiTatilKapali);
        Assert.Contains("resmî tatillerde kapalı", plan.OzetMetin());
        // Hafta sonu kapalı, hafta içi 08:00–17:00
        Assert.Equal("08:00", plan.Gunler[0].Acilis);
        Assert.False(plan.Gunler[6].Acik);
    }

    [Fact]
    public void MesaiPlani_JsonTuruDonusu_BilgiKaybetmiyor()
    {
        var once = MesaiPlani.ResmiKurum().Dogrula();
        var sonra = MesaiPlani.Coz(once.Serilestir());

        Assert.NotNull(sonra);
        Assert.Equal(once.Tip, sonra!.Tip);
        Assert.True(sonra.ResmiTatilKapali);
        Assert.Equal(once.OzetMetin(), sonra.OzetMetin());
    }

    [Fact]
    public void MesaiPlani_BozukJson_CokmuyorNullDonuyor()
    {
        // Kolon elle değiştirilmiş olabilir. Tek bir bozuk kayıt yüzünden
        // bütün POI listesinin 500 vermesi orantısız olurdu.
        Assert.Null(MesaiPlani.Coz("{ bu json degil"));
        Assert.Null(MesaiPlani.Coz(null));
    }

    [Theory]
    [InlineData("7/24", MesaiTipi.Surekli)]
    [InlineData("09:00 - 18:00", MesaiTipi.Haftalik)]
    public void EskiMetin_Odev12Bicimleri_PlanaCevriliyor(string metin, string beklenenTip)
    {
        var plan = MesaiPlani.EskiMetindenCoz(metin);

        Assert.NotNull(plan);
        Assert.Equal(beklenenTip, plan!.Tip);
    }

    [Fact]
    public void EskiMetin_TaninmayanBicim_NullDonuyor()
    {
        // "Hafta içi 09:00-18:00, Cumartesi 10:00-14:00" gibi serbest bir metni
        // tahminle plana çevirmek, kullanıcının girmediği bir bilgiyi uydurmak
        // olurdu. Metin olduğu gibi duruyor, plan yok.
        Assert.Null(MesaiPlani.EskiMetindenCoz("Hafta içi 09:00-18:00, Cmt yarım gün"));
    }

    // ==================================================================
    //  Madde 3 — resmî tatiller
    // ==================================================================

    [Fact]
    public void ResmiTatiller_SabitTatiller_HerYilVar()
    {
        var tatiller = ResmiTatiller.Yil(2026);

        Assert.Contains(tatiller, t => t.Tarih == new DateOnly(2026, 10, 29) && !t.YarimGun);
        Assert.Contains(tatiller, t => t.Tarih == new DateOnly(2026, 4, 23));
        Assert.Contains(tatiller, t => t.Tarih == new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void ResmiTatiller_28Ekim_YarimGun()
    {
        // Kanunda "13.00'ten itibaren": tam gün kapalı saymak yanlış olurdu.
        var yirmiSekiz = ResmiTatiller.Yil(2026).Single(t => t.Tarih == new DateOnly(2026, 10, 28));
        Assert.True(yirmiSekiz.YarimGun);
    }

    [Fact]
    public void ResmiTatiller_DiniBayram_TanimliYildaArifeVeGunleriIceriyor()
    {
        var tatiller = ResmiTatiller.Yil(2026);

        Assert.True(ResmiTatiller.DiniBayramTablosuVarMi(2026));
        // Ramazan Bayramı 2026: 19 Mart arife (yarım gün) + 20-21-22 Mart
        Assert.True(tatiller.Single(t => t.Tarih == new DateOnly(2026, 3, 19)).YarimGun);
        Assert.Contains(tatiller, t => t.Tarih == new DateOnly(2026, 3, 20) && !t.YarimGun);
        Assert.Contains(tatiller, t => t.Tarih == new DateOnly(2026, 3, 22));
    }

    [Fact]
    public void ResmiTatiller_TanimsizYil_YalnizcaSabitTatilleriDonuyor()
    {
        // Hicrî takvime bağlı tarihler elle tutuluyor. Tanımlı olmayan bir yıl
        // için EKSİK listeyi tam listeymiş gibi sunmuyoruz; bayrak arayüze
        // "bu yılın dinî bayramları tanımlı değil" dedirtiyor.
        const int uzakYil = 2040;

        Assert.False(ResmiTatiller.DiniBayramTablosuVarMi(uzakYil));

        var tatiller = ResmiTatiller.Yil(uzakYil);

        // Sabit tatiller yerinde (bir kısmının adında "Bayramı" geçiyor:
        // Zafer Bayramı, Cumhuriyet Bayramı…)
        Assert.Contains(tatiller, t => t.Tarih == new DateOnly(uzakYil, 10, 29));
        Assert.Equal(8, tatiller.Count);   // yalnızca SabitTatiller listesi kadar

        // Kayan tarihli DİNÎ bayramlar yok.
        Assert.DoesNotContain(tatiller, t => t.Ad.StartsWith("Ramazan"));
        Assert.DoesNotContain(tatiller, t => t.Ad.StartsWith("Kurban"));
    }

    [Fact]
    public void ResmiTatiller_TarihSiraliDonuyor()
    {
        var tatiller = ResmiTatiller.Yil(2026);
        Assert.Equal(tatiller.OrderBy(t => t.Tarih).ToList(), tatiller);
    }

    // ==================================================================
    //  Madde 4 — kategori önerisi
    // ==================================================================

    [Theory]
    [InlineData("library", "amenity", "Millî Kütüphane", "Eğitim", "Kütüphane")]
    [InlineData("pharmacy", "amenity", "Tunalı", "Sağlık", "Eczane")]
    [InlineData("hotel", "tourism", "Ankara Otel", "Konaklama", "Otel")]
    [InlineData("restaurant", "amenity", "Bir Yer", "Yeme-İçme", "Restoran")]
    public void KategoriEsleme_OsmTuru_YolaCevriliyor(
        string tur, string sinif, string isim, string kok, string alt)
    {
        var oneri = KategoriEsleme.Oner(tur, sinif, isim);

        Assert.NotNull(oneri);
        Assert.Equal(kok, oneri!.Kok);
        Assert.Equal(alt, oneri.Alt);
    }

    [Fact]
    public void KategoriEsleme_TurYoksa_AdaBakiyor()
    {
        // Ödev metnindeki örnek: tür etiketi olmasa bile ad yeterli.
        var oneri = KategoriEsleme.Oner(null, null, "Millî Kütüphane");

        Assert.NotNull(oneri);
        Assert.Equal("Eğitim", oneri!.Kok);
        Assert.Equal("Kütüphane", oneri.Alt);
    }

    [Theory]
    [InlineData("MİLLÎ KÜTÜPHANE")]
    [InlineData("milli kutuphane")]
    [InlineData("Milli Kütüphane Şubesi")]
    public void KategoriEsleme_TurkceHarfler_EslesmeyiBozmuyor(string isim)
    {
        // "I / ı" tuzağı: ToLowerInvariant tek başına yetmiyordu, harfler
        // elle katlanıyor (bkz. KategoriEsleme.Sadelestir).
        Assert.NotNull(KategoriEsleme.Oner(null, null, isim));
    }

    [Fact]
    public void KategoriEsleme_TanimadigiYer_OneriUretmiyor()
    {
        // Rastgele bir kategori atamak, yanlış veriyi sessizce kaydetmenin
        // en kolay yolu olurdu.
        Assert.Null(KategoriEsleme.Oner("bus_stop", "highway", "Kızılay Durağı"));
    }

    [Fact]
    public async Task KategoriOner_AgactakiKarsiligiyla_AtaCocukDonuyor()
    {
        var o = Kur();
        var (kutuphaneId, _, egitimId) = await AgacKur(o);

        var oneri = await o.Poi.KategoriOnerAsync("library", "amenity", "Millî Kütüphane");

        Assert.NotNull(oneri);
        Assert.Equal(kutuphaneId, oneri!.KategoriId);
        Assert.Equal("Kütüphane", oneri.KategoriAdi);
        Assert.Equal(egitimId, oneri.ParentId);
        Assert.Equal("Eğitim", oneri.ParentAdi);
        Assert.Equal($"Eğitim{PoiCategoryService.YolAyraci}Kütüphane", oneri.TamYol);
    }

    [Fact]
    public async Task KategoriOner_AgacBoşsa_OneriYok()
    {
        // Sözlük sabit, ağaç ise yöneticinin elinde. Karşılığı olmayan bir
        // öneri döndürseydik arayüz var olmayan bir kategoriyi seçmeye
        // çalışırdı.
        var o = Kur();
        Assert.Null(await o.Poi.KategoriOnerAsync("library", "amenity", "Millî Kütüphane"));
    }

    [Fact]
    public async Task KategoriOner_AltKategoriYokAmaKokVarsa_KokOneriliyor()
    {
        var o = Kur();
        var egitim = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Eğitim" });

        var oneri = await o.Poi.KategoriOnerAsync("library", "amenity", "Millî Kütüphane");

        Assert.NotNull(oneri);
        Assert.Equal(egitim.Id, oneri!.KategoriId);
        Assert.Null(oneri.ParentId);   // öneri zaten kök
    }

    [Fact]
    public async Task KategoriOner_PasifKategoriyi_Onermiyor()
    {
        // Pasif kategoriye POI eklenemiyor (PoiService.KategoriyiDogrulaAsync);
        // önerseydik kaydetmede reddedilen bir form üretirdik.
        var o = Kur();
        var egitim = await o.Kategori.CreateAsync(new PoiKategoriSaveDto { Ad = "Eğitim" });
        await o.Kategori.CreateAsync(new PoiKategoriSaveDto
        {
            Ad = "Kütüphane", ParentId = egitim.Id, IsActive = false,
        });

        var oneri = await o.Poi.KategoriOnerAsync("library", "amenity", "Millî Kütüphane");

        // Alt kategori pasif → köke düşüyor, pasif kategori dönmüyor.
        Assert.NotNull(oneri);
        Assert.Equal(egitim.Id, oneri!.KategoriId);
    }

    // ==================================================================
    //  Madde 2 — POI arama
    // ==================================================================

    private static async Task<int> PoiEkle(Ortam o, int kategoriId, string isim)
    {
        var olusan = await o.Poi.CreateAsync(new PoiCreateDto
        {
            Isim = isim,
            KategoriId = kategoriId,
            Wkt = "POINT (5 5)",
            MesaiPlani = MesaiPlani.Varsayilan(),
        });

        return olusan.Id;
    }

    [Fact]
    public async Task Ara_AdinIcindeGecenler_Donuyor()
    {
        var o = Kur();
        var (kutuphaneId, eczaneId, _) = await AgacKur(o);

        await PoiEkle(o, kutuphaneId, "Millî Kütüphane");
        await PoiEkle(o, eczaneId, "Tunalı Eczanesi");

        var sonuc = await o.Poi.AraAsync("kütüp");

        var tek = Assert.Single(sonuc);
        Assert.Equal("Millî Kütüphane", tek.Isim);
        // Arama sonucu kategori YOLUNU ve KÖKÜNÜ taşıyor: arayüz satırı
        // haritadaki simgeyle aynı renkte gösteriyor.
        Assert.Equal($"Eğitim{PoiCategoryService.YolAyraci}Kütüphane", tek.KategoriYolu);
        Assert.Equal("Eğitim", tek.KokKategori);
        Assert.Equal("POINT (5 5)", tek.Wkt);
    }

    [Fact]
    public async Task Ara_BuyukKucukHarf_Gozetmiyor()
    {
        var o = Kur();
        var (kutuphaneId, _, _) = await AgacKur(o);
        await PoiEkle(o, kutuphaneId, "Millî Kütüphane");

        Assert.Single(await o.Poi.AraAsync("KÜTÜPHANE"));
        Assert.Single(await o.Poi.AraAsync("kütüphane"));
        Assert.Single(await o.Poi.AraAsync("KüTüPhAnE"));
    }

    // NOT — "İ / I" ÇİFTİ SINANMIYOR, bilerek.
    //
    // "MİLLÎ" yazıp "Millî" kaydını bulmak kulağa doğal gelir ama garanti
    // edilebilir bir davranış değil: eşleştirmeyi VERİTABANI yapıyor ve
    // PostgreSQL'in büyük/küçük harf katlaması kolonun harmanlama (collation)
    // ayarına bağlı. tr_TR harmanlamasında "İ" → "i" olur, varsayılan
    // en_US.UTF-8'de ise "i" + birleşen nokta çıkar ve eşleşmez.
    //
    // Testin sahte depoda geçmesi, üretimde geçeceği anlamına gelmezdi —
    // geçmeyeceği bir davranışı "doğrulanmış" göstermek en kötü test türüdür.
    // Gerçekten gerekirse çözüm veritabanı tarafında: kolona tr-TR-x-icu
    // harmanlaması vermek ya da aramayı unaccent/normalize edilmiş bir
    // ifade üzerinden yapmak.

    [Fact]
    public async Task Ara_CokKisaSorgu_BosDonuyor()
    {
        // Tek harf neredeyse bütün tabloyla eşleşir; kullanıcıya faydası
        // olmayan bir liste için sunucuyu meşgul etmenin anlamı yok.
        var o = Kur();
        var (kutuphaneId, _, _) = await AgacKur(o);
        await PoiEkle(o, kutuphaneId, "Millî Kütüphane");

        Assert.Empty(await o.Poi.AraAsync("M"));
        Assert.Empty(await o.Poi.AraAsync(null));
        Assert.Empty(await o.Poi.AraAsync("   "));
    }

    [Fact]
    public async Task Ara_KisaAdlar_UzunlarindanOnceGeliyor()
    {
        var o = Kur();
        var (kutuphaneId, _, _) = await AgacKur(o);

        await PoiEkle(o, kutuphaneId, "Beytepe Kampüs Kütüphanesi");
        await PoiEkle(o, kutuphaneId, "Kütüphane");

        var sonuc = await o.Poi.AraAsync("kütüphane");

        Assert.Equal("Kütüphane", sonuc[0].Isim);
    }

    [Fact]
    public async Task Ara_EnFazlaSiniri_UstundekiIstekleriKirpiyor()
    {
        var o = Kur();
        var (kutuphaneId, _, _) = await AgacKur(o);
        for (var i = 0; i < 30; i++) await PoiEkle(o, kutuphaneId, $"Kütüphane {i:00}");

        // İstemci ne isterse istesin arama kutusuna sığmayacak bir liste dönmüyor.
        Assert.Equal(25, (await o.Poi.AraAsync("kütüphane", 1000)).Count);
    }

    // ==================================================================
    //  Madde 3 — planın kayda yansıması
    // ==================================================================

    [Fact]
    public async Task Kaydet_PlanVerildiginde_OzetMetinPlandanUretiliyor()
    {
        var o = Kur();
        var (kutuphaneId, _, _) = await AgacKur(o);

        var olusan = await o.Poi.CreateAsync(new PoiCreateDto
        {
            Isim = "Millî Kütüphane",
            KategoriId = kutuphaneId,
            Wkt = "POINT (5 5)",
            // İstemcinin gönderdiği metin BİLEREK planla çelişiyor.
            MesaiSaatleri = "7/24",
            MesaiPlani = MesaiPlani.ResmiKurum(),
        });

        // Tek kaynak plan: metin yok sayılıyor, özet plandan üretiliyor.
        Assert.Equal("Pzt-Cum 08:00-17:00 · Cmt-Paz kapalı · resmî tatillerde kapalı",
                     olusan.MesaiSaatleri);
        Assert.NotNull(olusan.MesaiPlani);
        Assert.Equal(MesaiTipi.ResmiKurum, olusan.MesaiPlani!.Tip);
        Assert.True(olusan.MesaiPlani.ResmiTatilKapali);
    }

    [Fact]
    public async Task Kaydet_PlanYoksa_Odev12Davranisi_MetinSerbest()
    {
        // Plan göndermeyen bir istemci (ya da Swagger'dan elle atılan istek)
        // çalışmaya devam ediyor.
        var o = Kur();
        var (kutuphaneId, _, _) = await AgacKur(o);

        var olusan = await o.Poi.CreateAsync(new PoiCreateDto
        {
            Isim = "Eski Kayıt",
            KategoriId = kutuphaneId,
            Wkt = "POINT (5 5)",
            MesaiSaatleri = "  Hafta içi 09:00-18:00  ",
        });

        Assert.Equal("Hafta içi 09:00-18:00", olusan.MesaiSaatleri);
    }

    [Fact]
    public async Task Guncelle_Plan_VeOzetBirlikteDegisiyor()
    {
        var o = Kur();
        var (kutuphaneId, _, _) = await AgacKur(o);
        var id = await PoiEkle(o, kutuphaneId, "Millî Kütüphane");

        var guncel = await o.Poi.UpdateAsync(id, new PoiUpdateDto
        {
            Isim = "Millî Kütüphane",
            KategoriId = kutuphaneId,
            MesaiPlani = MesaiPlani.ResmiKurum(),
        });

        Assert.NotNull(guncel);
        Assert.Contains("resmî tatillerde kapalı", guncel!.MesaiSaatleri);
        Assert.Equal(MesaiTipi.ResmiKurum, guncel.MesaiPlani!.Tip);
    }

    [Fact]
    public async Task Okuma_PlaniOlmayanEskiKayit_MetindenPlanTuretiyor()
    {
        // Ödev 12'den kalan kayıtlar düzenleme formunda boş açılmamalı;
        // açılsaydı kullanıcı kaydettiğinde mevcut mesai sessizce silinirdi.
        var o = Kur();
        var (kutuphaneId, _, _) = await AgacKur(o);

        o.Db.Poiler.Add(new Poi
        {
            Id = o.Db.SonrakiPoiId(),
            Isim = "Ödev 12 Kaydı",
            KategoriId = kutuphaneId,
            MesaiSaatleri = "09:00 - 18:00",
            MesaiPlani = null,
            Geom = new NetTopologySuite.Geometries.Point(5, 5) { SRID = 4326 },
            UserId = Ayse,
        });

        var dto = (await o.Poi.GetAllAsync()).Single(p => p.Isim == "Ödev 12 Kaydı");

        Assert.NotNull(dto.MesaiPlani);
        Assert.Equal(MesaiTipi.Haftalik, dto.MesaiPlani!.Tip);
        Assert.All(dto.MesaiPlani.Gunler, g => Assert.True(g.Acik));
    }

    [Fact]
    public async Task Kaydet_GecersizPlan_400UretenIsKuraliHatasi()
    {
        var o = Kur();
        var (kutuphaneId, _, _) = await AgacKur(o);

        var bozuk = new MesaiPlani
        {
            Tip = MesaiTipi.Haftalik,
            Gunler = new List<MesaiGunu>
            {
                new() { Gun = 1, Acik = true, Acilis = "18:00", Kapanis = "09:00" },
            },
        };

        await Assert.ThrowsAsync<IsKuraliException>(() => o.Poi.CreateAsync(new PoiCreateDto
        {
            Isim = "Bozuk", KategoriId = kutuphaneId, Wkt = "POINT (5 5)", MesaiPlani = bozuk,
        }));
    }

    // ==================================================================
    //  Madde 3 — tatil listesi ucunun sözleşmesi
    // ==================================================================

    [Fact]
    public void ResmiTatilleriGetir_YilVerilmezse_IcindeBulunulanYil()
    {
        var o = Kur();
        var liste = o.Poi.ResmiTatilleriGetir(null);

        Assert.Equal(DateTime.UtcNow.Year, liste.Yil);
        Assert.NotEmpty(liste.Tatiller);
        // Tarihler ISO biçiminde: arayüz Date ile ayrıştırıyor.
        Assert.All(liste.Tatiller, t => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", t.Tarih));
    }

    [Fact]
    public void ResmiTatilleriGetir_TanimsizYil_BayrakDusuyor()
    {
        var o = Kur();
        Assert.False(o.Poi.ResmiTatilleriGetir(2040).DiniBayramlarTanimli);
        Assert.True(o.Poi.ResmiTatilleriGetir(2026).DiniBayramlarTanimli);
    }
}
