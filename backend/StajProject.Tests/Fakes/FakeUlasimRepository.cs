using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Bellekte çalışan sahte ulaşım deposu (Ödev 16).
///
/// Gerçek repository'nin sorgu filtresi İKİ koşul taşıyor: durak silinmemiş
/// OLACAK ve bağlı güzergahı da silinmemiş olacak. İkincisini burada da
/// uyguluyoruz — yoksa "güzergah silinince duraklara ne oluyor?" sorusunu
/// sınayan bir test, gerçekte olmayan bir davranışı doğrulardı.
/// (FakePoiRepository'deki kategori filtresiyle aynı gerekçe.)
/// </summary>
public class FakeUlasimRepository : IUlasimRepository
{
    private readonly SahteVeritabani _db;

    public FakeUlasimRepository() : this(new SahteVeritabani()) { }

    public FakeUlasimRepository(SahteVeritabani db)
    {
        _db = db;
    }

    private IEnumerable<Guzergah> YasayanGuzergahlar
        => _db.Guzergahlar.Where(g => !g.IsDeleted);

    private bool GuzergahYasiyorMu(Durak durak)
        => _db.Guzergahlar.FirstOrDefault(g => g.Id == durak.GuzergahId) is not { IsDeleted: true };

    private IEnumerable<Durak> YasayanDuraklar
        => _db.Duraklar.Where(d => !d.IsDeleted && GuzergahYasiyorMu(d));

    /// <summary>
    /// Gerçek repository Include ile kullanıcı ve durak navigasyonlarını
    /// dolduruyor; karşılığı bu. Duraklar SIRALI bağlanıyor — sıralamanın
    /// veri katmanında yapıldığı kuralı testte de geçerli olsun.
    /// </summary>
    private Guzergah Bagla(Guzergah guzergah)
    {
        guzergah.User = _db.Kullanicilar.FirstOrDefault(k => k.Id == guzergah.UserId && !k.IsDeleted);
        guzergah.Duraklar = YasayanDuraklar
            .Where(d => d.GuzergahId == guzergah.Id)
            .OrderBy(d => d.Sira)
            .Select(BaglaDurak)
            .ToList();

        return guzergah;
    }

    private Durak BaglaDurak(Durak durak)
    {
        durak.User = _db.Kullanicilar.FirstOrDefault(k => k.Id == durak.UserId && !k.IsDeleted);
        durak.Guzergah = _db.Guzergahlar.FirstOrDefault(g => g.Id == durak.GuzergahId);
        return durak;
    }

    // ---------- Güzergah ----------

    public Task<List<Guzergah>> GuzergahlariGetirAsync()
        => Task.FromResult(YasayanGuzergahlar
            .OrderBy(g => g.Ad, StringComparer.CurrentCulture)
            .Select(Bagla)
            .ToList());

    public Task<Guzergah?> GuzergahGetirAsync(int id)
    {
        var guzergah = YasayanGuzergahlar.FirstOrDefault(g => g.Id == id);
        return Task.FromResult(guzergah is null ? null : Bagla(guzergah));
    }

    public Task<Guzergah> GuzergahEkleAsync(Guzergah guzergah)
    {
        guzergah.Id = _db.SonrakiGuzergahId();
        _db.Guzergahlar.Add(guzergah);
        return Task.FromResult(guzergah);
    }

    public Task<Guzergah?> GuzergahGuncelleAsync(Guzergah guzergah)
    {
        var mevcut = YasayanGuzergahlar.FirstOrDefault(g => g.Id == guzergah.Id);
        if (mevcut is null) return Task.FromResult<Guzergah?>(null);

        mevcut.Ad = guzergah.Ad;
        mevcut.Renk = guzergah.Renk;
        mevcut.Aciklama = guzergah.Aciklama;
        mevcut.IsActive = guzergah.IsActive;
        mevcut.ModifiedDate = DateTime.UtcNow;

        return Task.FromResult<Guzergah?>(Bagla(mevcut));
    }

    public Task<bool> GuzergahSilAsync(int id)
    {
        var guzergah = YasayanGuzergahlar.FirstOrDefault(g => g.Id == id);
        if (guzergah is null) return Task.FromResult(false);

        guzergah.IsDeleted = true;
        guzergah.IsActive = false;
        guzergah.ModifiedDate = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    // ---------- Durak ----------

    public Task<List<Durak>> DuraklariGetirAsync()
        => Task.FromResult(YasayanDuraklar
            .OrderBy(d => d.GuzergahId)
            .ThenBy(d => d.Sira)
            .Select(BaglaDurak)
            .ToList());

    public Task<Durak?> DurakGetirAsync(int id)
    {
        var durak = YasayanDuraklar.FirstOrDefault(d => d.Id == id);
        return Task.FromResult(durak is null ? null : BaglaDurak(durak));
    }

    public Task<Durak> DurakEkleAsync(Durak durak)
    {
        durak.Id = _db.SonrakiDurakId();
        _db.Duraklar.Add(durak);
        return Task.FromResult(durak);
    }

    public Task<Durak?> DurakGuncelleAsync(Durak durak)
    {
        var mevcut = YasayanDuraklar.FirstOrDefault(d => d.Id == durak.Id);
        if (mevcut is null) return Task.FromResult<Durak?>(null);

        mevcut.Ad = durak.Ad;
        mevcut.GuzergahId = durak.GuzergahId;
        mevcut.Sira = durak.Sira;
        mevcut.Aciklama = durak.Aciklama;
        mevcut.Geom = durak.Geom;
        mevcut.ModifiedDate = DateTime.UtcNow;

        return Task.FromResult<Durak?>(BaglaDurak(mevcut));
    }

    public Task<bool> DurakSilAsync(int id)
    {
        var durak = YasayanDuraklar.FirstOrDefault(d => d.Id == id);
        if (durak is null) return Task.FromResult(false);

        durak.IsDeleted = true;
        durak.IsActive = false;
        durak.ModifiedDate = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    public Task<int> SiralariYazAsync(int guzergahId, IReadOnlyList<int> siraliIdler)
    {
        var duraklar = _db.Duraklar.Where(d => d.GuzergahId == guzergahId).ToList();
        var yazilan = 0;

        for (var i = 0; i < siraliIdler.Count; i++)
        {
            var durak = duraklar.FirstOrDefault(d => d.Id == siraliIdler[i]);
            if (durak is null) continue;

            durak.Sira = i + 1;
            yazilan++;
        }

        return Task.FromResult(yazilan);
    }

    public Task<int> SonSiraAsync(int guzergahId)
    {
        var siralar = _db.Duraklar
            .Where(d => d.GuzergahId == guzergahId && !d.IsDeleted)
            .Select(d => d.Sira)
            .ToList();

        return Task.FromResult(siralar.Count == 0 ? 0 : siralar.Max());
    }
}
