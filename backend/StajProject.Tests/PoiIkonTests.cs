using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using StajProject.Business.Poiler;
using StajProject.Business.Services;
using StajProject.Business.Validation;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 15 testleri: KATEGORİYE ÖZGÜ SİMGELER.
///
/// Bu modülün riski "hata verir" değil, "sessizce yanlış çizer":
///   • bozuk bir SVG yolu → simge boş çıkar, kimse fark etmez
///   • dosya yüklenmeden stil yazılır → simge bazen görünür bazen görünmez
///   • miras kurulmaz → alt kategoriler varsayılan iğneye düşer
/// Üçü de aşağıda teste bağlandı.
/// </summary>
public class PoiIkonTests
{
    // ==================================================================
    //  Katalog
    // ==================================================================

    /// <summary>
    /// Her ikonun en az bir çizim parçası olmalı ve parçalar GEÇERLİ path
    /// verisi taşımalı.
    ///
    /// "Geçerli"yi burada tam bir SVG ayrıştırıcısıyla sınamıyoruz; bir
    /// komutla başlayıp yalnızca izinli karakterler içermesi, elle yazılmış
    /// bir yolda olabilecek hataların (eksik komut, yapıştırma artığı,
    /// Türkçe karakter) hepsini yakalıyor.
    /// </summary>
    [Fact]
    public void Katalog_ButunIkonlarGecerliYolTasir()
    {
        Assert.NotEmpty(PoiIkonlari.Tumu);

        foreach (var ikon in PoiIkonlari.Tumu)
        {
            Assert.False(string.IsNullOrWhiteSpace(ikon.Anahtar), $"{ikon.Ad}: anahtar boş");
            Assert.False(string.IsNullOrWhiteSpace(ikon.Ad), $"{ikon.Anahtar}: ad boş");
            Assert.NotEmpty(ikon.Parcalar);

            foreach (var parca in ikon.Parcalar)
            {
                Assert.False(string.IsNullOrWhiteSpace(parca.D), $"{ikon.Anahtar}: boş yol");

                // SVG yolları bir "moveto" ile başlar — M ya da m.
                Assert.True(parca.D.TrimStart()[0] is 'M' or 'm',
                    $"{ikon.Anahtar}: yol M ile başlamıyor: {parca.D[..Math.Min(20, parca.D.Length)]}");

                Assert.Matches(new Regex(@"^[MmLlHhVvCcSsQqTtAaZz0-9\s.,\-]+$"), parca.D);
            }

            // Her ikonun en az bir RENKLİ parçası olmalı: hepsi beyaz olsaydı
            // simge beyaz zeminde görünmezdi.
            Assert.Contains(ikon.Parcalar, p => !p.Beyaz);
        }
    }

