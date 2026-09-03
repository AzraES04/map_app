using Microsoft.Extensions.Logging.Abstractions;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.Business.Validation;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

// ============================================================================
//  CANLI TUR OTURUMU — paylaşılabilir bağlantı ve rol ayrımı
//
//  Modülün iki kuralı var ve ikisi de sessizce bozulabilecek cinsten:
//
//    1. KATILIM KODU YALNIZCA REHBERE GİDER. Katılımcıya da gitseydi, tura
//       katılan biri kodu başkalarına dağıtabilir; rehber grubunun kimlerden
//       oluştuğunu kontrol edemezdi.
//
//    2. TURU YALNIZCA O OTURUMUN REHBERİ İLERLETİR. "Tur Yönetimi" yetkisine
//       baksaydık, tur açabilen herkes BAŞKASININ grubunu yönlendirebilirdi —
//       ve bu, hata vermeyen, yalnızca yanlış davranan bir açık olurdu.
// ============================================================================

public class TurOturumTests
{
    private const int RehberId = 1;
    private const int KatilimciId = 2;

    private static (TurOturumServisi servis, FakeTurRepository depo, FakeCurrentUserService kullanici)
        Kur(int kullaniciId = RehberId)
    {
        var depo = new FakeTurRepository();
        var kullanici = new FakeCurrentUserService { UserId = kullaniciId };

        return (
            new TurOturumServisi(depo, kullanici, NullLogger<TurOturumServisi>.Instance),
            depo, kullanici);
    }

    private static TurKaydetDto KayitIstegi(int durakSayisi = 3) => new()
    {
        Name = "Ankara Kale Turu",
        Color = "#7b5cd6",
        RouteWkt = "LINESTRING (32.85 39.93, 32.86 39.94)",
        RouteDistanceMeters = 2500,
        RouteDurationSeconds = 1800,
        Waypoints = Enumerable.Range(1, durakSayisi).Select(i => new TurDurakKaydetDto
        {
            Name = $"Durak {i}",
            PlaceId = $"osm:node/{i}",
            VenueType = "Museum",
            DwellMinutes = 30,
            Wkt = $"POINT (32.8{i} 39.93)",
        }).ToList(),
    };

    // ------------------------------------------------------------------
    //  Tur şablonu
    // ------------------------------------------------------------------

    [Fact]
    public async Task Oneri_kalici_tura_cevriliyor()
    {
        var (servis, _, _) = Kur();

        var tur = await servis.TuruKaydetAsync(KayitIstegi());

        // Paylaşılabilir bağlantının ön koşulu: turun SUNUCUDA bir id'si olması.
        Assert.True(tur.Id > 0);
        Assert.Equal(RehberId, tur.GuideUserId);
        Assert.Equal(3, tur.Waypoints.Count);

        // Sıra 1..N: istemcinin gönderdiği sıraya güvenmiyoruz.
        Assert.Equal(new[] { 1, 2, 3 }, tur.Waypoints.Select(w => w.Order).ToArray());
        Assert.StartsWith("LINESTRING", tur.RouteWkt);
    }

    [Fact]
    public async Task Kimliksiz_duraga_kimlik_uretiliyor()
    {
        var (servis, _, _) = Kur();

        var istek = KayitIstegi();
        istek.Waypoints[0].PlaceId = "";

        var tur = await servis.TuruKaydetAsync(istek);

        // Boş kimlik "aynı mekan mı?" sorusunu cevapsız bırakır ve mükerrer
        // duraklara kapı açardı.
        Assert.StartsWith("manual:", tur.Waypoints[0].PlaceId);
    }

    [Fact]
    public void Kaydetme_duraklari_tur_id_ISTEMIYOR()
    {
        // CANLIDA ÇIKAN HATA: kaydetme, var olan tura durak ekleyen DTO'yu
        // kullanıyordu ve o DTO TourId'yi zorunlu tutuyor. Tur henüz
        // oluşmadığı için istek her durak başına bir "Tur seçilmelidir"
        // hatasıyla reddediliyordu — altı duraklı öneride altı kez.
        var alanlar = typeof(TurDurakKaydetDto).GetProperties().Select(p => p.Name);

        Assert.DoesNotContain("TourId", alanlar);
        Assert.DoesNotContain("Order", alanlar);   // sıra listeden geliyor
    }

