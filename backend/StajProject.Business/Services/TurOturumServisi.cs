using NetTopologySuite.Geometries;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Validation;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

/// <summary>
/// <see cref="ITurOturumServisi"/> gerçeklemesi.
///
/// Bu sınıfın taşıdığı iki kural, modülün tamamını ayakta tutuyor:
///
///   1. YETKİ OTURUMA ÖZGÜ. "Tur Yönetimi" yetkisi tur AÇMAYA yeter; ama bir
///      oturumu ilerletmek yalnızca O OTURUMUN rehberinin işi. Yetkiye
///      baksaydık, tur açabilen herkes başkasının grubunu yönlendirebilirdi.
///
///   2. KOD, KİMLİĞİN YERİNE GEÇMİYOR. Katılım kodu bilen kişi oturuma
///      KATILABİLİR ama yine de giriş yapmış olmak zorunda: katılım satırı bir
///      kullanıcıya bağlanıyor, yoksa "kimler katıldı" listesi anlamsız olurdu.
/// </summary>
public class TurOturumServisi : ITurOturumServisi
{
    /// <summary>
    /// Katılım kodunun alfabesi — KARIŞTIRILABİLECEK harfler yok.
    ///
    /// 0/O, 1/I/l, 5/S, 8/B dışarıda: kod telefonda okunuyor ve elle
    /// yazılıyor. "Kod yanlış" diyen bir katılımcının çoğu zaman kodu değil
    /// harfi yanlış okuduğu, bu tür ekranların bilinen sorunu.
    /// </summary>
    private const string KodAlfabesi = "ACDEFGHJKMNPQRTUVWXYZ2346789";

    /// <summary>Kod uzunluğu. 6 karakter ≈ 480 milyon olasılık.</summary>
    private const int KodUzunlugu = 6;

    /// <summary>Kod çakışırsa kaç kez yeniden denensin.</summary>
    private const int KodDenemeSayisi = 8;

    private readonly ITurRepository _depo;
    private readonly ICurrentUserService _kullanici;
    private readonly ILogger<TurOturumServisi> _logger;

    public TurOturumServisi(
        ITurRepository depo,
        ICurrentUserService kullanici,
        ILogger<TurOturumServisi> logger)
    {
        _depo = depo;
        _kullanici = kullanici;
        _logger = logger;
    }

    // ==================================================================
    //  Tur şablonu
    // ==================================================================

    public async Task<TourDto> TuruKaydetAsync(TurKaydetDto istek)
    {
        if (istek.Waypoints.Count < 2)
        {
            throw new IsKuraliException("Tur en az iki durak içermelidir.");
        }

        var kullaniciId = _kullanici.RequireUserId();

        var tur = new Tour
        {
            Name = istek.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(istek.Description) ? null : istek.Description.Trim(),
            Color = istek.Color,
            GuideUserId = kullaniciId,
            RouteDistanceMeters = istek.RouteDistanceMeters,
            RouteDurationSeconds = istek.RouteDurationSeconds,
            CreatedDate = DateTime.UtcNow,
        };

        if (!string.IsNullOrWhiteSpace(istek.RouteWkt))
        {
            // Bozuk WKT WktFormatException fırlatıyor ve 400'e çevriliyor:
            // rota geometrisini sessizce atmak, haritada çizgisiz bir tur
            // bırakır ve sebebi hiçbir yerde görünmezdi.
            tur.Route = WktConverter.Read<NetTopologySuite.Geometries.LineString>(istek.RouteWkt);
            tur.RouteCalculatedUtc = DateTime.UtcNow;
        }

        var sira = 1;

        foreach (var durak in istek.Waypoints)
        {
            tur.Waypoints.Add(new Waypoint
            {
                Order = sira++,
                Name = durak.Name.Trim(),
                PlaceId = string.IsNullOrWhiteSpace(durak.PlaceId)
                    // Kimliksiz durak "aynı mekan mı?" sorusunu cevapsız
                    // bırakırdı; elle eklenen noktalara üretiliyor.
                    ? $"manual:{Guid.NewGuid():N}"
                    : durak.PlaceId.Trim(),
                PoiId = durak.PoiId,
                VenueType = MekanTipiCoz(durak.VenueType),
                DwellMinutes = durak.DwellMinutes,
                Note = durak.Note,
                Geom = WktConverter.Read<NetTopologySuite.Geometries.Point>(durak.Wkt),
                CreatedDate = DateTime.UtcNow,
            });
        }

        var kayitli = await _depo.TurEkleAsync(tur);

        _logger.LogInformation(
            "Tur kaydedildi: {Ad} ({Durak} durak), rehber {Kullanici}.",
            kayitli.Name, kayitli.Waypoints.Count, kullaniciId);

        return TuraCevir(kayitli);
    }

