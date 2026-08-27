using NetTopologySuite.Geometries;
using StajProject.Business.Geo;
using StajProject.DataAccess.GeoServer;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 8 / Madde 2 testleri: GeoServer WFS üzerinden okuma.
///
/// Testler iki soruya cevap veriyor:
///   1. GeoJSON doğru çözümleniyor mu? (koordinat sırası, id, kolonlar)
///   2. Süzme gerçekten GEOSERVER'DA mı yapılıyor? Bunu isteğin CQL_FILTER
///      içeriğine bakarak doğruluyoruz — "kayıtları çekip backend'de eledik"
///      ile "sunucuya süzdürdük" arasındaki fark tam olarak burada görünür.
/// </summary>
public class GeoServerTests
{
    // ------------------------------------------------------------------
    //  Test verisi
    // ------------------------------------------------------------------

    /// <summary>GeoServer'ın gerçekte döndürdüğü biçime birebir benzeyen bir cevap.</summary>
    private const string NoktaCevabi = """
    {
      "type": "FeatureCollection",
      "features": [
        {
          "type": "Feature",
          "id": "tbl_point.7",
          "geometry": { "type": "Point", "coordinates": [32.8597, 39.9334] },
          "properties": {
            "id": 7,
            "name": "Anıtkabir",
            "description": "Deneme",
            "image_url": null,
            "color": "#2e8fa8",
            "inserted_date": "2026-08-19T09:15:00Z",
            "inserted_user_id": 3,
            "modified_date": null,
            "is_deleted": false,
            "is_active": true
          }
        }
      ]
    }
    """;

    private static FakeGeoServerClient NoktaIstemcisi(string cevap = NoktaCevabi)
    {
        var istemci = new FakeGeoServerClient();
        istemci.Cevaplar[GeoServerKatmanlari.Nokta] = cevap;
        return istemci;
    }

    private static GeoServerGeometryRepository<PointEntity> NoktaDeposu(FakeGeoServerClient istemci)
        => new(istemci, new FakeGeometryRepository<PointEntity>());

    // ==================================================================
    //  1) GeoJSON çözümleme
    // ==================================================================

    [Fact]
    public void GeoJson_Nokta_KoordinatSirasiBoylamEnlem()
    {
        var kayitlar = GeoJsonOkuyucu.Oku(NoktaCevabi);

        var nokta = Assert.IsType<Point>(kayitlar[0].Geometry);

        // GeoJSON'da dizi [boylam, enlem] sırasındadır (RFC 7946).
        // Ters okusaydık Ankara, Hint Okyanusu'na düşerdi.
        Assert.Equal(32.8597, nokta.X, precision: 6);   // X = boylam
        Assert.Equal(39.9334, nokta.Y, precision: 6);   // Y = enlem
        Assert.Equal(4326, nokta.SRID);
    }

    [Fact]
    public void GeoJson_Cizgi_TumNoktalariOkur()
    {
        var kayitlar = GeoJsonOkuyucu.Oku("""
        {
          "type": "FeatureCollection",
          "features": [{
            "type": "Feature", "id": "tbl_line.1",
            "geometry": { "type": "LineString",
                          "coordinates": [[32.85,39.93],[32.86,39.94],[32.87,39.92]] },
            "properties": { "id": 1, "name": "Yol" }
          }]
        }
        """);

        var cizgi = Assert.IsType<LineString>(kayitlar[0].Geometry);
        Assert.Equal(3, cizgi.NumPoints);
        Assert.Equal(32.87, cizgi.Coordinates[2].X, precision: 6);
    }

    [Fact]
    public void GeoJson_Poligon_KapaliHalkaOkunur()
    {
        var kayitlar = GeoJsonOkuyucu.Oku("""
        {
          "type": "FeatureCollection",
          "features": [{
            "type": "Feature", "id": "tbl_polygon.4",
            "geometry": { "type": "Polygon", "coordinates":
              [[[32.85,39.93],[32.87,39.93],[32.87,39.95],[32.85,39.93]]] },
            "properties": { "id": 4, "name": "Alan" }
          }]
        }
        """);

        var poligon = Assert.IsType<Polygon>(kayitlar[0].Geometry);
        Assert.True(poligon.Shell.IsClosed);
        Assert.Equal(4, poligon.ExteriorRing.NumPoints);
    }

