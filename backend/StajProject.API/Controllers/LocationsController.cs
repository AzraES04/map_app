using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.API.Authorization;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;

namespace StajProject.API.Controllers;

/// <summary>
/// Ödev 2'den kalan <c>locations</c> tablosu (geriye dönük uyumluluk).
///
/// Bu tabloyu <c>tbl_point</c> devraldı (Ödev 3); uçlar arayüz tarafından
/// artık çağrılmıyor. Yine de duruyor: ödevin ilk adımının (PostGIS'e Point
/// yazma) somut karşılığı ve testleri (<c>LocationServiceTests</c>) geçerli.
///
/// ---- YETKİ: sonradan kapatılan bir açık ----
///
/// Ödev 6'da yetki sistemi kurulurken bu controller ATLANMIŞTI: yalnızca
/// <c>[Authorize]</c> vardı, yani GİRİŞ YAPMIŞ HERKES buraya kayıt
/// ekleyebiliyor ve — silme burada HARD DELETE olduğu için — kaydı kalıcı
/// olarak yok edebiliyordu. Hiçbir yetkisi olmayan "Ulaşım Kullanıcısı"
/// rolü bile POST'a 201 alıyordu.
///
/// Artık geometri uçlarıyla AYNI kurala tabi:
///     ekleme → "Point Ekleme"   (locations bir nokta tablosudur)
///     silme  → "Kayıt Silme"
///     okuma  → yetki istemez    (geometri listelemesiyle aynı)
///
/// Yeni bir yetki adı UYDURULMADI: "Locations Ekleme" gibi bir yetki, aynı
/// işin (haritaya nokta yazma) ikinci bir adla yönetilmesi olurdu ve
/// yöneticinin rol ekranında iki kutuyu birden işaretlemesi gerekirdi.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize] // Tüm uçlar geçerli bir JWT ister; token yoksa/süresi dolduysa 401
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

    /// <summary>
    /// Yeni konum ekler (PostGIS Point olarak kaydedilir).
    /// <c>tbl_point</c>'e nokta eklemekle aynı yetkiyi ister.
    /// </summary>
    [HttpPost]
    [YetkiGerekli(Yetkiler.NoktaEkleme)]
    public async Task<ActionResult<LocationDto>> Create([FromBody] LocationCreateDto dto)
    {
        var created = await _locationService.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Konum siler.
    ///
    /// DİKKAT — bu uç HARD DELETE yapıyor: satır tablodan fiziksel olarak
    /// kalkıyor ve geri alınamıyor. Projenin geri kalanı Ödev 3'ten beri soft
    /// delete kullanıyor; <c>locations</c> o kuraldan ÖNCE yazıldığı için
    /// <c>is_deleted</c> kolonu bile yok. Yetki artık arandığı için erişim
    /// açığı kapandı; tabloyu soft delete'e taşımak ayrı bir iş (şema
    /// değişikliği) ve eksikler listesinde duruyor.
    /// </summary>
    [HttpDelete("{id:int}")]
    [YetkiGerekli(Yetkiler.KayitSilme)]
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
