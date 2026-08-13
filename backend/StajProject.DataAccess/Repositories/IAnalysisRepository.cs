using NetTopologySuite.Geometries;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// Mekânsal analiz sorguları (Ödev 4 / Görev 3).
///
/// Bu sorgular BİLEREK veritabanında çalışıyor. Alternatif, tüm envanteri
/// belleğe çekip C# tarafında kesişim hesaplamaktı; o yol hem tüm tabloyu
/// ağdan geçirir hem de PostGIS'in GIST index'ini kullanamazdı.
/// EF Core, NetTopologySuite'in Intersects() çağrısını ST_Intersects'e çevirir.
/// </summary>
public interface IAnalysisRepository
{
    /// <summary>Verilen alanla kesişen noktalar.</summary>
    Task<List<PointEntity>> KesisenNoktalarAsync(Polygon alan);

    /// <summary>Verilen alanla kesişen çizgiler.</summary>
    Task<List<LineEntity>> KesisenCizgilerAsync(Polygon alan);

    /// <summary>Verilen alanla kesişen poligonlar (istenirse bir kayıt hariç tutulur).</summary>
    Task<List<PolygonEntity>> KesisenPoligonlarAsync(Polygon alan, int? haricTutulanId = null);
}
