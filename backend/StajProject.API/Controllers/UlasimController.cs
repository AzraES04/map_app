using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.API.Authorization;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;

namespace StajProject.API.Controllers;

/// <summary>
/// Akıllı ulaşım modülü — güzergah ve durak uçları (Ödev 16).
///
/// TEK CONTROLLER, İKİ KAYNAK. Güzergah ve durak ayrı controller'lara
/// bölünebilirdi ama sıralama ucu (<c>PUT .../{id}/sira</c>) ikisinin
/// arasında duruyor: bir güzergahın duraklarını yeniden diziyor. Ayırsaydık
/// o ucun hangi controller'a ait olduğu tartışmalı olurdu ve iki dosya
/// sürekli birlikte değişirdi.
///
/// OKUMA UÇLARI YETKİSİZ — yalnızca <c>[Authorize]</c>.
/// Ödev notu ulaşım rollerinin POI ve çizim EKLEYEMEMESİNİ istiyor ama
/// görüntülemeyi açık bırakıyor. Aynı ilke burada da geçerli: güzergah ve
/// durak ortak referans verisidir (bkz. <see cref="IUlasimService"/>).
/// "Ulaşım Kullanıcısı" rolünün hiç yetkisi yok ve tam da bu yüzden
/// listeleri görebiliyor.
/// </summary>
[ApiController]
[Route("api/ulasim")]
[Authorize]
public class UlasimController : YonetimControllerBase
{
    private readonly IUlasimService _service;

    public UlasimController(IUlasimService service, ILogger<UlasimController> logger)
        : base(logger)
    {
        _service = service;
    }

    // ==================================================================
    //  Güzergah
    // ==================================================================

    /// <summary>
    /// Bütün güzergahlar, durakları SIRALI hâlde.
    ///
    /// Harita katmanı hattın çizgisini bu sıradan üretiyor; yönetim
    /// ekranındaki sürükle-bırak listesi de aynı cevaptan besleniyor.
    /// </summary>
    [HttpGet("guzergahlar")]
    public Task<ActionResult<List<GuzergahDto>>> GuzergahlariGetir()
        => Calistir<List<GuzergahDto>>(async () => Ok(await _service.GuzergahlariGetirAsync()));

    /// <summary>Tek güzergah; yoksa 404.</summary>
    [HttpGet("guzergahlar/{id:int}")]
    public Task<ActionResult<GuzergahDto>> GuzergahGetir(int id)
        => Calistir<GuzergahDto>(async () =>
        {
            var guzergah = await _service.GuzergahGetirAsync(id);
            return guzergah is null ? Bulunamadi(id) : Ok(guzergah);
        });

    /// <summary>Yeni güzergah (ad + renk). "Güzergah Yönetimi" yetkisi ister.</summary>
    [HttpPost("guzergahlar")]
    [YetkiGerekli(Yetkiler.GuzergahYonetimi)]
    public Task<ActionResult<GuzergahDto>> GuzergahEkle([FromBody] GuzergahSaveDto dto)
        => Calistir<GuzergahDto>(async () =>
        {
            var olusan = await _service.GuzergahEkleAsync(dto);
            return CreatedAtAction(nameof(GuzergahGetir), new { id = olusan.Id }, olusan);
        });

    [HttpPut("guzergahlar/{id:int}")]
    [YetkiGerekli(Yetkiler.GuzergahYonetimi)]
    public Task<ActionResult<GuzergahDto>> GuzergahGuncelle(int id, [FromBody] GuzergahSaveDto dto)
        => Calistir<GuzergahDto>(async () =>
        {
            var guncel = await _service.GuzergahGuncelleAsync(id, dto);
            return guncel is null ? Bulunamadi(id) : Ok(guncel);
        });

    /// <summary>
    /// Güzergahı siler (soft delete). Durağı varsa 400 ile reddedilir.
    /// </summary>
    [HttpDelete("guzergahlar/{id:int}")]
    [YetkiGerekli(Yetkiler.GuzergahYonetimi)]
    public Task<IActionResult> GuzergahSil(int id)
        => Calistir(async () =>
            await _service.GuzergahSilAsync(id) ? NoContent() : BulunamadiSonuc(id));

    /// <summary>
    /// Durak SIRASINI değiştirir — sürükle-bırakın sunucudaki karşılığı
    /// (Ödev 16 / Madde 2).
    ///
    /// Gövde bütün durak id'lerini YENİ sırasıyla taşıyor; sunucu 1..N olarak
    /// yazıyor. Tek tek "şunun sırası 3 oldu" demek yerine toplu liste
    /// gönderilmesinin gerekçesi <see cref="DurakSiralamaDto"/>'da.
    /// </summary>
    [HttpPut("guzergahlar/{id:int}/sira")]
    [YetkiGerekli(Yetkiler.GuzergahYonetimi)]
    public Task<ActionResult<GuzergahDto>> SiralamaGuncelle(int id, [FromBody] DurakSiralamaDto dto)
        => Calistir<GuzergahDto>(async () =>
        {
            var guncel = await _service.SiralamaGuncelleAsync(id, dto);
            return guncel is null ? Bulunamadi(id) : Ok(guncel);
        });

    // ==================================================================
    //  Durak
    // ==================================================================

    /// <summary>Bütün duraklar — sahiplik süzgeci YOK (ortak veri).</summary>
    [HttpGet("duraklar")]
    public Task<ActionResult<List<DurakDto>>> DuraklariGetir()
        => Calistir<List<DurakDto>>(async () => Ok(await _service.DuraklariGetirAsync()));

    [HttpGet("duraklar/{id:int}")]
    public Task<ActionResult<DurakDto>> DurakGetir(int id)
        => Calistir<DurakDto>(async () =>
        {
            var durak = await _service.DurakGetirAsync(id);
            return durak is null ? Bulunamadi(id) : Ok(durak);
        });

    /// <summary>
    /// Yeni durak. "Durak Ekleme" yetkisi ister; konum, kullanıcının coğrafi
    /// yetki alanının dışındaysa 400 ile reddedilir (Ödev 7 kuralı).
    /// </summary>
    [HttpPost("duraklar")]
    [YetkiGerekli(Yetkiler.DurakEkleme)]
    public Task<ActionResult<DurakDto>> DurakEkle([FromBody] DurakCreateDto dto)
        => Calistir<DurakDto>(async () =>
        {
            var olusan = await _service.DurakEkleAsync(dto);
            return CreatedAtAction(nameof(DurakGetir), new { id = olusan.Id }, olusan);
        });

    /// <summary>
    /// Durak günceller. Öznitelik YOK: gereken yetki "Güzergah Yönetimi"
    /// VEYA ("Durak Ekleme" + kaydın sahibi olmak) — bu VEYA'yı tek yetki adı
    /// alan <c>[YetkiGerekli]</c> ifade edemiyor, kural servis katmanında.
    /// (POI güncellemesinde de aynı durum var.)
    /// </summary>
    [HttpPut("duraklar/{id:int}")]
    public Task<ActionResult<DurakDto>> DurakGuncelle(int id, [FromBody] DurakUpdateDto dto)
        => Calistir<DurakDto>(async () =>
        {
            var guncel = await _service.DurakGuncelleAsync(id, dto);
            return guncel is null ? Bulunamadi(id) : Ok(guncel);
        });

    /// <summary>Soft delete; yetki kuralı güncellemedekiyle aynı.</summary>
    [HttpDelete("duraklar/{id:int}")]
    public Task<IActionResult> DurakSil(int id)
        => Calistir(async () =>
            await _service.DurakSilAsync(id) ? NoContent() : BulunamadiSonuc(id));
}
