using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.API.Authorization;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Validation;
using StajProject.Business.Geo;
using StajProject.Business.Services;
using StajProject.DataAccess.GeoServer;
using StajProject.Entities;

namespace StajProject.API.Controllers;

/// <summary>
/// Üç geometri controller'ının ortak gövdesi.
///
/// abstract → ASP.NET Core bunu tek başına bir controller olarak keşfetmez;
/// sadece PointsController / LinesController / PolygonsController'a gövde sağlar.
/// [Route] ve [ApiController] öznitelikleri de bilerek burada değil, türeyen sınıflarda.
///
/// HATA YÖNETİMİ (Ödev 5 / Madde 1)
/// Her uç try-catch ile sarılıdır ve hepsi AYNI kalıbı izler:
///   WktFormatException → 400 (istemcinin gönderdiği veri hatalı)
///   Exception          → 500 (sunucu hatası; ayrıntı log'a, istemciye genel mesaj)
/// Kalıbın tek yerde durması için gövdeler <see cref="Calistir{T}"/> yardımcılarına
/// verilir; böylece "standartlaştırma" gerçekten standart olur — 7 uçta 7 farklı
/// catch bloğu yazılmaz. ExceptionHandlingMiddleware de son güvenlik ağı olarak durur.
/// </summary>
[Authorize]  // üç controller da JWT ister; token yoksa/süresi dolduysa 401
public abstract class GeometryControllerBase<TEntity> : ControllerBase, IEklemeYetkisiTasiyan
    where TEntity : GeometryEntityBase
{
    /// <summary>
    /// Bu controller'da kayıt EKLEMEK hangi yetkiyi gerektirir?
    /// Üçünde farklı olduğu için (Point/Line/Polygon Ekleme) türeyen sınıf söyler;
    /// <see cref="EklemeYetkisiGerekliAttribute"/> bu değeri çalışma anında okur.
    /// </summary>
    public abstract string EklemeYetkisi { get; }

    private readonly IGeometryService<TEntity> _service;
    private readonly ILogger _logger;

    protected GeometryControllerBase(IGeometryService<TEntity> service, ILogger logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>Giriş yapan kullanıcının kayıtlarını WKT formatında listeler.</summary>
    [HttpGet]
    public Task<ActionResult<List<GeometryDto>>> GetAll()
        => Calistir<List<GeometryDto>>(async () => Ok(await _service.GetAllAsync()));

    /// <summary>Id ile tek kayıt (yalnızca kaydın sahibi erişebilir).</summary>
    [HttpGet("{id:int}")]
    public Task<ActionResult<GeometryDto>> GetById(int id)
        => Calistir<GeometryDto>(async () =>
        {
            var item = await _service.GetByIdAsync(id);
            return item is null ? Bulunamadi(id) : Ok(item);
        });

    /// <summary>
    /// WKT metninden yeni geometri kaydeder. Kayıt, giriş yapan kullanıcıya bağlanır.
    /// Tipe göre "Point/Line/Polygon Ekleme" yetkisi gerektirir.
    /// </summary>
    [HttpPost]
    [EklemeYetkisiGerekli]
    public Task<ActionResult<GeometryDto>> Create([FromBody] GeometryCreateDto dto)
        => Calistir<GeometryDto>(async () =>
        {
            var created = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        });

    /// <summary>Ad, renk, açıklama ve istenirse GEOMETRİ günceller (Ödev 5 / Madde 4).</summary>
    [HttpPut("{id:int}")]
    [YetkiGerekli(Yetkiler.KayitGuncelleme)]
    public Task<ActionResult<GeometryDto>> Update(int id, [FromBody] GeometryUpdateDto dto)
        => Calistir<GeometryDto>(async () =>
        {
            var updated = await _service.UpdateAsync(id, dto);
            return updated is null ? Bulunamadi(id) : Ok(updated);
        });

    /// <summary>
    /// Silinen kaydı geri getirir. Soft delete kullandığımız için veri hâlâ
    /// tabloda duruyor; bu uç tek bir UPDATE ile silmeyi geri alıyor.
    /// </summary>
    [HttpPost("{id:int}/restore")]
    [YetkiGerekli(Yetkiler.KayitSilme)]   // geri alma, silme yetkisinin parçasıdır
    public Task<IActionResult> Restore(int id)
        => Calistir(async () =>
        {
            var geriAlindi = await _service.RestoreAsync(id);
            return geriAlindi
                ? NoContent()
                : NotFound(new { message = $"Id={id} için geri alınacak silinmiş kayıt bulunamadı." });
        });

    /// <summary>
    /// Kaydı askıya alır / yeniden aktif eder (is_active kolonu).
    /// Silmekten farkı: pasif kayıt listelerde görünmeye devam eder.
    /// </summary>
    [HttpPost("{id:int}/active")]
    [YetkiGerekli(Yetkiler.KayitGuncelleme)]
    public Task<IActionResult> SetActive(int id, [FromBody] SetActiveDto dto)
        => Calistir(async () =>
        {
            var degisti = await _service.SetActiveAsync(id, dto.IsActive);
            return degisti ? NoContent() : BulunamadiSonuc(id);
        });

    /// <summary>Soft delete: kayıt fiziksel olarak silinmez, is_deleted işaretlenir.</summary>
    [HttpDelete("{id:int}")]
    [YetkiGerekli(Yetkiler.KayitSilme)]
    public Task<IActionResult> Delete(int id)
        => Calistir(async () =>
        {
            var deleted = await _service.DeleteAsync(id);
            return deleted ? NoContent() : BulunamadiSonuc(id);
        });

    // ---------------------------------------------------------------------
    //  Ortak hata yönetimi
    // ---------------------------------------------------------------------

    /// <summary>Değer döndüren uçlar için try-catch kalıbı.</summary>
    private async Task<ActionResult<T>> Calistir<T>(Func<Task<ActionResult<T>>> govde)
    {
        try
        {
            return await govde();
        }
        catch (WktFormatException ex)
        {
            // İstemcinin gönderdiği veri hatalı → 4xx. Mesaj kullanıcıya gösterilebilir.
            _logger.LogWarning(ex, "Geçersiz istek verisi");
            return BadRequest(new { message = ex.Message });
        }
        catch (IsKuraliException ex)
        {
            // İş kuralı ihlali — Ödev 7'de "çizim izinli alanın dışında" bu yoldan gelir.
            // 403 değil 400: kullanıcının yetkisi var, gönderdiği VERİ kurala uymuyor.
            _logger.LogWarning(ex, "İş kuralı ihlali");
            return BadRequest(new { message = ex.Message });
        }
        catch (GeoServerErisimException ex)
        {
            // Ödev 8: listeleme artık GeoServer'dan geliyor. Sunucu kapalıysa
            // bu 500 değil 503'tür — hata bizde değil, bağımlı serviste.
            _logger.LogError(ex, "GeoServer erişim hatası");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = ex.Message });
        }
        catch (Exception ex)
        {
            // Beklenmeyen hata → 5xx. Ayrıntı log'a yazılır, istemciye sızdırılmaz.
            _logger.LogError(ex, "Beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Beklenmeyen bir hata oluştu." });
        }
    }

    /// <summary>Gövdesiz (204/404) dönen uçlar için aynı kalıp.</summary>
    private async Task<IActionResult> Calistir(Func<Task<IActionResult>> govde)
    {
        try
        {
            return await govde();
        }
        catch (WktFormatException ex)
        {
            _logger.LogWarning(ex, "Geçersiz istek verisi");
            return BadRequest(new { message = ex.Message });
        }
        catch (IsKuraliException ex)
        {
            _logger.LogWarning(ex, "İş kuralı ihlali");
            return BadRequest(new { message = ex.Message });
        }
        catch (GeoServerErisimException ex)
        {
            _logger.LogError(ex, "GeoServer erişim hatası");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Beklenmeyen bir hata oluştu." });
        }
    }

    private ActionResult Bulunamadi(int id)
        => NotFound(new { message = $"Id={id} olan kayıt bulunamadı." });

    private IActionResult BulunamadiSonuc(int id)
        => NotFound(new { message = $"Id={id} olan kayıt bulunamadı." });
}