    [Fact]
    public void GeoJson_IdKolonuYoksaFeatureIdindenCozulur()
    {
        // Store'da "Expose primary keys" kapalıysa id kolonu gelmez; geriye
        // yalnızca "tbl_point.42" biçimindeki feature id kalır.
        var kayitlar = GeoJsonOkuyucu.Oku("""
        {
          "type": "FeatureCollection",
          "features": [{
            "type": "Feature", "id": "tbl_point.42",
            "geometry": { "type": "Point", "coordinates": [32.0, 39.0] },
            "properties": { "name": "Adsiz" }
          }]
        }
        """);

        Assert.Equal(42, kayitlar[0].Id);
    }

    [Fact]
    public void GeoJson_GeoJsonOlmayanCevapAnlamliHataVerir()
    {
        // GeoServer hata durumunda XML döner. Bunu "bozuk JSON" diye
        // genel bir hataya bırakmak, sunumda sebebi bulmayı imkânsızlaştırırdı.
        var hata = Assert.Throws<GeoServerErisimException>(() =>
            GeoJsonOkuyucu.Oku("<ows:ExceptionReport>Layer not found</ows:ExceptionReport>"));

        Assert.Contains("GeoJSON", hata.Message);
    }

    // ==================================================================
    //  2) Kolonlardan entity'ye çeviri
    // ==================================================================

    [Fact]
    public async Task Listeleme_KolonlariEntityyeDogruEsler()
    {
        var depo = NoktaDeposu(NoktaIstemcisi());

        var kayitlar = await depo.GetAllAsync(userId: 3);

        var kayit = Assert.Single(kayitlar);
        Assert.Equal(7, kayit.Id);
        Assert.Equal("Anıtkabir", kayit.Name);
        Assert.Equal("#2e8fa8", kayit.Color);
        Assert.Equal(3, kayit.InsertedUserId);
        Assert.True(kayit.IsActive);
        Assert.Null(kayit.ModifiedDate);

        // Tarih UTC'ye sabitlenmeli: makinenin saat dilimine göre kayarsa
        // "en yeni kayıt hangisi?" sıralaması sessizce bozulur.
        Assert.Equal(DateTimeKind.Utc, kayit.InsertedDate.Kind);
        Assert.Equal(new DateTime(2026, 8, 19, 9, 15, 0, DateTimeKind.Utc), kayit.InsertedDate);
    }

    [Fact]
    public async Task Listeleme_WktYazimiIcinGeometriKullanilabilirDurumda()
    {
        // Servis katmanı entity'yi WKT'ye çevirip istemciye gönderiyor.
        // Bu testin amacı zincirin sonuna kadar çalıştığını göstermek.
        var kayitlar = await NoktaDeposu(NoktaIstemcisi()).GetAllAsync();

        Assert.Equal("POINT (32.8597 39.9334)", WktConverter.Write(kayitlar[0].Geometry));
    }

    // ==================================================================
    //  3) Süzme GeoServer'da yapılıyor mu?
    // ==================================================================

    [Fact]
    public async Task Listeleme_SahiplikVeSilinmeSuzgeciCqlIleGonderilir()
    {
        var istemci = NoktaIstemcisi();

        await NoktaDeposu(istemci).GetAllAsync(userId: 3);

        var cagri = Assert.Single(istemci.Cagrilar);
        Assert.Equal(GeoServerKatmanlari.Nokta, cagri.Tablo);

        // Ödev 5'in sahiplik süzgeci artık WFS isteğinin içinde gidiyor.
        // Başkasının kaydı ağdan hiç geçmiyor.
        Assert.Equal("inserted_user_id = 3", cagri.Cql);

        // Ödev 9: soft delete kuralı KATMANIN SQL View'ında; süzgeçte
        // tekrarlanmamalı. Tekrarlansaydı "kural nerede?" sorusunun iki
        // cevabı olurdu — ve view kolonu yayınlamadığı için hata verirdi.
        Assert.DoesNotContain("is_deleted", cagri.Cql);

        // EF'teki OrderByDescending(InsertedDate) karşılığı
        Assert.Equal("inserted_date D", cagri.Siralama);
    }

