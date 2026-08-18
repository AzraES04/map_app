using StajProject.Business.DTOs;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

public class PermissionService : IPermissionService
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUserService _currentUser;

    public PermissionService(
        IPermissionRepository permissionRepository,
        IUserRepository userRepository,
        ICurrentUserService currentUser)
    {
        _permissionRepository = permissionRepository;
        _userRepository = userRepository;
        _currentUser = currentUser;
    }

    public async Task<List<PermissionDto>> GetAllAsync()
    {
        var yetkiler = await _permissionRepository.GetAllAsync();
        return yetkiler.Select(DtoyaCevir).ToList();
    }

    public async Task<UserPermissionsDto?> GetForUserAsync(int userId)
    {
        var kullanici = await _userRepository.GetByIdAsync(userId);
        if (kullanici is null)
        {
            return null;
        }

        var tumYetkiler = await _permissionRepository.GetAllAsync();
        return Birlestir(kullanici, tumYetkiler);
    }

    public async Task<UserPermissionsDto?> GetForCurrentUserAsync()
    {
        var id = _currentUser.UserId;
        return id is null ? null : await GetForUserAsync(id.Value);
    }

    public async Task<bool> HasPermissionAsync(int userId, string permissionName)
    {
        var matris = await GetForUserAsync(userId);
        return matris?.Permissions
            .Any(y => y.Granted && y.Name == permissionName) ?? false;
    }

    // ------------------------------------------------------------------
    //  Yetki birleştirme — bu sınıfın asıl işi
    // ------------------------------------------------------------------

    /// <summary>
    /// Kullanıcının rollerinden gelen yetkileri çıkarır:
    /// yetki id'si → o yetkiyi veren rol adları.
    ///
    /// PASİF roller sayılmaz. Rolü askıya almanın anlamı "şu an geçerli değil"
    /// olduğuna göre, yetkilerini dağıtmaya devam etmesi çelişki olurdu.
    /// </summary>
    private static Dictionary<int, List<string>> RolYetkileri(User kullanici)
    {
        var sonuc = new Dictionary<int, List<string>>();

        foreach (var kullaniciRol in kullanici.UserRoles)
        {
            var rol = kullaniciRol.Role;
            if (rol is null || !rol.IsActive)
            {
                continue;
            }

            foreach (var rolYetki in rol.RolePermissions)
            {
                if (!sonuc.TryGetValue(rolYetki.PermissionId, out var roller))
                {
                    roller = new List<string>();
                    sonuc[rolYetki.PermissionId] = roller;
                }

                // Aynı yetki iki farklı rolden gelebilir; ikisini de gösteriyoruz.
                if (!roller.Contains(rol.Name))
                {
                    roller.Add(rol.Name);
                }
            }
        }

        return sonuc;
    }

    /// <summary>
    /// Matrisi kurar: sistemdeki HER yetki için "rolden mi geliyor, doğrudan mı
    /// verilmiş, hiç yok mu" bilgisini üretir.
    ///
    /// Sahip olunmayan yetkiler de listeye giriyor — arayüzdeki işaret kutuları
    /// tam listeyi gösterip yalnızca işaretli olanları farklılaştırıyor.
    /// </summary>
    private static UserPermissionsDto Birlestir(User kullanici, List<Permission> tumYetkiler)
    {
        var roldenGelen = RolYetkileri(kullanici);
        var dogrudanVerilen = kullanici.UserPermissions
            .Select(ky => ky.PermissionId)
            .ToHashSet();

        return new UserPermissionsDto
        {
            UserId = kullanici.Id,
            Username = kullanici.Username,
            Roles = kullanici.UserRoles
                .Where(kr => kr.Role is not null)
                .Select(kr => new RoleOzetDto { Id = kr.RoleId, Name = kr.Role!.Name })
                .OrderBy(r => r.Name)
                .ToList(),
            Permissions = tumYetkiler.Select(yetki => new EffectivePermissionDto
            {
                PermissionId = yetki.Id,
                Name = yetki.Name,
                Description = yetki.Description,
                FromRole = roldenGelen.ContainsKey(yetki.Id),
                RoleNames = roldenGelen.TryGetValue(yetki.Id, out var roller)
                    ? roller
                    : new List<string>(),
                Direct = dogrudanVerilen.Contains(yetki.Id),
            }).ToList(),
        };
    }

    private static PermissionDto DtoyaCevir(Permission yetki) => new()
    {
        Id = yetki.Id,
        Name = yetki.Name,
        Description = yetki.Description,
    };
}
