using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Bellekte çalışan sahte kategori deposu (Ödev 12).
///
/// Gerçek repository'deki global sorgu filtresinin karşılığı burada elle
/// yazılıyor: silinmiş kategoriler hiçbir okuma metodundan dönmez. Bunu
/// yapmasaydık servis testleri, üretimde görünmeyen satırları görür ve
/// yanlış bir güven verirdi.
/// </summary>
public class FakePoiCategoryRepository : IPoiCategoryRepository
{
    private readonly SahteVeritabani _db;

    public FakePoiCategoryRepository() : this(new SahteVeritabani()) { }

    public FakePoiCategoryRepository(SahteVeritabani db)
    {
        _db = db;
    }

    private IEnumerable<PoiCategory> Yasayanlar => _db.PoiKategorileri.Where(k => !k.IsDeleted);

    public Task<List<PoiCategory>> GetAllAsync()
        => Task.FromResult(Yasayanlar.OrderBy(k => k.Ad).ToList());

    public Task<PoiCategory?> GetByIdAsync(int id)
        => Task.FromResult(Yasayanlar.FirstOrDefault(k => k.Id == id));

    public Task<PoiCategory?> GetByNameAsync(string ad, int? parentId)
        => Task.FromResult(Yasayanlar.FirstOrDefault(k => k.Ad == ad && k.ParentId == parentId));

    public Task<PoiCategory> AddAsync(PoiCategory kategori)
    {
        kategori.Id = _db.SonrakiPoiKategoriId();
        _db.PoiKategorileri.Add(kategori);
        return Task.FromResult(kategori);
    }

    public Task<PoiCategory?> UpdateAsync(PoiCategory kategori)
    {
        var mevcut = Yasayanlar.FirstOrDefault(k => k.Id == kategori.Id);
        if (mevcut is null)
        {
            return Task.FromResult<PoiCategory?>(null);
        }

        mevcut.Ad = kategori.Ad;
        mevcut.Aciklama = kategori.Aciklama;
        mevcut.Ikon = kategori.Ikon;
        mevcut.ParentId = kategori.ParentId;
        mevcut.IsActive = kategori.IsActive;
        mevcut.ModifiedDate = DateTime.UtcNow;

        return Task.FromResult<PoiCategory?>(mevcut);
    }

    public Task<bool> SoftDeleteAsync(int id)
    {
        var kategori = Yasayanlar.FirstOrDefault(k => k.Id == id);
        if (kategori is null)
        {
            return Task.FromResult(false);
        }

        kategori.IsDeleted = true;
        kategori.IsActive = false;
        kategori.ModifiedDate = DateTime.UtcNow;
        return Task.FromResult(true);
    }
}