    [Fact]
    public async Task Listeleme_KullaniciVerilmezseHicSuzgecGitmez()
    {
        // Sahiplik süzgeci yoksa gönderilecek başka bir koşul da kalmıyor:
        // silinmişleri eleme işi katmanın SQL View'ında yapılıyor (Ödev 9).
        var istemci = NoktaIstemcisi();

        await NoktaDeposu(istemci).GetAllAsync();

        Assert.Null(istemci.SonCql);
    }

    [Fact]
    public async Task TekKayit_IdVeSahiplikBirlikteSorulur()
    {
        var istemci = NoktaIstemcisi();

        await NoktaDeposu(istemci).GetByIdAsync(7, userId: 3);

        Assert.Equal("id = 7 AND inserted_user_id = 3", istemci.SonCql);
    }

    [Fact]
    public async Task TekKayit_BaskasininKaydiBulunamadiSayilir()
    {
        // Sunucu süzgece uyan kayıt bulamazsa boş koleksiyon döner;
        // repository bunu null'a çevirir → servis 404 üretir.
        // "Yetkisiz erişim" yerine "yok" demek, kaydın varlığını bile sızdırmaz.
        var istemci = new FakeGeoServerClient();   // hiçbir katman için cevap tanımlı değil

        var kayit = await NoktaDeposu(istemci).GetByIdAsync(7, userId: 99);

        Assert.Null(kayit);
    }

    // ==================================================================
    //  4) Kesişim analizi — CQL INTERSECTS
    // ==================================================================

    [Fact]
    public async Task Analiz_IntersectsSuzgeciyleSorar()
    {
        var istemci = new FakeGeoServerClient();
        var depo = new GeoServerAnalysisRepository(istemci);
        var alan = WktConverter.Read<Polygon>(
            "POLYGON ((32.8 39.9, 32.9 39.9, 32.9 40.0, 32.8 40.0, 32.8 39.9))");

        await depo.KesisenNoktalarAsync(alan);

        var cql = istemci.SonCql!;

        // ST_Intersects'in CQL karşılığı. "Tamamen kapsanma" değil, en ufak
        // temas bile sayılıyor — ödevin şartı buydu.
        Assert.StartsWith("INTERSECTS(geom, POLYGON ((32.8 39.9", cql);

        // Noktalı virgül CQL'de filtre listesi ayıracıdır; süzgecin içinde
        // hiç bulunmamalı (EWKT öneki bu yüzden kullanılmıyor).
        Assert.DoesNotContain(";", cql);

        // Ödev 9: silinmişleri eleme katmanın SQL View'ında.
        Assert.DoesNotContain("is_deleted", cql);
    }

    [Fact]
    public async Task Analiz_KaydedilmisPoligonKendisiHaricTutulur()
    {
        var istemci = new FakeGeoServerClient();
        var depo = new GeoServerAnalysisRepository(istemci);
        var alan = WktConverter.Read<Polygon>(
            "POLYGON ((32.8 39.9, 32.9 39.9, 32.9 40.0, 32.8 39.9))");

        await depo.KesisenPoligonlarAsync(alan, haricTutulanId: 12);

        // Bir geometri her zaman kendisiyle kesişir; sonuçta görünmesi anlamsız.
        Assert.Contains("id <> 12", istemci.SonCql);
    }

    [Fact]
    public async Task Analiz_UcKatmaniDaSorar()
    {
        var istemci = new FakeGeoServerClient();
        var depo = new GeoServerAnalysisRepository(istemci);
        var alan = WktConverter.Read<Polygon>(
            "POLYGON ((32.8 39.9, 32.9 39.9, 32.9 40.0, 32.8 39.9))");

        await depo.KesisenNoktalarAsync(alan);
        await depo.KesisenCizgilerAsync(alan);
        await depo.KesisenPoligonlarAsync(alan);

        Assert.Equal(
            new[] { GeoServerKatmanlari.Nokta, GeoServerKatmanlari.Cizgi, GeoServerKatmanlari.Poligon },
            istemci.Cagrilar.Select(c => c.Tablo));
    }

    // ==================================================================
    //  5) Yazma yolu değişmedi
    // ==================================================================

