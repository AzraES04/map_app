using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using StajProject.DataAccess.Context;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

public class AnalysisRepository : IAnalysisRepository
{
    private readonly AppDbContext _context;

    public AnalysisRepository(AppDbContext context)
    {
        _context = context;
    }

    // Not: alan.Intersects(e.Geom) ifadesi SQL'de ST_Intersects(@alan, geom) olur.
    // ST_Intersects "en ufak temas bile sayılır" anlamına gelir — ödevin
    // "tamamen kapsanması gerekmez" şartının tam karşılığı.
    // (Tamamen içinde kalanları isteseydik ST_Contains / ST_Within kullanırdık.)
    //
    // Global query filter sayesinde silinmiş kayıtlar sorguya hiç girmez.

    public Task<List<PointEntity>> KesisenNoktalarAsync(Polygon alan)
        => _context.Points
            .AsNoTracking()
            .Where(e => alan.Intersects(e.Geom))
            .OrderBy(e => e.Id)
            .ToListAsync();

    public Task<List<LineEntity>> KesisenCizgilerAsync(Polygon alan)
        => _context.Lines
            .AsNoTracking()
            .Where(e => alan.Intersects(e.Geom))
            .OrderBy(e => e.Id)
            .ToListAsync();

    public Task<List<PolygonEntity>> KesisenPoligonlarAsync(Polygon alan, int? haricTutulanId = null)
        => _context.Polygons
            .AsNoTracking()
            // Kaydedilmiş bir poligonun analizinde kendisini saymamak için:
            .Where(e => haricTutulanId == null || e.Id != haricTutulanId)
            .Where(e => alan.Intersects(e.Geom))
            .OrderBy(e => e.Id)
            .ToListAsync();
}
