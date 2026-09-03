using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.Business.Tur;
using StajProject.Business.Validation;
using StajProject.DataAccess.Google;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

// ============================================================================
//  TUR ROTA ÖNERİSİ — Google Places + Directions
//
//  Bu dosyanın sorduğu üç soru var ve üçü de gözle görülmeyen cinsten:
//
//    1. KOTA: bir öneri kaç dış istek harcıyor? Sayı, bulunan mekan sayısına
//       göre BÜYÜMEMELİ. Bu kural bozulduğunda hiçbir şey "hata" vermez —
//       yalnızca fatura büyür, üstelik aylar sonra fark edilir.
//
//    2. ÖDEVİN KURALLARI: 4.5 altı elenmeli, her durağa kalış süresi
//       atanmalı, toplam süre AŞILMAMALI.
//
//    3. SIRA: Google'ın optimize ettiği sıra doğru uygulanmalı. Yanlış
//       eşlenen bir sıra, durakları birbirinin koordinatına oturtan sessiz
//       bir hataya dönüşür.
// ============================================================================

public class TurPlanlamaTests
{
    /// <summary>Ankara'yı temsil eden kare il — merkezi (32.85, 39.93) civarı.</summary>
    private const string AnkaraKaresi =
        "POLYGON ((32.6 39.8, 33.1 39.8, 33.1 40.1, 32.6 40.1, 32.6 39.8))";

    private static (TurPlanlamaServisi servis, FakePlacesClient places,
                    FakeDirectionsClient directions, FakeIstekButcesi butce)
        Kur(GoogleMapsSettings? ayarlar = null)
    {
        ayarlar ??= new GoogleMapsSettings { Enabled = true, ApiKey = "test" };

        var butce = new FakeIstekButcesi();
        var places = new FakePlacesClient { Butce = butce };
        var directions = new FakeDirectionsClient { Butce = butce };
        var iller = new FakeIlRepository().Ekle(6, "Ankara", AnkaraKaresi);

        // POI depoları BOŞ: yerel yedek yalnızca Overpass boş döndüğünde
        // devreye giriyor ve buradaki testlerin çoğu sahte Places
        // istemcisiyle dolu bir havuz kuruyor. Yedeğin kendi testleri
        // (Overpass_bos_donunce_*) depoyu ayrıca dolduruyor.
        var servis = new TurPlanlamaServisi(
            places, directions, iller,
            new FakePoiRepository(), new FakePoiCategoryRepository(),
            butce, ayarlar,
            NullLogger<TurPlanlamaServisi>.Instance);

        return (servis, places, directions, butce);
    }

    private static TurRotaIstegiDto Istek(
        int toplamDakika = 240,
        string tema = "Kulturel",
        string ulasim = "Yaya",
        string beslenme = "Yok",
        string? ilce = null)
        => new()
        {
            Lokasyon = new TurLokasyonDto { IlPlaka = 6, IlAdi = "Ankara", Ilce = ilce },
            UlasimTipi = ulasim,
            Sure = new TurSureDto { Birim = "Saat", Deger = toplamDakika / 60, ToplamDakika = toplamDakika },
            Tema = tema,
            BeslenmeKisiti = beslenme,
        };

    /// <summary>Havuza, birbirinden ayrı konumlarda N adet müze koyar.</summary>
    private static void MuzeEkle(FakePlacesClient places, int adet, double puan = 4.7, int oy = 1_000)
    {
        for (var i = 0; i < adet; i++)
        {
            places.Ekle($"m{i}", $"Müze {i}", 39.90 + i * 0.01, 32.80 + i * 0.01, puan, oy, "museum");
        }
    }

    // ------------------------------------------------------------------
    //  1 — Kota: istek sayısı mekan sayısıyla BÜYÜMÜYOR
    // ------------------------------------------------------------------

    [Fact]
    public async Task Oneri_sabit_sayida_dis_istek_harciyor()
    {
        var (servis, places, directions, butce) = Kur();
        MuzeEkle(places, 40);   // kırk aday: naif bir gerçeklemede yüzlerce istek

        var sonuc = await servis.RotaOnerAsync(Istek(toplamDakika: 300));

        // Kültürel temada 4 arama tanımlı; ayarlardaki TemaBasinaArama da 4.
        Assert.Equal(4, places.CagriSayisi);

        // Bir sıralama + (gerekirse) bir yeniden sıralama. Üçüncüsü YOK.
        Assert.InRange(directions.CagriSayisi, 1, 2);

        // Toplam maliyet cevapta da raporlanıyor — kota görünür bir sayı olmalı.
        Assert.Equal(butce.BugunKullanilan, sonuc.IstekMaliyeti);
        Assert.InRange(sonuc.IstekMaliyeti, 5, 6);
    }

