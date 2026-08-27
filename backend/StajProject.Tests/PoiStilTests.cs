using Microsoft.Extensions.Logging.Abstractions;
using StajProject.Business.DTOs;
using StajProject.Business.Mesai;
using StajProject.Business.Poiler;
using StajProject.Business.Services;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 13 iyileştirmeleri:
///   • POI stillerinin KATEGORİ TABLOSUNDAN üretilmesi
///   • "şu an açık mı?" kuralı
///
/// Bu testlerin asıl değeri, ilk uygulamada SESSİZCE yanlış çalışan iki
/// durumu kalıcı olarak kapatmaları:
///   1. Kategori eklendi ama stili yok → POI haritada yanlış simgeyle çıkıyor
///   2. SLD süzgeci metin karşılaştırması → kodlama bozulunca hiçbir satırla
///      eşleşmiyor, hata da vermiyor
/// İkisi de artık teste bağlı.
/// </summary>
public class PoiStilTests
{
    private static SahteVeritabani AgacKur(params (string Kok, string[] Altlar)[] agac)
    {
        var db = new SahteVeritabani();

        foreach (var (kokAd, altlar) in agac)
        {
            var kok = new PoiCategory { Id = db.SonrakiPoiKategoriId(), Ad = kokAd };
            db.PoiKategorileri.Add(kok);

            foreach (var alt in altlar)
            {
                db.PoiKategorileri.Add(new PoiCategory
                {
                    Id = db.SonrakiPoiKategoriId(), Ad = alt, ParentId = kok.Id,
                });
            }
        }

        return db;
    }

    private static PoiStyleService Servis(SahteVeritabani db, FakeGeoServerClient gs)
        => new(new FakePoiCategoryRepository(db), gs, NullLogger<PoiStyleService>.Instance);

    // ==================================================================
    //  Üretim
    // ==================================================================

    [Fact]
    public void Uretim_HerKategoriIcin_AyriStilUretiyor()
    {
        // Ödevin harfi harfine istediği bu: "her bir POI kategorisi için
        // ayrı bir Style". Kök başına stil, Restoran ile Kafe'yi birleştirirdi.
        var db = AgacKur(("Yeme-İçme", new[] { "Restoran", "Kafe" }), ("Sağlık", new[] { "Eczane" }));

        var stiller = PoiStilUretici.Uret(db.PoiKategorileri);

        // 5 kategori + 1 yedek
        Assert.Equal(6, stiller.Count);
        Assert.Contains(stiller, s => s.Ad == "Restoran");
        Assert.Contains(stiller, s => s.Ad == "Kafe");
        Assert.Contains(stiller, s => s.StilAdi == PoiStilUretici.YedekStil);
    }

    [Fact]
    public void Uretim_SuzgecKategoriIdSine_BakiyorAdinaDegil()
    {
        // KRİTİK: ilk uygulamada süzgeç metin karşılaştırmasıydı
        // (<ogc:Literal>Sağlık</ogc:Literal>) ve SLD UTF-8 olarak
        // yüklenmezse hiçbir satırla eşleşmiyordu — hata vermeden yanlış
        // çalışan bir durum. Sayı karşılaştırmasının kodlaması yoktur.
        var db = AgacKur(("Sağlık", new[] { "Eczane" }));
        var eczane = db.PoiKategorileri.Single(k => k.Ad == "Eczane");

        var stil = PoiStilUretici.Uret(db.PoiKategorileri).Single(s => s.KategoriId == eczane.Id);

        Assert.Contains($"<ogc:PropertyName>kategori_id</ogc:PropertyName>", stil.Sld);
        Assert.Contains($"<ogc:Literal>{eczane.Id}</ogc:Literal>", stil.Sld);
        // Kategori ADI süzgeçte GEÇMEMELİ — yalnızca başlıkta bilgi olarak durur.
        Assert.DoesNotContain("<ogc:Literal>Eczane</ogc:Literal>", stil.Sld);
    }