    [Fact]
    public async Task Tek_durakli_tur_reddediliyor()
    {
        var (servis, _, _) = Kur();

        await Assert.ThrowsAsync<IsKuraliException>(
            () => servis.TuruKaydetAsync(KayitIstegi(durakSayisi: 1)));
    }

    // ------------------------------------------------------------------
    //  Oturum açma ve paylaşım
    // ------------------------------------------------------------------

    [Fact]
    public async Task Oturum_acilinca_katilim_kodu_uretiliyor()
    {
        var (servis, depo, _) = Kur();
        var tur = depo.TurKoy(rehberId: RehberId);

        var oturum = await servis.OturumAcAsync(new TourSessionCreateDto { TourId = tur.Id });

        Assert.Equal("Live", oturum.Status);
        Assert.NotNull(oturum.JoinCode);
        Assert.Equal(6, oturum.JoinCode!.Length);

        // Karıştırılabilecek KARAKTERLER alfabede yok: kod telefonda okunup
        // elle yazılıyor. Çiftler: O/0, I/1/L, S/5, B/8.
        //
        // Not: 8 alfabede VAR ve bu bilinçli — karıştığı harf B, o da
        // alfabede olmadığı için belirsizlik doğmuyor. (İlk yazılan test 8'i
        // de yasaklıyordu ve kod 8 içerdiğinde rastgele kırmızı yanıyordu.)
        Assert.DoesNotContain(oturum.JoinCode, c => "OIL015SB".Contains(c));

        // Rehber de katılımcı satırı alıyor — "kim bağlı" listesi oradan.
        Assert.Equal("Guide", oturum.MyRole);
        Assert.Equal(1, oturum.ParticipantCount);
    }

    [Fact]
    public async Task Ayni_turda_ikinci_oturum_acilmiyor()
    {
        var (servis, depo, _) = Kur();
        var tur = depo.TurKoy();

        var ilk = await servis.OturumAcAsync(new TourSessionCreateDto { TourId = tur.Id });
        var ikinci = await servis.OturumAcAsync(new TourSessionCreateDto { TourId = tur.Id });

        // İki açık oturum olsaydı katılımcının hangisine katıldığı belirsizleşir,
        // rehber de iki gruba birden yayın yaptığını sanırdı.
        Assert.Equal(ilk.Id, ikinci.Id);
        Assert.Equal(ilk.JoinCode, ikinci.JoinCode);
    }

    [Fact]
    public async Task Baskasinin_turunun_oturumu_devralinamiyor()
    {
        var (rehberServis, depo, _) = Kur(RehberId);
        var tur = depo.TurKoy(rehberId: RehberId);
        await rehberServis.OturumAcAsync(new TourSessionCreateDto { TourId = tur.Id });

        var baskasi = new TurOturumServisi(
            depo,
            new FakeCurrentUserService { UserId = 99 },
            NullLogger<TurOturumServisi>.Instance);

        await Assert.ThrowsAsync<IsKuraliException>(
            () => baskasi.OturumAcAsync(new TourSessionCreateDto { TourId = tur.Id }));
    }

    [Fact]
    public async Task Baslatilmamis_oturum_planlandi_durumunda_aciliyor()
    {
        var (servis, depo, _) = Kur();
        var tur = depo.TurKoy();

        var oturum = await servis.OturumAcAsync(
            new TourSessionCreateDto { TourId = tur.Id, StartNow = false });

        // Rehber kodu önceden dağıtıp turu sonra başlatabilsin.
        Assert.Equal("Planned", oturum.Status);
        Assert.Null(oturum.StartedUtc);
        Assert.NotNull(oturum.JoinCode);
    }

    // ------------------------------------------------------------------
    //  MİSAFİR görünümü — hesapsız, koda dayalı okuma
    // ------------------------------------------------------------------

    [Fact]
    public async Task Misafir_koduyla_turu_gorebiliyor()
    {
        var (servis, depo, _) = Kur();
        var tur = depo.TurKoy(rehberId: RehberId);
        var oturum = await servis.OturumAcAsync(new TourSessionCreateDto { TourId = tur.Id });

        var misafir = await servis.MisafirGorunumuAsync(oturum.JoinCode!);

        // Paylaşımın bütün anlamı bu: hesabı olmayan biri turu görebilsin.
        Assert.NotNull(misafir);
        Assert.Equal(tur.Name, misafir!.TourName);
        Assert.Equal(3, misafir.Waypoints.Count);
        Assert.Equal(new[] { 1, 2, 3 }, misafir.Waypoints.Select(w => w.Order).ToArray());
    }