    [Fact]
    public async Task Aramalar_kaynaga_TEK_cagrida_veriliyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 10);

        await servis.RotaOnerAsync(Istek(toplamDakika: 300));

        // Servis aramaları TOPLU veriyor; kaç isteğe bölüneceğine kaynak karar
        // veriyor. Google dördünü paralel atıyor, Overpass tek sorguda
        // birleştiriyor — bu ayrım olmasaydı anahtarsız kip genel sunucunun
        // eşzamanlılık sınırına takılıp 429 alırdı.
        Assert.Equal(1, places.TopluCagriSayisi);
        Assert.Equal(4, places.Aramalar.Count);
    }

    [Fact]
    public async Task Mekan_sayisi_artinca_istek_sayisi_degismiyor()
    {
        var (servisAz, placesAz, directionsAz, _) = Kur();
        MuzeEkle(placesAz, 5);
        await servisAz.RotaOnerAsync(Istek());

        var (servisCok, placesCok, directionsCok, _) = Kur();
        MuzeEkle(placesCok, 60);
        await servisCok.RotaOnerAsync(Istek());

        // Asıl iddia bu: maliyet, veri büyüklüğünden BAĞIMSIZ.
        Assert.Equal(placesAz.CagriSayisi, placesCok.CagriSayisi);
        Assert.Equal(directionsAz.CagriSayisi, directionsCok.CagriSayisi);
    }

    [Fact]
    public async Task Directions_istegi_ara_nokta_sinirini_asmiyor()
    {
        var ayarlar = new GoogleMapsSettings { Enabled = true, ApiKey = "test", MaxAraNokta = 8 };
        var (servis, places, directions, _) = Kur(ayarlar);
        MuzeEkle(places, 50);

        // Çok uzun bir süre: kapasite hesabı sınırlanmasaydı 50 nokta gönderilirdi
        // ve Google MAX_WAYPOINTS_EXCEEDED dönerdi — faturalanan, işe yaramaz bir istek.
        await servis.RotaOnerAsync(Istek(toplamDakika: 12 * 60));

        Assert.True(directions.SonNoktaSayisi <= ayarlar.MaxAraNokta + 2);
    }

    [Fact]
    public async Task Butce_dolmussa_oneri_uretilmiyor()
    {
        var (servis, places, _, butce) = Kur();
        MuzeEkle(places, 10);
        butce.Tavan = 0;   // günlük tavan dolu

        // Sessizce yarım bir öneri döndürmek yerine görünür biçimde duruyoruz.
        await Assert.ThrowsAsync<DisServisException>(() => servis.RotaOnerAsync(Istek()));
    }

    // ------------------------------------------------------------------
    //  2 — Ödevin kuralları
    // ------------------------------------------------------------------

    [Fact]
    public async Task Puani_dusuk_mekanlar_oneriye_girmiyor()
    {
        var (servis, places, _, _) = Kur();

        places.Ekle("iyi1", "İyi Müze", 39.91, 32.81, 4.8, 2_000, "museum");
        places.Ekle("iyi2", "İyi Müze 2", 39.92, 32.82, 4.6, 900, "museum");
        // Sunucu süzgeci (minRating) atlansa bile servisin kendi kontrolü
        // bunları elemeli:
        places.Ekle("kotu", "Vasat Müze", 39.93, 32.83, 4.1, 5_000, "museum");
        places.Ekle("azoy", "Tek Oylu Müze", 39.94, 32.84, 5.0, 3, "museum");

        var sonuc = await servis.RotaOnerAsync(Istek(toplamDakika: 300));

        var adlar = sonuc.Waypoints.Select(w => w.Name).ToList();
        Assert.Contains("İyi Müze", adlar);
        Assert.DoesNotContain("Vasat Müze", adlar);      // 4.5 altı
        Assert.DoesNotContain("Tek Oylu Müze", adlar);   // yeterli oy yok
    }

    [Fact]
    public async Task Her_duraga_kalis_suresi_atanıyor()
    {
        var (servis, places, _, _) = Kur();
        places.Ekle("m1", "Müze", 39.91, 32.81, 4.8, 1_000, "museum");
        places.Ekle("m2", "Popüler Müze", 39.92, 32.82, 4.9, 20_000, "museum");

        var sonuc = await servis.RotaOnerAsync(Istek(toplamDakika: 300));

        Assert.All(sonuc.Waypoints, w => Assert.True(w.DwellMinutes > 0));

        // Çok ziyaret edilen mekan daha uzun: 45 → 55 (5'e yuvarlanmış %25).
        var populer = sonuc.Waypoints.Single(w => w.Name == "Popüler Müze");
        var normal = sonuc.Waypoints.Single(w => w.Name == "Müze");
        Assert.True(populer.DwellMinutes > normal.DwellMinutes);
        Assert.Equal(TurKatalogu.KalisSuresiDakika(VenueType.Museum, 20_000), populer.DwellMinutes);
    }

    [Fact]
    public async Task Toplam_sure_asilmiyor()
    {
        var (servis, places, directions, _) = Kur();
        MuzeEkle(places, 20);

        // Bacaklar KASITLI olarak uzun (30 dk): kapasite ön hesabı yaya için
        // ortalama 12 dk varsayıyor, yani fazla iyimser kalıyor ve kırpma
        // adımının gerçekten çalışması gerekiyor. Testin sınadığı şey bu.
        directions.BacakSaniye = 1_800;

        var sonuc = await servis.RotaOnerAsync(Istek(toplamDakika: 180));

        var kalis = sonuc.Waypoints.Sum(w => w.DwellMinutes);
        var yol = (int)Math.Round((sonuc.RouteDurationSeconds ?? 0) / 60);

        Assert.True(kalis + yol <= 180, $"Toplam {kalis + yol} dk, bütçe 180 dk.");
        Assert.Equal(kalis + yol, sonuc.EstimatedTotalMinutes);

        // Kırpma yapıldıysa kullanıcı bunu görmeli.
        Assert.Contains(sonuc.Uyarilar, u => u.Contains("çıkarıldı"));
    }

    [Fact]
    public async Task Kisa_sureye_az_durak_uzun_sureye_cok_durak_giriyor()
    {
        var (kisaServis, kisaPlaces, _, _) = Kur();
        MuzeEkle(kisaPlaces, 20);
        var kisa = await kisaServis.RotaOnerAsync(Istek(toplamDakika: 120));

        var (uzunServis, uzunPlaces, _, _) = Kur();
        MuzeEkle(uzunPlaces, 20);
        var uzun = await uzunServis.RotaOnerAsync(Istek(toplamDakika: 600));

        Assert.True(uzun.Waypoints.Count > kisa.Waypoints.Count);
    }

    [Fact]
    public async Task Beslenme_kisiti_yeme_icme_mekanlarina_uygulaniyor()
    {
        var (servis, places, _, _) = Kur();

        // OSM kipi: puan verisi yok, kalite süzgeci devrede.
        places.PuanVerisiVar = false;

        // Mutfak etiketi ŞART: kalite süzgeci artık tur önerisinde de
        // çalışıyor, sinyalsiz mekan hiç girmiyor (bkz. MekanKalitesi).
        var yoresel = new Dictionary<string, string> { ["cuisine"] = "turkish" };

        // Vejetaryen seçeneği OLMADIĞI bilinen restoran → elenmeli.
        places.Ekle("r1", "Etçi Lokanta", 39.91, 32.81, null, 0, "restaurant",
            vejetaryen: false, etiketler: yoresel);
        places.Ekle("r2", "Yeşil Lokanta", 39.92, 32.82, null, 0, "restaurant",
            vejetaryen: true, etiketler: yoresel);
        // Bilinmiyor (null) → ELENMİYOR: etiketi girilmemiş her yeri atmak
        // küçük şehirlerde listeyi boşaltırdı.
        places.Ekle("r3", "Bilinmeyen Lokanta", 39.93, 32.83, null, 0, "restaurant",
            etiketler: yoresel);

        var sonuc = await servis.RotaOnerAsync(
            Istek(toplamDakika: 600, tema: "Gastronomi", beslenme: "Vejetaryen"));

        var adlar = sonuc.Waypoints.Select(w => w.Name).ToList();
        Assert.DoesNotContain("Etçi Lokanta", adlar);
        Assert.Contains("Yeşil Lokanta", adlar);
        Assert.Contains("Bilinmeyen Lokanta", adlar);

        // Kısıt arama METNİNE de giriyor (Places'te vegan süzgeci yok).
        Assert.Contains(places.Aramalar, a => a.Sorgu.Contains("vejetaryen"));
    }

    [Fact]
    public async Task Kalitesiz_yeme_icme_mekanlari_ONERIYE_girmiyor()
    {
        // CANLIDA ÇIKAN HATA: iki günlük Ankara turuna "Melek Cafem" ve
        // "476. Yurt Kantini" düştü. Kural POI aktarımında vardı ama öneri
        // onu hiç çağırmıyordu.
        var (servis, places, _, _) = Kur();
        places.PuanVerisiVar = false;

        places.Ekle("k1", "476. Yurt Kantini", 39.91, 32.81, null, 0, "cafe");
        places.Ekle("k2", "Melek Cafem", 39.92, 32.82, null, 0, "cafe");
        places.Ekle("k3", "Starbucks Kızılay", 39.93, 32.83, null, 0, "cafe",
            etiketler: new() { ["brand:wikidata"] = "Q37158" });
        places.Ekle("iyi1", "Çorbacı Hasan Usta", 39.94, 32.84, null, 0, "restaurant",
            etiketler: new() { ["cuisine"] = "turkish" });
        places.Ekle("iyi2", "Trilye Restoran", 39.95, 32.85, null, 0, "restaurant",
            etiketler: new() { ["cuisine"] = "seafood" });

        var sonuc = await servis.RotaOnerAsync(Istek(toplamDakika: 600, tema: "Gastronomi"));

        var adlar = sonuc.Waypoints.Select(w => w.Name).ToList();
        Assert.DoesNotContain("476. Yurt Kantini", adlar);
        Assert.DoesNotContain("Melek Cafem", adlar);
        Assert.DoesNotContain("Starbucks Kızılay", adlar);
        Assert.Contains("Çorbacı Hasan Usta", adlar);
        Assert.Contains("Trilye Restoran", adlar);
    }

    [Fact]
    public void Gezi_temalarinda_yeme_icme_ARAMASI_yok()
    {
        // Şehir turuna rastgele kafe konmaz: "Karma" artık kültür + inanç +
        // doğa demek, yeme-içme isteyenin teması Gastronomi.
        foreach (var tema in new[] { "Karma", "Populer", "Kulturel", "Doga" })
        {
            var tipler = TurKatalogu.TemaAramalari(tema).Select(a => a.GoogleTipi).ToList();

            Assert.DoesNotContain("cafe", tipler);
            Assert.DoesNotContain("restaurant", tipler);
        }

        Assert.Contains("restaurant", TurKatalogu.TemaAramalari("Gastronomi").Select(a => a.GoogleTipi));
    }

    [Fact]
    public async Task Ilce_verilince_arama_metnine_ve_yaricapa_yansiyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 5);

        await servis.RotaOnerAsync(Istek(ilce: "Çankaya"));

        Assert.All(places.Aramalar, a => Assert.Contains("Çankaya", a.Sorgu));

        // İlçe verilince yarıçap daralıyor: şehrin tamamını taramak,
        // kullanıcının seçtiği ilçenin dışına düşen duraklar üretirdi.
        Assert.All(places.Aramalar, a => Assert.True(a.YaricapMetre < 15_000));
    }

    // ------------------------------------------------------------------
    //  Aday seçimi — canlıda çıkan üç hatanın testi
    // ------------------------------------------------------------------

    /// <summary>Belirli konum ve önemle aday üretir.</summary>
    // ------------------------------------------------------------------
    //  YEMEK MOLASI ve KONAKLAMA durakları
    // ------------------------------------------------------------------

    /// <summary>Havuza N adet yöresel lokanta koyar (mola adayı olsun diye).</summary>
    private static void LokantaEkle(FakePlacesClient places, int adet)
    {
        for (var i = 0; i < adet; i++)
        {
            places.Ekle(
                $"lokanta-{i}", $"Yöresel Lokanta {i}",
                39.93 + i * 0.004, 32.85 + i * 0.004,
                puan: 4.7, degerlendirme: 800, tip: "restaurant",
                etiketler: new Dictionary<string, string> { ["cuisine"] = "turkish" });
        }
    }

    /// <summary>Havuza N adet otel koyar.</summary>
    private static void OtelEkle(FakePlacesClient places, int adet)
    {
        for (var i = 0; i < adet; i++)
        {
            places.Ekle(
                $"otel-{i}", $"Otel {i}",
                39.94 + i * 0.004, 32.86 + i * 0.004,
                puan: 4.6, degerlendirme: 300, tip: "lodging");
        }
    }

    [Fact]
    public async Task Yemek_molasi_ISTENMEDIKCE_eklenmiyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 6);
        LokantaEkle(places, 4);

        var oneri = await servis.RotaOnerAsync(Istek());

        // Gezi temalarında yeme-içme araması bilerek yok: bir şehir turuna
        // rastgele kafe düşmesi canlıda yaşandı.
        Assert.DoesNotContain(oneri.Waypoints, w => w.Note == "Yemek molası");
    }

    [Fact]
    public async Task Yemek_molasi_istenince_programa_giriyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 6);
        LokantaEkle(places, 4);

        var istek = Istek(toplamDakika: 480);
        istek.YemekMolasi = true;

        var oneri = await servis.RotaOnerAsync(istek);

        var molalar = oneri.Waypoints.Where(w => w.Note == "Yemek molası").ToList();

        Assert.Single(molalar);
        // Mola BAŞTA ya da SONDA değil: öğle yemeği turun ortasında.
        Assert.NotEqual(1, molalar[0].Order);
        Assert.NotEqual(oneri.Waypoints.Count, molalar[0].Order);
    }

    [Fact]
    public async Task Cok_gunlu_turda_HER_GUNE_bir_mola()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 12);
        LokantaEkle(places, 6);

        var istek = Istek(toplamDakika: 3 * 480, ulasim: "Arac");
        istek.Sure.Birim = "Gun";
        istek.Sure.Deger = 3;
        istek.YemekMolasi = true;

        var oneri = await servis.RotaOnerAsync(istek);

        Assert.Equal(3, oneri.Waypoints.Count(w => w.Note == "Yemek molası"));

        // AYNI lokanta iki güne konmuyor: üç gün üst üste aynı yerde yemek
        // yenmez ve liste kendini tekrar ederdi.
        var adlar = oneri.Waypoints.Where(w => w.Note == "Yemek molası")
            .Select(w => w.Name).ToList();
        Assert.Equal(adlar.Count, adlar.Distinct().Count());
    }

    [Fact]
    public async Task Yemek_molasi_BESLENME_KISITINA_uyuyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 6);

        // Biri vegan, diğeri değil — mola yalnızca uyanı seçmeli.
        places.Ekle(
            "etli", "Kebapçı", 39.931, 32.851,
            puan: 4.9, degerlendirme: 900, tip: "restaurant", vejetaryen: false,
            etiketler: new Dictionary<string, string> { ["cuisine"] = "kebab" });

        places.Ekle(
            "vegan", "Vegan Mutfak", 39.932, 32.852,
            puan: 4.6, degerlendirme: 200, tip: "restaurant", vejetaryen: true,
            etiketler: new Dictionary<string, string> { ["cuisine"] = "turkish" });

        var istek = Istek(beslenme: "Vegan");
        istek.YemekMolasi = true;

        var oneri = await servis.RotaOnerAsync(istek);
        var mola = oneri.Waypoints.FirstOrDefault(w => w.Note == "Yemek molası");

        Assert.NotNull(mola);
        Assert.Equal("Vegan Mutfak", mola!.Name);
    }

    [Fact]
    public async Task Konaklama_BAYRAK_GONDERILMEZSE_cok_gunlu_turda_yine_geliyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 12);
        OtelEkle(places, 6);

        var istek = Istek(toplamDakika: 2 * 480, ulasim: "Arac");
        istek.Sure.Birim = "Gun";
        istek.Sure.Deger = 2;
        // Konaklama HİÇ SET EDİLMİYOR (null) — eski istemci ya da eksik
        // gövde. Canlıda tam olarak bu yaşandı: sunucu tarafı sonuna kadar
        // doğruydu, bayrak gelmiyordu ve "false" ile "gönderilmedi" ayırt
        // edilemediği için konaklama sessizce atlanıyordu.
        Assert.Null(istek.Konaklama);

        var oneri = await servis.RotaOnerAsync(istek);

        Assert.Contains(oneri.Waypoints, w => w.Note == "Önerilen konaklama bölgesi");
    }

    [Fact]
    public async Task Konaklama_ACIKCA_false_ise_eklenmiyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 12);
        OtelEkle(places, 6);

        var istek = Istek(toplamDakika: 2 * 480, ulasim: "Arac");
        istek.Sure.Birim = "Gun";
        istek.Sure.Deger = 2;
        istek.Konaklama = false;   // istemiyorum

        var oneri = await servis.RotaOnerAsync(istek);

        // Varsayılanın açık olması, HAYIR diyeni yok saymak anlamına gelmemeli.
        Assert.DoesNotContain(oneri.Waypoints, w => w.Note == "Önerilen konaklama bölgesi");
    }

    [Fact]
    public async Task Konaklama_GUNUBIRLIK_turda_eklenmiyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 6);
        OtelEkle(places, 4);

        var istek = Istek(toplamDakika: 480);
        istek.Konaklama = true;          // istek gelse bile yok sayılmalı

        var oneri = await servis.RotaOnerAsync(istek);

        // Akşam eve dönülüyor; otel durağı anlamsız olurdu.
        Assert.DoesNotContain(oneri.Waypoints, w => w.VenueType == "Hotel");
    }

    [Fact]
    public async Task Konaklama_SON_GUN_HARIC_her_gunun_sonunda()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 12);
        OtelEkle(places, 6);

        var istek = Istek(toplamDakika: 3 * 480, ulasim: "Arac");
        istek.Sure.Birim = "Gun";
        istek.Sure.Deger = 3;
        istek.Konaklama = true;

        var oneri = await servis.RotaOnerAsync(istek);
        var konaklamalar = oneri.Waypoints
            .Where(w => w.Note == "Önerilen konaklama bölgesi").ToList();

        // 3 gün → 2 gece. Son geceye otel yok: o akşam şehirden ayrılınıyor.
        Assert.Equal(2, konaklamalar.Count);
        Assert.DoesNotContain(konaklamalar, k => k.Order == oneri.Waypoints.Count);
    }

    [Fact]
    public async Task Mola_ve_konaklama_TEK_EK_ISTEK_atiyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 10);
        LokantaEkle(places, 4);
        OtelEkle(places, 4);

        var istek = Istek(toplamDakika: 2 * 480, ulasim: "Arac");
        istek.Sure.Birim = "Gun";
        istek.Sure.Deger = 2;
        istek.YemekMolasi = true;
        istek.Konaklama = true;

        await servis.RotaOnerAsync(istek);

        // ÖNCE ÜÇ ÇAĞRI vardı (tema + yemek + konaklama) ve öneri süresi üç
        // katına çıkıyordu. Sonra hepsi TEK sorguda birleştirildi ama bu kez
        // oteller Overpass'in sonuç sınırında kesildi ("gece konaklama hâlâ
        // yok") ve sorgu 504 vermeye başladı.
        //
        // Doğru orta yol İKİ sorgu: gezi ve mola. Her birinin kendi sonuç
        // bütçesi oluyor, mola sorgusu dar yarıçapta çalıştığı için ucuz.
        Assert.Equal(2, places.TopluCagriSayisi);
    }

    [Fact]
    public async Task Gastronomi_temasinda_lokantalar_TURUN_KENDISI()
    {
        var (servis, places, _, _) = Kur();
        LokantaEkle(places, 6);

        var oneri = await servis.RotaOnerAsync(Istek(tema: "Gastronomi"));

        // Yeme-içme mekanları gezi temalarında yalnızca MOLA adayı; burada
        // ise turun durakları. Ayrım yapılmasaydı Gastronomi turu boş kalırdı.
        Assert.NotEmpty(oneri.Waypoints);
        Assert.Contains(oneri.Waypoints, w => w.VenueType == "Restaurant");
    }

    [Fact]
    public async Task Vejetaryen_kisitinda_KEBAPCI_elenmiyor_diye_gecmiyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 6);

        // İkisinin de diyet etiketi YOK — Ankara'da ölçüldü, hiçbir mekanda
        // yok. Eski süzgeç yalnızca diyet alanına baktığı için ikisini de
        // geçiriyordu; kısıt sessizce uygulanmıyordu.
        places.Ekle(
            "kebapci", "Ocakbaşı", 39.931, 32.851,
            puan: 4.9, degerlendirme: 900, tip: "restaurant",
            etiketler: new Dictionary<string, string> { ["cuisine"] = "kebab" });

        places.Ekle(
            "corbaci", "Çorbacı", 39.932, 32.852,
            puan: 4.6, degerlendirme: 400, tip: "restaurant",
            etiketler: new Dictionary<string, string> { ["cuisine"] = "soup" });

        var istek = Istek(beslenme: "Vejetaryen");
        istek.YemekMolasi = true;

        var oneri = await servis.RotaOnerAsync(istek);
        var mola = oneri.Waypoints.FirstOrDefault(w => w.Note == "Yemek molası");

        Assert.NotNull(mola);
        Assert.Equal("Çorbacı", mola!.Name);
    }

    [Fact]
    public async Task Vegan_kisitinda_BALIK_da_eleniyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 6);

        places.Ekle(
            "balikci", "Balık Evi", 39.931, 32.851,
            puan: 4.8, degerlendirme: 700, tip: "restaurant",
            etiketler: new Dictionary<string, string> { ["cuisine"] = "seafood" });

        places.Ekle(
            // Puan 4.5 üstü olmalı: bu kurulum Google kipinde ve orada
            // 4.5+ süzgeci de çalışıyor (testin ölçtüğü şey beslenme kısıtı).
            "salata", "Yeşil Mutfak", 39.932, 32.852,
            puan: 4.6, degerlendirme: 200, tip: "restaurant",
            etiketler: new Dictionary<string, string> { ["cuisine"] = "vegan" });

        var istek = Istek(beslenme: "Vegan");
        istek.YemekMolasi = true;

        var oneri = await servis.RotaOnerAsync(istek);
        var mola = oneri.Waypoints.FirstOrDefault(w => w.Note == "Yemek molası");

        // Vejetaryende balık tartışmalı, veganda değil.
        Assert.Equal("Yeşil Mutfak", mola!.Name);
    }

    [Fact]
    public async Task Diyet_etiketi_VARSA_mutfaga_bakilmiyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 6);

        // Mutfağı "kebab" ama işletme vejetaryen menü olduğunu ETİKETLEMİŞ.
        // Açık etiket, tahminden üstün.
        places.Ekle(
            "vejkebap", "Vejetaryen Mutfak", 39.931, 32.851,
            puan: 4.7, degerlendirme: 500, tip: "restaurant",
            etiketler: new Dictionary<string, string>
            {
                ["cuisine"] = "kebab",
                ["diet:vegetarian"] = "only",
            });

        var istek = Istek(beslenme: "Vejetaryen");
        istek.YemekMolasi = true;

        var oneri = await servis.RotaOnerAsync(istek);

        Assert.Contains(oneri.Waypoints, w => w.Name == "Vejetaryen Mutfak");
    }

    [Fact]
    public async Task Kisit_yokken_kebapci_elenmiyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 6);

        places.Ekle(
            "kebapci", "Ocakbaşı", 39.931, 32.851,
            puan: 4.9, degerlendirme: 900, tip: "restaurant",
            etiketler: new Dictionary<string, string> { ["cuisine"] = "kebab" });

        var istek = Istek();
        istek.YemekMolasi = true;

        var oneri = await servis.RotaOnerAsync(istek);

        // Süzgeç YALNIZCA kısıt seçiliyken çalışmalı.
        Assert.Contains(oneri.Waypoints, w => w.Name == "Ocakbaşı");
    }

    [Fact]
    public async Task Kisit_secilince_DOGASI_uyariyla_soyleniyor()
    {
        var (servis, places, _, _) = Kur();
        places.PuanVerisiVar = false;          // anahtarsız kip
        MuzeEkle(places, 6);

        var oneri = await servis.RotaOnerAsync(Istek(beslenme: "Vegan"));

        // Süzgeç mutfağa dayanıyor, garanti değil — kullanıcıya böyle söylemek
        // "vegan onaylı" demekten dürüst.
        Assert.Contains(oneri.Uyarilar, u => u.Contains("mutfak", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------
    //  SERBEST ZAMAN
    // ------------------------------------------------------------------

    /// <summary>Havuza gezinmeye uygun (park) duraklar koyar.</summary>
    private static void ParkEkle(FakePlacesClient places, int adet)
    {
        for (var i = 0; i < adet; i++)
        {
            places.Ekle(
                $"park-{i}", $"Park {i}",
                39.935 + i * 0.006, 32.855 + i * 0.006,
                puan: 4.6, degerlendirme: 500, tip: "park");
        }
    }

    [Fact]
    public async Task Serbest_zaman_UYGUN_duragin_suresini_uzatiyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 4);
        ParkEkle(places, 2);

        // Tema "Karma": park araması bu temada var, parklar havuza girsin.
        var istek = Istek(toplamDakika: 480, tema: "Karma");
        istek.SerbestZaman = true;

        var oneri = await servis.RotaOnerAsync(istek);
        var serbest = oneri.Waypoints.Where(w => w.Note != null
            && w.Note.StartsWith("Serbest zaman")).ToList();

        Assert.Single(serbest);

        // YENİ DURAK AÇILMIYOR: serbest zaman bir yere gitmek değil, bir
        // yerde kalmak. Ayrı durak olsaydı rota kendi üstüne dönerdi.
        Assert.True(serbest[0].DwellMinutes > 30);
    }

    [Fact]
    public async Task Serbest_zaman_MUZEYE_eklenmiyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 4);
        ParkEkle(places, 2);

        var istek = Istek(toplamDakika: 480, tema: "Karma");
        istek.SerbestZaman = true;

        var oneri = await servis.RotaOnerAsync(istek);
        var serbest = oneri.Waypoints.First(w => w.Note != null
            && w.Note.StartsWith("Serbest zaman"));

        // Kapalı bir mekanda "serbest zaman" beklemek demek olurdu; müzede
        // geçirilecek süre zaten kalış süresinde var.
        Assert.NotEqual("Museum", serbest.VenueType);
    }

    [Fact]
    public async Task Serbest_zaman_ISTENMEDIKCE_eklenmiyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 4);
        ParkEkle(places, 2);

        var oneri = await servis.RotaOnerAsync(Istek(toplamDakika: 480));

        Assert.DoesNotContain(oneri.Waypoints,
            w => w.Note != null && w.Note.StartsWith("Serbest zaman"));
    }

    [Fact]
    public async Task Cok_gunlu_turda_HER_GUNE_serbest_zaman()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 8);
        ParkEkle(places, 4);

        var istek = Istek(toplamDakika: 2 * 480, ulasim: "Arac", tema: "Karma");
        istek.Sure.Birim = "Gun";
        istek.Sure.Deger = 2;
        istek.SerbestZaman = true;

        var oneri = await servis.RotaOnerAsync(istek);

        Assert.Equal(2, oneri.Waypoints.Count(w => w.Note != null
            && w.Note.StartsWith("Serbest zaman")));
    }

    [Fact]
    public async Task Konaklama_bulunamazsa_UYARI_veriyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 8);
        // Otel HİÇ YOK.

        var istek = Istek(toplamDakika: 2 * 480, ulasim: "Arac");
        istek.Sure.Birim = "Gun";
        istek.Sure.Deger = 2;
        istek.Konaklama = true;

        var oneri = await servis.RotaOnerAsync(istek);

        // Sessiz kalmak, kullanıcının kutucuğu işaretleyip hiçbir şey
        // görmemesi ve sebebini bilmemesi demekti — canlıda bu yaşandı.
        Assert.Contains(oneri.Uyarilar, u => u.Contains("konaklama", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------
    //  ROTA YENİDEN HESAPLAMA — elle düzenlenmiş durak listesi
    // ------------------------------------------------------------------

    private static TurRotaHesapIstegiDto HesapIstegi(int durakSayisi = 3, string ulasim = "Yaya")
        => new()
        {
            UlasimTipi = ulasim,
            Duraklar = Enumerable.Range(0, durakSayisi)
                .Select(i => new TurRotaNoktasiDto { Lat = 39.93 + i * 0.01, Lon = 32.85 + i * 0.01 })
                .ToList(),
        };

    [Fact]
    public async Task Rota_hesaplama_MEKAN_ARAMASI_yapmiyor()
    {
        var (servis, places, directions, _) = Kur();

        await servis.RotaHesaplaAsync(HesapIstegi());

        // ASIL MESELE BU: kullanıcı listeye elle bir durak eklediğinde seçim
        // zaten yapılmış. Arama yapsaydık her küçük düzenleme yeni bir mekan
        // sorgusunu ve dış servis maliyetini tetiklerdi.
        Assert.Equal(0, places.CagriSayisi);
        Assert.Equal(1, directions.CagriSayisi);
    }

    [Fact]
    public async Task Rota_hesaplama_verilen_SIRAYI_koruyor()
    {
        var (servis, _, directions, _) = Kur();

        var sonuc = await servis.RotaHesaplaAsync(HesapIstegi(durakSayisi: 4));

        // Noktalar verildiği sırayla gidiyor: kullanıcı durağı bilerek araya
        // soktu, sunucunun onu "daha kısa olur" diye taşıması yaptığı işi
        // geri almak olurdu.
        Assert.Equal(4, directions.SonNoktaSayisi);
        Assert.NotNull(sonuc.RouteWkt);
    }

    [Fact]
    public async Task Rota_hesaplamada_toplu_tasima_carpani_uygulaniyor()
    {
        var (servis, _, directions, _) = Kur();
        directions.BacakSaniye = 600;

        var yaya = await servis.RotaHesaplaAsync(HesapIstegi(ulasim: "Yaya"));
        var toplu = await servis.RotaHesaplaAsync(HesapIstegi(ulasim: "TopluTasima"));

        // Öneri ucunda da aynı çarpan var. Ayrışsalardı ekrandaki toplam
        // süre, kullanıcı bir durak ekleyip çıkardığında sıçrardı.
        Assert.True(toplu.RouteDurationSeconds > yaya.RouteDurationSeconds);
    }

    [Fact]
    public async Task Rota_cizilemezse_HATA_DEGIL_uyari_donuyor()
    {
        var (servis, _, directions, _) = Kur();
        directions.BasarisizOl = true;

        var sonuc = await servis.RotaHesaplaAsync(HesapIstegi());

        // Rota olmadan da durak listesi geçerli bir tur. 503 fırlatsaydık dış
        // servisin geçici aksaklığı, kullanıcının elle yaptığı düzenlemeyi de
        // çöpe atardı.
        Assert.Null(sonuc.RouteWkt);
        Assert.NotNull(sonuc.Uyari);
    }

    [Fact]
    public async Task Rota_servisi_kapaliyken_de_uyari_donuyor()
    {
        var (servis, _, directions, _) = Kur();
        directions.Etkin = false;

        var sonuc = await servis.RotaHesaplaAsync(HesapIstegi());

        Assert.Null(sonuc.RouteWkt);
        Assert.NotNull(sonuc.Uyari);
        Assert.Equal(0, directions.CagriSayisi);
    }

    [Fact]
    public async Task Tek_durakla_rota_hesaplanmiyor()
    {
        var (servis, _, _, _) = Kur();

        await Assert.ThrowsAsync<IsKuraliException>(
            () => servis.RotaHesaplaAsync(HesapIstegi(durakSayisi: 1)));
    }

    private static AdayMekan Aday(string ad, double lat, double lon, double onem,
        VenueType tip = VenueType.Monument)
        => new(
            new GooglePlace($"osm:node/{ad.GetHashCode()}", ad, lat, lon, null, 0,
                new[] { "tourist_attraction" }, "tourist_attraction", onem, null),
            tip, 20);

    [Fact]
    public void Secim_ONEM_sirasina_gore_yapiliyor()
    {
        // Önceki sürüm tipler arasında sırayla ilerliyordu; sonuç, her tipin
        // EN İYİSİ değil sırası geleni almaktı ve Ankara turunda Anadolu
        // Medeniyetleri Müzesi listeye hiç girmiyordu.
        var adaylar = new[]
        {
            Aday("Küçük Anıt", 39.90, 32.80, 0.20),
            Aday("Anadolu Medeniyetleri Müzesi", 39.94, 32.86, 0.95, VenueType.Museum),
            Aday("Mahalle Parkı", 39.91, 32.81, 0.30, VenueType.Park),
        };

        var secim = AdaySecici.Sec(adaylar, 2);

        Assert.Equal("Anadolu Medeniyetleri Müzesi", secim[0].Mekan.Ad);
    }

    [Fact]
    public void Cok_yakin_kayitlar_TEK_durak_sayiliyor()
    {
        // CANLIDA: tek bir anıtın beş kulesi ayrı durak olarak girdi
        // (aralarında 57 m, 59 m, 107 m). "15 duraklı" ama aslında birkaç
        // noktayı gezen bir program çıkıyordu.
        var adaylar = new[]
        {
            Aday("Cumhuriyet Kulesi", 39.9330, 32.8600, 0.60),
            Aday("Hürriyet Kulesi", 39.9331, 32.8601, 0.59),      // ~15 m
            Aday("İstiklal Kulesi", 39.9332, 32.8602, 0.58),      // ~30 m
            Aday("Anıtkabir", 39.9250, 32.8360, 0.90),            // ~2,4 km
        };

        var secim = AdaySecici.Sec(adaylar, 4);

        Assert.Equal(2, secim.Count);
        Assert.Contains(secim, a => a.Mekan.Ad == "Anıtkabir");
    }

    [Fact]
    public void Ayni_kompleksin_parcalari_eleniyor()
    {
        // 300 m'den uzak ama aynı külliyenin parçaları: ad benzerliği yakalıyor.
        var adaylar = new[]
        {
            Aday("Hacıbayram Camii", 39.9440, 32.8540, 0.80),
            Aday("Hacıbayram Türbesi", 39.9445, 32.8550, 0.70),   // ~100 m değil, ~90 m
            Aday("Kuğulu Park", 39.9000, 32.8600, 0.60, VenueType.Park),
        };

        var secim = AdaySecici.Sec(adaylar, 3);

        Assert.DoesNotContain(secim, a => a.Mekan.Ad == "Hacıbayram Türbesi");
    }

    [Fact]
    public void Genel_sozcukler_farkli_yerleri_ayni_saymiyor()
    {
        // "Parkı" ve "Müzesi" gibi sözcükler eşleşmeye girseydi şehirdeki
        // bütün parklar tek yer sayılırdı.
        var adaylar = new[]
        {
            Aday("Gençlik Parkı", 39.9400, 32.8500, 0.70, VenueType.Park),
            Aday("Kuğulu Parkı", 39.9420, 32.8560, 0.65, VenueType.Park),   // ~550 m
        };

        var secim = AdaySecici.Sec(adaylar, 2);

        Assert.Equal(2, secim.Count);
    }

    [Fact]
    public void Tip_tavani_listenin_tek_tipe_kaymasini_engelliyor()
    {
        // Puanlama müzeleri öne aldığı için tavansız seçimde 12 durağın 9'u
        // müze çıkıyordu.
        var adaylar = Enumerable.Range(1, 10)
            .Select(i => Aday($"Müze {i}", 39.90 + i * 0.01, 32.80 + i * 0.01, 0.9 - i * 0.01, VenueType.Museum))
            .Concat(new[] { Aday("Kale", 39.80, 32.70, 0.50) })
            .ToArray();

        var secim = AdaySecici.Sec(adaylar, 8);

        Assert.Equal(4, secim.Count(a => a.Tip == VenueType.Museum));
        Assert.Contains(secim, a => a.Mekan.Ad == "Kale");
    }

    // ------------------------------------------------------------------
    //  3 — Sıra ve rota
    // ------------------------------------------------------------------

    [Fact]
    public async Task Duraklar_1_den_baslayarak_sirali_numaralaniyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 6);

        var sonuc = await servis.RotaOnerAsync(Istek(toplamDakika: 480));

        Assert.Equal(
            Enumerable.Range(1, sonuc.Waypoints.Count).ToList(),
            sonuc.Waypoints.Select(w => w.Order).ToList());
    }

    [Fact]
    public void Waypoint_order_dizisi_ara_noktalara_uygulaniyor()
    {
        var adaylar = Enumerable.Range(0, 5)
            .Select(i => new AdayMekan(
                new GooglePlace($"p{i}", $"Yer {i}", 39.9, 32.8, 4.7, 100,
                    new[] { "museum" }, "museum", 0.7, null),
                VenueType.Museum, 30))
            .ToList();

        // Ara noktalar: 1,2,3 → Google "üçüncüyü öne al" diyor.
        var sirali = AdaySecici.SirayiUygula(adaylar, new[] { 2, 0, 1 });

        Assert.Equal(
            new[] { "Yer 0", "Yer 3", "Yer 1", "Yer 2", "Yer 4" },
            sirali.Select(a => a.Mekan.Ad).ToArray());
    }

    [Fact]
    public void Bozuk_waypoint_order_girdi_sirasini_bozmuyor()
    {
        var adaylar = Enumerable.Range(0, 4)
            .Select(i => new AdayMekan(
                new GooglePlace($"p{i}", $"Yer {i}", 39.9, 32.8, 4.7, 100,
                    new[] { "museum" }, "museum", 0.7, null),
                VenueType.Museum, 30))
            .ToList();

        // Tekrar eden indis ve taşan indis: ikisi de sessizce yanlış eşleşme
        // üretebilirdi. Doğru davranış girdi sırasını KORUMAK.
        Assert.Equal(adaylar, AdaySecici.SirayiUygula(adaylar, new[] { 0, 0 }));
        Assert.Equal(adaylar, AdaySecici.SirayiUygula(adaylar, new[] { 0, 9 }));
        Assert.Equal(adaylar, AdaySecici.SirayiUygula(adaylar, new[] { 0 }));
    }

    [Fact]
    public async Task Rota_cizgisi_ve_mesafesi_cevaba_giriyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 5);

        var sonuc = await servis.RotaOnerAsync(Istek(toplamDakika: 480));

        Assert.StartsWith("LINESTRING", sonuc.RouteWkt);
        Assert.True(sonuc.RouteDistanceMeters > 0);
        Assert.True(sonuc.RouteUpToDate);

        // Öneri henüz KAYDEDİLMEDİ: id'ler 0 olmalı.
        Assert.Equal(0, sonuc.Id);
        Assert.All(sonuc.Waypoints, w => Assert.Equal(0, w.Id));

        // PlaceId sağlayıcı ön ekiyle yazılıyor (Waypoint.PlaceId sözleşmesi).
        Assert.All(sonuc.Waypoints, w => Assert.StartsWith("google:", w.PlaceId));
    }

    [Fact]
    public async Task Karma_temada_tek_tip_durak_yigilmiyor()
    {
        var (servis, places, _, _) = Kur();

        // Müzelerin oyu çok daha yüksek: düz sıralamada listeyi kaplarlardı.
        for (var i = 0; i < 8; i++)
        {
            places.Ekle($"m{i}", $"Müze {i}", 39.90 + i * 0.01, 32.80 + i * 0.01, 4.9, 50_000, "museum");
        }
        for (var i = 0; i < 8; i++)
        {
            places.Ekle($"p{i}", $"Park {i}", 39.90 + i * 0.01, 32.90 + i * 0.01, 4.6, 600, "park");
        }

        var sonuc = await servis.RotaOnerAsync(Istek(toplamDakika: 600, tema: "Karma"));

        var tipler = sonuc.Waypoints.Select(w => w.VenueType).Distinct().ToList();
        Assert.True(tipler.Count > 1, "Karma tur tek tipe düştü.");
    }

    // ------------------------------------------------------------------
    //  Hata yolları
    // ------------------------------------------------------------------

    [Fact]
    public async Task Servis_kapaliysa_anlamli_hata_veriyor()
    {
        var (servis, places, directions, _) = Kur(new GoogleMapsSettings { Enabled = false });
        MuzeEkle(places, 5);

        // Gerçek istemcilerde Etkin doğrudan ayarlardan gelir (KullanimaHazir);
        // sahtelerde ayrı bir bayrak olduğu için burada elle kapatıyoruz.
        places.Etkin = false;
        directions.Etkin = false;

        var hata = await Assert.ThrowsAsync<DisServisException>(
            () => servis.RotaOnerAsync(Istek()));

        Assert.Contains("kullanılamıyor", hata.Message);
        Assert.Equal(0, places.CagriSayisi);   // kapalıyken hiç istek atılmıyor
    }

    [Fact]
    public async Task Uygun_mekan_bulunamazsa_anlamli_hata_veriyor()
    {
        var (servis, _, directions, _) = Kur();

        var hata = await Assert.ThrowsAsync<DisServisException>(
            () => servis.RotaOnerAsync(Istek()));

        Assert.Contains("bulunamadı", hata.Message);

        // Aday yokken rota istemenin anlamı yok — boşuna istek atılmamalı.
        Assert.Equal(0, directions.CagriSayisi);
    }

    [Fact]
    public async Task Rota_servisi_cevap_vermezse_503_yolu_isliyor()
    {
        var (servis, places, directions, _) = Kur();
        MuzeEkle(places, 5);
        directions.BasarisizOl = true;

        await Assert.ThrowsAsync<DisServisException>(() => servis.RotaOnerAsync(Istek()));
    }

    [Theory]
    [InlineData(0, "Yaya", "Kulturel")]      // plaka yok
    [InlineData(6, "Helikopter", "Kulturel")] // tanınmayan ulaşım
    public async Task Gecersiz_girdi_is_kurali_hatasi_veriyor(int plaka, string ulasim, string tema)
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 5);

        var istek = Istek(tema: tema, ulasim: ulasim);
        istek.Lokasyon.IlPlaka = plaka;

        await Assert.ThrowsAsync<IsKuraliException>(() => servis.RotaOnerAsync(istek));
    }

    // ------------------------------------------------------------------
    //  ÇOK ŞEHİRLİ TUR (Madde 6)
    //
    //  "bölge ya da şehir seçip birden fazla şehirle tur yapılabilmesi".
    //  Buradaki testler üç sessiz hatayı koruyor: şehirlerin aranmaması,
    //  sınırın sessizce aşılması ve aynı şehrin iki kez taranması.
    // ------------------------------------------------------------------

    /// <summary>İki şehirli (Ankara + Eskişehir) sahte kurulum.</summary>
    private static (TurPlanlamaServisi servis, FakePlacesClient places)
        CokSehirliKur()
    {
        var butce = new FakeIstekButcesi();
        var places = new FakePlacesClient { Butce = butce };
        var directions = new FakeDirectionsClient { Butce = butce };

        var iller = new FakeIlRepository()
            .Ekle(6, "Ankara", AnkaraKaresi)
            .Ekle(26, "Eskişehir",
                "POLYGON ((30.3 39.6, 31.2 39.6, 31.2 40.0, 30.3 40.0, 30.3 39.6))")
            .Ekle(42, "Konya",
                "POLYGON ((32.2 37.7, 32.9 37.7, 32.9 38.2, 32.2 38.2, 32.2 37.7))")
            .Ekle(35, "İzmir",
                "POLYGON ((26.8 38.2, 27.5 38.2, 27.5 38.7, 26.8 38.7, 26.8 38.2))");

        var servis = new TurPlanlamaServisi(
            places, directions, iller,
            new FakePoiRepository(), new FakePoiCategoryRepository(),
            butce,
            new GoogleMapsSettings { Enabled = true, ApiKey = "test" },
            NullLogger<TurPlanlamaServisi>.Instance);

        return (servis, places);
    }

    [Fact]
    public async Task Ek_sehir_verilince_HER_SEHIR_ayri_araniyor()
    {
        var (servis, places) = CokSehirliKur();
        MuzeEkle(places, 8);

        var tekSehir = Istek();
        await servis.RotaOnerAsync(tekSehir);
        var tekSehirCagrisi = places.TopluCagriSayisi;

        var (servis2, places2) = CokSehirliKur();
        MuzeEkle(places2, 8);

        var ikiSehir = Istek();
        ikiSehir.Lokasyon.EkIlPlakalari = new List<int> { 26 };
        await servis2.RotaOnerAsync(ikiSehir);

        // Tek merkezden yarıçapı büyütmek "iki şehir arasındaki her şey"
        // demek olurdu; her şehir KENDİ merkezinden aranıyor.
        Assert.Equal(tekSehirCagrisi * 2, places2.TopluCagriSayisi);
    }

    [Fact]
    public async Task Cok_sehirli_turun_ADI_sehirleri_yaziyor()
    {
        var (servis, places) = CokSehirliKur();
        MuzeEkle(places, 8);

        var istek = Istek();
        istek.Lokasyon.EkIlPlakalari = new List<int> { 26 };

        var oneri = await servis.RotaOnerAsync(istek);

        // Adlar İL TABLOSUNDAN geliyor, istekteki IlAdi'den değil.
        Assert.Contains("Ankara", oneri.Name);
        Assert.Contains("Eskişehir", oneri.Name);
    }

    [Fact]
    public async Task Sehir_sinirini_asan_istek_KIRPILIP_uyariliyor()
    {
        var (servis, places) = CokSehirliKur();
        MuzeEkle(places, 8);

        var istek = Istek();
        istek.Lokasyon.EkIlPlakalari = new List<int> { 26, 42, 35 };   // toplam 4

        var oneri = await servis.RotaOnerAsync(istek);

        // Sessizce kırpsaydık kullanıcı seçtiği şehrin neden turda
        // olmadığını anlamazdı.
        Assert.Contains(oneri.Uyarilar, u => u.Contains("en fazla"));
        Assert.DoesNotContain("İzmir", oneri.Name);
    }

    [Fact]
    public async Task Ayni_sehir_iki_kez_verilirse_BIR_KEZ_araniyor()
    {
        var (servis, places) = CokSehirliKur();
        MuzeEkle(places, 8);

        var istek = Istek();
        // Başlangıç şehri tekrar + gerçek bir tekrar.
        istek.Lokasyon.EkIlPlakalari = new List<int> { 6, 26, 26 };

        var oneri = await servis.RotaOnerAsync(istek);

        // İki şehir kaldı: sınır uyarısı çıkmamalı ve ad iki şehir yazmalı.
        Assert.DoesNotContain(oneri.Uyarilar, u => u.Contains("en fazla"));
        Assert.Contains("Ankara", oneri.Name);
        Assert.Contains("Eskişehir", oneri.Name);
    }

    [Fact]
    public async Task Tek_sehirli_istek_ESKISI_gibi_calisiyor()
    {
        // Genişletmenin bozmaması gereken şey: var olan bütün istemciler
        // EkIlPlakalari göndermiyor.
        var (servis, places) = CokSehirliKur();
        MuzeEkle(places, 8);

        var oneri = await servis.RotaOnerAsync(Istek());

        Assert.Equal("Ankara · Kültürel tur (4 sa)", oneri.Name);
        Assert.True(oneri.Waypoints.Count >= 2);
    }

    // ------------------------------------------------------------------
    //  YEREL POI YEDEĞİ — Overpass düştüğünde rota yine oluşsun
    //
    //  Canlıda ölçülen: aynı Overpass sorgusu arka arkaya 504 / 6 sn /
    //  16,9 sn dönüyor. İki deneme de 25 sn zaman aşımına uğradığında
    //  kullanıcı 50 saniye bekleyip "uygun mekan bulunamadı" alıyordu.
    //  Aradığımız veri ise zaten veritabanında (turistik POI aktarımı).
    // ------------------------------------------------------------------

    /// <summary>Overpass'in HİÇ cevap vermediği, POI tablosunun dolu olduğu kurulum.</summary>
    private static (TurPlanlamaServisi servis, FakePlacesClient places)
        YerelYedekliKur(int poiSayisi = 8)
    {
        var db = new SahteVeritabani();

        // Turistik aktarımın açtığı kategorilerin adları BİREBİR — yedek
        // eşlemesi ada bakıyor (bkz. YerelPoiKaynagi.KategoriAdlari).
        var kategoriler = new[] { "Müze", "Tarihi Yer", "Park", "İbadet Yeri" };

        for (var i = 0; i < kategoriler.Length; i++)
        {
            db.PoiKategorileri.Add(new PoiCategory
            {
                Id = 18 + i,
                Ad = kategoriler[i],
                IsActive = true,
            });
        }

        var fabrika = NetTopologySuite.NtsGeometryServices.Instance
            .CreateGeometryFactory(srid: 4326);

        for (var i = 0; i < poiSayisi; i++)
        {
            db.Poiler.Add(new Poi
            {
                Id = 100 + i,
                Isim = $"Yerel Mekan {i}",
                // Ankara merkezine yakın, birbirinden ayrı noktalar.
                KategoriId = 18 + (i % kategoriler.Length),
                Geom = fabrika.CreatePoint(new Coordinate(32.85 + i * 0.004, 39.93 + i * 0.003)),
                IsActive = true,
            });
        }

        var butce = new FakeIstekButcesi();

        // BosDon: Overpass'in zaman aşımına uğrayıp boş dönmesinin karşılığı.
        var places = new FakePlacesClient { Butce = butce, BosDon = true };
        var directions = new FakeDirectionsClient { Butce = butce };
        var iller = new FakeIlRepository().Ekle(6, "Ankara", AnkaraKaresi);

        var servis = new TurPlanlamaServisi(
            places, directions, iller,
            new FakePoiRepository(db), new FakePoiCategoryRepository(db),
            butce,
            new GoogleMapsSettings { Enabled = true, ApiKey = "test" },
            NullLogger<TurPlanlamaServisi>.Instance);

        return (servis, places);
    }

    [Fact]
    public async Task Overpass_bos_donunce_YEREL_POI_ile_rota_olusuyor()
    {
        var (servis, _) = YerelYedekliKur();

        // Eskiden burada DisServisException fırlıyordu ("uygun mekan
        // bulunamadı") ve kullanıcı hiç rota alamıyordu.
        var oneri = await servis.RotaOnerAsync(Istek());

        Assert.True(oneri.Waypoints.Count >= 2);
        Assert.All(oneri.Waypoints, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
    }

    [Fact]
    public async Task Yerel_yedek_kullanilinca_KULLANICIYA_soyleniyor()
    {
        var (servis, _) = YerelYedekliKur();

        var oneri = await servis.RotaOnerAsync(Istek());

        // Sessiz kalmak, kullanıcının güncel veriyle çalıştığını sanmasına
        // yol açardı.
        Assert.Contains(oneri.Uyarilar, u => u.Contains("kayıtlı mekanlardan"));
    }

    [Fact]
    public async Task Yerel_POI_de_YOKSA_hata_veriyor()
    {
        // Yedek "her durumda bir şey uydur" demek değil: veritabanı da
        // boşsa söyleyecek doğru şey hâlâ "mekan bulunamadı".
        var (servis, _) = YerelYedekliKur(poiSayisi: 0);

        await Assert.ThrowsAsync<DisServisException>(() => servis.RotaOnerAsync(Istek()));
    }

    [Fact]
    public async Task Overpass_CALISIYORSA_yerel_yedek_devreye_girmiyor()
    {
        // Yedek yalnızca boşlukta devreye girmeli; canlı veriyi
        // bastırmamalı.
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 6);

        var oneri = await servis.RotaOnerAsync(Istek());

        Assert.DoesNotContain(oneri.Uyarilar, u => u.Contains("kayıtlı mekanlardan"));
    }

    [Fact]
    public async Task Cok_kisa_sure_reddediliyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 5);

        await Assert.ThrowsAsync<IsKuraliException>(
            () => servis.RotaOnerAsync(Istek(toplamDakika: 10)));
    }

    [Fact]
    public async Task Toplu_tasimada_sure_payi_ekleniyor_ve_uyari_veriliyor()
    {
        var (yayaServis, yayaPlaces, _, _) = Kur();
        MuzeEkle(yayaPlaces, 12);
        var yaya = await yayaServis.RotaOnerAsync(Istek(toplamDakika: 300, ulasim: "Yaya"));

        var (ttServis, ttPlaces, ttDirections, _) = Kur();
        MuzeEkle(ttPlaces, 12);
        var tt = await ttServis.RotaOnerAsync(Istek(toplamDakika: 300, ulasim: "TopluTasima"));

        // Aynı bacak süreleriyle, toplu taşımada beklemeler eklendiği için
        // AYNI bütçeye daha az durak sığıyor.
        Assert.True(tt.Waypoints.Count <= yaya.Waypoints.Count);
        Assert.Contains(tt.Uyarilar, u => u.Contains("Toplu taşıma"));

        // Kip istemciye TopluTasima olarak gidiyor; "transit ara nokta kabul
        // etmiyor" kuralı istemcinin içinde (DirectionsClient.KipAdi) sürüş
        // kipine çevriliyor. Servisin işi kipi doğru iletmek.
        Assert.Equal(SeyahatKipi.ToplaTasima, ttDirections.SonKip);
    }

    // ------------------------------------------------------------------
    //  Anahtarsız kip (OpenStreetMap): puan verisi olmayan kaynak
    // ------------------------------------------------------------------

    [Fact]
    public async Task Puansiz_kaynakta_4_5_suzgeci_atlaniyor()
    {
        var (servis, places, _, _) = Kur();

        // OSM gibi davran: puan yok, sıralama Onem alanından.
        places.PuanVerisiVar = false;
        places.Ekle("o1", "Etnografya Müzesi", 39.91, 32.81, null, 0, "museum", onem: 0.8);
        places.Ekle("o2", "Cumhuriyet Müzesi", 39.92, 32.82, null, 0, "museum", onem: 0.6);

        var sonuc = await servis.RotaOnerAsync(Istek(toplamDakika: 300));

        // Google kipinde bu iki mekan "puanı yok" diye elenirdi ve öneri hiç
        // üretilemezdi. Anahtarsız kipin varlık sebebi tam olarak bu.
        Assert.Equal(2, sonuc.Waypoints.Count);
    }

    [Fact]
    public async Task Puansiz_kaynakta_suzgecin_atlandigi_kullaniciya_soyleniyor()
    {
        var (servis, places, _, _) = Kur();
        places.PuanVerisiVar = false;
        places.Ekle("o1", "Müze", 39.91, 32.81, null, 0, "museum", onem: 0.8);
        places.Ekle("o2", "Park", 39.92, 32.82, null, 0, "park", onem: 0.5);

        var sonuc = await servis.RotaOnerAsync(Istek(toplamDakika: 300, tema: "Karma"));

        // Sessiz kalmak, puansız veriyi "4.5+ seçildi" diye sunmak olurdu.
        Assert.Contains(sonuc.Uyarilar, u => u.Contains("OpenStreetMap") && u.Contains("4.5+"));
    }

    [Fact]
    public async Task Puanli_kaynakta_ayni_uyari_verilmiyor()
    {
        var (servis, places, _, _) = Kur();
        MuzeEkle(places, 5);

        var sonuc = await servis.RotaOnerAsync(Istek(toplamDakika: 300));

        Assert.DoesNotContain(sonuc.Uyarilar, u => u.Contains("4.5+"));
    }

    [Fact]
    public async Task Puansiz_kaynakta_siralama_onem_alanindan_yapiliyor()
    {
        var (servis, places, _, _) = Kur();
        places.PuanVerisiVar = false;

        // Üçü de aynı tipte; yalnızca önem ayırıyor. Süre iki durağa yetiyor.
        places.Ekle("dusuk", "Küçük Müze", 39.91, 32.81, null, 0, "museum", onem: 0.2);
        places.Ekle("yuksek", "Anıtkabir", 39.92, 32.82, null, 0, "museum", onem: 0.95);
        places.Ekle("orta", "Orta Müze", 39.93, 32.83, null, 0, "museum", onem: 0.55);

        var sonuc = await servis.RotaOnerAsync(Istek(toplamDakika: 130));

        // En önemsizi listeye girmemeli: skor Onem'e bağlı olmasaydı üçü de
        // sıfır skor alır ve seçim rastgeleye dönerdi.
        Assert.DoesNotContain(sonuc.Waypoints, w => w.Name == "Küçük Müze");
        Assert.Contains(sonuc.Waypoints, w => w.Name == "Anıtkabir");
    }
}