    public async Task<List<TourDto>> TurlariGetirAsync()
        => (await _depo.TurlariGetirAsync()).Select(TuraCevir).ToList();

    public async Task<TourDto?> TurGetirAsync(int id)
    {
        var tur = await _depo.TurGetirAsync(id);
        return tur is null ? null : TuraCevir(tur);
    }

    public async Task<bool> TurSilAsync(int id)
    {
        var tur = await _depo.TurGetirAsync(id);
        if (tur is null) return false;

        // Turu yalnızca tanımlayan rehber silebilir: ortak bir katalog değil,
        // kişinin kendi planı. (Yönetici gerekirse çöp kutusundan yönetiyor.)
        if (tur.GuideUserId != _kullanici.UserId)
        {
            throw new IsKuraliException("Bu turu yalnızca oluşturan kullanıcı silebilir.");
        }

        if (await _depo.AcikOturumAsync(id) is not null)
        {
            throw new IsKuraliException("Turun açık bir oturumu var; önce oturumu bitirin.");
        }

        return await _depo.TurSilAsync(id);
    }

    // ==================================================================
    //  Canlı oturum
    // ==================================================================

    public async Task<TourSessionDto> OturumAcAsync(TourSessionCreateDto istek)
    {
        var kullaniciId = _kullanici.RequireUserId();

        var tur = await _depo.TurGetirAsync(istek.TourId)
                  ?? throw new IsKuraliException("Tur bulunamadı.");

        if (tur.Waypoints.Count < 2)
        {
            throw new IsKuraliException("Turda en az iki durak olmalı.");
        }

        // Aynı turun açık oturumu varsa YENİSİ AÇILMIYOR.
        var mevcut = await _depo.AcikOturumAsync(istek.TourId);

        if (mevcut is not null)
        {
            // Rehber aynı kişiyse bu bir "yeniden aç" isteği: kodu ve oturumu
            // geri veriyoruz, yeni bir grup oluşturmuyoruz.
            if (mevcut.GuideUserId != kullaniciId)
            {
                throw new IsKuraliException(
                    "Bu turun başka bir rehber tarafından açılmış canlı oturumu var.");
            }

            return await OturumaCevirAsync(mevcut, kullaniciId);
        }

        var oturum = new TourSession
        {
            TourId = istek.TourId,
            GuideUserId = kullaniciId,
            Status = istek.StartNow ? TourSessionStatus.Live : TourSessionStatus.Planned,
            StartedUtc = istek.StartNow ? DateTime.UtcNow : null,
            JoinCode = await KodUretAsync(),
            CreatedDate = DateTime.UtcNow,
        };

        var kayitli = await _depo.OturumEkleAsync(oturum);

        // Rehber de KATILIMCI satırı alıyor: "kimler bağlı" listesi ve yayın
        // grubu üyeliği bu satırdan yürüyor. GuideUserId "kim sorumlu"
        // sorusunun cevabı, katılım satırı "kim bağlı" sorusunun.
        await _depo.KatilimYazAsync(new TourSessionParticipant
        {
            TourSessionId = kayitli.Id,
            UserId = kullaniciId,
            Role = TourRole.Guide,
            JoinedUtc = DateTime.UtcNow,
        });

        _logger.LogInformation(
            "Tur oturumu açıldı: tur {Tur}, kod {Kod}, rehber {Kullanici}.",
            istek.TourId, kayitli.JoinCode, kullaniciId);

        var taze = await _depo.OturumGetirAsync(kayitli.Id) ?? kayitli;
        return await OturumaCevirAsync(taze, kullaniciId);
    }

