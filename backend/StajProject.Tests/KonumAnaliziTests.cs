using NetTopologySuite.Geometries;
using StajProject.Business.Analiz;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Services;
using StajProject.Business.Validation;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 14 testleri: KONUM ANALİZİ.
///
/// Üç kural kümesi sınanıyor:
///   1. Doğrulama — kriter sayısı (2–5), ağırlık toplamı (tam 100), alan seçimi
///   2. Kapsam    — analiz YALNIZCA seçilen alandaki POI'ler üzerinde çalışıyor
///   3. Hesap     — ağırlık değiştirmek yüzeyi GERÇEKTEN değiştiriyor
///
/// Üçüncüsü en önemlisi: ilk ikisi "hata veriyor mu?" diye sorar, üçüncüsü
/// "sonuç doğru mu?" diye. Bir analiz aracının sessizce yanlış çalışması,
/// açıkça hata vermesinden çok daha tehlikeli.
///
/// Koordinatlar 0–10 aralığında tutuldu (PoiTests ve CografiYetkiTests ile
/// aynı yaklaşım): hangi noktanın nerede olduğu okurken hesap gerektirmiyor.
/// Bu aralıkta 1 derece ≈ 111 km, yani "sol alt küme" ile "sağ üst küme"
/// arasında yüzlerce kilometre var — çekirdekler birbirine karışmıyor.
/// </summary>
public class KonumAnaliziTests
{
    /// <summary>Analiz alanı: 0,0 – 10,10 karesi.</summary>
    private const string Kare = "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))";

    private sealed record Ortam(
        SahteVeritabani Db,
        KonumAnaliziService Analiz,
        FakeIlRepository Iller,
        int EczaneId,
        int OkulId,
        int SaglikId);

    /// <summary>
    /// İki kök ("Sağlık", "Eğitim") ve iki yaprak ("Eczane", "Okul") kategorili
    /// bir dünya kurar. Kök + yaprak birlikte var, çünkü "kök kategori seçilince
    /// altındakiler de sayılıyor mu?" kuralı ancak böyle sınanabilir.
    /// </summary>
    private static Ortam Kur()
    {
        var db = new SahteVeritabani();

        var saglik = db.PoiKategoriEkle("Sağlık");
        var eczane = db.PoiKategoriEkle("Eczane", saglik.Id);
        var egitim = db.PoiKategoriEkle("Eğitim");
        var okul = db.PoiKategoriEkle("Okul", egitim.Id);

        var iller = new FakeIlRepository();

        var analiz = new KonumAnaliziService(
            new FakePoiRepository(db),
            new FakePoiCategoryRepository(db),
            iller);

        // "Eğitim" kökü testlerde doğrudan kullanılmıyor ama ağacın iki dallı
        // olması, kriter eşlemesinin yanlış dala sıçramadığını da gösteriyor.
        _ = egitim;

        return new Ortam(db, analiz, iller, eczane.Id, okul.Id, saglik.Id);
    }

    /// <summary>Belirtilen konuma, belirtilen kategoride bir POI koyar.</summary>
    private static void PoiEkle(SahteVeritabani db, int kategoriId, double x, double y, string? isim = null)
        => db.Poiler.Add(new Poi
        {
            Id = db.SonrakiPoiId(),
            Isim = isim ?? $"POI {x},{y}",
            KategoriId = kategoriId,
            Geom = WktConverter.Read<Point>($"POINT ({x.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                                            $"{y.ToString(System.Globalization.CultureInfo.InvariantCulture)})"),
            IsActive = true,
        });

    private static KonumAnaliziRequestDto Istek(string? wkt, params (int Kategori, int Agirlik)[] kriterler)
        => new()
        {
            Wkt = wkt,
            Kriterler = kriterler
                .Select(k => new AnalizKriteriDto { KategoriId = k.Kategori, Agirlik = k.Agirlik })
                .ToList(),
        };

    // ==================================================================
    //  1) DOĞRULAMA — ödev metnindeki sayısal kurallar
    // ==================================================================

    [Fact]
    public async Task Kriter_TekTaneyse_Reddedilir()
    {
        var o = Kur();

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Analiz.CalistirAsync(Istek(Kare, (o.EczaneId, 100))));

