using NetTopologySuite.Geometries;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

public class AnalysisService : IAnalysisService
{
    private readonly IAnalysisRepository _repository;

    public AnalysisService(IAnalysisRepository repository)
    {
        _repository = repository;
    }

    public async Task<AnalysisResultDto> KesisimAnaliziAsync(AnalysisRequestDto request)
    {
        // WKT → Polygon + tip doğrulaması + SRID 4326.
        // Hatalıysa WktFormatException fırlar, middleware onu 400'e çevirir.
        var alan = WktConverter.Read<Polygon>(request.Wkt);

        // Üç sorgu paralel değil sıralı: hepsi AYNI DbContext'i kullanıyor ve
        // DbContext thread-safe değildir. Paralel çalıştırmak
        // "A second operation was started on this context" hatası verir.
        var noktalar = await _repository.KesisenNoktalarAsync(alan);
        var cizgiler = await _repository.KesisenCizgilerAsync(alan);
        var poligonlar = await _repository.KesisenPoligonlarAsync(alan, request.HaricTutulanPolygonId);

        var sonuc = new AnalysisResultDto
        {
            PointCount = noktalar.Count,
            LineCount = cizgiler.Count,
            PolygonCount = poligonlar.Count,
        };

        sonuc.Items.AddRange(noktalar.Select(MapToItem));
        sonuc.Items.AddRange(cizgiler.Select(MapToItem));
        sonuc.Items.AddRange(poligonlar.Select(MapToItem));

        return sonuc;
    }

    private static AnalysisItemDto MapToItem(GeometryEntityBase entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        GeometryType = entity.Geometry.GeometryType,
        Wkt = WktConverter.Write(entity.Geometry),
        Color = entity.Color,
    };
}