    [Fact]
    public async Task Misafir_gorunumunde_KATILIMCI_ADLARI_yok()
    {
        var (servis, depo, _) = Kur();
        var tur = depo.TurKoy(rehberId: RehberId);
        var oturum = await servis.OturumAcAsync(new TourSessionCreateDto { TourId = tur.Id });

        var misafir = await servis.MisafirGorunumuAsync(oturum.JoinCode!);

        // Bu, kimlik doğrulaması OLMADAN dışarı çıkan tek tur biçimi.
        // TourSessionDto'yu anonim uca verseydik gruptaki herkesin kullanıcı
        // adı, kodu ele geçiren birine açılırdı.
        var alanlar = typeof(MisafirTurDto).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("Participants", alanlar);
        Assert.DoesNotContain("JoinCode", alanlar);
        Assert.DoesNotContain("GuideUserId", alanlar);

        // Rehberin ADI kalıyor: "kimin turundayım?" sorusunun cevabı.
        Assert.False(string.IsNullOrWhiteSpace(misafir!.GuideUserName));
    }

    [Fact]
    public async Task Misafir_KATILIMCI_SAYISINI_artirmiyor()
    {
        var (servis, depo, _) = Kur();
        var tur = depo.TurKoy(rehberId: RehberId);
        var oturum = await servis.OturumAcAsync(new TourSessionCreateDto { TourId = tur.Id });

        await servis.MisafirGorunumuAsync(oturum.JoinCode!);
        await servis.MisafirGorunumuAsync(oturum.JoinCode!);

        var guncel = await servis.OturumGetirAsync(oturum.Id);

        // Kimliksiz ziyaretçiyi "katılımcı" saymak rehberin gördüğü grup
        // sayısını yalanlardı: aynı kişi sayfayı üç kez açsa üç kişi görünürdü.
        Assert.Equal(1, guncel!.ParticipantCount);
    }

    [Fact]
    public async Task Sona_ermis_turun_baglantisi_calismiyor()
    {
        var (servis, depo, _) = Kur();
        var tur = depo.TurKoy(rehberId: RehberId);
        var oturum = await servis.OturumAcAsync(new TourSessionCreateDto { TourId = tur.Id });

        await servis.OturumGuncelleAsync(oturum.Id, new TourSessionUpdateDto { Status = "Completed" });

        // Paylaşılan bağlantı tur bitince ölmeli; yoksa eski bir bağlantı
        // aylar sonra da turun programını göstermeye devam ederdi.
        Assert.Null(await servis.MisafirGorunumuAsync(oturum.JoinCode!));
    }

    [Fact]
    public async Task Gecersiz_kod_bos_donuyor()
    {
        var (servis, _, _) = Kur();

        Assert.Null(await servis.MisafirGorunumuAsync("YOKBOY"));
        Assert.Null(await servis.MisafirGorunumuAsync(""));
        Assert.Null(await servis.MisafirGorunumuAsync("   "));
    }

    [Fact]
    public async Task Misafir_kodu_kucuk_harfle_de_calisiyor()
    {
        var (servis, depo, _) = Kur();
        var tur = depo.TurKoy(rehberId: RehberId);
        var oturum = await servis.OturumAcAsync(new TourSessionCreateDto { TourId = tur.Id });

        // Bağlantı elle yazılıyor ya da kopyalanırken kırpılıyor olabilir.
        // Büyük/küçük harf ayrımı yüzünden "kod geçersiz" demek, kullanıcıya
        // sebebini bulamayacağı bir hata göstermek olurdu.
        var misafir = await servis.MisafirGorunumuAsync($"  {oturum.JoinCode!.ToLowerInvariant()}  ");

        Assert.NotNull(misafir);
    }

    // ------------------------------------------------------------------
    //  Katılma
    // ------------------------------------------------------------------

    private static async Task<(TurOturumServisi rehber, TurOturumServisi katilimci, TourSessionDto oturum)>
        AcikOturumKur()
    {
        var depo = new FakeTurRepository();
        var tur = depo.TurKoy(rehberId: RehberId);

        var rehber = new TurOturumServisi(
            depo, new FakeCurrentUserService { UserId = RehberId },
            NullLogger<TurOturumServisi>.Instance);

        var katilimci = new TurOturumServisi(
            depo, new FakeCurrentUserService { UserId = KatilimciId },
            NullLogger<TurOturumServisi>.Instance);

        var oturum = await rehber.OturumAcAsync(new TourSessionCreateDto { TourId = tur.Id });

        return (rehber, katilimci, oturum);
    }

