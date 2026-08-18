using Microsoft.AspNetCore.Mvc;
using StajProject.Business.Auth;
using StajProject.Business.Services;
using StajProject.Entities;

namespace StajProject.API.Controllers;

// Üç controller da gövdesini GeometryControllerBase'den alıyor.
// Buradaki tek iş: URL'i belirlemek, bağımlılıkları DI'dan istemek ve
// EKLEME için hangi yetkinin arandığını söylemek (Ödev 6).
// ILogger<T> tip parametresi sayesinde log kayıtlarında hangi controller
// olduğu görünüyor.

/// <summary>POINT çizimleri → tbl_point</summary>
[ApiController]
[Route("api/points")]
public class PointsController : GeometryControllerBase<PointEntity>
{
    public PointsController(IGeometryService<PointEntity> service, ILogger<PointsController> logger)
        : base(service, logger) { }

    /// <summary>Bu uca kayıt eklemek "Point Ekleme" yetkisi ister.</summary>
    public override string EklemeYetkisi => Yetkiler.NoktaEkleme;
}

/// <summary>LINESTRING çizimleri → tbl_line</summary>
[ApiController]
[Route("api/lines")]
public class LinesController : GeometryControllerBase<LineEntity>
{
    public LinesController(IGeometryService<LineEntity> service, ILogger<LinesController> logger)
        : base(service, logger) { }

    /// <summary>Bu uca kayıt eklemek "Line Ekleme" yetkisi ister.</summary>
    public override string EklemeYetkisi => Yetkiler.CizgiEkleme;
}

/// <summary>POLYGON çizimleri → tbl_polygon</summary>
[ApiController]
[Route("api/polygons")]
public class PolygonsController : GeometryControllerBase<PolygonEntity>
{
    public PolygonsController(IGeometryService<PolygonEntity> service, ILogger<PolygonsController> logger)
        : base(service, logger) { }

    /// <summary>Bu uca kayıt eklemek "Polygon Ekleme" yetkisi ister.</summary>
    public override string EklemeYetkisi => Yetkiler.PoligonEkleme;
}