        Assert.Contains("en az 2", hata.Message);
    }

    [Fact]
    public async Task Kriter_AltidanFazlaysa_Reddedilir()
    {
        var o = Kur();

        // Altı kriter için dört kategori yetmiyor; iki tane daha açıyoruz.
        var ek1 = o.Db.PoiKategoriEkle("Market");
        var ek2 = o.Db.PoiKategoriEkle("Park");
        var ek3 = o.Db.PoiKategoriEkle("Spor");

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Analiz.CalistirAsync(Istek(Kare,
                (o.EczaneId, 20), (o.OkulId, 20), (o.SaglikId, 20),
                (ek1.Id, 20), (ek2.Id, 10), (ek3.Id, 10))));

        Assert.Contains("en fazla 5", hata.Message);
    }

    /// <summary>
    /// Ödev metninin en açık kuralı: "Puan toplamı 100'den farklı ise analiz
    /// başlatılmamalıdır." Hem eksik hem fazla toplam sınanıyor — birini
    /// kontrol edip diğerini atlayan bir kod bu testlerden birinde takılır.
    /// </summary>
    [Theory]
    [InlineData(40, 40)]   // toplam 80 — eksik
    [InlineData(60, 60)]   // toplam 120 — fazla
    [InlineData(50, 49)]   // toplam 99 — bir puan bile kaçamaz
    public async Task Agirlik_Toplami100Degilse_Reddedilir(int birinci, int ikinci)
    {
        var o = Kur();

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Analiz.CalistirAsync(Istek(Kare, (o.EczaneId, birinci), (o.OkulId, ikinci))));

        Assert.Contains("100", hata.Message);
    }

    [Fact]
    public async Task Agirlik_Toplami100ise_Calisir()
    {
        var o = Kur();
        PoiEkle(o.Db, o.EczaneId, 2, 2);
        PoiEkle(o.Db, o.OkulId, 3, 3);

        var sonuc = await o.Analiz.CalistirAsync(Istek(Kare, (o.EczaneId, 70), (o.OkulId, 30)));

        Assert.Equal(2, sonuc.Kriterler.Count);
        Assert.Equal(2, sonuc.ToplamPoi);
        Assert.True(sonuc.Izgara.EnYuksekSkor > 0);
    }

    [Fact]
    public async Task AyniKategori_IkiKerede_Reddedilir()
    {
        var o = Kur();

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Analiz.CalistirAsync(Istek(Kare, (o.EczaneId, 50), (o.EczaneId, 50))));

        Assert.Contains("birden fazla", hata.Message);
    }

    // ==================================================================
    //  2) ALAN SEÇİMİ — iki yol, biri zorunlu
    // ==================================================================

    [Fact]
    public async Task Alan_HicSecilmemisse_Reddedilir()
    {
        var o = Kur();

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Analiz.CalistirAsync(Istek(null, (o.EczaneId, 50), (o.OkulId, 50))));

        Assert.Contains("hedef bölge", hata.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Alan_HemIlHemCizimVerilirse_Reddedilir()
    {
        var o = Kur();
        o.Iller.Ekle(6, "Ankara", Kare);

        var istek = Istek(Kare, (o.EczaneId, 50), (o.OkulId, 50));
        istek.IlPlakalari = new List<int> { 6 };

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Analiz.CalistirAsync(istek));

        Assert.Contains("ikisi birden", hata.Message);
    }

    /// <summary>
    /// İl listesinden seçim: alan, seçilen illerin BİRLEŞİMİ olmalı ve sonuç
    /// hangi illerin analiz edildiğini adıyla söylemeli.
    /// </summary>
    [Fact]
    public async Task Alan_IlListesindenSecilebilir()
    {
        var o = Kur();
        o.Iller.Ekle(6, "Ankara", "POLYGON ((0 0, 5 0, 5 5, 0 5, 0 0))");
        o.Iller.Ekle(42, "Konya", "POLYGON ((5 0, 10 0, 10 5, 5 5, 5 0))");

        PoiEkle(o.Db, o.EczaneId, 2, 2);     // Ankara
        PoiEkle(o.Db, o.OkulId, 7, 2);       // Konya
        PoiEkle(o.Db, o.EczaneId, 2, 8);     // İKİSİNİN DE DIŞINDA

        var istek = Istek(null, (o.EczaneId, 50), (o.OkulId, 50));
        istek.IlPlakalari = new List<int> { 6, 42 };

        var sonuc = await o.Analiz.CalistirAsync(istek);

        Assert.Equal("Ankara, Konya", sonuc.AlanAdi);
        // Dışarıdaki eczane sayılmadı.
        Assert.Equal(2, sonuc.ToplamPoi);
    }

    /// <summary>
    /// Ödev metni: "Analiz yalnızca seçilen alan içerisindeki POI'ler üzerinde
    /// çalışmalıdır." Alanın dışındaki POI ne sayıya girmeli ne de yüzeyi
    /// etkilemeli.
    /// </summary>
    [Fact]
    public async Task Analiz_AlanDisindakiPoileriSaymaz()
    {
        var o = Kur();
        PoiEkle(o.Db, o.EczaneId, 2, 2, "İçerideki Eczane");
        PoiEkle(o.Db, o.OkulId, 3, 3, "İçerideki Okul");
        PoiEkle(o.Db, o.EczaneId, 50, 50, "Uzaktaki Eczane");   // kareden çok uzakta

        var sonuc = await o.Analiz.CalistirAsync(
            Istek(Kare, (o.EczaneId, 50), (o.OkulId, 50)));

        Assert.Equal(2, sonuc.ToplamPoi);
        Assert.Equal(1, sonuc.Kriterler.Single(k => k.KategoriId == o.EczaneId).PoiSayisi);
    }

    /// <summary>
    /// Kriterlerden hiçbirine girmeyen kategori, alanın içinde bile olsa
    /// hesaba katılmamalı: kullanıcı "eczane ve okul" dediyse kütüphanenin
    /// yüzeyi etkilemesi sonucu sessizce bozardı.
    /// </summary>
    [Fact]
    public async Task Analiz_KriterDisiKategoriyiHesabaKatmaz()
    {
        var o = Kur();
        var kutuphane = o.Db.PoiKategoriEkle("Kütüphane");

        PoiEkle(o.Db, o.EczaneId, 2, 2);
        PoiEkle(o.Db, o.OkulId, 3, 3);
        PoiEkle(o.Db, kutuphane.Id, 4, 4);

        var sonuc = await o.Analiz.CalistirAsync(
            Istek(Kare, (o.EczaneId, 50), (o.OkulId, 50)));

        Assert.Equal(2, sonuc.ToplamPoi);
    }

    /// <summary>
    /// Kök kategori seçilince alt kategorilerdeki POI'ler de sayılmalı.
    /// POI'ler daima yaprağa bağlandığı için bu olmasaydı "Sağlık" kriteri
    /// her zaman sıfır POI bulurdu.
    /// </summary>
    [Fact]
    public async Task KokKategori_AltindakiPoileriDeKapsar()
    {
        var o = Kur();
        PoiEkle(o.Db, o.EczaneId, 2, 2);     // Sağlık › Eczane
        PoiEkle(o.Db, o.EczaneId, 2.5, 2.5);
        PoiEkle(o.Db, o.OkulId, 8, 8);

        var sonuc = await o.Analiz.CalistirAsync(
            Istek(Kare, (o.SaglikId, 50), (o.OkulId, 50)));

        Assert.Equal(2, sonuc.Kriterler.Single(k => k.KategoriId == o.SaglikId).PoiSayisi);
    }

    /// <summary>
    /// Pasif POI analize girmemeli: askıya alınmış bir kayıt haritada durabilir
    /// ama "burada bir eczane var" demenin dayanağı olamaz.
    /// </summary>
    [Fact]
    public async Task PasifPoi_AnalizeGirmez()
    {
        var o = Kur();
        PoiEkle(o.Db, o.EczaneId, 2, 2);
        PoiEkle(o.Db, o.OkulId, 3, 3);

        o.Db.Poiler[0].IsActive = false;

        var sonuc = await o.Analiz.CalistirAsync(
            Istek(Kare, (o.EczaneId, 50), (o.OkulId, 50)));

        Assert.Equal(1, sonuc.ToplamPoi);
        Assert.Equal(0, sonuc.Kriterler.Single(k => k.KategoriId == o.EczaneId).PoiSayisi);
    }

    [Fact]
    public async Task Alan_PoligonDegilse_Reddedilir()
    {
        var o = Kur();

        await Assert.ThrowsAsync<WktFormatException>(
            () => o.Analiz.CalistirAsync(
                Istek("LINESTRING (0 0, 5 5)", (o.EczaneId, 50), (o.OkulId, 50))));
    }

    // ==================================================================
    //  3) HESAP — ağırlık gerçekten belirleyici mi?
    // ==================================================================

    /// <summary>
    /// TESTİN KALBİ.
    ///
    /// Kurgu: sol altta (2,2) bir eczane kümesi, sağ üstte (8,8) bir okul
    /// kümesi var. Ağırlığın büyük kısmı eczaneye verilirse en iyi aday sol
    /// alta, okula verilirse sağ üste düşmeli.
    ///
    /// Bu test geçmiyorsa analiz "çalışıyor" ama ağırlıkları yok sayıyor
    /// demektir — ödevin asıl istediği şey tam olarak bu davranış.
    /// </summary>
    [Fact]
    public async Task Agirlik_Degisince_EnIyiAdayYerDegistirir()
    {
        var o = Kur();

        // Sol alt: üç eczane. Sağ üst: üç okul.
        PoiEkle(o.Db, o.EczaneId, 2, 2);
        PoiEkle(o.Db, o.EczaneId, 2.2, 1.9);
        PoiEkle(o.Db, o.EczaneId, 1.8, 2.1);

        PoiEkle(o.Db, o.OkulId, 8, 8);
        PoiEkle(o.Db, o.OkulId, 8.2, 7.9);
        PoiEkle(o.Db, o.OkulId, 7.8, 8.1);

        var eczaneAgirlikli = await o.Analiz.CalistirAsync(
            Istek(Kare, (o.EczaneId, 90), (o.OkulId, 10)));

        var okulAgirlikli = await o.Analiz.CalistirAsync(
            Istek(Kare, (o.EczaneId, 10), (o.OkulId, 90)));

        var eczaneAdayi = eczaneAgirlikli.Adaylar[0];
        var okulAdayi = okulAgirlikli.Adaylar[0];

        // Eczane ağırlıklı analizde en iyi nokta sol altta (küçük koordinat),
        // okul ağırlıklıda sağ üstte (büyük koordinat).
        Assert.True(eczaneAdayi.Boylam < 5, $"Eczane ağırlıklı aday sol yarıda olmalı, boylam: {eczaneAdayi.Boylam}");
        Assert.True(eczaneAdayi.Enlem < 5, $"Eczane ağırlıklı aday alt yarıda olmalı, enlem: {eczaneAdayi.Enlem}");
        Assert.True(okulAdayi.Boylam > 5, $"Okul ağırlıklı aday sağ yarıda olmalı, boylam: {okulAdayi.Boylam}");
        Assert.True(okulAdayi.Enlem > 5, $"Okul ağırlıklı aday üst yarıda olmalı, enlem: {okulAdayi.Enlem}");
    }

    /// <summary>
    /// Kriter başına normalleştirme sınanıyor: 30 eczaneye karşı 2 okul olsa
    /// bile, ağırlığın tamamına yakını okuldaysa sonuç okulun tarafına
    /// eğilmeli. Normalleştirme olmasaydı ham eczane yoğunluğu (sayıca 15
    /// katı) her ağırlıkta baskın çıkardı.
    /// </summary>
    [Fact]
    public async Task Normallestirme_SayicaAzKriteriEzdirmez()
    {
        var o = Kur();

        for (var i = 0; i < 30; i++)
        {
            // Sol altta sıkışık bir eczane bulutu
            PoiEkle(o.Db, o.EczaneId, 2 + i % 6 * 0.1, 2 + i / 6 * 0.1);
        }

        PoiEkle(o.Db, o.OkulId, 8, 8);
        PoiEkle(o.Db, o.OkulId, 8.1, 8.1);

        var sonuc = await o.Analiz.CalistirAsync(
            Istek(Kare, (o.EczaneId, 5), (o.OkulId, 95)));

        Assert.True(sonuc.Adaylar[0].Boylam > 5,
            $"Ağırlığın %95'i okuldayken en iyi aday okulların yanında olmalı, boylam: {sonuc.Adaylar[0].Boylam}");
    }

    /// <summary>
    /// Alanın DIŞINDA kalan hücreler -1 ile işaretlenmeli. "0" ile
    /// karıştırılsaydı harita, analiz edilmemiş bölgeyi "puanı sıfır olan
    /// bölge" gibi boyar ve alan sınırı ekranda kaybolurdu.
    /// </summary>
    [Fact]
    public async Task Izgara_AlanDisiHucreleriIsaretler()
    {
        var o = Kur();
        PoiEkle(o.Db, o.EczaneId, 2, 2);
        PoiEkle(o.Db, o.OkulId, 3, 3);

        // Üçgen: zarfının yaklaşık yarısı alanın dışında kalıyor.
        var ucgen = "POLYGON ((0 0, 10 0, 0 10, 0 0))";

        var sonuc = await o.Analiz.CalistirAsync(
            Istek(ucgen, (o.EczaneId, 50), (o.OkulId, 50)));

        var disarida = sonuc.Izgara.Degerler.Count(d => d < 0);
        var icerde = sonuc.Izgara.Degerler.Length - disarida;

        Assert.True(disarida > 0, "Üçgenin dışında kalan hücreler işaretlenmeliydi.");
        Assert.True(icerde > 0, "Üçgenin içinde kalan hücreler değer almalıydı.");

        // Üçgen zarfın yarısı: iki taraf da toplamın üçte birinden fazla olmalı.
        Assert.True(disarida > sonuc.Izgara.Degerler.Length / 3);
        Assert.True(icerde > sonuc.Izgara.Degerler.Length / 3);
    }

    /// <summary>
    /// Bir kriterin alan içinde hiç POI'si yoksa analiz ÇALIŞMALI (hata
    /// vermemeli) ama kullanıcı bunu görmeli: sayı 0 olarak dönüyor.
    /// Sessizce çalışsaydı kullanıcı, ağırlık verdiği bir kriterin hiçbir
    /// etkisi olmadığını anlayamazdı.
    /// </summary>
    [Fact]
    public async Task Kriter_AlandaPoisiYoksa_SifirIleBildirilir()
    {
        var o = Kur();
        PoiEkle(o.Db, o.EczaneId, 2, 2);

        var sonuc = await o.Analiz.CalistirAsync(
            Istek(Kare, (o.EczaneId, 50), (o.OkulId, 50)));

        Assert.Equal(0, sonuc.Kriterler.Single(k => k.KategoriId == o.OkulId).PoiSayisi);
        Assert.Equal(1, sonuc.ToplamPoi);
    }

    /// <summary>
    /// Aday konumlar birbirine YAPIŞMAMALI. Ayrık mesafe kuralı olmasaydı
    /// ilk beş aday hep aynı tepenin komşu hücreleri olur, kullanıcı beş
    /// öneri gördüğünü sanarken tek bir yer görürdü.
    /// </summary>
    [Fact]
    public async Task Adaylar_BirbirineYapismaz()
    {
        var o = Kur();

        // Üç ayrı köşede birer küme
        foreach (var (x, y) in new[] { (2.0, 2.0), (8.0, 2.0), (5.0, 8.0) })
        {
            PoiEkle(o.Db, o.EczaneId, x, y);
            PoiEkle(o.Db, o.OkulId, x + 0.1, y + 0.1);
        }

        var sonuc = await o.Analiz.CalistirAsync(
            Istek(Kare, (o.EczaneId, 50), (o.OkulId, 50)));

        Assert.True(sonuc.Adaylar.Count >= 3, "Üç ayrı küme için en az üç aday beklenirdi.");

        for (var i = 0; i < sonuc.Adaylar.Count; i++)
        {
            for (var j = i + 1; j < sonuc.Adaylar.Count; j++)
            {
                var a = sonuc.Adaylar[i];
                var b = sonuc.Adaylar[j];
                var uzaklik = Math.Sqrt(
                    Math.Pow(a.Boylam - b.Boylam, 2) + Math.Pow(a.Enlem - b.Enlem, 2));

                Assert.True(uzaklik > 0.5, $"{i + 1}. ve {j + 1}. aday çok yakın: {uzaklik:F2}°");
            }
        }
    }

    /// <summary>
    /// Aday konum, her kriter için EN YAKIN POI'yi ve uzaklığını taşımalı —
    /// "burası neden iyi?" sorusunun somut cevabı bu.
    /// </summary>
    [Fact]
    public async Task Aday_HerKriterIcinEnYakinPoiyiBildirir()
    {
        var o = Kur();
        PoiEkle(o.Db, o.EczaneId, 5, 5, "Merkez Eczanesi");
        PoiEkle(o.Db, o.OkulId, 5.1, 5.1, "Merkez İlkokulu");

        var sonuc = await o.Analiz.CalistirAsync(
            Istek(Kare, (o.EczaneId, 50), (o.OkulId, 50)));

        var aday = sonuc.Adaylar[0];
        Assert.Equal(2, aday.Mesafeler.Count);

        var eczane = aday.Mesafeler.Single(m => m.KategoriId == o.EczaneId);
        Assert.Equal("Merkez Eczanesi", eczane.PoiAdi);
        Assert.NotNull(eczane.MesafeMetre);

        // (5,5) çevresindeki bir hücrede duruyoruz; en yakın eczane birkaç
        // kilometreden uzakta olamaz (hücre ≈ 11 km, çekirdek onun katı).
        Assert.True(eczane.MesafeMetre < 50_000);
    }

    // ==================================================================
    //  Hesap motorunun kendisi — veritabanı olmadan
    // ==================================================================

    /// <summary>
    /// Motor doğrudan çağrılıyor: tek POI'nin çevresinde skor, uzaklaştıkça
    /// AZALMALI. Gauss çekirdeğinin tanımı bu; bozulursa ısı haritası
    /// "yoğunluk" olmaktan çıkar.
    /// </summary>
    [Fact]
    public void Motor_SkorUzaklastikcaAzalir()
    {
        var alan = WktConverter.Read<Polygon>(Kare);
        var agirliklar = new[] { new AnalizAgirligi(1, 100) };
        var poiler = new[] { new AnalizPoisi(1, "Tek POI", 5, 5) };

        var sonuc = AgirlikliIsiIzgarasi.Hesapla(alan, agirliklar, poiler);

        // Merkeze en yakın hücre ile köşedeki hücre karşılaştırılıyor.
        var merkezSatir = sonuc.Satir / 2;
        var merkezSutun = sonuc.Sutun / 2;

        var merkez = sonuc.Degerler[merkezSatir * sonuc.Sutun + merkezSutun];
        var kose = sonuc.Degerler[0];

        Assert.True(merkez > 0.5, $"POI'nin üstündeki hücre yüksek skor almalıydı: {merkez}");
        Assert.True(kose >= 0 && kose < 0.05, $"Uzak köşe neredeyse sıfır olmalıydı: {kose}");
    }

    /// <summary>
    /// Izgara SATIR 0 = EN ÜST kuralı. Arayüz değerleri doğrudan tuvale
    /// çiziyor; sıra ters olsaydı ısı haritası dikeyde AYNALANIRDI ve bu,
    /// simetrik verilerde fark edilmezdi — bu yüzden ayrı bir test.
    /// </summary>
    [Fact]
    public void Motor_IlkSatirEnUsttekiEnlemdir()
    {
        var alan = WktConverter.Read<Polygon>(Kare);
        var agirliklar = new[] { new AnalizAgirligi(1, 100) };

        // POI karenin ÜST kenarına yakın (enlem 9).
        var poiler = new[] { new AnalizPoisi(1, "Kuzeydeki", 5, 9) };

        var sonuc = AgirlikliIsiIzgarasi.Hesapla(alan, agirliklar, poiler);

        // TEK satır değil, ÜST ÇEYREK ile ALT ÇEYREK karşılaştırılıyor:
        // çekirdeğin kesme yarıçapı 6 hücre, yani 96 satırlık bir ızgarada
        // en uçtaki satır POI'den uzak kalıp sıfır okunabiliyor. Sıfıra sıfır
        // karşılaştırmak testi, orantı doğruyken bile başarısız gösterirdi.
        var ceyrek = Math.Max(1, sonuc.Satir / 4);

        double BantToplami(int ilkSatir, int sonSatir)
        {
            var toplam = 0.0;
            for (var r = ilkSatir; r < sonSatir; r++)
            {
                for (var c = 0; c < sonuc.Sutun; c++)
                {
                    toplam += Math.Max(0, sonuc.Degerler[r * sonuc.Sutun + c]);
                }
            }
            return toplam;
        }

        var ustBant = BantToplami(0, ceyrek);
        var altBant = BantToplami(sonuc.Satir - ceyrek, sonuc.Satir);

        Assert.True(ustBant > altBant,
            $"Kuzeydeki POI dizinin İLK satırlarını ısıtmalıydı (satır 0 = en üst). " +
            $"Üst bant: {ustBant:F2}, alt bant: {altBant:F2}");
    }
}
