using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.Business.Validation;

namespace StajProject.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Kullanıcı adı ve şifre ile giriş yapar, başarılıysa JWT döner.
    /// IP başına dakikada 5 deneme ile sınırlıdır (kaba kuvvet koruması).
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("giris")]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
    {
        // Ödev 5 / Madde 1: standart hata yönetimi kalıbı
        try
        {
            var response = await _authService.LoginAsync(request);
            if (response is null)
            {
                // Kullanıcı yok, şifre yanlış, hesap pasif veya silinmiş —
                // hepsine AYNI cevap. Farklı mesaj vermek saldırgana
                // "bu kullanıcı adı var" bilgisini doğrulardı.
                return Unauthorized(new { message = "Kullanıcı adı veya şifre hatalı." });
            }

            return Ok(response);
        }
        catch (IsKuraliException ex)
        {
            // Ödev 10: şifre doğru ama hesap onay bekliyor. Bu mesajı
            // göstermek gerekiyor — kullanıcı aksi hâlde neden giremediğini
            // bilemezdi. 403: kimlik doğru, erişim henüz açık değil.
            _logger.LogInformation(ex, "Onaylanmamış hesap giriş denedi");
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Giriş sırasında beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Giriş yapılamadı. Lütfen tekrar deneyin." });
        }
    }

    /// <summary>
    /// Yeni hesap açar (Ödev 10). Hesap yönetici onayına düşer, token DÖNMEZ.
    /// Giriş ucuyla aynı hız sınırına tabidir: kayıt ucu da kullanıcı adı
    /// taraması için kötüye kullanılabilir.
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting("giris")]
    public async Task<ActionResult<RegisterResponseDto>> Register([FromBody] RegisterRequestDto request)
    {
        try
        {
            return Ok(await _authService.RegisterAsync(request));
        }
        catch (IsKuraliException ex)
        {
            _logger.LogWarning(ex, "Kayıt reddedildi");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kayıt sırasında beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Kayıt yapılamadı. Lütfen tekrar deneyin." });
        }
    }

    /// <summary>
    /// Eksik 5 — oturumu yeniler.
    ///
    /// Frontend bunu iki durumda çağırıyor: erişim token'ının süresi
    /// dolmadan hemen önce (sessiz yenileme) ve bir istek 401 dönerse
    /// (o isteği yeni token'la bir kez tekrarlamak için).
    ///
    /// Cevap giriş cevabıyla AYNI biçimde: yeni erişim token'ı VE yeni
    /// yenileme anahtarı. Eski anahtar bu çağrıyla birlikte ölür (döndürme);
    /// istemci yeni değeri saklamazsa bir sonraki yenileme başarısız olur.
    ///
    /// Anahtar geçersizse 401 döner — istemci için anlamı: oturum bitti,
    /// giriş ekranına dön.
    /// </summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("yenileme")]
    public async Task<ActionResult<LoginResponseDto>> Refresh([FromBody] RefreshRequestDto request)
    {
        try
        {
            var response = await _authService.RefreshAsync(request.RefreshToken);
            if (response is null)
            {
                // Geçersiz, süresi dolmuş, iptal edilmiş ve "hesap artık uygun
                // değil" durumlarına AYNI cevap. Ayırt etseydik, elinde bir
                // anahtar olan saldırgana onun ne durumda olduğunu — dolayısıyla
                // gerçek olup olmadığını — söylemiş olurduk.
                return Unauthorized(new { message = "Oturumunuz sona erdi. Lütfen tekrar giriş yapın." });
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Oturum yenilenirken beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Oturum yenilenemedi. Lütfen tekrar giriş yapın." });
        }
    }

    /// <summary>
    /// Eksik 5 — çıkış: yenileme anahtarını iptal eder.
    ///
    /// Erişim token'ı iptal EDİLEMEZ (imzaya bakılır, veritabanına değil), bu
    /// yüzden çıkıştan sonra en fazla token süresi kadar (10 dk) geçerli
    /// kalmaya devam eder. Kapatılan şey oturumun devamı: o pencere
    /// dolduğunda yeni token üretilemez.
    ///
    /// [Authorize] YOK — bilerek. Çıkış, erişim token'ının süresi dolmuşken
    /// de yapılabilmeli; aksi hâlde oturumu bırakmak isteyen kullanıcı, tam
    /// da bunu yapamadığı için yenileme anahtarını sunucuda açık bırakırdı.
    /// Yetki yerine anahtarın kendisi kanıt: onu bilen zaten sahibidir.
    ///
    /// Her zaman 204 döner; geçersiz anahtar da hata sayılmaz.
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequestDto request)
    {
        try
        {
            await _authService.LogoutAsync(request.RefreshToken);
        }
        catch (Exception ex)
        {
            // Çıkışın kullanıcı tarafında başarısız olması diye bir şey yok:
            // istemci zaten yerel oturumunu siliyor. Hatayı kaydedip sessizce
            // geçiyoruz ki kullanıcı "çıkış yapılamadı" ekranında kalmasın.
            _logger.LogError(ex, "Çıkış sırasında beklenmeyen hata");
        }

        return NoContent();
    }

    // ==================================================================
    //  İKİ ADIMLI DOĞRULAMA (TOTP)
    // ==================================================================

    /// <summary>
    /// Girişin İKİNCİ adımı: ara token + 6 haneli kod → gerçek oturum.
    ///
    /// GİRİŞ UCUYLA AYNI HIZ SINIRINDA (5/dk) — ve bu şart. Kod yalnızca
    /// 6 haneli, yani 1.000.000 olasılık; sınırsız deneme hakkı olsaydı
    /// saldırgan ara token'ın 5 dakikalık ömrü içinde binlerce kod
    /// deneyebilirdi. Sınırla birlikte pencere başına en fazla ~25 deneme
    /// düşüyor.
    ///
    /// [Authorize] YOK: kullanıcı henüz giriş yapmış değil, elindeki tek şey
    /// ara token. Kanıt olarak o kabul ediliyor.
    /// </summary>
    [HttpPost("login/2fa")]
    [EnableRateLimiting("giris")]
    public async Task<ActionResult<LoginResponseDto>> IkinciAdim([FromBody] IkinciAdimGirisDto request)
    {
        try
        {
            var response = await _authService.IkinciAdimGirisAsync(request);
            if (response is null)
            {
                // Ara token geçersiz, süresi dolmuş, kod yanlış, hesap
                // kapatılmış… hepsine AYNI cevap. Ayırt etseydik saldırgana
                // hangi kısmın tuttuğunu söylerdik.
                return Unauthorized(new { message = "Doğrulama kodu geçersiz veya süresi dolmuş." });
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "İkinci adım doğrulamasında beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Doğrulama yapılamadı. Lütfen tekrar deneyin." });
        }
    }

    /// <summary>
    /// Kurulumu başlatır — gizli anahtarı ve QR adresini döner.
    ///
    /// [Authorize] VAR: kendi hesabının korumasını ancak giriş yapmış
    /// kullanıcı kurabilir. Yetki özniteliği YOK — bu bir yönetim işlemi
    /// değil, herkesin kendi hesabı için yapabileceği bir ayar.
    /// </summary>
    [HttpPost("2fa/baslat")]
    [Authorize]
    public Task<ActionResult<TotpKurulumDto>> TotpBaslat()
        => Calistir(() => _authService.TotpBaslatAsync());

    /// <summary>Kurulumu tamamlar: kod doğruysa koruma açılır.</summary>
    [HttpPost("2fa/dogrula")]
    [Authorize]
    [EnableRateLimiting("giris")]
    public Task<IActionResult> TotpDogrula([FromBody] TotpDogrulaDto request)
        => CalistirBossa(() => _authService.TotpDogrulaVeAcAsync(request));

    /// <summary>Korumayı kapatır (şifre ister).</summary>
    [HttpPost("2fa/kapat")]
    [Authorize]
    [EnableRateLimiting("giris")]
    public Task<IActionResult> TotpKapat([FromBody] TotpKapatDto request)
        => CalistirBossa(() => _authService.TotpKapatAsync(request));

    /// <summary>Giriş yapmış kullanıcının iki adımlı doğrulama durumu.</summary>
    [HttpGet("2fa/durum")]
    [Authorize]
    public Task<ActionResult<TotpDurumDto>> TotpDurum()
        => Calistir(() => _authService.TotpDurumAsync());

    // ---------------------------------------------------------------- ortak

    /// <summary>
    /// Değer DÖNEN uçlar için ortak hata kalıbı.
    ///
    /// Ayrı bir yardımcı yazıldı çünkü dört uç da aynı üç şeyi yapıyor:
    /// iş kuralı hatasını 400'e, beklenmeyeni 500'e çevir, gerisini Ok ile
    /// döndür. Dört kez kopyalasaydık biri güncellenip diğerleri unutulurdu.
    /// </summary>
    private async Task<ActionResult<T>> Calistir<T>(Func<Task<T>> is_)
    {
        try
        {
            return Ok(await is_());
        }
        catch (IsKuraliException ex)
        {
            _logger.LogWarning(ex, "İki adımlı doğrulama isteği reddedildi");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "İki adımlı doğrulamada beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "İşlem tamamlanamadı. Lütfen tekrar deneyin." });
        }
    }

    /// <summary>Değer DÖNMEYEN uçlar için aynı kalıp (204).</summary>
    private async Task<IActionResult> CalistirBossa(Func<Task> is_)
    {
        try
        {
            await is_();
            return NoContent();
        }
        catch (IsKuraliException ex)
        {
            _logger.LogWarning(ex, "İki adımlı doğrulama isteği reddedildi");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "İki adımlı doğrulamada beklenmeyen hata");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "İşlem tamamlanamadı. Lütfen tekrar deneyin." });
        }
    }
}
