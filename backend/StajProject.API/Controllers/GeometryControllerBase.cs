using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Services;
using StajProject.Entities;

namespace StajProject.API.Controllers;

/// <summary>
/// Üç geometri controller'ının ortak gövdesi.
///
/// abstract → ASP.NET Core bunu tek başına bir controller olarak keşfetmez;
/// sadece PointsController / LinesController / PolygonsController'a gövde sağlar.
/// [Route] ve [ApiController] öznitelikleri de bilerek burada değil, türeyen sınıflarda.
/// </summary>
[Authorize]  // üç controller da JWT ister; token yoksa/süresi dolduysa 401
public abstract class GeometryControllerBase<TEntity> : ControllerBase
    where TEntity : GeometryEntityBase
{
    private readonly IGeometryService<TEntity> _service;

    protected GeometryControllerBase(IGeometryService<TEntity> service)
    {
        _service = service;
    }

    /// <summary>Silinmemiş tüm kayıtları WKT formatında listeler.</summary>
    [HttpGet]
    public async Task<ActionResult<List<GeometryDto>>> GetAll()
    {
        return Ok(await _service.GetAllAsync());
    }

    /// <summary>Id ile tek kayıt.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<GeometryDto>> GetById(int id)
    {
        var item = await _service.GetByIdAsync(id);
        if (item is null)
        {
            return NotFound(new { message = $"Id={id} olan kayıt bulunamadı." });
        }

        return Ok(item);
    }

    /// <summary>WKT metninden yeni geometri kaydeder.</summary>
    [HttpPost]
    public async Task<ActionResult<GeometryDto>> Create([FromBody] GeometryCreateDto dto)
    {
        try
        {
            var created = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (WktFormatException ex)
        {
            // Hata sunucuda değil, istemcinin gönderdiği veride → 500 değil 400.
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Ad/açıklama (ve istenirse geometri) günceller.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<GeometryDto>> Update(int id, [FromBody] GeometryUpdateDto dto)
    {
        try
        {
            var updated = await _service.UpdateAsync(id, dto);
            if (updated is null)
            {
                return NotFound(new { message = $"Id={id} olan kayıt bulunamadı." });
            }

            return Ok(updated);
        }
        catch (WktFormatException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Silinen kaydı geri getirir. Soft delete kullandığımız için veri hâlâ
    /// tabloda duruyor; bu uç tek bir UPDATE ile silmeyi geri alıyor.
    /// </summary>
    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        var geriAlindi = await _service.RestoreAsync(id);
        if (!geriAlindi)
        {
            return NotFound(new { message = $"Id={id} için geri alınacak silinmiş kayıt bulunamadı." });
        }

        return NoContent();
    }

    /// <summary>
    /// Kaydı askıya alır / yeniden aktif eder (Ödev 3 / Görev 1'deki is_active kolonu).
    /// Silmekten farkı: pasif kayıt listelerde görünmeye devam eder.
    /// </summary>
    [HttpPost("{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, [FromBody] SetActiveDto dto)
    {
        var degisti = await _service.SetActiveAsync(id, dto.IsActive);
        if (!degisti)
        {
            return NotFound(new { message = $"Id={id} olan kayıt bulunamadı." });
        }

        return NoContent();
    }

    /// <summary>Soft delete: kayıt fiziksel olarak silinmez, is_deleted işaretlenir.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _service.DeleteAsync(id);
        if (!deleted)
        {
            return NotFound(new { message = $"Id={id} olan kayıt bulunamadı." });
        }

        return NoContent();
    }
}