    [Fact]
    public void Katalog_AnahtarlarBenzersiz()
    {
        var anahtarlar = PoiIkonlari.Tumu.Select(i => i.Anahtar).ToList();
        Assert.Equal(anahtarlar.Count, anahtarlar.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Katalog_VarsayilanIkonTanimli()
        => Assert.NotNull(PoiIkonlari.Bul(PoiIkonlari.Varsayilan));

    /// <summary>
    /// Üretilen SVG AYRIŞTIRILABİLİR XML olmalı.
    ///
    /// Bu test bir kez gerçek bir hatayı yakalayacak konumda: yol verisine
    /// tırnak ya da &amp; gibi bir karakter sızarsa dosya bozulur, GeoServer
    /// onu sessizce çizemez ve haritada simge yerine boşluk kalır.
    /// </summary>
    [Fact]
    public void SvgUret_GecerliXmlUretir()
    {
        foreach (var ikon in PoiIkonlari.Tumu)
        {
            var svg = PoiIkonlari.SvgUret(ikon, "#d64550");

            var belge = XDocument.Parse(svg);   // bozuksa burada patlar
            var kok = belge.Root!;

            Assert.Equal("svg", kok.Name.LocalName);
            Assert.Equal(PoiIkonlari.ViewBox, kok.Attribute("viewBox")!.Value);

            // Gövde + beyaz kontur: parça sayısından fazla path olmalı.
            var yollar = kok.Elements().Where(e => e.Name.LocalName == "path").ToList();
            Assert.True(yollar.Count >= ikon.Parcalar.Count, $"{ikon.Anahtar}: yol sayısı az");

            // Kategori rengi gerçekten kullanılmış mı?
            Assert.Contains(yollar, y => y.Attribute("fill")?.Value == "#d64550");
        }
    }

    // ==================================================================
    //  Miras
    // ==================================================================

    private static Dictionary<int, PoiCategory> Sozluk(params PoiCategory[] kategoriler)
        => kategoriler.ToDictionary(k => k.Id);

    [Fact]
    public void EtkinIkon_KendiIkonuVarsaOnuKullanir()
    {
        var kok = new PoiCategory { Id = 1, Ad = "Sağlık", Ikon = "kalp" };
        var alt = new PoiCategory { Id = 2, Ad = "Eczane", ParentId = 1, Ikon = "eczane" };

        Assert.Equal("eczane",
            PoiIkonlari.EtkinAnahtar(alt, Sozluk(kok, alt), k => k.Ikon, k => k.ParentId));
    }

    /// <summary>
    /// Simgesi olmayan çocuk ATASININ simgesini alır. Bu kural olmasaydı
    /// "Yeme-İçme"ye çatal-bıçak verip altına "Kebapçı" açan yönetici,
    /// yeni kategoriyi haritada anlamsız bir iğneyle görürdü.
    /// </summary>
    [Fact]
    public void EtkinIkon_KendiYoksaAtadanMirasAlir()
    {
        var kok = new PoiCategory { Id = 1, Ad = "Yeme-İçme", Ikon = "catal-bicak" };
        var alt = new PoiCategory { Id = 2, Ad = "Kebapçı", ParentId = 1 };

        Assert.Equal("catal-bicak",
            PoiIkonlari.EtkinAnahtar(alt, Sozluk(kok, alt), k => k.Ikon, k => k.ParentId));
    }

    [Fact]
    public void EtkinIkon_ZincirdeHicYoksaVarsayilanaDuser()
    {
        var kok = new PoiCategory { Id = 1, Ad = "Ulaşım" };
        var alt = new PoiCategory { Id = 2, Ad = "Durak", ParentId = 1 };

        Assert.Equal(PoiIkonlari.Varsayilan,
            PoiIkonlari.EtkinAnahtar(alt, Sozluk(kok, alt), k => k.Ikon, k => k.ParentId));
    }

    /// <summary>
    /// Tanınmayan bir anahtar veriye sızmışsa (elle UPDATE, eski sürüm)
    /// miras zinciri DURMAMALI, yukarı yürümeye devam etmeli.
    /// </summary>
    [Fact]
    public void EtkinIkon_TaninmayanAnahtariAtlar()
    {
        var kok = new PoiCategory { Id = 1, Ad = "Sağlık", Ikon = "kalp" };
        var alt = new PoiCategory { Id = 2, Ad = "Eczane", ParentId = 1, Ikon = "olmayan-simge" };

        Assert.Equal("kalp",
            PoiIkonlari.EtkinAnahtar(alt, Sozluk(kok, alt), k => k.Ikon, k => k.ParentId));
    }

    /// <summary>
    /// Veriye döngü sızarsa (A'nın atası B, B'nin atası A) yukarı yürüyüş
    /// sonsuza kadar dönmemeli. Servis katmanı döngüyü engelliyor ama bu
    /// fonksiyon elle düzenlenmiş bir veritabanına da dayanmalı.
    /// </summary>
    [Fact]
    public void EtkinIkon_DonguyeGirmez()
    {
        var a = new PoiCategory { Id = 1, Ad = "A", ParentId = 2 };
        var b = new PoiCategory { Id = 2, Ad = "B", ParentId = 1 };

        Assert.Equal(PoiIkonlari.Varsayilan,
            PoiIkonlari.EtkinAnahtar(a, Sozluk(a, b), k => k.Ikon, k => k.ParentId));
    }

    // ==================================================================
    //  Stil üretimi
    // ==================================================================

    private static SahteVeritabani AgacKur()
    {
        var db = new SahteVeritabani();

        var yeme = db.PoiKategoriEkle("Yeme-İçme");
        yeme.Ikon = "catal-bicak";
        var kafe = db.PoiKategoriEkle("Kafe", yeme.Id);
        kafe.Ikon = "fincan";
        db.PoiKategoriEkle("Kebapçı", yeme.Id);   // simgesiz → miras

        return db;
    }

    [Fact]
    public void Stil_KategorininSimgesiniSldyeYazar()
    {
        var db = AgacKur();
        var stiller = PoiStilUretici.Uret(db.PoiKategorileri);

        var kafe = stiller.Single(s => s.Ad == "Kafe");

        Assert.Equal("fincan", kafe.Ikon);
        Assert.Equal(kafe.StilAdi + ".svg", kafe.SvgDosyaAdi);

        // SLD, SVG dosyasını ExternalGraphic ile göstermeli…
        Assert.Contains("<ExternalGraphic>", kafe.Sld);
        Assert.Contains(kafe.SvgDosyaAdi, kafe.Sld);
        Assert.Contains("image/svg+xml", kafe.Sld);

        // …ama geometrik işaret YEDEK olarak kalmalı: SVG okunamazsa POI
        // haritadan tamamen kaybolmasın.
        Assert.Contains("<WellKnownName>", kafe.Sld);
    }

    [Fact]
    public void Stil_SimgesizKategoriAtanınSimgesiniAlir()
    {
        var db = AgacKur();
        var stiller = PoiStilUretici.Uret(db.PoiKategorileri);

        Assert.Equal("catal-bicak", stiller.Single(s => s.Ad == "Kebapçı").Ikon);
    }

    /// <summary>
    /// Üretilen SLD'nin tamamı geçerli XML olmalı. ExternalGraphic elemanları
    /// elle metin olarak ekleniyor; bir kapanış etiketi unutulsa GeoServer
    /// stili hiç kabul etmez ve bütün POI katmanı çizilmez.
    /// </summary>
    /// <summary>
    /// ÜÇÜNCÜ SEVİYEDEKİ kategori de kendi stilini almalı.
    ///
    /// Önceki üretici yalnızca kökleri ve DOĞRUDAN çocuklarını geziyordu;
    /// "Eğitim › Kütüphane › B blok" gibi bir düğüm hiç stil almıyor, yedek
    /// "Diğer" stiliyle çiziliyordu. Renkler birbirine yakın olduğu için bu
    /// gözden kaçıyordu; simgeler gelince açıkça görünür oldu (kitap yerine
    /// gri iğne). Bu test o gerilemeyi kalıcı olarak kapatıyor.
    /// </summary>
    [Fact]
    public void Stil_UcuncuSeviyeKategoriDeStilAlirVeSimgeyiMirasAlir()
    {
        var db = new SahteVeritabani();

        var egitim = db.PoiKategoriEkle("Eğitim");
        egitim.Ikon = "mezuniyet";
        var kutuphane = db.PoiKategoriEkle("Kütüphane", egitim.Id);
        kutuphane.Ikon = "kitap";
        var bBlok = db.PoiKategoriEkle("B blok", kutuphane.Id);   // simgesiz

        var stiller = PoiStilUretici.Uret(db.PoiKategorileri);

        var stil = stiller.SingleOrDefault(s => s.KategoriId == bBlok.Id);
        Assert.NotNull(stil);

        // Simge en yakın atadan (Kütüphane) geliyor, kökten değil.
        Assert.Equal("kitap", stil!.Ikon);

        // Tam yol üç parçalı olmalı — stil başlığı hiyerarşiyi göstersin.
        Assert.Equal("Eğitim › Kütüphane › B blok", stil.TamYol);
    }

    [Fact]
    public void Stil_UretilenSldGecerliXml()
    {
        var db = AgacKur();

        foreach (var stil in PoiStilUretici.Uret(db.PoiKategorileri))
        {
            var belge = XDocument.Parse(stil.Sld);
            Assert.Equal("StyledLayerDescriptor", belge.Root!.Name.LocalName);
        }
    }

    [Fact]
    public void Stil_HerStilinSvgIcerigiUretilir()
    {
        var db = AgacKur();

        foreach (var stil in PoiStilUretici.Uret(db.PoiKategorileri))
        {
            Assert.False(string.IsNullOrWhiteSpace(stil.Svg), $"{stil.StilAdi}: SVG boş");
            Assert.Contains(stil.Renk, stil.Svg);
            XDocument.Parse(stil.Svg);
        }
    }

    // ==================================================================
    //  GeoServer'a yazma
    // ==================================================================

    private static (PoiStyleService Servis, FakeGeoServerClient Gs) ServisKur(SahteVeritabani db)
    {
        var gs = new FakeGeoServerClient();
        return (new PoiStyleService(
            new FakePoiCategoryRepository(db), gs, NullLogger<PoiStyleService>.Instance), gs);
    }

    /// <summary>
    /// Yenileme, her stilin SVG dosyasını GeoServer'ın stil dizinine yazmalı.
    /// Yazılmazsa SLD var olmayan bir dosyayı gösterir; GeoServer hata
    /// vermeden simgesiz çizer — sessiz bir arıza.
    /// </summary>
    [Fact]
    public async Task Yenileme_SimgeDosyalariniDaYazar()
    {
        var db = AgacKur();
        var (servis, gs) = ServisKur(db);

        var sonuc = await servis.YenileAsync();

        Assert.NotEmpty(gs.StilKaynaklari);

        foreach (var stil in sonuc.Stiller)
        {
            var dosya = stil.Stil + ".svg";
            Assert.True(gs.StilKaynaklari.ContainsKey(dosya), $"{dosya} yazılmadı");
            XDocument.Parse(gs.StilKaynaklari[dosya]);
        }
    }

    [Fact]
    public async Task Yenileme_StilDtosuCizimParcalariniTasir()
    {
        var db = AgacKur();
        var (servis, _) = ServisKur(db);

        var stiller = await servis.YenileAsync();
        var kafe = stiller.Stiller.Single(s => s.Ad == "Kafe");

        Assert.Equal("fincan", kafe.Ikon);
        Assert.NotEmpty(kafe.IkonParcalari);
        Assert.All(kafe.IkonParcalari, p => Assert.False(string.IsNullOrWhiteSpace(p.D)));
    }

    /// <summary>
    /// GeoServer kapalıyken yenileme hata fırlatmalı — sessiz yenileme onu
    /// yakalıyor ama açık çağrı (yönetim panelindeki düğme) sonucu bilmeli.
    /// </summary>
    [Fact]
    public async Task Yenileme_GeoServerKapaliysaHataVerir()
    {
        var db = AgacKur();
        var (servis, gs) = ServisKur(db);
        gs.StilYazmaHataVersin = true;

        await Assert.ThrowsAsync<StajProject.DataAccess.GeoServer.GeoServerErisimException>(
            () => servis.YenileAsync());
    }

    // ==================================================================
    //  Kategori servisi — doğrulama
    // ==================================================================

    private static PoiCategoryService KategoriServisi(SahteVeritabani db)
        => new(new FakePoiCategoryRepository(db), new FakePoiRepository(db));

    [Fact]
    public async Task Kategori_TaninmayanSimgeReddedilir()
    {
        var db = new SahteVeritabani();
        var servis = KategoriServisi(db);

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => servis.CreateAsync(new Business.DTOs.PoiKategoriSaveDto
            {
                Ad = "Ulaşım",
                Ikon = "uydurma-simge",
            }));

        Assert.Contains("Tanınmayan simge", hata.Message);
    }

    [Fact]
    public async Task Kategori_SimgeKaydedilirVeDtodaDoner()
    {
        var db = new SahteVeritabani();
        var servis = KategoriServisi(db);

        var olusan = await servis.CreateAsync(new Business.DTOs.PoiKategoriSaveDto
        {
            Ad = "Ulaşım",
            Ikon = "otobus",
        });

        Assert.Equal("otobus", olusan.Ikon);
        Assert.Equal("otobus", olusan.EtkinIkon);
    }

    /// <summary>
    /// Simge seçilmemişse <c>Ikon</c> null kalmalı ama <c>EtkinIkon</c>
    /// dolu gelmeli. İkisi ayrı alan olduğu için düzenleme formu "seçilmedi"
    /// durumunu koruyabiliyor.
    /// </summary>
    [Fact]
    public async Task Kategori_SimgesizKategoriEtkinIkonuDoldurur()
    {
        var db = new SahteVeritabani();
        var servis = KategoriServisi(db);

        var kok = await servis.CreateAsync(new Business.DTOs.PoiKategoriSaveDto
        {
            Ad = "Sağlık",
            Ikon = "kalp",
        });

        var alt = await servis.CreateAsync(new Business.DTOs.PoiKategoriSaveDto
        {
            Ad = "Eczane",
            ParentId = kok.Id,
        });

        Assert.Null(alt.Ikon);
        Assert.Equal("kalp", alt.EtkinIkon);
    }

    [Fact]
    public async Task Kategori_SimgeGuncellenebilir()
    {
        var db = new SahteVeritabani();
        var servis = KategoriServisi(db);

        var olusan = await servis.CreateAsync(new Business.DTOs.PoiKategoriSaveDto { Ad = "Kafe" });

        var guncel = await servis.UpdateAsync(olusan.Id, new Business.DTOs.PoiKategoriSaveDto
        {
            Ad = "Kafe",
            Ikon = "fincan",
            IsActive = true,
        });

        Assert.Equal("fincan", guncel!.Ikon);
    }

    // ==================================================================
    //  Seed
    // ==================================================================

    /// <summary>
    /// Seed'deki simge anahtarları katalogla eşleşmeli. Yazım hatası olsaydı
    /// kategori yine oluşur ama haritada varsayılan iğneyle çizilir — hata
    /// vermeyen, yalnızca gözle fark edilebilecek bir kusur.
    /// </summary>
    [Fact]
    public async Task Seed_BaslangicKategorileriGecerliSimgeAlir()
    {
        var db = new SahteVeritabani();
        var servis = KategoriServisi(db);
        _ = servis;   // kategori ağacını seeder kuruyor, servis yalnızca doğrulama için

        var seeder = SeederKur(db);
        await seeder.SeedAsync();

        var kategoriler = db.PoiKategorileri.Where(k => !k.IsDeleted).ToList();
        Assert.NotEmpty(kategoriler);

        // Başlangıç ağacındaki her düğümün simgesi olmalı ve tanınmalı.
        foreach (var kategori in kategoriler)
        {
            Assert.False(string.IsNullOrWhiteSpace(kategori.Ikon),
                $"{kategori.Ad}: seed'de simge verilmemiş");
            Assert.True(PoiIkonlari.GecerliMi(kategori.Ikon),
                $"{kategori.Ad}: tanınmayan simge \"{kategori.Ikon}\"");
        }
    }

    private static DatabaseSeeder SeederKur(SahteVeritabani db)
        => new(
            new FakeUserRepository(db),
            new FakeRoleRepository(db),
            new FakePermissionRepository(db),
            new FakeGeoPermissionRepository(db),
            new FakeIlRepository(),
            new FakePoiRepository(db),
            new FakePoiCategoryRepository(db),
            new FakeGeometryRepository<PointEntity>(),
            new FakeGeometryRepository<LineEntity>(),
            new FakeGeometryRepository<PolygonEntity>(),
            new PoiStyleService(
                new FakePoiCategoryRepository(db),
                new FakeGeoServerClient(),
                NullLogger<PoiStyleService>.Instance));
}
