using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.API.Authorization;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;

namespace StajProject.API.Controllers;

/// <summary>
/// Yönetim paneli — Coğrafi Yetki Tanımlama (Ödev 7 / Madde 2).
///
/// Kullanıcıya veya role, haritadan çizilen bir poligon alan bağlanır.
/// O kullanıcı bundan sonra yalnızca bu alanın içine çizim yapabilir;
/// kontrol GeoPermissionService.DogrulaAsync içinde, çizim uçlarında uygulanır.
/// </summary>
[ApiController]
[Route("api/admin/geo-permissions")]
[Authorize]
[YetkiGerekli(Yetkiler.CografiYetkiTanimlama)]
public class AdminGeoPermissionsController : YonetimControllerBase
{
    private readonly IGeoPermissionService _service;

    public AdminGeoPermissionsController(
        IGeoPermissionService service,
        ILogger<AdminGeoPermissionsController> logger)
        : base(logger)
    {
        _service = service;
    }

    /// <summary>
    /// Tanımlı alanlar. <paramref name="userId"/> veya <paramref name="roleId"/>
    /// verilirse yalnızca o sahibin alanları döner.
    /// </summary>
    [HttpGet]
    public Task<ActionResult<List<GeoPermissionDto>>> GetAll(
        [FromQuery] int? userId, [FromQuery] int? roleId)
        => Calistir<List<GeoPermissionDto>>(async () =>
        {
            if (userId is not null) return Ok(await _service.GetForUserAsync(userId.Value));
            if (roleId is not null) return Ok(await _service.GetForRoleAsync(roleId.Value));
            return Ok(await _service.GetAllAsync());
        });

    /// <summary>
    /// Yeni alan tanımlar. Gövdede UserId veya RoleId'den YALNIZCA biri dolu olmalı;
    /// Wkt haritada çizilen poligondur (EPSG:4326).
    /// </summary>
    [HttpPost]
    public Task<ActionResult<GeoPermissionDto>> Create([FromBody] GeoPermissionCreateDto dto)
        => Calistir<GeoPermissionDto>(async () => Ok(await _service.CreateAsync(dto)));

    /// <summary>Alan tanımını kaldırır (soft delete — kimin ne zaman tanımladığı izi kalır).</summary>
    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id)
        => Calistir(async () =>
        {
            var silindi = await _service.DeleteAsync(id);
            return silindi ? NoContent() : BulunamadiSonuc(id);
        });
}
