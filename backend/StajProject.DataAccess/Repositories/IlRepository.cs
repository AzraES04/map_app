using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using StajProject.DataAccess.Context;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// <see cref="IIlRepository"/>'nin EF Core gerçeklemesi (Ödev 10).
/// </summary>
public class IlRepository : IIlRepository
{
    private readonly AppDbContext _context;

    public IlRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<int> SayAsync() => _context.Iller.CountAsync();

    public async Task EkleAsync(IEnumerable<Il> iller)
    {
        _context.Iller.AddRange(iller);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Geometri okunmadan liste. <c>Select</c> ile projeksiyon yapıyoruz ki
    /// SQL'e <c>geom</c> kolonu hiç girmesin — 81 ilin sınırı ~240 KB, sadece
    /// ad listesi için taşınması gereksiz.
    /// </summary>
    public Task<List<Il>> OzetGetirAsync()
        => _context.Iller
            .AsNoTracking()
            .OrderBy(i => i.Id)
            .Select(i => new Il { Id = i.Id, Ad = i.Ad, Bolge = i.Bolge })
            .ToListAsync();

    public Task<List<Il>> SinirlariGetirAsync()
        => _context.Iller.AsNoTracking().OrderBy(i => i.Id).ToListAsync();

    public async Task<Geometry?> IllerinBirlesimiAsync(IReadOnlyCollection<int> plakalar)
    {
        if (plakalar.Count == 0)
        {
            return null;
        }

        var geometriler = await _context.Iller
            .AsNoTracking()
            .Where(i => plakalar.Contains(i.Id))
            .Select(i => i.Geom)
            .ToListAsync();

        return Birlestir(geometriler);
    }

    public async Task<Geometry?> BolgeGeometrisiAsync(string bolge)
    {
        var geometriler = await _context.Iller
            .AsNoTracking()
            .Where(i => i.Bolge == bolge)
            .Select(i => i.Geom)
            .ToListAsync();

        return Birlestir(geometriler);
    }

    /// <summary>
    /// Geometrileri tek bir alana birleştirir.
    ///
    /// NEDEN VERİTABANINDA DEĞİL DE BELLEKTE?
    /// PostGIS'in <c>ST_Union</c> toplayıcısı da yapardı ama EF Core'da
    /// toplayıcı mekânsal fonksiyon karşılığı yok; ham SQL yazmak gerekirdi.
    /// Burada en fazla 81 geometri birleşiyor ve bu işlem yılda bir kez değil,
    /// yalnızca yönetici bir alan tanımlarken çalışıyor. Ham SQL'in bakım
    /// maliyeti kazancından büyük olurdu.
    ///
    /// <c>UnaryUnionOp</c> tek tek <c>Union</c> zincirlemekten çok daha hızlı:
    /// geometrileri önce gruplayıp ağaç şeklinde birleştiriyor.
    /// </summary>
    private static Geometry? Birlestir(List<Geometry> geometriler)
    {
        if (geometriler.Count == 0)
        {
            return null;
        }

        var birlesim = UnaryUnionOp.Union(geometriler);

        // SRID birleşimde kaybolabiliyor; PostGIS SRID'siz geometriyi
        // geometry(Geometry, 4326) kolonuna yazdırmaz.
        birlesim.SRID = 4326;
        return birlesim;
    }
}
