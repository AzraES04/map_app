using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.API.Authorization;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;

namespace StajProject.API.Controllers;

/// <summary>
/// Yönetim paneli — POI kategori sözlüğü (Ödev 12 / Madde 2).
///
/// Kategoriler ORTAK SÖZLÜKTÜR: operatör onları seçer, yönetici tanımlar.
/// Bu yüzden yazma uçlarının tamamı "POI Yönetimi" yetkisine bağlı.
/// Operatörün ihtiyacı olan salt-okunur liste ayrı bir uçta duruyor
/// (<c>GET /api/poi/kategoriler</c>) ve yalnızca aktif kategorileri döner.
/// </summary>
[ApiController]
[Route("api/admin/poi-categories")]
[Authorize]
[YetkiGerekli(Yetkiler.PoiYonetimi)]
public class AdminPoiCategoriesController : YonetimControllerBase
{
    private readonly IPoiCategoryService _service;
    private readonly IPoiStyleService _stilService;

    public AdminPoiCategoriesController(
        IPoiCategoryService service,
        IPoiStyleService stilService,
        ILogger<AdminPoiCategoriesController> logger)
        : base(logger)
    {
        _service = service;
        _stilService = stilService;
    }

    /// <summary>
    /// POI stillerini kategori tablosundan yeniden üretip GeoServer'a yazar
    /// (Ödev 13 iyileştirmesi).
    ///
    /// Normalde ELLE ÇAĞIRMAYA GEREK YOK: kategori eklendiğinde, güncellendiğinde
    /// veya silindiğinde stiller kendiliğinden yenileniyor. Bu uç iki durum için
    /// var:
    ///   • Kategori değiştiği anda GeoServer kapalıydı — o yenileme sessizce
    ///     atlandı (gerekçe: IPoiStyleService.SessizYenileAsync), sonradan
    ///     tamamlanması gerekiyor.
    ///   • GeoServer sıfırdan kuruldu ve stilleri yeniden yazmak gerekiyor.
    ///
    /// Otomatik yenilemenin aksine burada hata YUTULMUYOR: yönetici düğmeye
    /// bastıysa sonucu bilmeli, "yenilendi" deyip hiçbir şey yapmamış olmak
    /// en kötüsü olurdu.
    /// </summary>
    [HttpPost("stilleri-yenile")]
    public Task<ActionResult<PoiStilYenilemeDto>> StilleriYenile(CancellationToken iptal)
        => Calistir<PoiStilYenilemeDto>(async () => Ok(await _stilService.YenileAsync(iptal)));

    /// <summary>
    /// Kategori AĞACI — pasifler dahil. Kökler döner, alt kategoriler
    /// her düğümün <c>cocuklar</c> alanında.
    /// </summary>
    [HttpGet]
    public Task<ActionResult<List<PoiKategoriDto>>> GetAll()
        => Calistir<List<PoiKategoriDto>>(async () => Ok(await _service.GetTreeAsync()));

    /// <summary>Yeni kategori. <c>parentId</c> boş bırakılırsa kök kategori olur.</summary>
    [HttpPost]
    public Task<ActionResult<PoiKategoriDto>> Create([FromBody] PoiKategoriSaveDto dto)
        => Calistir<PoiKategoriDto>(async () => Ok(await _service.CreateAsync(dto)));

    /// <summary>
    /// Kategoriyi günceller — adını, açıklamasını, aktifliğini ve ÜST KATEGORİSİNİ.
    /// Ata değişikliği döngü üretiyorsa 400 ile reddedilir.
    /// </summary>
    [HttpPut("{id:int}")]
    public Task<ActionResult<PoiKategoriDto>> Update(int id, [FromBody] PoiKategoriSaveDto dto)
        => Calistir<PoiKategoriDto>(async () =>
        {
            var guncel = await _service.UpdateAsync(id, dto);
            return guncel is null ? Bulunamadi(id) : Ok(guncel);
        });

    /// <summary>
    /// Soft delete. Alt kategorisi veya bağlı POI'si olan kategori silinmez;
    /// sebebi mesajda yazar (400).
    /// </summary>
    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id)
        => Calistir(async () =>
        {
            var silindi = await _service.DeleteAsync(id);
            return silindi ? NoContent() : BulunamadiSonuc(id);
        });
}
