using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.API.Authorization;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;

namespace StajProject.API.Controllers;

/// <summary>
/// Yönetim paneli — Rol Listesi ekranı (Ödev 6 / Madde 1).
/// Rolün yetkileri de rolle birlikte, tek istekte kaydedilir.
/// </summary>
[ApiController]
[Route("api/admin/roles")]
[Authorize]
[YetkiGerekli(Yetkiler.RolYonetimi)]
public class AdminRolesController : YonetimControllerBase
{
    private readonly IRoleService _service;

    public AdminRolesController(IRoleService service, ILogger<AdminRolesController> logger)
        : base(logger)
    {
        _service = service;
    }

    /// <summary>Tüm roller — yetkileri ve kullanıcı sayılarıyla.</summary>
    [HttpGet]
    public Task<ActionResult<List<RoleDto>>> GetAll()
        => Calistir<List<RoleDto>>(async () => Ok(await _service.GetAllAsync()));

    /// <summary>Tek rol.</summary>
    [HttpGet("{id:int}")]
    public Task<ActionResult<RoleDto>> GetById(int id)
        => Calistir<RoleDto>(async () =>
        {
            var rol = await _service.GetByIdAsync(id);
            return rol is null ? Bulunamadi(id) : Ok(rol);
        });

    /// <summary>Yeni rol oluşturur ve seçilen yetkileri bağlar.</summary>
    [HttpPost]
    public Task<ActionResult<RoleDto>> Create([FromBody] RoleSaveDto dto)
        => Calistir<RoleDto>(async () =>
        {
            var olusan = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = olusan.Id }, olusan);
        });

    /// <summary>Rolün adını, açıklamasını, aktifliğini ve yetkilerini günceller.</summary>
    [HttpPut("{id:int}")]
    public Task<ActionResult<RoleDto>> Update(int id, [FromBody] RoleSaveDto dto)
        => Calistir<RoleDto>(async () =>
        {
            var guncel = await _service.UpdateAsync(id, dto);
            return guncel is null ? Bulunamadi(id) : Ok(guncel);
        });

    /// <summary>
    /// Soft delete. Roldeki kullanıcıların atamaları silinmez ama rol
    /// artık yetki üretmez; rol geri alınırsa atamalar yerinde bulunur.
    /// </summary>
    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id)
        => Calistir(async () =>
        {
            var silindi = await _service.DeleteAsync(id);
            return silindi ? NoContent() : BulunamadiSonuc(id);
        });
}
