using StajProject.Business.DTOs;
using StajProject.Business.Validation;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

public class RoleService : IRoleService
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;

    public RoleService(IRoleRepository roleRepository, IPermissionRepository permissionRepository)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
    }

    public async Task<List<RoleDto>> GetAllAsync()
    {
        var roller = await _roleRepository.GetAllAsync();

        // Kullanıcı sayıları TEK sorguda geliyor; her rol için ayrı COUNT atmıyoruz.
        var sayilar = await _roleRepository.GetUserCountsAsync();

        return roller.Select(rol => DtoyaCevir(rol, sayilar)).ToList();
    }

    public async Task<RoleDto?> GetByIdAsync(int id)
    {
        var rol = await _roleRepository.GetByIdAsync(id);
        if (rol is null)
        {
            return null;
        }

        return DtoyaCevir(rol, await _roleRepository.GetUserCountsAsync());
    }

    public async Task<RoleDto> CreateAsync(RoleSaveDto dto)
    {
        var ad = AdiDogrula(dto.Name);
        await AdBenzersizMiAsync(ad, haricId: null);
        var yetkiIdleri = await YetkileriDogrulaAsync(dto.PermissionIds);

        var olusan = await _roleRepository.AddAsync(new Role
        {
            Name = ad,
            Description = dto.Description?.Trim(),
            IsActive = dto.IsActive,
            InsertedDate = DateTime.UtcNow,
        });

        await _roleRepository.SetPermissionsAsync(olusan.Id, yetkiIdleri);

        // Yetkiler ayrı bir çağrıyla yazıldığı için kaydı yeniden okuyoruz:
        // dönen DTO, veritabanındaki SON hâli yansıtsın.
        return (await GetByIdAsync(olusan.Id))!;
    }

    public async Task<RoleDto?> UpdateAsync(int id, RoleSaveDto dto)
    {
        var mevcut = await _roleRepository.GetByIdAsync(id);
        if (mevcut is null)
        {
            return null;
        }

        var ad = AdiDogrula(dto.Name);
        await AdBenzersizMiAsync(ad, haricId: id);
        var yetkiIdleri = await YetkileriDogrulaAsync(dto.PermissionIds);

        var guncellendi = await _roleRepository.UpdateAsync(new Role
        {
            Id = id,
            Name = ad,
            Description = dto.Description?.Trim(),
            IsActive = dto.IsActive,
        });

        if (guncellendi is null)
        {
            return null;
        }

        await _roleRepository.SetPermissionsAsync(id, yetkiIdleri);
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // Soft delete: rol satırı kalır, user_roles satırları da öyle.
        // Sorgu filtresi silinmiş rolü elediği için o roldeki kullanıcılar
        // yetkilerini anında kaybeder — ama rol geri alınırsa atamalar yerinde durur.
        return await _roleRepository.SoftDeleteAsync(id);
    }

    // ------------------------------------------------------------------
    //  Doğrulamalar
    // ------------------------------------------------------------------

    private static string AdiDogrula(string ad)
    {
        var temiz = ad.Trim();
        if (temiz.Length == 0)
        {
            throw new IsKuraliException("Rol adı boş olamaz.");
        }

        return temiz;
    }

    /// <summary>
    /// Aynı adda ikinci bir rol açılmasını engeller.
    /// Veritabanındaki benzersiz index de bunu engelliyor ama oradan gelen hata
    /// kullanıcıya gösterilebilir bir mesaj değil; kuralı burada anlamlı
    /// bir cümleyle karşılıyoruz.
    /// </summary>
    private async Task AdBenzersizMiAsync(string ad, int? haricId)
    {
        var ayniAdli = await _roleRepository.GetByNameAsync(ad);
        if (ayniAdli is not null && ayniAdli.Id != haricId)
        {
            throw new IsKuraliException($"\"{ad}\" adında bir rol zaten var.");
        }
    }

    /// <summary>
    /// Gelen yetki id'lerinin gerçekten var olduğunu doğrular.
    /// Olmayan id'yi doğrudan yazsaydık veritabanı yabancı anahtar hatası
    /// fırlatır, kullanıcı da anlaşılmaz bir 500 görürdü.
    /// </summary>
    private async Task<List<int>> YetkileriDogrulaAsync(List<int> istenen)
    {
        var benzersiz = istenen.Distinct().ToList();
        if (benzersiz.Count == 0)
        {
            return benzersiz;
        }

        var varOlanlar = await _permissionRepository.GetExistingIdsAsync(benzersiz);
        if (varOlanlar.Count != benzersiz.Count)
        {
            throw new IsKuraliException("Seçilen yetkilerden bazıları bulunamadı. Sayfayı yenileyip tekrar deneyin.");
        }

        return varOlanlar;
    }

    private static RoleDto DtoyaCevir(Role rol, IReadOnlyDictionary<int, int> kullaniciSayilari) => new()
    {
        Id = rol.Id,
        Name = rol.Name,
        Description = rol.Description,
        IsActive = rol.IsActive,
        InsertedDate = rol.InsertedDate,
        ModifiedDate = rol.ModifiedDate,
        Permissions = rol.RolePermissions
            .Where(rp => rp.Permission is not null)
            .Select(rp => new PermissionDto
            {
                Id = rp.PermissionId,
                Name = rp.Permission!.Name,
                Description = rp.Permission.Description,
            })
            .OrderBy(p => p.Id)
            .ToList(),
        UserCount = kullaniciSayilari.TryGetValue(rol.Id, out var sayi) ? sayi : 0,
    };
}
