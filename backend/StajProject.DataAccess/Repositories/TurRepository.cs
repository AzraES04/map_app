using Microsoft.EntityFrameworkCore;
using StajProject.DataAccess.Context;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary><see cref="ITurRepository"/>'nin EF Core gerçeklemesi.</summary>
public class TurRepository : ITurRepository
{
    /// <summary>Oturumun "hâlâ sürüyor" sayıldığı durumlar.</summary>
    private static readonly TourSessionStatus[] AcikDurumlar =
    {
        TourSessionStatus.Planned,
        TourSessionStatus.Live,
        TourSessionStatus.Paused,
    };

    private readonly AppDbContext _context;

    public TurRepository(AppDbContext context)
    {
        _context = context;
    }

    // ======================================================================
    //  Tur şablonu
    // ======================================================================

    /// <summary>
    /// Turu DURAKLARIYLA okuyan temel sorgu.
    ///
    /// Sıralama <c>Include</c> içinde: "duraklar sıralı gelsin" kuralı veri
    /// katmanında bir kez tanımlanıyor, her çağıranın ayrıca sıralamasına
    /// gerek kalmıyor (UlasimRepository'deki desenin aynısı).
    /// </summary>
    private IQueryable<Tour> TurSorgusu()
        => _context.Tours
            .AsNoTracking()
            .Include(t => t.GuideUser)
            .Include(t => t.Waypoints.OrderBy(w => w.Order));

    public Task<List<Tour>> TurlariGetirAsync(int? kullaniciId = null)
    {
        var sorgu = TurSorgusu();

        if (kullaniciId is not null)
        {
            sorgu = sorgu.Where(t => t.GuideUserId == kullaniciId);
        }

        return sorgu.OrderByDescending(t => t.CreatedDate).ToListAsync();
    }

    public Task<Tour?> TurGetirAsync(int id)
        => TurSorgusu().FirstOrDefaultAsync(t => t.Id == id);

    public async Task<Tour> TurEkleAsync(Tour tur)
    {
        _context.Tours.Add(tur);
        await _context.SaveChangesAsync();

        // Yeniden okuyoruz: ekleme sırasında navigasyonlar (rehber kaydı)
        // dolu değil, oysa çağıran taraf DTO'ya çevirirken rehberin adını
        // istiyor.
        return await TurGetirAsync(tur.Id) ?? tur;
    }

    public async Task<bool> TurSilAsync(int id)
    {
        var tur = await _context.Tours.FirstOrDefaultAsync(t => t.Id == id);
        if (tur is null) return false;

        tur.IsDeleted = true;
        await _context.SaveChangesAsync();
        return true;
    }

    // ======================================================================
    //  Canlı oturum
    // ======================================================================

    /// <summary>
    /// Oturumu; turu, durakları, rehberi ve katılımcılarıyla okuyan sorgu.
    ///
    /// Dört <c>Include</c> fazla görünüyor ama hepsi TEK bir ekranı besliyor
    /// (ActiveTourView): tur adı, durak listesi, rehber adı ve katılımcı
    /// sayısı aynı anda gösteriliyor. Ayrı sorgulara bölmek N+1 demek olurdu.
    /// </summary>
    private IQueryable<TourSession> OturumSorgusu()
        => _context.TourSessions
            .AsNoTracking()
            .Include(o => o.GuideUser)
            .Include(o => o.Tour!)
                .ThenInclude(t => t.Waypoints.OrderBy(w => w.Order))
            .Include(o => o.Participants)
                .ThenInclude(k => k.User);

    public Task<TourSession?> OturumGetirAsync(int id)
        => OturumSorgusu().FirstOrDefaultAsync(o => o.Id == id);

    public Task<TourSession?> OturumKodlaGetirAsync(string katilimKodu)
        => OturumSorgusu().FirstOrDefaultAsync(o =>
            o.JoinCode == katilimKodu && AcikDurumlar.Contains(o.Status));

    public Task<TourSession?> AcikOturumAsync(int turId)
        => OturumSorgusu().FirstOrDefaultAsync(o =>
            o.TourId == turId && AcikDurumlar.Contains(o.Status));

    public Task<List<TourSession>> KullanicininOturumlariAsync(int kullaniciId)
        => OturumSorgusu()
            .Where(o => AcikDurumlar.Contains(o.Status)
                && o.Participants.Any(k => k.UserId == kullaniciId && k.LeftUtc == null))
            .OrderByDescending(o => o.CreatedDate)
            .ToListAsync();

    public async Task<TourSession> OturumEkleAsync(TourSession oturum)
    {
        _context.TourSessions.Add(oturum);
        await _context.SaveChangesAsync();

        return await OturumGetirAsync(oturum.Id) ?? oturum;
    }

    public async Task<TourSession?> OturumGuncelleAsync(TourSession oturum)
    {
        var mevcut = await _context.TourSessions.FirstOrDefaultAsync(o => o.Id == oturum.Id);
        if (mevcut is null) return null;

        mevcut.Status = oturum.Status;
        mevcut.CurrentWaypointId = oturum.CurrentWaypointId;
        mevcut.CurrentWaypointArrivedUtc = oturum.CurrentWaypointArrivedUtc;
        mevcut.LastPosition = oturum.LastPosition;
        mevcut.LastPositionUtc = oturum.LastPositionUtc;
        mevcut.ProgressPercent = oturum.ProgressPercent;
        mevcut.StartedUtc = oturum.StartedUtc;
        mevcut.EndedUtc = oturum.EndedUtc;

        await _context.SaveChangesAsync();

        return await OturumGetirAsync(mevcut.Id);
    }

    public async Task<TourSessionParticipant> KatilimYazAsync(TourSessionParticipant katilim)
    {
        var mevcut = await _context.TourSessionParticipants
            .FirstOrDefaultAsync(k =>
                k.TourSessionId == katilim.TourSessionId && k.UserId == katilim.UserId);

        if (mevcut is null)
        {
            _context.TourSessionParticipants.Add(katilim);
        }
        else
        {
            // Geri dönen kişi AYNI satıra dönüyor: yeni satır bileşik anahtarı
            // ihlal ederdi ve yoklama listesi aynı kişiyi iki kez gösterirdi.
            mevcut.LeftUtc = null;
            mevcut.Role = katilim.Role;
            katilim = mevcut;
        }

        await _context.SaveChangesAsync();
        return katilim;
    }

    public async Task<bool> AyrilAsync(int oturumId, int kullaniciId)
    {
        var katilim = await _context.TourSessionParticipants
            .FirstOrDefaultAsync(k => k.TourSessionId == oturumId && k.UserId == kullaniciId);

        if (katilim is null) return false;

        // Satır SİLİNMİYOR: "kimler katıldı" bilgisi tur bittikten sonra da
        // anlamlı (yoklama).
        katilim.LeftUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public Task<bool> KodKullanimdaAsync(string katilimKodu)
        => _context.TourSessions
            .AsNoTracking()
            .AnyAsync(o => o.JoinCode == katilimKodu && AcikDurumlar.Contains(o.Status));
}