    [Fact]
    public async Task Yazma_HalaVeritabaninaGider()
    {
        // Ödev "veri GETİRME isteklerini" GeoServer'a taşımayı istiyor.
        // Ekleme/silme yolu EF Core'da kalmalı: coğrafi yetki kontrolü,
        // sahiplik damgası ve soft delete orada yaşıyor.
        var istemci = NoktaIstemcisi();
        var yazmaDeposu = new FakeGeometryRepository<PointEntity>();
        var depo = new GeoServerGeometryRepository<PointEntity>(istemci, yazmaDeposu);

        var eklenen = await depo.AddAsync(new PointEntity
        {
            Name = "Yeni",
            Geom = WktConverter.Read<Point>("POINT (32.0 39.0)"),
            InsertedUserId = 3,
        });

        // Kayıt sahte veritabanına düştü, GeoServer'a hiç istek gitmedi.
        Assert.True(eklenen.Id > 0);
        Assert.Empty(istemci.Cagrilar);
    }

    [Fact]
    public async Task Katman_EntityTipineGoreSecilir()
    {
        var istemci = new FakeGeoServerClient();

        await new GeoServerGeometryRepository<LineEntity>(
            istemci, new FakeGeometryRepository<LineEntity>()).GetAllAsync();

        await new GeoServerGeometryRepository<PolygonEntity>(
            istemci, new FakeGeometryRepository<PolygonEntity>()).GetAllAsync();

        Assert.Equal(
            new[] { GeoServerKatmanlari.Cizgi, GeoServerKatmanlari.Poligon },
            istemci.Cagrilar.Select(c => c.Tablo));
    }

    [Fact]
    public void KatmanAdi_WorkspaceIleNitelenir()
    {
        // WFS'te katman her zaman "workspace:katman" biçiminde anılır.
        var ayarlar = new GeoServerSettings { Workspace = "staj" };

        Assert.Equal("staj:vw_point", ayarlar.KatmanAdi(GeoServerKatmanlari.Nokta));
        Assert.Equal("http://localhost:8080/geoserver/staj/wfs", ayarlar.ServisAdresi("wfs"));
    }

    // ==================================================================
    //  6) Ödev 9 — SQL View katmanları
    // ==================================================================

    [Fact]
    public void Katmanlar_SqlViewAdlariniKullanir()
    {
        // Ödev 9 / Madde 1: katman artık tabloya değil bir SQL sorgusuna bakıyor.
        // Adların "vw" ile başlaması bunu isimden okunur kılıyor ve
        // gs-yapilandir.ps1 içindeki $Katmanlar listesiyle eşleşmek zorunda.
        Assert.Equal("vw_point", GeoServerKatmanlari.Nokta);
        Assert.Equal("vw_line", GeoServerKatmanlari.Cizgi);
        Assert.Equal("vw_polygon", GeoServerKatmanlari.Poligon);
    }

    [Fact]
    public async Task HicbirIstekteIsDeletedSuzgeciGitmez()
    {
        // Tek bir yerde bile kalsaydı GeoServer "böyle bir kolon yok" derdi:
        // SQL View is_deleted kolonunu yayınlamıyor. Bu test, kuralın
        // yanlışlıkla geri eklenmesini yakalar.
        var istemci = NoktaIstemcisi();
        var depo = NoktaDeposu(istemci);
        var analiz = new GeoServerAnalysisRepository(istemci);
        var alan = WktConverter.Read<Polygon>(
            "POLYGON ((32.8 39.9, 32.9 39.9, 32.9 40.0, 32.8 39.9))");

        await depo.GetAllAsync(userId: 3);
        await depo.GetByIdAsync(7, userId: 3);
        await analiz.KesisenNoktalarAsync(alan);
        await analiz.KesisenPoligonlarAsync(alan, haricTutulanId: 4);

        Assert.All(istemci.Cagrilar, c =>
            Assert.DoesNotContain("is_deleted", c.Cql ?? string.Empty));
    }

    [Fact]
    public void IsiHaritasiStili_VarsayilanAdTanimli()
    {
        // Stil adı üç yerde kullanılıyor (WMS vekili, lejant, durum ucu);
        // tek kaynaktan gelmesi için ayarlarda duruyor.
        Assert.Equal("isi_haritasi", new GeoServerSettings().IsiHaritasiStili);
    }
}