    public async Task<TourSessionDto> OturumaKatilAsync(TourSessionJoinDto istek)
    {
        var kullaniciId = _kullanici.RequireUserId();
        var kod = (istek.JoinCode ?? string.Empty).Trim().ToUpperInvariant();

        if (kod.Length == 0)
        {
            throw new IsKuraliException("Katılım kodu zorunludur.");
        }

        var oturum = await _depo.OturumKodlaGetirAsync(kod)
                     ?? throw new IsKuraliException(
                         "Bu katılım kodu geçerli değil ya da tur sona ermiş.");

        // Rehberin kendi turuna "katılması" rolünü DEĞİŞTİRMEMELİ: bağlantıya
        // kendi tıkladığında katılımcıya dönüşseydi ilerletme düğmesini
        // kaybederdi.
        var rol = oturum.GuideUserId == kullaniciId ? TourRole.Guide : TourRole.Participant;

        await _depo.KatilimYazAsync(new TourSessionParticipant
        {
            TourSessionId = oturum.Id,
            UserId = kullaniciId,
            Role = rol,
            JoinedUtc = DateTime.UtcNow,
        });

        var taze = await _depo.OturumGetirAsync(oturum.Id) ?? oturum;
        return await OturumaCevirAsync(taze, kullaniciId);
    }

    /// <summary>
    /// Misafirin gördüğü tur (bkz. arayüzdeki gerekçe).
    ///
    /// ---- BURADA YAZMA YOK ----
    /// <see cref="OturumaKatilAsync"/> katılım satırı yazıyor; bu metot
    /// yalnızca okuyor. Misafir sayacı tutmak cazip görünüyor ama kimliksiz
    /// bir ziyaretçiyi "katılımcı" saymak, rehberin gördüğü grup sayısını
    /// yalanlardı: aynı kişi sayfayı üç kez açsa üç kişi görünürdü.
    /// </summary>
    public async Task<MisafirTurDto?> MisafirGorunumuAsync(string katilimKodu)
    {
        var kod = (katilimKodu ?? string.Empty).Trim().ToUpperInvariant();

        if (kod.Length == 0)
        {
            return null;
        }

        // Depo yalnızca AÇIK oturumları koda göre buluyor (kısmi benzersiz
        // index de yalnızca onları kapsıyor), yani kapanmış tur zaten null.
        var oturum = await _depo.OturumKodlaGetirAsync(kod);

        if (oturum is null || oturum.Status is TourSessionStatus.Completed
                                            or TourSessionStatus.Cancelled)
        {
            return null;
        }

        var duraklar = oturum.Tour?.Waypoints.OrderBy(w => w.Order).ToList()
                       ?? new List<Waypoint>();

        var mevcut = duraklar.FirstOrDefault(w => w.Id == oturum.CurrentWaypointId);
        var sonraki = mevcut is null
            ? duraklar.FirstOrDefault()
            : duraklar.FirstOrDefault(w => w.Order == mevcut.Order + 1);

        return new MisafirTurDto
        {
            TourName = oturum.Tour?.Name ?? string.Empty,
            Color = oturum.Tour?.Color ?? "#7b5cd6",
            Status = oturum.Status.ToString(),
            GuideUserName = oturum.GuideUser?.Username ?? string.Empty,

            // Numara YALNIZCA CANLI turda dışarı çıkıyor. Planlanmış ya da
            // duraklatılmış bir oturumda kimse yolda değildir; kodu eline
            // geçiren birine rehberin numarasını vermenin gerekçesi de olmaz.
            GuidePhone = oturum.Status == TourSessionStatus.Live
                ? oturum.GuideUser?.PhoneNumber
                : null,

            StartedUtc = oturum.StartedUtc,

            CurrentWaypointOrder = mevcut?.Order,
            CurrentWaypointName = mevcut?.Name,
            CurrentWaypointArrivedUtc = oturum.CurrentWaypointArrivedUtc,
            NextWaypointOrder = sonraki?.Order,
            NextWaypointName = sonraki?.Name,
            ProgressPercent = oturum.ProgressPercent,

            OturumId = oturum.Id,

            GuideLon = oturum.LastPosition?.X,
            GuideLat = oturum.LastPosition?.Y,
            GuidePositionUtc = oturum.LastPositionUtc,

            RouteWkt = oturum.Tour?.Route?.AsText(),
            RouteDistanceMeters = oturum.Tour?.RouteDistanceMeters,
            RouteDurationSeconds = oturum.Tour?.RouteDurationSeconds,

            Waypoints = duraklar.Select(w => new MisafirDurakDto
            {
                Order = w.Order,
                Name = w.Name,
                VenueType = w.VenueType.ToString(),
                DwellMinutes = w.DwellMinutes,
                Wkt = w.Geom.AsText(),
                Note = w.Note,
            }).ToList(),
        };
    }