    [Fact]
    public void Uretim_KardesKategoriler_AyniSekliFarkliTonuAliyor()
    {
        // Hiyerarşi haritada korunuyor: aynı kökün çocukları aynı şekli
        // paylaşıyor ("bunlar aynı ailedendir") ama tonları farklı.
        var db = AgacKur(("Yeme-İçme", new[] { "Restoran", "Kafe", "Fırın" }));
        var stiller = PoiStilUretici.Uret(db.PoiKategorileri);

        var aile = stiller.Where(s => s.Ad is "Yeme-İçme" or "Restoran" or "Kafe" or "Fırın").ToList();

        Assert.Single(aile.Select(s => s.Sekil).Distinct());          // tek şekil
        Assert.Equal(4, aile.Select(s => s.Renk).Distinct().Count()); // dört ayrı ton
    }

    [Fact]
    public void Uretim_FarkliKokler_FarkliSekilAliyor()
    {
        // Yalnızca renkle ayırmak renk körlüğünde okunmazdı.
        var db = AgacKur(("Yeme-İçme", Array.Empty<string>()), ("Sağlık", Array.Empty<string>()));
        var stiller = PoiStilUretici.Uret(db.PoiKategorileri);

        var yeme = stiller.Single(s => s.Ad == "Yeme-İçme");
        var saglik = stiller.Single(s => s.Ad == "Sağlık");

        Assert.NotEqual(yeme.Sekil, saglik.Sekil);
        Assert.NotEqual(yeme.Renk, saglik.Renk);
    }

    [Fact]
    public void Uretim_PasifKategoriye_StilUretmiyor()
    {
        var db = AgacKur(("Eğitim", new[] { "Kütüphane" }));
        db.PoiKategorileri.Single(k => k.Ad == "Kütüphane").IsActive = false;

        var stiller = PoiStilUretici.Uret(db.PoiKategorileri);

        Assert.DoesNotContain(stiller, s => s.Ad == "Kütüphane");
        // Ama yedek stil duruyor: pasif kategorideki mevcut POI'ler haritadan
        // TAMAMEN kaybolmasın, nötr simgeyle görünsün.
        Assert.Contains(stiller, s => s.StilAdi == PoiStilUretici.YedekStil);
    }

    [Fact]
    public void Uretim_YedekStil_BilinenKategorilerinDisiniYakaliyor()
    {
        var db = AgacKur(("Eğitim", new[] { "Kütüphane" }));
        var idler = db.PoiKategorileri.Select(k => k.Id).ToList();

        var yedek = PoiStilUretici.Uret(db.PoiKategorileri)
            .Single(s => s.StilAdi == PoiStilUretici.YedekStil);

        Assert.Contains("<ogc:Not>", yedek.Sld);
        foreach (var id in idler)
        {
            Assert.Contains($"<ogc:Literal>{id}</ogc:Literal>", yedek.Sld);
        }
    }

    [Fact]
    public void Uretim_BosAgacta_Cokmuyor()
    {
        // Sıfırdan kurulumda kategori tablosu boş olabiliyor. Boş bir
        // <ogc:Or> geçersiz XML olurdu; üretici bunu ayrı ele alıyor.
        var stiller = PoiStilUretici.Uret(Array.Empty<PoiCategory>());

        var yedek = Assert.Single(stiller);
        Assert.Equal(PoiStilUretici.YedekStil, yedek.StilAdi);
        Assert.DoesNotContain("<ogc:Or>", yedek.Sld);
    }

