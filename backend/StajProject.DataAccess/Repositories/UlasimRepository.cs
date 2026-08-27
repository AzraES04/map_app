using Microsoft.EntityFrameworkCore;
using StajProject.DataAccess.Context;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary><see cref="IUlasimRepository"/>'nin EF Core gerçeklemesi (Ödev 16).</summary>
public class UlasimRepository : IUlasimRepository
{
    private readonly AppDbContext _context;

    public UlasimRepository(AppDbContext context)
    {
        _context = context;
    }

    // ======================================================================
    //  Güzergah
    // ======================================================================

    /// <summary>
    /// Güzergahı DURAKLARIYLA okuyan temel sorgu.
    ///
    /// <c>Include</c> + <c>ThenInclude</c> zinciri burada gerekli, POI'deki
    /// gibi atlanamıyor: güzergahın anlamı duraklarının sırasıyla ortaya
    /// çıkıyor ve arayüz ikisini birlikte istiyor. Ayrı sorgu atsaydık N+1
    /// olurdu (her güzergah için bir durak sorgusu).
    ///
    /// EF Core 5'ten beri <c>Include</c> içinde sıralama yazılabiliyor;
    /// böylece "duraklar sıralı gelsin" kuralı veri katmanında bir kez
    /// tanımlanıyor, her çağıranın ayrıca sıralaması gerekmiyor.
    /// </summary>
    private IQueryable<Guzergah> GuzergahSorgusu()
        => _context.Guzergahlar
            .AsNoTracking()
            .Include(g => g.User)
            .Include(g => g.Duraklar.OrderBy(d => d.Sira))
                .ThenInclude(d => d.User);

    public Task<List<Guzergah>> GuzergahlariGetirAsync()
        => GuzergahSorgusu().OrderBy(g => g.Ad).ToListAsync();

    public Task<Guzergah?> GuzergahGetirAsync(int id)
        => GuzergahSorgusu().FirstOrDefaultAsync(g => g.Id == id);

    public async Task<Guzergah> GuzergahEkleAsync(Guzergah guzergah)
    {
        _context.Guzergahlar.Add(guzergah);
        await _context.SaveChangesAsync();
        return guzergah;
    }

    public async Task<Guzergah?> GuzergahGuncelleAsync(Guzergah guzergah)
    {
        // AsNoTracking YOK: değiştirip kaydedeceğiz.
        var mevcut = await _context.Guzergahlar.FirstOrDefaultAsync(g => g.Id == guzergah.Id);
        if (mevcut is null)
        {
            return null;
        }

        mevcut.Ad = guzergah.Ad;
        mevcut.Renk = guzergah.Renk;
        mevcut.Aciklama = guzergah.Aciklama;
        mevcut.IsActive = guzergah.IsActive;

        // ModifiedDate elle yazılmıyor — AppDbContext.ApplyAuditRules() basıyor.
        await _context.SaveChangesAsync();

        return await GuzergahGetirAsync(mevcut.Id);
    }

    public async Task<bool> GuzergahSilAsync(int id)
    {
        var guzergah = await _context.Guzergahlar.FirstOrDefaultAsync(g => g.Id == id);
        if (guzergah is null)
        {
            return false;
        }

        guzergah.IsDeleted = true;
        guzergah.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    // ======================================================================
    //  Durak
    // ======================================================================

    private IQueryable<Durak> DurakSorgusu()
        => _context.Duraklar
            .AsNoTracking()
            .Include(d => d.Guzergah)
            .Include(d => d.User);

    public Task<List<Durak>> DuraklariGetirAsync()
        => DurakSorgusu()
            .OrderBy(d => d.GuzergahId)
            .ThenBy(d => d.Sira)
            .ToListAsync();

    public Task<Durak?> DurakGetirAsync(int id)
        => DurakSorgusu().FirstOrDefaultAsync(d => d.Id == id);

    public async Task<Durak> DurakEkleAsync(Durak durak)
    {
        _context.Duraklar.Add(durak);
        await _context.SaveChangesAsync();
        return durak;
    }

    public async Task<Durak?> DurakGuncelleAsync(Durak durak)
    {
        var mevcut = await _context.Duraklar.FirstOrDefaultAsync(d => d.Id == durak.Id);
        if (mevcut is null)
        {
            return null;
        }

        mevcut.Ad = durak.Ad;
        mevcut.GuzergahId = durak.GuzergahId;
        mevcut.Sira = durak.Sira;
        mevcut.Aciklama = durak.Aciklama;
        mevcut.Geom = durak.Geom;

        await _context.SaveChangesAsync();
        return await DurakGetirAsync(mevcut.Id);
    }

    public async Task<bool> DurakSilAsync(int id)
    {
        var durak = await _context.Duraklar.FirstOrDefaultAsync(d => d.Id == id);
        if (durak is null)
        {
            return false;
        }

        durak.IsDeleted = true;
        durak.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Sürükle-bırak sıralamasını tek SaveChanges ile yazar.
    ///
    /// TEK İŞLEM OLMASI ŞART: bir durağı yukarı taşımak aradaki bütün
    /// durakların sırasını kaydırıyor. Her satır ayrı kaydedilseydi araya
    /// düşen bir hata listeyi yarı taşınmış hâlde bırakırdı — ekranda
    /// "iki durak da 3. sırada" gibi bir sonuç.
    ///
    /// Gelen listede olmayan duraklara DOKUNULMUYOR: çağıran taraf listeyi
    /// eksik gönderirse (yarış durumu — başkası aynı anda durak eklediyse)
    /// o durak eski sırasında kalıyor, silinmiyor.
    /// </summary>
    public async Task<int> SiralariYazAsync(int guzergahId, IReadOnlyList<int> siraliIdler)
    {
        var duraklar = await _context.Duraklar
            .Where(d => d.GuzergahId == guzergahId)
            .ToListAsync();

        var yazilan = 0;

        for (var i = 0; i < siraliIdler.Count; i++)
        {
            var durak = duraklar.FirstOrDefault(d => d.Id == siraliIdler[i]);
            if (durak is null) continue;

            var yeniSira = i + 1;
            if (durak.Sira == yeniSira) { yazilan++; continue; }

            durak.Sira = yeniSira;
            yazilan++;
        }

        await _context.SaveChangesAsync();
        return yazilan;
    }

    /// <summary>
    /// Güzergahtaki en büyük sıra; hiç durak yoksa 0.
    ///
    /// <c>DefaultIfEmpty</c> olmadan boş bir kümede <c>Max</c> çağırmak
    /// çalışma zamanı hatası verirdi (SQL tarafında NULL → int'e
    /// dönüştürülemez).
    /// </summary>
    public async Task<int> SonSiraAsync(int guzergahId)
        => await _context.Duraklar
            .Where(d => d.GuzergahId == guzergahId)
            .Select(d => (int?)d.Sira)
            .MaxAsync() ?? 0;
}