    public async Task<TourSessionDto> KonumBildirAsync(int oturumId, TurKonumDto konum)
    {
        var oturum = await RehberOturumu(oturumId);

        oturum.LastPosition = new Point(konum.Lon, konum.Lat) { SRID = 4326 };
        oturum.LastPositionUtc = DateTime.UtcNow;

        var guncel = await _depo.OturumGuncelleAsync(oturum) ?? oturum;
        return await OturumaCevirAsync(guncel, _kullanici.UserId);
    }

    public async Task<TourSessionDto> KonumYayininiDurdurAsync(int oturumId)
    {
        var oturum = await RehberOturumu(oturumId);

        oturum.LastPosition = null;
        oturum.LastPositionUtc = null;

        var guncel = await _depo.OturumGuncelleAsync(oturum) ?? oturum;
        return await OturumaCevirAsync(guncel, _kullanici.UserId);
    }

    public Task RehberDogrulaAsync(int oturumId) => RehberOturumu(oturumId);

    public async Task<int?> MisafirOturumIdAsync(string katilimKodu)
    {
        var kod = (katilimKodu ?? string.Empty).Trim().ToUpperInvariant();

        if (kod.Length == 0)
        {
            return null;
        }

        var oturum = await _depo.OturumKodlaGetirAsync(kod);

        if (oturum is null || oturum.Status is TourSessionStatus.Completed
                                            or TourSessionStatus.Cancelled)
        {
            return null;
        }

        return oturum.Id;
    }

    /// <summary>
    /// Oturumu getirip çağıranın O OTURUMUN REHBERİ olduğunu doğrular.
    ///
    /// "Tur Yönetimi" yetkisine bakmıyoruz: tur açabilen herkes BAŞKASININ
    /// grubunun konumunu yayınlayabilir olurdu — hata vermeyen, yalnızca
    /// yanlış davranan bir açık (oturum ilerletmedeki kararla aynı).
    /// </summary>
    private async Task<TourSession> RehberOturumu(int oturumId)
    {
        var kullaniciId = _kullanici.RequireUserId();

        var oturum = await _depo.OturumGetirAsync(oturumId)
                     ?? throw new IsKuraliException("Oturum bulunamadı.");

        if (oturum.GuideUserId != kullaniciId)
        {
            throw new IsKuraliException("Konumu yalnızca turun rehberi paylaşabilir.");
        }

        return oturum;
    }

    public async Task<TourSessionDto?> OturumGetirAsync(int id)
    {
        var oturum = await _depo.OturumGetirAsync(id);
        return oturum is null ? null : await OturumaCevirAsync(oturum, _kullanici.UserId);
    }

    public async Task<List<TourSessionDto>> OturumlarimAsync()
    {
        var kullaniciId = _kullanici.RequireUserId();
        var oturumlar = await _depo.KullanicininOturumlariAsync(kullaniciId);

        var sonuc = new List<TourSessionDto>();

        foreach (var oturum in oturumlar)
        {
            sonuc.Add(await OturumaCevirAsync(oturum, kullaniciId));
        }

        return sonuc;
    }

    public async Task<TourSessionDto> OturumGuncelleAsync(int id, TourSessionUpdateDto istek)
    {
        var kullaniciId = _kullanici.RequireUserId();

        var oturum = await _depo.OturumGetirAsync(id)
                     ?? throw new IsKuraliException("Oturum bulunamadı.");

        // ---- YETKİ: OTURUMUN REHBERİ Mİ? ----
        //
        // "Tur Yönetimi" yetkisine bakmak YETMEZ: o yetki tur açmaya yeter,
        // başkasının grubunu yönlendirmeye değil. Kontrol bu yüzden yetkide
        // değil burada (POI'de "kendi kaydını düzenleyebilir" kuralının aynı
        // fikri).
        if (oturum.GuideUserId != kullaniciId)
        {
            throw new IsKuraliException("Turu yalnızca rehber ilerletebilir.");
        }

        if (oturum.Status is TourSessionStatus.Completed or TourSessionStatus.Cancelled)
        {
            throw new IsKuraliException("Sona ermiş bir tur güncellenemez.");
        }

        var yeniDurum = DurumCoz(istek.Status);

        oturum.Status = yeniDurum;

        if (yeniDurum == TourSessionStatus.Live && oturum.StartedUtc is null)
        {
            oturum.StartedUtc = DateTime.UtcNow;
        }

        if (yeniDurum is TourSessionStatus.Completed or TourSessionStatus.Cancelled)
        {
            oturum.EndedUtc = DateTime.UtcNow;
        }

        if (istek.CurrentWaypointId is not null)
        {
            var durak = oturum.Tour?.Waypoints
                .FirstOrDefault(w => w.Id == istek.CurrentWaypointId.Value);

            if (durak is null)
            {
                throw new IsKuraliException("Durak bu tura ait değil.");
            }

            oturum.CurrentWaypointId = durak.Id;

            // Varış anı burada damgalanıyor: kalış süresinin geri sayımı ve
            // "planlanan süre doldu" uyarısı bu değere dayanıyor.
            oturum.CurrentWaypointArrivedUtc = DateTime.UtcNow;
            oturum.ProgressPercent = IlerlemeYuzdesi(oturum.Tour, durak.Order);
        }

        var guncel = await _depo.OturumGuncelleAsync(oturum)
                     ?? throw new IsKuraliException("Oturum bulunamadı.");

        return await OturumaCevirAsync(guncel, kullaniciId);
    }

