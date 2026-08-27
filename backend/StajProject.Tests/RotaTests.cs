using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.Business.Validation;
using StajProject.Entities;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 17 / Madde 1 — OSRM rotası.
///
/// Buradaki testlerin ortak konusu ŞU: rota bir DIŞ SERVİSTEN geliyor ve o
/// servis her an kapalı olabilir. Ödev "durak sırası değiştiğinde rota
/// otomatik güncellenmelidir" diyor — ama "OSRM kapalıysa kullanıcı sıralama
/// yapamasın" demiyor. İkisini birden sağlamanın tek yolu, rotanın
/// güncelliğini AYRI bir bilgi olarak taşımak.
///
/// Testler gerçek OSRM'e bağlanmıyor (gerekçe: <see cref="FakeOsrmClient"/>).
/// </summary>
public class RotaTests
{
    // ==================================================================
    //  Ortam
    // ==================================================================

    private sealed record Ortam(
        SahteVeritabani Db,
        UlasimService Servis,
        FakeOsrmClient Osrm);

    /// <summary>
    /// Rota işlemleri "Güzergah Yönetimi" yetkisi istiyor; ortam bu yetkiyi
    /// baştan veriyor ki testler yetki kurulumuyla değil ROTA davranışıyla
    /// ilgilensin (yetki kuralları UlasimTests'te sınanıyor).
    /// </summary>
    private const int Operator = 1;

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
        var servis = new UlasimService(
            new FakeUlasimRepository(db),
            oturum,
            izinler,
            new FakeGeoPermissionService(),
            osrm);