    [Fact]
    public void Uretim_KategoriAdindakiOzelKarakterler_XmlIKirmiyor()
    {
        // Kategori adını yönetici giriyor. "Yeme & İçme <özel>" gibi bir ad
        // kaçırılmazsa üretilen SLD geçersiz XML olur ve GeoServer stili
        // reddeder — yönetici sebebini asla anlamaz.
        var db = AgacKur(("Yeme & İçme <özel>", Array.Empty<string>()));

        var stil = PoiStilUretici.Uret(db.PoiKategorileri).First();

        Assert.Contains("&amp;", stil.Sld);
        Assert.Contains("&lt;özel&gt;", stil.Sld);
        // Geçerli XML mi? Ayrıştırılabiliyorsa evet.
        var belge = System.Xml.Linq.XDocument.Parse(stil.Sld);
        Assert.NotNull(belge.Root);
    }

    [Fact]
    public void Uretim_EtiketKurali_ZoomEsigiTasiyor()
    {
        var db = AgacKur(("Eğitim", Array.Empty<string>()));
        var stil = PoiStilUretici.Uret(db.PoiKategorileri).First();

        Assert.Contains("<MaxScaleDenominator>150000</MaxScaleDenominator>", stil.Sld);
        Assert.Contains("<ogc:PropertyName>isim</ogc:PropertyName>", stil.Sld);
    }

    [Fact]
    public void TonAyarla_PozitifAcar_NegatifKoyulastirir()
    {
        Assert.Equal("#ffffff", PoiStilUretici.TonAyarla("#808080", 1));
        Assert.Equal("#000000", PoiStilUretici.TonAyarla("#808080", -1));
        Assert.Equal("#808080", PoiStilUretici.TonAyarla("#808080", 0));
    }

    // ==================================================================
    //  GeoServer'a yazma
    // ==================================================================

    [Fact]
    public async Task Yenile_StilleriYaziyor_VeKatmanaBagliyor()
    {
        var db = AgacKur(("Eğitim", new[] { "Kütüphane" }));
        var gs = new FakeGeoServerClient();

        var sonuc = await Servis(db, gs).YenileAsync();

        Assert.Equal(3, sonuc.Yazilan);           // kök + yaprak + yedek
        Assert.Equal(3, gs.Stiller.Count);
        Assert.Equal("staj:vw_poi", gs.BaglananKatman);
        // Bağlama SIRASI çizim sırasıdır: yaprak kökün üstünde kalmalı.
        Assert.Equal(sonuc.Stiller.Select(s => s.Stil), gs.BaglananStiller);
    }

    [Fact]
    public async Task Yenile_KarsiligiKalmayanStilleri_Siliyor()
    {
        var db = AgacKur(("Eğitim", new[] { "Kütüphane" }));
        var gs = new FakeGeoServerClient();

        await Servis(db, gs).YenileAsync();

        // Kategori siliniyor → stilinin de gitmesi gerekiyor.
        var kutuphane = db.PoiKategorileri.Single(k => k.Ad == "Kütüphane");
        kutuphane.IsDeleted = true;

        var sonuc = await Servis(db, gs).YenileAsync();

        Assert.Equal(1, sonuc.Silinen);
        Assert.Contains(PoiStilUretici.StilOneki + kutuphane.Id, gs.SilinenStiller);
        Assert.DoesNotContain(PoiStilUretici.StilOneki + kutuphane.Id, gs.Stiller.Keys);
    }

    [Fact]
    public async Task Yenile_IlkUygulamadanKalanStilleri_Temizliyor()
    {
        // Yükseltilen bir kurulumda eski (kök bazlı) stiller GeoServer'da
        // duruyor olabilir. Çalışmayan artıkların stil listesinde kalması,
        // birinin onları katmana bağlayıp haritayı sessizce bozmasına
        // kapı açardı.
        var db = AgacKur(("Sağlık", Array.Empty<string>()));
        var gs = new FakeGeoServerClient();
        gs.Stiller["poi_saglik"] = "<eski/>";
        gs.Stiller["poi_yeme_icme"] = "<eski/>";

        await Servis(db, gs).YenileAsync();

        Assert.Contains("poi_saglik", gs.SilinenStiller);
        Assert.Contains("poi_yeme_icme", gs.SilinenStiller);
    }