    public async Task<bool> OturumdanAyrilAsync(int id)
        => await _depo.AyrilAsync(id, _kullanici.RequireUserId());

    // ==================================================================
    //  Yardımcılar
    // ==================================================================

    /// <summary>
    /// Kullanımdaki kodlarla çakışmayan bir katılım kodu üretir.
    ///
    /// Rastgelelik <see cref="RandomNumberGenerator"/>'dan: <c>Random</c>
    /// tahmin edilebilir bir dizi üretiyor ve kod, oturuma katılmanın tek
    /// anahtarı. Birkaç yüz milyon olasılık içinde tahmin etmek zor ama
    /// SIRAYLA üretilen bir dizide bir sonraki kodu bilmek kolay olurdu.
    /// </summary>
    private async Task<string> KodUretAsync()
    {
        for (var deneme = 0; deneme < KodDenemeSayisi; deneme++)
        {
            var kod = string.Concat(Enumerable.Range(0, KodUzunlugu)
                .Select(_ => KodAlfabesi[RandomNumberGenerator.GetInt32(KodAlfabesi.Length)]));

            if (!await _depo.KodKullanimdaAsync(kod))
            {
                return kod;
            }
        }

        // Buraya düşmek pratikte imkânsız (aynı anda milyonlarca açık oturum
        // gerekirdi); yine de sessizce çakışan bir kod döndürmüyoruz.
        throw new IsKuraliException("Katılım kodu üretilemedi, lütfen tekrar deneyin.");
    }

    /// <summary>Metin durumu enum'a çevirir; tanınmazsa iş kuralı hatası.</summary>
    private static TourSessionStatus DurumCoz(string? durum)
        => durum switch
        {
            "Live" => TourSessionStatus.Live,
            "Paused" => TourSessionStatus.Paused,
            "Completed" => TourSessionStatus.Completed,
            "Cancelled" => TourSessionStatus.Cancelled,
            "Planned" => TourSessionStatus.Planned,
            _ => throw new IsKuraliException(
                "Durum Live, Paused, Completed, Cancelled ya da Planned olmalıdır."),
        };

    private static VenueType MekanTipiCoz(string? tip)
        => Enum.TryParse<VenueType>(tip, ignoreCase: true, out var sonuc)
            ? sonuc
            : VenueType.Other;

    /// <summary>
    /// Kaçıncı duraktayız → yüzde.
    ///
    /// Rota MESAFESİNE değil DURAK SAYISINA dayanıyor: arayüz "3 / 8 durak"
    /// yazıyor ve iki farklı yüzde göstermek kafa karıştırırdı
    /// (bkz. turIlerleme.js → durakYuzdesi).
    /// </summary>
    private static double IlerlemeYuzdesi(Tour? tur, int durakSirasi)
    {
        var toplam = tur?.Waypoints.Count ?? 0;
        return toplam == 0 ? 0 : Math.Round((double)durakSirasi / toplam * 100, 1);
    }

    // ---------------- DTO çevirimi ----------------