        return new Ortam(db, servis, osrm);
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
                Wkt = $"POINT ({lon.ToString(System.Globalization.CultureInfo.InvariantCulture)} "
                    + $"{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)})",
            });
        }

        return (await o.Servis.GuzergahGetirAsync(hat.Id))!;
    }

    // Ankara'dan üç nokta.
    private static readonly (double, double)[] UcDurak =
    {
        (32.8541, 39.9208),   // Kızılay
        (32.8520, 39.9420),   // Ulus
        (32.7200, 39.9700),   // Batıkent
    };

    // ==================================================================
    //  1) Elle "Rota Oluştur"
    // ==================================================================

    [Fact]
    public async Task RotaOlustur_CizgiVeOlculeriYazar()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        var guncel = await o.Servis.RotaHesaplaAsync(hat.Id);

        Assert.NotNull(guncel);
        Assert.NotNull(guncel!.RotaWkt);
        Assert.StartsWith("LINESTRING", guncel.RotaWkt);
        Assert.True(guncel.RotaMesafeMetre > 0);
        Assert.True(guncel.RotaSureSaniye > 0);
        Assert.NotNull(guncel.RotaHesaplandi);
    }

    [Fact]
    public async Task RotaOlustur_DuraklariSIRAYLAGonderiyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);

        await o.Servis.RotaHesaplaAsync(hat.Id);

        // OSRM sırayı OPTİMİZE ETMİYOR, verilen sırayla gidiyor. Sıralamayı
        // yanlış göndermek, haritadaki hattın yönetim ekranındaki listeden
        // farklı olması demekti — ve bu ekranda gözle fark edilmesi zor.
        var gonderilen = Assert.IsAssignableFrom<IReadOnlyList<NetTopologySuite.Geometries.Coordinate>>(
            o.Osrm.SonNoktalar);

        Assert.Equal(3, gonderilen.Count);
        Assert.Equal(32.8541, gonderilen[0].X, 4);
        Assert.Equal(32.8520, gonderilen[1].X, 4);
        Assert.Equal(32.7200, gonderilen[2].X, 4);
    }

    [Fact]
    public async Task RotaOlustur_TekDurakla_REDDEDILIR()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, (32.8541, 39.9208));

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.RotaHesaplaAsync(hat.Id));

        // Kullanıcı düğmeye BASTI: sebebini görmeli. Sessizce hiçbir şey
        // yapmasaydık "düğme çalışmıyor" izlenimi verirdi.
        Assert.Contains("en az 2 durak", hata.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RotaOlustur_OsrmKapaliyken_SEBEBINISOYLER()
    {
        var o = OrtamKur();
        o.Osrm.Etkin = false;
        var hat = await HatKurAsync(o, UcDurak);

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => o.Servis.RotaHesaplaAsync(hat.Id));

        Assert.Contains("OSRM", hata.Message);
    }

    [Fact]
    public async Task RotaOlustur_OsrmCevapVermezse_ESKIROTAYIKORUR()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);
        await o.Servis.RotaHesaplaAsync(hat.Id);

        var oncekiRota = (await o.Servis.GuzergahGetirAsync(hat.Id))!.RotaWkt;
        Assert.NotNull(oncekiRota);

        o.Osrm.BasarisizOl = true;
        await Assert.ThrowsAsync<IsKuraliException>(() => o.Servis.RotaHesaplaAsync(hat.Id));

        // Başarısız bir yeniden hesaplama, elimizdeki EN İYİ bilgiyi yok
        // etmemeli. Temizleseydik harita o an boşalırdı ve kullanıcı hem
        // hatayı hem de hattın kaybolduğunu görürdü.
        Assert.Equal(oncekiRota, (await o.Servis.GuzergahGetirAsync(hat.Id))!.RotaWkt);
    }

    [Fact]
    public async Task RotaOlustur_OlmayanGuzergah_NullDoner()
    {
        var o = OrtamKur();
        Assert.Null(await o.Servis.RotaHesaplaAsync(9999));
    }

    // ==================================================================
    //  2) Otomatik yenileme — ödevin açıkça istediği davranış
    // ==================================================================

    [Fact]
    public async Task SiraDegisince_ROTAOTOMATIKYenileniyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);
        await o.Servis.RotaHesaplaAsync(hat.Id);

        var oncekiCagri = o.Osrm.CagriSayisi;
        var tersSira = hat.Duraklar.Select(d => d.Id).Reverse().ToList();

        await o.Servis.SiralamaGuncelleAsync(hat.Id, new DurakSiralamaDto { DurakIdleri = tersSira });

        // ÖDEV METNİ: "durak sırası değiştiğinde OSRM'e YENİ İSTEK atılarak
        // rota otomatik güncellenmelidir." Sınanan tam olarak bu.
        Assert.True(o.Osrm.CagriSayisi > oncekiCagri);

        // Ve yeni istek TERS sırayla gitmiş olmalı.
        Assert.Equal(32.7200, o.Osrm.SonNoktalar![0].X, 4);
        Assert.Equal(32.8541, o.Osrm.SonNoktalar[2].X, 4);
    }

    [Fact]
    public async Task SiraDegisince_RotaGUNCELKaliyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);
        await o.Servis.RotaHesaplaAsync(hat.Id);

        await o.Servis.SiralamaGuncelleAsync(hat.Id, new DurakSiralamaDto
        {
            DurakIdleri = hat.Duraklar.Select(d => d.Id).Reverse().ToList(),
        });

        Assert.True((await o.Servis.GuzergahGetirAsync(hat.Id))!.RotaGuncel);
    }

    [Fact]
    public async Task DurakEklenince_RotaYenileniyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);
        await o.Servis.RotaHesaplaAsync(hat.Id);
        var oncekiCagri = o.Osrm.CagriSayisi;

        await o.Servis.DurakEkleAsync(new DurakCreateDto
        {
            Ad = "Yeni", GuzergahId = hat.Id, Wkt = "POINT (32.75 39.95)",
        });

        Assert.True(o.Osrm.CagriSayisi > oncekiCagri);
        Assert.Equal(4, o.Osrm.SonNoktalar!.Count);
    }

    [Fact]
    public async Task DurakSilinince_RotaYenileniyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);
        await o.Servis.RotaHesaplaAsync(hat.Id);
        var oncekiCagri = o.Osrm.CagriSayisi;

        await o.Servis.DurakSilAsync(hat.Duraklar[1].Id);

        Assert.True(o.Osrm.CagriSayisi > oncekiCagri);
        Assert.Equal(2, o.Osrm.SonNoktalar!.Count);
    }

    [Fact]
    public async Task IkiDuragınAltinaDusunce_RotaTEMIZLENIYOR()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);
        await o.Servis.RotaHesaplaAsync(hat.Id);

        await o.Servis.DurakSilAsync(hat.Duraklar[0].Id);
        await o.Servis.DurakSilAsync(hat.Duraklar[1].Id);

        // Tek durak kaldı. Eski rota bırakılsaydı haritada, artık var
        // olmayan duraklara giden bir hat asılı kalırdı.
        var guncel = await o.Servis.GuzergahGetirAsync(hat.Id);
        Assert.Null(guncel!.RotaWkt);
        Assert.Null(guncel.RotaHesaplandi);
    }

    [Fact]
    public async Task DurakTasininca_RotaYenileniyor()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);
        await o.Servis.RotaHesaplaAsync(hat.Id);
        var oncekiCagri = o.Osrm.CagriSayisi;

        await o.Servis.DurakGuncelleAsync(hat.Duraklar[1].Id, new DurakUpdateDto
        {
            Ad = hat.Duraklar[1].Ad,
            GuzergahId = hat.Id,
            Wkt = "POINT (32.9000 39.9500)",   // konum DEĞİŞTİ
        });

        Assert.True(o.Osrm.CagriSayisi > oncekiCagri);
    }

    // ==================================================================
    //  3) Gereksiz istek atmıyor
    // ==================================================================

    [Fact]
    public async Task YalnizcaADDegisince_OSRMEHICGITMIYOR()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);
        await o.Servis.RotaHesaplaAsync(hat.Id);
        var oncekiCagri = o.Osrm.CagriSayisi;

        await o.Servis.DurakGuncelleAsync(hat.Duraklar[1].Id, new DurakUpdateDto
        {
            Ad = "Yeni Ad",          // yalnızca ad
            GuzergahId = hat.Id,
            // Wkt boş → konum değişmiyor
        });

        // Geometri ve sıra aynı kaldığı için rota da aynı olacaktı. İmza
        // bunu görüyor ve dış servise boşuna gidilmiyor. Bu kontrol
        // olmasaydı her isim düzeltmesi bir OSRM isteği doğururdu.
        Assert.Equal(oncekiCagri, o.Osrm.CagriSayisi);
    }

    [Fact]
    public async Task AYNIsiraYenidenYazilinca_OSRMEHICGITMIYOR()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);
        await o.Servis.RotaHesaplaAsync(hat.Id);
        var oncekiCagri = o.Osrm.CagriSayisi;

        // Sürükle-bırakta kullanıcı durağı kaldırıp AYNI yere bırakabilir.
        await o.Servis.SiralamaGuncelleAsync(hat.Id, new DurakSiralamaDto
        {
            DurakIdleri = hat.Duraklar.Select(d => d.Id).ToList(),
        });

        Assert.Equal(oncekiCagri, o.Osrm.CagriSayisi);
    }

    // ==================================================================
    //  4) Güncellik takibi — asıl tasarım kararı
    // ==================================================================

    [Fact]
    public async Task OsrmKapaliykenSiraDegisirse_ROTAESKIISARETLENIYOR()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);
        await o.Servis.RotaHesaplaAsync(hat.Id);

        // OSRM düştü.
        o.Osrm.BasarisizOl = true;

        await o.Servis.SiralamaGuncelleAsync(hat.Id, new DurakSiralamaDto
        {
            DurakIdleri = hat.Duraklar.Select(d => d.Id).Reverse().ToList(),
        });

        var guncel = await o.Servis.GuzergahGetirAsync(hat.Id);

        // Eski rota DURUYOR (silmedik) ama GÜNCEL DEĞİL diye işaretli.
        // Bu ikisi birlikte, tasarımın özü: veriyi kaybetmiyoruz ve yanlış
        // olduğunu da gizlemiyoruz.
        Assert.NotNull(guncel!.RotaWkt);
        Assert.False(guncel.RotaGuncel);
    }

    [Fact]
    public async Task OsrmKapaliyken_SIRALAMAYINEDEKAYDEDILIYOR()
    {
        var o = OrtamKur();
        var hat = await HatKurAsync(o, UcDurak);
        o.Osrm.BasarisizOl = true;

        var tersSira = hat.Duraklar.Select(d => d.Id).Reverse().ToList();
        var guncel = await o.Servis.SiralamaGuncelleAsync(hat.Id,
            new DurakSiralamaDto { DurakIdleri = tersSira });

        // ASIL İDDİA: dış bir servisin arızası, kullanıcının kendi işini
        // engellemiyor. İstisna fırlatsaydık kullanıcı "sıralama
        // kaydedilemedi" görürdü — oysa kaydedildi.
        Assert.NotNull(guncel);
        Assert.Equal(tersSira, guncel!.Duraklar.Select(d => d.Id).ToList());
        Assert.Equal(new[] { 1, 2, 3 }, guncel.Duraklar.Select(d => d.Sira).ToArray());
    }

    [Fact]
    public async Task RotaHicYOKKEN_GuncelSayiliyor()
    {
        var o = OrtamKur();

        // OSRM baştan kapalı: durak eklerken otomatik yenileme de çalışmayacak,
        // yani hat hiç rota görmeden kurulacak. (OSRM açıkken durak eklemek
        // rotayı zaten kendiliğinden hesaplatıyor — o da ayrı bir testin konusu.)
        o.Osrm.Etkin = false;
        var hat = await HatKurAsync(o, UcDurak);

        // Rota hiç hesaplanmadı. "Güncel değil" deseydik, hiç rotası olmayan
        // her hatta sebepsiz bir uyarı çıkardı — uyarı da anlamını yitirirdi.
        var guncel = await o.Servis.GuzergahGetirAsync(hat.Id);
        Assert.Null(guncel!.RotaWkt);
        Assert.True(guncel.RotaGuncel);
    }

    // ==================================================================
    //  5) İmza — güncelliğin dayandığı mekanizma
    // ==================================================================

    private static Durak D(int id, double lon, double lat) => new()
    {
        Id = id,
        Sira = id,
        Geom = new NetTopologySuite.Geometries.Point(lon, lat) { SRID = 4326 },
    };

    [Fact]
    public void Imza_AyniDizilimIcinAYNI()
    {
        var a = new[] { D(1, 32.85, 39.92), D(2, 32.86, 39.94) };
        var b = new[] { D(1, 32.85, 39.92), D(2, 32.86, 39.94) };

        Assert.Equal(UlasimService.RotaImzasiUret(a), UlasimService.RotaImzasiUret(b));
    }

    [Fact]
    public void Imza_SIRADegisince_DEGISIYOR()
    {
        var duz = new[] { D(1, 32.85, 39.92), D(2, 32.86, 39.94) };
        var ters = new[] { D(2, 32.86, 39.94), D(1, 32.85, 39.92) };

        // İmza yalnızca id KÜMESİNE baksaydı (örn. sıralı id listesi) bu iki
        // dizilim aynı görünürdü — ve tam da ödevin istediği "sıra değişince
        // rota güncellensin" kuralı sessizce çalışmazdı.
        Assert.NotEqual(UlasimService.RotaImzasiUret(duz), UlasimService.RotaImzasiUret(ters));
    }

    [Fact]
    public void Imza_KONUMDegisince_DEGISIYOR()
    {
        var once = new[] { D(1, 32.85, 39.92), D(2, 32.86, 39.94) };
        var sonra = new[] { D(1, 32.85, 39.92), D(2, 32.90, 39.94) };

        Assert.NotEqual(UlasimService.RotaImzasiUret(once), UlasimService.RotaImzasiUret(sonra));
    }

    [Fact]
    public void Imza_SabitUzunlukta()
    {
        var imza = UlasimService.RotaImzasiUret(new[] { D(1, 32.85, 39.92), D(2, 32.86, 39.94) });

        // Kolon varchar(64); SHA-256 hex çıktısı her zaman tam 64 karakter.
        Assert.Equal(64, imza.Length);
        Assert.Matches("^[0-9a-f]{64}$", imza);
    }

    [Fact]
    public void Imza_COKKUCUKKonumFarkiniYOKSAYIYOR()
    {
        // 6 basamak ≈ 11 cm. Altındaki farklar yuvarlanıyor.
        //
        // NEDEN? Aynı nokta, veritabanından okunuşuna göre son bitlerinde
        // farklı gelebiliyor. Ham double yazsaydık imza durduk yere değişir,
        // rota "eskimiş" görünür ve her okumada boşuna OSRM isteği atılırdı.
        var a = new[] { D(1, 32.8500000, 39.92), D(2, 32.86, 39.94) };
        var b = new[] { D(1, 32.8500001, 39.92), D(2, 32.86, 39.94) };

        Assert.Equal(UlasimService.RotaImzasiUret(a), UlasimService.RotaImzasiUret(b));
    }
}