    [Fact]
    public async Task Kodla_katilan_kullanici_katilimci_rolu_aliyor()
    {
        var (_, katilimci, oturum) = await AcikOturumKur();

        var katilim = await katilimci.OturumaKatilAsync(
            new TourSessionJoinDto { JoinCode = oturum.JoinCode! });

        Assert.Equal("Participant", katilim.MyRole);
        Assert.Equal(2, katilim.ParticipantCount);
    }

    [Fact]
    public async Task Katilim_kodu_KATILIMCIYA_gonderilmiyor()
    {
        var (_, katilimci, oturum) = await AcikOturumKur();

        var katilim = await katilimci.OturumaKatilAsync(
            new TourSessionJoinDto { JoinCode = oturum.JoinCode! });

        // Kod herkese gitseydi, tura katılan biri onu başkalarına dağıtabilir
        // ve rehberin grubu kontrolü dışına çıkardı.
        Assert.Null(katilim.JoinCode);
    }

    [Fact]
    public async Task Rehber_kendi_baglantisina_tiklayinca_rolu_degismiyor()
    {
        var (rehber, _, oturum) = await AcikOturumKur();

        var tekrar = await rehber.OturumaKatilAsync(
            new TourSessionJoinDto { JoinCode = oturum.JoinCode! });

        // Katılımcıya dönüşseydi ilerletme düğmesini kaybederdi.
        Assert.Equal("Guide", tekrar.MyRole);
        Assert.NotNull(tekrar.JoinCode);
    }

    [Fact]
    public async Task Kod_kucuk_harfle_de_calisiyor()
    {
        var (_, katilimci, oturum) = await AcikOturumKur();

        // Bağlantıdan kopyalanan kod küçük harfe düşebiliyor; kullanıcıyı
        // "kod yanlış" diye geri çevirmek anlamsız olurdu.
        var katilim = await katilimci.OturumaKatilAsync(
            new TourSessionJoinDto { JoinCode = oturum.JoinCode!.ToLowerInvariant() });

        Assert.Equal(oturum.Id, katilim.Id);
    }

    [Fact]
    public async Task Gecersiz_kod_anlamli_hata_veriyor()
    {
        var (_, katilimci, _) = await AcikOturumKur();

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => katilimci.OturumaKatilAsync(new TourSessionJoinDto { JoinCode = "XXXXXX" }));