    [Fact]
    public async Task Yenile_YabanciStillere_Dokunmuyor()
    {
        // Aynı çalışma alanında ısı haritası stilleri de var. "Listede yoksa
        // sil" demek onları da götürürdü.
        var db = AgacKur(("Eğitim", Array.Empty<string>()));
        var gs = new FakeGeoServerClient();
        gs.Stiller["isi_haritasi"] = "<x/>";
        gs.Stiller["isi_deger"] = "<x/>";

        await Servis(db, gs).YenileAsync();

        Assert.Contains("isi_haritasi", gs.Stiller.Keys);
        Assert.Contains("isi_deger", gs.Stiller.Keys);
        Assert.Empty(gs.SilinenStiller);
    }

    [Fact]
    public async Task SessizYenile_GeoServerKapaliyken_HataFirlatmiyor()
    {
        // Kategori zaten veritabanına yazıldı. GeoServer kapalı diye
        // kullanıcıya "kategori eklenemedi" demek YANLIŞ bilgi olurdu.
        var db = AgacKur(("Eğitim", Array.Empty<string>()));
        var gs = new FakeGeoServerClient { StilYazmaHataVersin = true };

        await Servis(db, gs).SessizYenileAsync();   // fırlatmamalı

        Assert.Empty(gs.Stiller);
    }

    [Fact]
    public async Task Yenile_GeoServerKapaliyken_HataFirlatiyor()
    {
        // Sessiz yenilemenin aksine: yönetici düğmeye bastıysa sonucu bilmeli.
        var db = AgacKur(("Eğitim", Array.Empty<string>()));
        var gs = new FakeGeoServerClient { StilYazmaHataVersin = true };

        await Assert.ThrowsAsync<StajProject.DataAccess.GeoServer.GeoServerErisimException>(
            () => Servis(db, gs).YenileAsync());
    }

    [Fact]
    public async Task KategoriEklenince_StillerKendiliginden_Yenileniyor()
    {
        // İlk uygulamanın asıl açığı buydu: yeni kategori haritada tanınmıyordu.
        var db = new SahteVeritabani();
        var gs = new FakeGeoServerClient();

        var kategoriServisi = new PoiCategoryService(
            new FakePoiCategoryRepository(db),
            new FakePoiRepository(db),
            Servis(db, gs));

        await kategoriServisi.CreateAsync(new PoiKategoriSaveDto { Ad = "Ulaşım" });

        var yeni = db.PoiKategorileri.Single(k => k.Ad == "Ulaşım");
        Assert.Contains(PoiStilUretici.StilOneki + yeni.Id, gs.Stiller.Keys);
    }

    [Fact]
    public async Task StilServisiVerilmezse_KategoriYonetimi_CalismayaDevamEdiyor()
    {
        // GeoServer'sız kurulumda ve testlerde bağımlılık verilmiyor.
        var db = new SahteVeritabani();
        var kategoriServisi = new PoiCategoryService(
            new FakePoiCategoryRepository(db), new FakePoiRepository(db));

        var olusan = await kategoriServisi.CreateAsync(new PoiKategoriSaveDto { Ad = "Ulaşım" });

        Assert.Equal("Ulaşım", olusan.Ad);
    }

    // ==================================================================
    //  "Şu an açık mı?"
    // ==================================================================

    /// <summary>2026-08-25 bir PAZARTESİ. Testlerin sabit çıpası.</summary>
    private static DateTime Pazartesi(int saat, int dakika = 0)
        => new(2026, 8, 25, saat, dakika, 0);

    [Fact]
    public void Durum_MesaiIcinde_Acik()
    {
        var plan = MesaiPlani.Varsayilan().Dogrula();   // Pzt-Cum 09:00-18:00

        var durum = plan.Durum(Pazartesi(14), null);

        Assert.True(durum.Acik);
        Assert.Contains("18:00", durum.Aciklama);
    }

