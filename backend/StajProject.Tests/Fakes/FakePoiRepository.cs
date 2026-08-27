using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Bellekte çalışan sahte POI deposu (Ödev 12).
///
/// Gerçek repository'nin sorgu filtresi İKİ koşul taşıyor: POI silinmemiş
/// OLACAK ve bağlı kategorisi de silinmemiş olacak. İkincisini burada da
/// uyguluyoruz — yoksa "kategori silinince POI'ye ne oluyor?" sorusunu
/// sınayan bir test, gerçekte olmayan bir davranışı doğrulardı.
/// </summary>
public class FakePoiRepository : IPoiRepository
{
    private readonly SahteVeritabani _db;

    public FakePoiRepository() : this(new SahteVeritabani()) { }

    public FakePoiRepository(SahteVeritabani db)
    {
        _db = db;
    }

    private bool KategoriYasiyorMu(Poi poi)
        => _db.PoiKategorileri.FirstOrDefault(k => k.Id == poi.KategoriId) is not { IsDeleted: true };

    private IEnumerable<Poi> Yasayanlar
        => _db.Poiler.Where(p => !p.IsDeleted && KategoriYasiyorMu(p));

    /// <summary>Gerçek repository Include(p => p.User) yapıyor; karşılığı bu.</summary>
    private Poi KullaniciyiBagla(Poi poi)
    {
        poi.User = _db.Kullanicilar.FirstOrDefault(k => k.Id == poi.UserId && !k.IsDeleted);
        return poi;
    }

    public Task<List<Poi>> GetAllAsync(int? userId = null)
    {
        var sorgu = Yasayanlar;
        if (userId is not null)
        {
            sorgu = sorgu.Where(p => p.UserId == userId);
        }

        return Task.FromResult(sorgu
            .OrderByDescending(p => p.CreatedDate)
            .Select(KullaniciyiBagla)
            .ToList());
    }

    public Task<Poi?> GetByIdAsync(int id)
    {
        var poi = Yasayanlar.FirstOrDefault(p => p.Id == id);
        return Task.FromResult(poi is null ? null : KullaniciyiBagla(poi));
    }

    /// <summary>
    /// Gerçek depoda bu bir SQL ILIKE (ya da GeoServer'da CQL ILIKE); burada
    /// karşılığı büyük/küçük harf gözetmeyen Contains.
    ///
    /// Sıralama gerçeklerle AYNI kuralı izliyor (önce kısa ad): testin
    /// doğruladığı sıra, üretimde göreceğimiz sıra olsun.
    /// </summary>
    public Task<List<Poi>> AraAsync(string sorgu, int enFazla)
        => Task.FromResult(Yasayanlar
            .Where(p => p.Isim.Contains(sorgu, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Isim.Length)
            .ThenBy(p => p.Isim, StringComparer.CurrentCulture)
            .Take(enFazla)
            .Select(KullaniciyiBagla)
            .ToList());

    /// <summary>
    /// Gerçek depoda bu bir PostGIS <c>ST_Intersects</c>; burada karşılığı
    /// NetTopologySuite'in aynı adlı metodu — testte de üretimde de AYNI
    /// kütüphane, aynı kural. Aktiflik süzgeci de birebir kopyalanıyor
    /// (pasif POI analize girmiyor), yoksa test gerçekte olmayan bir
    /// davranışı doğrulardı.
    /// </summary>
    public Task<List<Poi>> AlandakileriGetirAsync(NetTopologySuite.Geometries.Geometry alan)
        => Task.FromResult(Yasayanlar
            .Where(p => p.IsActive && p.Geom is not null && alan.Intersects(p.Geom))
            .ToList());

    public Task<Poi> AddAsync(Poi poi)
    {
        poi.Id = _db.SonrakiPoiId();
        _db.Poiler.Add(poi);
        return Task.FromResult(poi);
    }

    public Task TopluEkleAsync(IEnumerable<Poi> poiler)
    {
        foreach (var poi in poiler)
        {
            poi.Id = _db.SonrakiPoiId();
            _db.Poiler.Add(poi);
        }

        return Task.CompletedTask;
    }

    public Task<Poi?> UpdateAsync(Poi poi)
    {
        var mevcut = Yasayanlar.FirstOrDefault(p => p.Id == poi.Id);
        if (mevcut is null)
        {
            return Task.FromResult<Poi?>(null);
        }

        mevcut.Isim = poi.Isim;
        mevcut.KategoriId = poi.KategoriId;
        mevcut.MesaiSaatleri = poi.MesaiSaatleri;
        mevcut.MesaiPlani = poi.MesaiPlani;
        mevcut.Geom = poi.Geom;
        mevcut.ModifiedDate = DateTime.UtcNow;

        return Task.FromResult<Poi?>(KullaniciyiBagla(mevcut));
    }

    public Task<bool> SoftDeleteAsync(int id)
    {
        var poi = Yasayanlar.FirstOrDefault(p => p.Id == id);
        if (poi is null)
        {
            return Task.FromResult(false);
        }

        poi.IsDeleted = true;
        poi.IsActive = false;
        poi.ModifiedDate = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    public Task<bool> RestoreAsync(int id)
    {
        // Gerçek repository burada IgnoreQueryFilters kullanıyor: silinmiş
        // kayda erişebilmek için filtreyi bilinçli olarak aşıyor.
        var poi = _db.Poiler.FirstOrDefault(p => p.Id == id);
        if (poi is null || !poi.IsDeleted)
        {
            return Task.FromResult(false);
        }

        poi.IsDeleted = false;
        poi.IsActive = true;
        poi.ModifiedDate = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    public Task<bool> SetActiveAsync(int id, bool isActive)
    {
        var poi = Yasayanlar.FirstOrDefault(p => p.Id == id);
        if (poi is null)
        {
            return Task.FromResult(false);
        }

        poi.IsActive = isActive;
        poi.ModifiedDate = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    public Task<Dictionary<int, int>> GetCountsByCategoryAsync()
        => Task.FromResult(Yasayanlar
            .GroupBy(p => p.KategoriId)
            .ToDictionary(g => g.Key, g => g.Count()));
}