    private static TourDto TuraCevir(Tour tur) => new()
    {
        Id = tur.Id,
        Name = tur.Name,
        Description = tur.Description,
        Color = tur.Color,
        ScheduledStartUtc = tur.ScheduledStartUtc,
        GuideUserId = tur.GuideUserId,
        GuideUserName = tur.GuideUser?.Username,
        Waypoints = tur.Waypoints
            .OrderBy(w => w.Order)
            .Select(DuragaCevir)
            .ToList(),
        RouteWkt = tur.Route is null ? null : WktConverter.Write(tur.Route),
        RouteDistanceMeters = tur.RouteDistanceMeters,
        RouteDurationSeconds = tur.RouteDurationSeconds,
        RouteUpToDate = tur.Route is not null,
        CreatedDate = tur.CreatedDate,
        ModifiedDate = tur.ModifiedDate,
        IsActive = tur.IsActive,
    };

    private static WaypointDto DuragaCevir(Waypoint durak) => new()
    {
        Id = durak.Id,
        TourId = durak.TourId,
        Order = durak.Order,
        PlaceId = durak.PlaceId,
        PoiId = durak.PoiId,
        Name = durak.Name,
        VenueType = durak.VenueType.ToString(),
        DwellMinutes = durak.DwellMinutes,
        Wkt = WktConverter.Write(durak.Geom),
        Note = durak.Note,
        CreatedDate = durak.CreatedDate,
        ModifiedDate = durak.ModifiedDate,
        IsActive = durak.IsActive,
    };

    /// <summary>
    /// Oturumu DTO'ya çevirir — İSTEĞİ YAPAN KULLANICIYA GÖRE.
    ///
    /// İki alan kişiye özel:
    ///   • <c>MyRole</c>  → arayüz hangi düğmeyi göstereceğini buradan biliyor
    ///   • <c>JoinCode</c> → YALNIZCA rehbere gidiyor; katılımcıya null.
    ///     Kod herkese gitseydi, tura katılan biri onu başkalarına dağıtabilir
    ///     ve rehberin grubu kontrolü dışına çıkardı.
    /// </summary>
    private Task<TourSessionDto> OturumaCevirAsync(TourSession oturum, int? kullaniciId)
    {
        var duraklar = oturum.Tour?.Waypoints.OrderBy(w => w.Order).ToList() ?? new List<Waypoint>();
        var mevcut = duraklar.FirstOrDefault(w => w.Id == oturum.CurrentWaypointId);
        var sonraki = mevcut is null
            ? duraklar.FirstOrDefault()
            : duraklar.FirstOrDefault(w => w.Order == mevcut.Order + 1);

        var katilim = oturum.Participants
            .FirstOrDefault(k => k.UserId == kullaniciId && k.LeftUtc == null);

        var rehberMi = katilim?.Role == TourRole.Guide || oturum.GuideUserId == kullaniciId;

        return Task.FromResult(new TourSessionDto
        {
            Id = oturum.Id,
            TourId = oturum.TourId,
            TourName = oturum.Tour?.Name ?? string.Empty,
            Color = oturum.Tour?.Color ?? "#7b5cd6",
            Status = oturum.Status.ToString(),
            JoinCode = rehberMi ? oturum.JoinCode : null,
            GuideUserId = oturum.GuideUserId,
            GuideUserName = oturum.GuideUser?.Username ?? string.Empty,
            StartedUtc = oturum.StartedUtc,
            EndedUtc = oturum.EndedUtc,
            CurrentWaypointId = mevcut?.Id,
            CurrentWaypointOrder = mevcut?.Order,
            CurrentWaypointName = mevcut?.Name,
            CurrentWaypointArrivedUtc = oturum.CurrentWaypointArrivedUtc,
            NextWaypointOrder = sonraki?.Order,
            NextWaypointName = sonraki?.Name,
            ProgressPercent = oturum.ProgressPercent,
            LastLon = oturum.LastPosition?.X,
            LastLat = oturum.LastPosition?.Y,
            LastPositionUtc = oturum.LastPositionUtc,
            ParticipantCount = oturum.Participants.Count(k => k.LeftUtc == null),
            MyRole = katilim?.Role.ToString(),
            Participants = oturum.Participants
                .OrderBy(k => k.JoinedUtc)
                .Select(k => new TourParticipantDto
                {
                    UserId = k.UserId,
                    UserName = k.User?.Username ?? string.Empty,
                    Role = k.Role.ToString(),
                    JoinedUtc = k.JoinedUtc,
                    LeftUtc = k.LeftUtc,
                })
                .ToList(),
        });
    }
}
