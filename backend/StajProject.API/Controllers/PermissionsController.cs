using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.Business.DTOs;
using StajProject.Business.Services;

namespace StajProject.API.Controllers;

/// <summary>
/// Yetki sözlüğü (Ödev 6 / Madde 2).
///
/// Yetkiler arayüzden EKLENMEZ: bir yetkinin anlamı, kodda onu kontrol eden
/// bir yer olmasıdır. Bu yüzden ekleme/silme ucu yok — liste okunur, dağıtımı
/// rol ve kullanıcı ekranlarından yapılır.
/// </summary>
[ApiController]
[Route("api/permissions")]
[Authorize]
public class PermissionsController : YonetimControllerBase
{
    private readonly IPermissionService _service;
    private readonly IGeoPermissionService _geoPermissionService;

    public PermissionsController(
        IPermissionService service,
        IGeoPermissionService geoPermissionService,
        ILogger<PermissionsController> logger)
        : base(logger)
    {
        _service = service;
        _geoPermissionService = geoPermissionService;
    }

    /// <summary>Sistemdeki tüm yetkiler — rol ve kullanıcı ekranlarındaki liste bundan doldurulur.</summary>
    [HttpGet]
    public Task<ActionResult<List<PermissionDto>>> GetAll()
        => Calistir<List<PermissionDto>>(async () => Ok(await _service.GetAllAsync()));

    /// <summary>
    /// GİRİŞ YAPMIŞ kullanıcının ÇALIŞMA ALANI (Ödev 7 / Madde 2).
    ///
    /// Harita ekranı bu alanı ekranda çizer: kullanıcı nereye çizebileceğini
    /// önceden görür, sınırı deneme yanılmayla keşfetmek zorunda kalmaz.
    /// Hiç tanım yoksa <c>kisitli: false</c> döner — kısıt konmamış demektir.
    /// </summary>
    [HttpGet("me/geo")]
    public Task<ActionResult<CalismaAlaniDto>> GetCalismaAlanim()
        => Calistir<CalismaAlaniDto>(async () => Ok(await _geoPermissionService.GetCalismaAlanimAsync()));

    /// <summary>
    /// GİRİŞ YAPMIŞ kullanıcının kendi yetki matrisi.
    ///
    /// Arayüz menüyü buna göre kısıyor: yetkisi olmayana "Yönetim" bağlantısı
    /// hiç gösterilmiyor. Yine de asıl kontrol sunucuda — gizlenmiş bir bağlantı
    /// güvenlik değildir, sadece nezakettir.
    /// </summary>
    [HttpGet("me")]
    public Task<ActionResult<UserPermissionsDto>> GetMine()
        => Calistir<UserPermissionsDto>(async () =>
        {
            var matris = await _service.GetForCurrentUserAsync();
            return matris is null
                ? Unauthorized(new { message = "Oturum bulunamadı." })
                : Ok(matris);
        });
}
