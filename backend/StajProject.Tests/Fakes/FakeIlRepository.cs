using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using StajProject.Business.Geo;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Bellekte çalışan sahte il repository'si (Ödev 10).
///
/// Varsayılan olarak BOŞ gelir; il/bölge seçimi test edilecekse
/// <see cref="Ekle"/> ile birkaç basit kare il eklenir. Gerçek 81 il
/// verisini teste taşımanın anlamı yok — sınanan şey coğrafyanın kendisi
/// değil, "seçilen illerin birleşimi alınıyor mu" kuralı.
/// </summary>
public class FakeIlRepository : IIlRepository
{
    private readonly List<Il> _iller = new();

    public Task<int> SayAsync() => Task.FromResult(_iller.Count);

    public Task EkleAsync(IEnumerable<Il> iller)
    {
        _iller.AddRange(iller);
        return Task.CompletedTask;
    }

    public Task<List<Il>> OzetGetirAsync()
        => Task.FromResult(_iller
            .OrderBy(i => i.Id)
            .Select(i => new Il { Id = i.Id, Ad = i.Ad, Bolge = i.Bolge })
            .ToList());

    public Task<List<Il>> SinirlariGetirAsync()
        => Task.FromResult(_iller.OrderBy(i => i.Id).ToList());

    public Task<Geometry?> IllerinBirlesimiAsync(IReadOnlyCollection<int> plakalar)
        => Task.FromResult(Birlestir(_iller.Where(i => plakalar.Contains(i.Id))));

    public Task<Geometry?> BolgeGeometrisiAsync(string bolge)
        => Task.FromResult(Birlestir(_iller.Where(i => i.Bolge == bolge)));

    /// <summary>Testin kolay okunması için: kare bir il ekler.</summary>
    public FakeIlRepository Ekle(int plaka, string ad, string wkt)
    {
        _iller.Add(new Il
        {
            Id = plaka,
            Ad = ad,
            Bolge = Bolgeler.BolgeBul(plaka),
            Geom = WktConverter.Read<Polygon>(wkt),
        });
        return this;
    }

    private static Geometry? Birlestir(IEnumerable<Il> iller)
    {
        var geometriler = iller.Select(i => i.Geom).ToList();
        if (geometriler.Count == 0) return null;

        var birlesim = UnaryUnionOp.Union(geometriler);
        birlesim.SRID = WktConverter.Srid;
        return birlesim;
    }
}
