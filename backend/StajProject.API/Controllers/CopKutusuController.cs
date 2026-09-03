using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.Business.Validation;

namespace StajProject.API.Controllers;

/// <summary>
/// ÇÖP KUTUSU — silinen her kaydın geri getirilebildiği tek yer.
///
/// ---- BU CONTROLLER NEDEN VAR? ----
///
/// Proje Ödev 3'ten beri soft delete kullanıyor: hiçbir kayıt veritabanından
/// gitmiyor. Yani veri ZATEN duruyordu — ama arayüzden görmenin bir yolu
/// yoktu. Geometri ve POI'de "restore" uçları vardı, ancak silinenleri
/// LİSTELEYEN hiçbir şey olmadığı için o uçları çağırmak silinen kaydın
/// id'sini bir yerden bilmeyi gerektiriyordu; pratikte erişilemezlerdi.
///
/// ---- SINIF DÜZEYİNDE YETKİ ÖZNİTELİĞİ YOK — BİLEREK ----
///
/// Çünkü gereken yetki KAYDIN TÜRÜNE göre değişiyor: bir noktayı geri almak
/// "Kayıt Silme", bir rolü geri almak "Rol Yönetimi" istiyor. Tek bir
/// öznitelik bunu ifade edemiyor; kural serviste ve testleri orada
/// (<c>CopKutusuTests</c>).
///
/// Yeni bir "Çöp Kutusu Yönetimi" yetkisi uydurmadık: o yetkiye sahip biri,
/// silemeyeceği bir kaydı geri alabilir hâle gelirdi.
/// </summary>
[ApiController]
[Route("api/cop")]
[Authorize]
public class CopKutusuController : ControllerBase
{
    private readonly ICopKutusuService _service;
    private readonly ILogger<CopKutusuController> _logger;

    public CopKutusuController(ICopKutusuService service, ILogger<CopKutusuController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Silinmiş bütün kayıtlar, en yeniden eskiye, tür özetiyle birlikte.
    ///
    /// Her kayıt <c>geriAlinabilir</c> alanını taşıyor: arayüz düğmeyi buna
    /// göre gösteriyor. Yetkiyi istemcide hesaplatmak, yetki kurallarını
    /// ikinci kez (ve er geç yanlış) uygulamak olurdu.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<CopKutusuDto>> Getir()
    {
        try
        {
            return Ok(await _service.GetirAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Çöp kutusu okunurken beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Çöp kutusu görüntülenemedi." });
        }
    }

    /// <summary>
    /// Bir kaydı geri alır.
    ///
    /// <paramref name="tur"/> yol parçası olarak geliyor: "nokta", "cizgi",
    /// "poligon", "poi", "kategori", "durak", "guzergah", "kullanici", "rol".
    ///
    /// POST, çünkü uç veritabanını DEĞİŞTİRİYOR. Gövde yok — geri almak için
    /// tür ve id yeterli.
    /// </summary>
    [HttpPost("{tur}/{id:int}/geri-al")]
    public async Task<IActionResult> GeriAl(string tur, int id)
    {
        try
        {
            if (!await _service.GeriAlAsync(tur, id))
            {
                // Kayıt yok ya da zaten silinmemiş. İkisini ayırmıyoruz:
                // kullanıcı için sonuç aynı ve "böyle bir id var ama silinmemiş"
                // demek, olmayan bir kaydın varlığını doğrulamak olurdu.
                return NotFound(new
                {
                    message = "Kayıt bulunamadı ya da zaten geri alınmış.",
                });
            }

            return NoContent();
        }
        catch (IsKuraliException ex)
        {
            // Yetki yok ya da tür tanınmıyor.
            _logger.LogWarning(ex, "Geri alma reddedildi: {Tur}/{Id}", tur, id);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Geri alma sırasında beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Kayıt geri alınamadı." });
        }
    }

    /// <summary>
    /// Bir kaydı VERİTABANINDAN TAMAMEN SİLER — geri alma yok.
    ///
    /// DELETE, çünkü uç geri dönüşü olmayan bir işlem yapıyor. Arayüz bunu
    /// çağırmadan önce kullanıcıya açıkça sormalı ("bu işlem geri
    /// alınamaz") — sunucu tarafında ek bir onay adımı yok, çünkü çöp
    /// kutusuna düşmüş bir kayıt zaten bir kez "sil" denmiş bir kayıt.
    /// </summary>
    [HttpDelete("{tur}/{id:int}")]
    public async Task<IActionResult> KaliciSil(string tur, int id)
    {
        try
        {
            if (!await _service.KaliciSilAsync(tur, id))
            {
                return NotFound(new
                {
                    message = "Kayıt bulunamadı ya da henüz silinmemiş (önce çöp kutusuna düşmesi gerekir).",
                });
            }

            return NoContent();
        }
        catch (IsKuraliException ex)
        {
            // Yetki yok, tür tanınmıyor ya da başka kayıtlar hâlâ bağlı.
            _logger.LogWarning(ex, "Kalıcı silme reddedildi: {Tur}/{Id}", tur, id);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kalıcı silme sırasında beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Kayıt kalıcı silinemedi." });
        }
    }
}
