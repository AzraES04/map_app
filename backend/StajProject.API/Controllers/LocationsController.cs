using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.Business.DTOs;
using StajProject.Business.Services;

namespace StajProject.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Bu controller'daki tüm uçlar geçerli bir JWT ister; token yoksa/süresi dolduysa 401 döner
public class LocationsController : ControllerBase
{
    private readonly ILocationService _locationService;

    public LocationsController(ILocationService locationService)
    {
        _locationService = locationService;
    }

    /// <summary>Tüm konumları listeler.</summary>
    [HttpGet]
    public async Task<ActionResult<List<LocationDto>>> GetAll()
    {
        var locations = await _locationService.GetAllAsync();
        return Ok(locations);
    }

    /// <summary>Id'ye göre tek konum döner.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<LocationDto>> GetById(int id)
    {
        var location = await _locationService.GetByIdAsync(id);
        if (location is null)
        {
            return NotFound(new { message = $"Id={id} olan konum bulunamadı." });
        }

        return Ok(location);
    }

    /// <summary>Yeni konum ekler (PostGIS Point olarak kaydedilir).</summary>
    [HttpPost]
    public async Task<ActionResult<LocationDto>> Create([FromBody] LocationCreateDto dto)
    {
        var created = await _locationService.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Konum siler.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _locationService.DeleteAsync(id);
        if (!deleted)
        {
            return NotFound(new { message = $"Id={id} olan konum bulunamadı." });
        }

        return NoContent();
    }
}
