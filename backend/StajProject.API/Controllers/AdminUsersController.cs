using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.API.Authorization;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;

namespace StajProject.API.Controllers;

/// <summary>
/// Yönetim paneli — Kullanıcı Listesi ekranı (Ödev 6 / Madde 1).
/// Ekle / Güncelle / Çıkar işlemleri ve kullanıcının yetki matrisi buradan yönetilir.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize]                                     // token yoksa 401
[YetkiGerekli(Yetkiler.KullaniciYonetimi)]      // yetki yoksa 403
public class AdminUsersController : YonetimControllerBase
{
    private readonly IUserAdminService _service;
    private readonly IPermissionService _permissionService;

    public AdminUsersController(
        IUserAdminService service,
        IPermissionService permissionService,
        ILogger<AdminUsersController> logger)
        : base(logger)
    {
        _service = service;
        _permissionService = permissionService;
    }

    /// <summary>Tüm kullanıcılar — rolleri ve yetki sayılarıyla birlikte.</summary>
    [HttpGet]
    public Task<ActionResult<List<UserDto>>> GetAll()
        => Calistir<List<UserDto>>(async () => Ok(await _service.GetAllAsync()));

    /// <summary>Tek kullanıcı.</summary>
    [HttpGet("{id:int}")]
    public Task<ActionResult<UserDto>> GetById(int id)
        => Calistir<UserDto>(async () =>
        {
            var kullanici = await _service.GetByIdAsync(id);
            return kullanici is null ? Bulunamadi(id) : Ok(kullanici);
        });

    /// <summary>Yeni kullanıcı oluşturur ve verilen rolleri atar.</summary>
    [HttpPost]
    public Task<ActionResult<UserDto>> Create([FromBody] UserCreateDto dto)
        => Calistir<UserDto>(async () =>
        {
            var olusan = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = olusan.Id }, olusan);
        });

    /// <summary>Kullanıcı adı, şifre, aktiflik ve rolleri günceller.</summary>
    [HttpPut("{id:int}")]
    public Task<ActionResult<UserDto>> Update(int id, [FromBody] UserUpdateDto dto)
        => Calistir<UserDto>(async () =>
        {
            var guncel = await _service.UpdateAsync(id, dto);
            return guncel is null ? Bulunamadi(id) : Ok(guncel);
        });

    /// <summary>Soft delete: kullanıcı silinmiş işaretlenir, kayıtları veritabanında kalır.</summary>
    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id)
        => Calistir(async () =>
        {
            var silindi = await _service.DeleteAsync(id);
            return silindi ? NoContent() : BulunamadiSonuc(id);
        });

    /// <summary>
    /// Kayıt olan kullanıcıyı ONAYLAR (Ödev 10).
    ///
    /// Ayrı bir uç, güncelleme ucunun parçası değil: onay tek yönlü ve tek
    /// seferlik bir karar. Güncelleme gövdesine bir alan olarak koysaydık
    /// yönetici adı düzeltirken farkında olmadan onayı geri alabilirdi.
    /// </summary>
    [HttpPost("{id:int}/approve")]
    public Task<ActionResult<UserDto>> Approve(int id)
        => Calistir<UserDto>(async () =>
        {
            var guncel = await _service.OnaylaAsync(id);
            return guncel is null ? Bulunamadi(id) : Ok(guncel);
        });

    /// <summary>
    /// Kullanıcının YETKİ MATRİSİ: sistemdeki tüm yetkiler ve her birinin kaynağı
    /// (rolden mi geliyor, doğrudan mı verilmiş). Arayüz rolden gelenleri
    /// işaretli ve kilitli gösterir.
    /// </summary>
    [HttpGet("{id:int}/permissions")]
    public Task<ActionResult<UserPermissionsDto>> GetPermissions(int id)
        => Calistir<UserPermissionsDto>(async () =>
        {
            var matris = await _permissionService.GetForUserAsync(id);
            return matris is null ? Bulunamadi(id) : Ok(matris);
        });

    /// <summary>
    /// Kullanıcının DOĞRUDAN yetkilerini günceller. Rolden gelen yetkiler
    /// gönderilse bile kaydedilmez — onlar zaten rolde tanımlı.
    /// </summary>
    [HttpPut("{id:int}/permissions")]
    public Task<ActionResult<UserPermissionsDto>> SetPermissions(
        int id, [FromBody] SetUserPermissionsDto dto)
        => Calistir<UserPermissionsDto>(async () =>
        {
            var matris = await _service.SetPermissionsAsync(id, dto);
            return matris is null ? Bulunamadi(id) : Ok(matris);
        });
}
