using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Bellekte çalışan sahte tur deposu.
///
/// Gerçek depo EF Core üzerinden çalışıyor ve testlerin sorduğu sorular
/// ("kod üretiliyor mu, rol doğru mu, rehber olmayan ilerletebiliyor mu")
/// veritabanı gerektirmiyor. Gerçek DbContext'e bağlansaydık test paketi
/// PostgreSQL'in ayakta olmasına bağlanırdı.
///
/// Navigasyonlar ELLE bağlanıyor (tur ↔ oturum ↔ katılımcı): EF bunu kendisi
/// yapıyor, sahte depoda yapmazsak DTO çevirimi boş adlarla dönerdi ve
/// testler gerçekte olmayan bir hatayı raporlardı.
/// </summary>
public class FakeTurRepository : ITurRepository
{
    private static readonly TourSessionStatus[] AcikDurumlar =
    {
        TourSessionStatus.Planned,
        TourSessionStatus.Live,
        TourSessionStatus.Paused,
    };

    private readonly List<Tour> _turlar = new();
    private readonly List<TourSession> _oturumlar = new();

    private int _sonrakiTurId = 1;
    private int _sonrakiDurakId = 100;
    private int _sonrakiOturumId = 1;

    /// <summary>Üretilen bütün katılım kodları — benzersizlik testinin baktığı yer.</summary>
    public List<string> UretilenKodlar { get; } = new();

    // ---------------- Tur ----------------

    public Task<List<Tour>> TurlariGetirAsync(int? kullaniciId = null)
        => Task.FromResult(_turlar
            .Where(t => !t.IsDeleted && (kullaniciId is null || t.GuideUserId == kullaniciId))
            .ToList());

    public Task<Tour?> TurGetirAsync(int id)
        => Task.FromResult(_turlar.FirstOrDefault(t => t.Id == id && !t.IsDeleted));

    public Task<Tour> TurEkleAsync(Tour tur)
    {
        tur.Id = _sonrakiTurId++;

        foreach (var durak in tur.Waypoints)
        {
            durak.Id = _sonrakiDurakId++;
            durak.TourId = tur.Id;
        }

        _turlar.Add(tur);
        return Task.FromResult(tur);
    }

    public Task<bool> TurSilAsync(int id)
    {
        var tur = _turlar.FirstOrDefault(t => t.Id == id);
        if (tur is null) return Task.FromResult(false);

        tur.IsDeleted = true;
        return Task.FromResult(true);
    }

    /// <summary>Testin hazır bir tur koyması için.</summary>
    public Tour TurKoy(string ad = "Ankara Turu", int rehberId = 1, int durakSayisi = 3)
    {
        var tur = new Tour { Name = ad, GuideUserId = rehberId, Color = "#7b5cd6" };

        for (var i = 1; i <= durakSayisi; i++)
        {
            tur.Waypoints.Add(new Waypoint
            {
                Order = i,
                Name = $"Durak {i}",
                PlaceId = $"osm:node/{i}",
                DwellMinutes = 30,
                Geom = new NetTopologySuite.Geometries.Point(32.85 + i * 0.01, 39.93) { SRID = 4326 },
            });
        }

        return TurEkleAsync(tur).Result;
    }

    // ---------------- Oturum ----------------

    public Task<TourSession?> OturumGetirAsync(int id)
        => Task.FromResult(_oturumlar.FirstOrDefault(o => o.Id == id));

    public Task<TourSession?> OturumKodlaGetirAsync(string katilimKodu)
        => Task.FromResult(_oturumlar.FirstOrDefault(o =>
            o.JoinCode == katilimKodu && AcikDurumlar.Contains(o.Status)));

    public Task<TourSession?> AcikOturumAsync(int turId)
        => Task.FromResult(_oturumlar.FirstOrDefault(o =>
            o.TourId == turId && AcikDurumlar.Contains(o.Status)));

    public Task<List<TourSession>> KullanicininOturumlariAsync(int kullaniciId)
        => Task.FromResult(_oturumlar
            .Where(o => AcikDurumlar.Contains(o.Status)
                && o.Participants.Any(k => k.UserId == kullaniciId && k.LeftUtc is null))
            .ToList());

    public Task<TourSession> OturumEkleAsync(TourSession oturum)
    {
        oturum.Id = _sonrakiOturumId++;
        oturum.Tour = _turlar.FirstOrDefault(t => t.Id == oturum.TourId);
        oturum.GuideUser = new User { Id = oturum.GuideUserId, Username = $"kullanici{oturum.GuideUserId}" };

        UretilenKodlar.Add(oturum.JoinCode);
        _oturumlar.Add(oturum);

        return Task.FromResult(oturum);
    }

    public Task<TourSession?> OturumGuncelleAsync(TourSession oturum)
    {
        var mevcut = _oturumlar.FirstOrDefault(o => o.Id == oturum.Id);
        if (mevcut is null) return Task.FromResult<TourSession?>(null);

        mevcut.Status = oturum.Status;
        mevcut.CurrentWaypointId = oturum.CurrentWaypointId;
        mevcut.CurrentWaypointArrivedUtc = oturum.CurrentWaypointArrivedUtc;
        mevcut.LastPosition = oturum.LastPosition;
        mevcut.LastPositionUtc = oturum.LastPositionUtc;
        mevcut.ProgressPercent = oturum.ProgressPercent;
        mevcut.StartedUtc = oturum.StartedUtc;
        mevcut.EndedUtc = oturum.EndedUtc;

        return Task.FromResult<TourSession?>(mevcut);
    }

    public Task<TourSessionParticipant> KatilimYazAsync(TourSessionParticipant katilim)
    {
        var oturum = _oturumlar.First(o => o.Id == katilim.TourSessionId);
        var mevcut = oturum.Participants.FirstOrDefault(k => k.UserId == katilim.UserId);

        if (mevcut is null)
        {
            katilim.User = new User { Id = katilim.UserId, Username = $"kullanici{katilim.UserId}" };
            oturum.Participants.Add(katilim);
            return Task.FromResult(katilim);
        }

        mevcut.LeftUtc = null;
        mevcut.Role = katilim.Role;
        return Task.FromResult(mevcut);
    }

    public Task<bool> AyrilAsync(int oturumId, int kullaniciId)
    {
        var katilim = _oturumlar
            .FirstOrDefault(o => o.Id == oturumId)?.Participants
            .FirstOrDefault(k => k.UserId == kullaniciId);

        if (katilim is null) return Task.FromResult(false);

        katilim.LeftUtc = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    public Task<bool> KodKullanimdaAsync(string katilimKodu)
        => Task.FromResult(_oturumlar.Any(o =>
            o.JoinCode == katilimKodu && AcikDurumlar.Contains(o.Status)));
}