    [Fact]
    public void Durum_AcilistanOnce_BugunAcilisSaatiniSoyluyor()
    {
        var durum = MesaiPlani.Varsayilan().Dogrula().Durum(Pazartesi(7), null);

        Assert.False(durum.Acik);
        Assert.Contains("bugün 09:00", durum.Aciklama);
    }

    [Fact]
    public void Durum_KapanistanSonra_YarinAcilisiniSoyluyor()
    {
        var durum = MesaiPlani.Varsayilan().Dogrula().Durum(Pazartesi(21), null);

        Assert.False(durum.Acik);
        Assert.Contains("yarın", durum.Aciklama);
    }

    [Fact]
    public void Durum_CumaAksami_PazartesiyiSoyluyor()
    {
        // 2026-08-28 cuma. Hafta sonu kapalı olduğu için sıradaki açılış Pzt.
        var durum = MesaiPlani.Varsayilan().Dogrula().Durum(new DateTime(2026, 8, 28, 21, 0, 0), null);

        Assert.False(durum.Acik);
        Assert.Contains("Pzt", durum.Aciklama);
    }

    [Fact]
    public void Durum_YediGunKapaliysa_SadeceKapali()
    {
        // Olmayan bir açılışı uydurmuyoruz.
        var plan = new MesaiPlani { Tip = MesaiTipi.Haftalik }.Dogrula();

        Assert.Equal("Kapalı", plan.Durum(Pazartesi(12), null).Aciklama);
    }

    [Fact]
    public void Durum_SurekliKip_HerZamanAcik()
    {
        var plan = MesaiPlani.Varsayilan();
        plan.Tip = MesaiTipi.Surekli;

        Assert.True(plan.Dogrula().Durum(Pazartesi(3), null).Acik);
    }

    [Fact]
    public void Durum_ResmiTatil_MesaiSaatiIcindeBileKapali()
    {
        // Ödev 13 / Madde 3'ün asıl kazanımı: "resmî kurum tatilde kapalıdır"
        // kuralı gerçekten uygulanıyor.
        var tatil = new ResmiTatil(new DateOnly(2026, 8, 25), "Test Bayramı", YarimGun: false);

        var durum = MesaiPlani.ResmiKurum().Dogrula().Durum(Pazartesi(10), tatil);

        Assert.False(durum.Acik);
        Assert.Contains("Test Bayramı", durum.Aciklama);
    }

    [Fact]
    public void Durum_YarimGunTatil_OgledenOnceAcik_SonraKapali()
    {
        // 28 Ekim ve arifeler: kanunda tatil 13.00'te başlıyor. Bütün günü
        // kapalı saymak, sabah açık olan bir kurumu kapalı göstermek olurdu.
        var arife = new ResmiTatil(new DateOnly(2026, 8, 25), "Arife", YarimGun: true);
        var plan = MesaiPlani.ResmiKurum().Dogrula();

        Assert.True(plan.Durum(Pazartesi(10), arife).Acik);
        Assert.False(plan.Durum(Pazartesi(15), arife).Acik);
    }

    [Fact]
    public void Durum_TatilKapaliIsaretliDegilse_TatilGunuAcikKalabiliyor()
    {
        // Market bayramda açık olabilir; kural yalnızca işaretlenmiş POI'lere.
        var tatil = new ResmiTatil(new DateOnly(2026, 8, 25), "Test Bayramı", YarimGun: false);
        var plan = MesaiPlani.Varsayilan().Dogrula();   // resmiTatilKapali = false

        Assert.True(plan.Durum(Pazartesi(12), tatil).Acik);
    }

    [Fact]
    public void TurkiyeZamani_UtcyiUcSaatIleriye_Aliyor()
    {
        // Sunucu UTC ile çalışıyor; "şu an açık mı?" sorusu UTC ile
        // cevaplanamaz. 20:00'de kapanan bir yer, UTC 17:00 olduğu için
        // hâlâ açık görünürdü.
        var utc = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(15, TurkiyeZamani.Cevir(utc).Hour);
    }
}
