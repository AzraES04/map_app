using Microsoft.AspNetCore.Mvc;
using StajProject.Business.Services;
using StajProject.Entities;

namespace StajProject.API.Controllers;

// Üç controller da gövdesini GeometryControllerBase'den alıyor.
// Buradaki tek iş: URL'i belirlemek ve doğru tipteki servisi DI'dan istemek.

/// <summary>POINT çizimleri → tbl_point</summary>
[ApiController]
[Route("api/points")]
public class PointsController : GeometryControllerBase<PointEntity>
{
    public PointsController(IGeometryService<PointEntity> service) : base(service) { }
}

/// <summary>LINESTRING çizimleri → tbl_line</summary>
[ApiController]
[Route("api/lines")]
public class LinesController : GeometryControllerBase<LineEntity>
{
    public LinesController(IGeometryService<LineEntity> service) : base(service) { }
}

/// <summary>POLYGON çizimleri → tbl_polygon</summary>
[ApiController]
[Route("api/polygons")]
public class PolygonsController : GeometryControllerBase<PolygonEntity>
{
    public PolygonsController(IGeometryService<PolygonEntity> service) : base(service) { }
}