        Assert.Contains("geçerli değil", hata.Message);
    }

    [Fact]
    public async Task Biten_turun_koduyla_katilinamiyor()
    {
        var (rehber, katilimci, oturum) = await AcikOturumKur();

        await rehber.OturumGuncelleAsync(oturum.Id, new TourSessionUpdateDto { Status = "Completed" });

        await Assert.ThrowsAsync<IsKuraliException>(
            () => katilimci.OturumaKatilAsync(new TourSessionJoinDto { JoinCode = oturum.JoinCode! }));
    }

    // ------------------------------------------------------------------
    //  İlerletme
    // ------------------------------------------------------------------

    [Fact]
    public async Task Rehber_duragi_ilerletince_varis_damgalaniyor()
    {
        var (rehber, _, oturum) = await AcikOturumKur();
        var tam = await rehber.OturumGetirAsync(oturum.Id);
        var ilkDurak = tam!.CurrentWaypointId;

        Assert.Null(ilkDurak);   // henüz varılmadı

        var tur = await rehber.TurGetirAsync(oturum.TourId);
        var hedef = tur!.Waypoints[0];

        var guncel = await rehber.OturumGuncelleAsync(oturum.Id, new TourSessionUpdateDto
        {
            Status = "Live",
            CurrentWaypointId = hedef.Id,
        });

        Assert.Equal(hedef.Id, guncel.CurrentWaypointId);
        Assert.Equal(1, guncel.CurrentWaypointOrder);
        Assert.Equal(2, guncel.NextWaypointOrder);

        // Varış anı kalış süresi geri sayımının başlangıcı (turIlerleme.js).
        Assert.NotNull(guncel.CurrentWaypointArrivedUtc);

        // 3 duraklı turda 1. durak = %33.
        Assert.Equal(33.3, guncel.ProgressPercent, 1);
    }

    [Fact]
    public async Task Katilimci_turu_ilerletemiyor()
    {
        var (_, katilimci, oturum) = await AcikOturumKur();
        await katilimci.OturumaKatilAsync(new TourSessionJoinDto { JoinCode = oturum.JoinCode! });

        var hata = await Assert.ThrowsAsync<IsKuraliException>(
            () => katilimci.OturumGuncelleAsync(oturum.Id, new TourSessionUpdateDto { Status = "Paused" }));

        // Asıl kural: yetki değil, BU OTURUMUN rehberi olmak.
        Assert.Contains("rehber", hata.Message);
    }

    [Fact]
    public async Task Baska_turun_duragina_ilerletilemiyor()
    {
        var depo = new FakeTurRepository();
        var tur = depo.TurKoy(rehberId: RehberId);
        var baskaTur = depo.TurKoy("Başka Tur", RehberId);

        var rehber = new TurOturumServisi(
            depo, new FakeCurrentUserService { UserId = RehberId },
            NullLogger<TurOturumServisi>.Instance);

        var oturum = await rehber.OturumAcAsync(new TourSessionCreateDto { TourId = tur.Id });

        await Assert.ThrowsAsync<IsKuraliException>(
            () => rehber.OturumGuncelleAsync(oturum.Id, new TourSessionUpdateDto
            {
                Status = "Live",
                CurrentWaypointId = baskaTur.Waypoints.First().Id,
            }));
    }

    [Fact]
    public async Task Sona_ermis_oturum_guncellenemiyor()
    {
        var (rehber, _, oturum) = await AcikOturumKur();

        await rehber.OturumGuncelleAsync(oturum.Id, new TourSessionUpdateDto { Status = "Completed" });

        await Assert.ThrowsAsync<IsKuraliException>(
            () => rehber.OturumGuncelleAsync(oturum.Id, new TourSessionUpdateDto { Status = "Live" }));
    }

    [Fact]
    public async Task Tanimsiz_durum_reddediliyor()
    {
        var (rehber, _, oturum) = await AcikOturumKur();

        await Assert.ThrowsAsync<IsKuraliException>(
            () => rehber.OturumGuncelleAsync(oturum.Id, new TourSessionUpdateDto { Status = "Uyuyor" }));
    }

    // ------------------------------------------------------------------
    //  Listeleme ve ayrılma
    // ------------------------------------------------------------------

    [Fact]
    public async Task Katilinan_oturumlar_listeleniyor()
    {
        var (rehber, katilimci, oturum) = await AcikOturumKur();
        await katilimci.OturumaKatilAsync(new TourSessionJoinDto { JoinCode = oturum.JoinCode! });

        Assert.Single(await rehber.OturumlarimAsync());
        Assert.Single(await katilimci.OturumlarimAsync());
    }

    [Fact]
    public async Task Ayrilan_kullanici_listeden_dusuyor()
    {
        var (_, katilimci, oturum) = await AcikOturumKur();
        await katilimci.OturumaKatilAsync(new TourSessionJoinDto { JoinCode = oturum.JoinCode! });

        Assert.True(await katilimci.OturumdanAyrilAsync(oturum.Id));
        Assert.Empty(await katilimci.OturumlarimAsync());

        // Satır SİLİNMİYOR: yoklama listesi tur bittikten sonra da anlamlı.
        var oturumSon = await katilimci.OturumGetirAsync(oturum.Id);
        Assert.Contains(oturumSon!.Participants, k => k.UserId == KatilimciId && k.LeftUtc is not null);
    }

    [Fact]
    public async Task Acik_oturumu_olan_tur_silinemiyor()
    {
        var (rehber, depo, _) = Kur();
        var tur = depo.TurKoy(rehberId: RehberId);
        await rehber.OturumAcAsync(new TourSessionCreateDto { TourId = tur.Id });

        await Assert.ThrowsAsync<IsKuraliException>(() => rehber.TurSilAsync(tur.Id));
    }

    [Fact]
    public async Task Turu_yalnizca_olusturan_silebiliyor()
    {
        var (_, depo, _) = Kur();
        var tur = depo.TurKoy(rehberId: RehberId);

        var baskasi = new TurOturumServisi(
            depo, new FakeCurrentUserService { UserId = 99 },
            NullLogger<TurOturumServisi>.Instance);

        await Assert.ThrowsAsync<IsKuraliException>(() => baskasi.TurSilAsync(tur.Id));
    }
}
