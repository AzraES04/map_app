using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.Business.DTOs;
using StajProject.Business.Services;

namespace StajProject.API.Controllers;

/// <summary>
/// İl ve bölge referans verisi (Ödev 10).
///
/// Coğrafi yetki ekranı bu uçlardan besleniyor: yönetici ya haritadan il
/// tıklıyor, ya listeden bölge seçiyor, ya da eskisi gibi elle çiziyor.
///
/// Yetki kontrolü yok, yalnızca <c>[Authorize]</c>: il sınırları gizli bir
/// bilgi değil, herkesin bildiği idari coğrafya. Yetki asıl işlemde —
/// alanı KAYDEDERKEN — aranıyor.
/// </summary>
[ApiController]
[Route("api/iller")]
[Authorize]
public class IllerController : YonetimControllerBase
{
    private readonly IIlService _service;

    public IllerController(IIlService service, ILogger<IllerController> logger)
        : base(logger)
    {
        _service = service;
    }

    /// <summary>81 il — plaka, ad, bölge. Geometri İÇERMEZ.</summary>
    [HttpGet]
    public Task<ActionResult<List<IlOzetDto>>> GetAll()
        => Calistir<List<IlOzetDto>>(async () => Ok(await _service.GetIllerAsync()));

    /// <summary>
    /// İl sınırları (WKT). Haritadaki tıklanabilir seçim katmanı bunu kullanıyor.
    /// Cevap ~250 KB olduğu için arayüz bunu bir kez çekip saklıyor.
    /// </summary>
    [HttpGet("sinirlar")]
    public Task<ActionResult<List<IlSinirDto>>> GetSinirlar()
        => Calistir<List<IlSinirDto>>(async () => Ok(await _service.GetSinirlarAsync()));

    /// <summary>Yedi coğrafi bölge ve il sayıları.</summary>
    [HttpGet("bolgeler")]
    public Task<ActionResult<List<BolgeOzetDto>>> GetBolgeler()
        => Calistir<List<BolgeOzetDto>>(async () => Ok(await _service.GetBolgelerAsync()));
}
