using Microsoft.AspNetCore.Identity;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Validation;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

public class UserAdminService : IUserAdminService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserService _currentUser;

    // AuthService ile AYNI hash algoritması — burada üretilen şifreyle
    // giriş yapılabilmesi buna bağlı.
    private readonly PasswordHasher<User> _passwordHasher = new();

    public UserAdminService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IPermissionService permissionService,
        ICurrentUserService currentUser)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _permissionService = permissionService;
        _currentUser = currentUser;
    }

    public async Task<List<UserDto>> GetAllAsync()
    {
        var kullanicilar = await _userRepository.GetAllAsync();
        return kullanicilar.Select(DtoyaCevir).ToList();
    }

    public async Task<UserDto?> GetByIdAsync(int id)
    {
        var kullanici = await _userRepository.GetByIdAsync(id);
        return kullanici is null ? null : DtoyaCevir(kullanici);
    }

    public async Task<UserDto> CreateAsync(UserCreateDto dto)
    {
        var kullaniciAdi = KullaniciAdiniDogrula(dto.Username);
        await KullaniciAdiBenzersizMiAsync(kullaniciAdi, haricId: null);
        var rolIdleri = await RolleriDogrulaAsync(dto.RoleIds);

        var yeni = new User
        {
            Username = kullaniciAdi,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow,
        };
        yeni.PasswordHash = _passwordHasher.HashPassword(yeni, dto.Password);

        var olusan = await _userRepository.AddAsync(yeni);
        await _userRepository.SetRolesAsync(olusan.Id, rolIdleri);

        return (await GetByIdAsync(olusan.Id))!;
    }

    public async Task<UserDto?> UpdateAsync(int id, UserUpdateDto dto)
    {
        var mevcut = await _userRepository.GetByIdAsync(id);
        if (mevcut is null)
        {
            return null;
        }

        var kullaniciAdi = KullaniciAdiniDogrula(dto.Username);
        await KullaniciAdiBenzersizMiAsync(kullaniciAdi, haricId: id);
        var rolIdleri = await RolleriDogrulaAsync(dto.RoleIds);

        // Kendi hesabını pasifleştirmek = bir sonraki girişte kapıda kalmak.
        if (KendisiMi(id) && !dto.IsActive)
        {
            throw new IsKuraliException("Kendi hesabınızı pasife alamazsınız.");
        }

        await KendiniKilitlemeAsync(id, rolIdleri, yeniDogrudanYetkiler: null);

        var guncel = new User
        {
            Id = id,
            Username = kullaniciAdi,
            IsActive = dto.IsActive,
            // Şifre boş bırakıldıysa mevcut hash aynen korunur.
            PasswordHash = mevcut.PasswordHash,
        };

        if (!string.IsNullOrWhiteSpace(dto.Password))
        {
            guncel.PasswordHash = _passwordHasher.HashPassword(guncel, dto.Password);
        }

        if (await _userRepository.UpdateAsync(guncel) is null)
        {
            return null;
        }

        await _userRepository.SetRolesAsync(id, rolIdleri);
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // Kendini silmek, yönetim panelinden kendi kendini kilitlemenin en hızlı yolu.
        if (KendisiMi(id))
        {
            throw new IsKuraliException("Kendi hesabınızı silemezsiniz.");
        }

        return await _userRepository.SoftDeleteAsync(id);
    }

    /// <summary>
    /// Kayıt olan kullanıcıyı onaylar (Ödev 10).
    ///
    /// Onaylanan hesaba ROL VERİLMİYOR. Onay yalnızca kapıyı açar: kullanıcı
    /// girebilir ama yetkisi olmadığı için hiçbir araç görünmez (Ödev 7'nin
    /// "yetkisi olmayana buton gösterme" kuralı). Rol atamak yöneticinin ayrı
    /// ve bilinçli bir adımı — onayla birlikte otomatik rol verseydik, hangi
    /// rolün verildiği görünmez bir varsayım olurdu.
    /// </summary>
    public async Task<UserDto?> OnaylaAsync(int id)
    {
        var kullanici = await _userRepository.GetByIdAsync(id);
        if (kullanici is null)
        {
            return null;
        }

        if (kullanici.IsApproved)
        {
            throw new IsKuraliException("Bu hesap zaten onaylı.");
        }

        kullanici.IsApproved = true;

        if (await _userRepository.UpdateAsync(kullanici) is null)
        {
            return null;
        }

        // İlişkileriyle yeniden okuyoruz: UpdateAsync'in döndürdüğü nesnede
        // roller yüklü değil, DTO'daki rol listesi boş çıkardı.
        var tam = await _userRepository.GetByIdAsync(id);
        return tam is null ? null : DtoyaCevir(tam);
    }

    public async Task<UserPermissionsDto?> SetPermissionsAsync(int id, SetUserPermissionsDto dto)
    {
        var matris = await _permissionService.GetForUserAsync(id);
        if (matris is null)
        {
            return null;
        }

        var istenen = await YetkileriDogrulaAsync(dto.PermissionIds);

        // ROLDEN GELENLERİ ELE (Ödev 6 / Madde 2'nin son cümlesi).
        // Arayüz zaten o kutuları kilitli gösteriyor; yine de burada eliyoruz
        // çünkü servis, kendisini çağıran arayüze güvenmez. Rolden gelen bir
        // yetkiyi user_permissions'a kopyalasaydık, rol değiştiğinde bu kopya
        // arkada kalır ve "yetkiyi rolden aldım ama kaldırınca gitmedi" derdi.
        var roldenGelenler = matris.Permissions
            .Where(y => y.FromRole)
            .Select(y => y.PermissionId)
            .ToHashSet();

        var dogrudanVerilecek = istenen.Where(yetkiId => !roldenGelenler.Contains(yetkiId)).ToList();

        await KendiniKilitlemeAsync(id, yeniRolIdleri: null, yeniDogrudanYetkiler: dogrudanVerilecek);

        await _userRepository.SetPermissionsAsync(id, dogrudanVerilecek);
        return await _permissionService.GetForUserAsync(id);
    }

    // ------------------------------------------------------------------
    //  Doğrulamalar ve koruma kuralları
    // ------------------------------------------------------------------

    private bool KendisiMi(int kullaniciId) => _currentUser.UserId == kullaniciId;

    private static string KullaniciAdiniDogrula(string kullaniciAdi)
    {
        var temiz = kullaniciAdi.Trim();
        if (temiz.Length == 0)
        {
            throw new IsKuraliException("Kullanıcı adı boş olamaz.");
        }

        return temiz;
    }

    private async Task KullaniciAdiBenzersizMiAsync(string kullaniciAdi, int? haricId)
    {
        var ayniAdli = await _userRepository.GetByUsernameAsync(kullaniciAdi);
        if (ayniAdli is not null && ayniAdli.Id != haricId)
        {
            throw new IsKuraliException($"\"{kullaniciAdi}\" kullanıcı adı zaten kullanılıyor.");
        }
    }

    private async Task<List<int>> RolleriDogrulaAsync(List<int> istenen)
    {
        var benzersiz = istenen.Distinct().ToList();
        if (benzersiz.Count == 0)
        {
            return benzersiz;
        }

        var varOlanlar = (await _roleRepository.GetAllAsync()).Select(r => r.Id).ToHashSet();
        if (benzersiz.Any(id => !varOlanlar.Contains(id)))
        {
            throw new IsKuraliException("Seçilen rollerden bazıları bulunamadı. Sayfayı yenileyip tekrar deneyin.");
        }

        return benzersiz;
    }

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

    /// <summary>
    /// "Kendi ayağına sıkma" koruması: giriş yapmış yönetici, KENDİ üzerindeki
    /// Kullanıcı Yönetimi yetkisini kaldıramaz.
    ///
    /// Olmasaydı tek yöneticili bir kurulumda yanlış bir tık, yönetim panelini
    /// kimsenin açamayacağı hâle getirir; düzeltmenin tek yolu veritabanına elle
    /// müdahale olurdu. Başka bir yönetici bu işlemi yapabilir — kural yalnızca
    /// kişinin kendi hesabı için geçerli.
    /// </summary>
    /// <param name="kullaniciId">Değişikliğin uygulanacağı kullanıcı.</param>
    /// <param name="yeniRolIdleri">Roller değişiyorsa yeni liste, değişmiyorsa null.</param>
    /// <param name="yeniDogrudanYetkiler">Doğrudan yetkiler değişiyorsa yeni liste, değişmiyorsa null.</param>
    private async Task KendiniKilitlemeAsync(
        int kullaniciId,
        IReadOnlyCollection<int>? yeniRolIdleri,
        IReadOnlyCollection<int>? yeniDogrudanYetkiler)
    {
        if (!KendisiMi(kullaniciId))
        {
            return;
        }

        var yonetimYetkisi = await _permissionRepository.GetByNameAsync(Yetkiler.KullaniciYonetimi);
        if (yonetimYetkisi is null)
        {
            return;   // yetki henüz tanımlı değilse korunacak bir şey de yok
        }

        var matris = await _permissionService.GetForUserAsync(kullaniciId);
        if (matris is null)
        {
            return;
        }

        var mevcutDurum = matris.Permissions.First(y => y.PermissionId == yonetimYetkisi.Id);
        if (!mevcutDurum.Granted)
        {
            return;   // zaten yoktu; kaybedeceği bir şey yok
        }

        // Değişmeyen taraf mevcut hâliyle kalır.
        var roldenGelecekMi = yeniRolIdleri is null
            ? mevcutDurum.FromRole
            : (await _roleRepository.GetAllAsync())
                .Where(r => r.IsActive && yeniRolIdleri.Contains(r.Id))
                .Any(r => r.RolePermissions.Any(rp => rp.PermissionId == yonetimYetkisi.Id));

        var dogrudanKalacakMi = yeniDogrudanYetkiler is null
            ? mevcutDurum.Direct
            : yeniDogrudanYetkiler.Contains(yonetimYetkisi.Id);

        if (!roldenGelecekMi && !dogrudanKalacakMi)
        {
            throw new IsKuraliException(
                "Kendi \"Kullanıcı Yönetimi\" yetkinizi kaldıramazsınız — paneli açamaz hâle gelirsiniz. " +
                "Bu değişikliği başka bir yönetici yapabilir.");
        }
    }

    // ------------------------------------------------------------------

    private static UserDto DtoyaCevir(User kullanici)
    {
        // Etkin yetki = rollerden gelenler ∪ doğrudan verilenler.
        // HashSet birleşimi tekrarları kendiliğinden eler; aynı yetki hem rolden
        // hem doğrudan geliyorsa bir kez sayılır.
        var etkin = kullanici.UserRoles
            .Where(kr => kr.Role is not null && kr.Role.IsActive)
            .SelectMany(kr => kr.Role!.RolePermissions.Select(rp => rp.PermissionId))
            .ToHashSet();

        etkin.UnionWith(kullanici.UserPermissions.Select(ky => ky.PermissionId));

        return new UserDto
        {
            Id = kullanici.Id,
            Username = kullanici.Username,
            IsActive = kullanici.IsActive,
            IsApproved = kullanici.IsApproved,
            CreatedAt = kullanici.CreatedAt,
            ModifiedDate = kullanici.ModifiedDate,
            Roles = kullanici.UserRoles
                .Where(kr => kr.Role is not null)
                .Select(kr => new RoleOzetDto { Id = kr.RoleId, Name = kr.Role!.Name })
                .OrderBy(r => r.Name)
                .ToList(),
            DirectPermissionCount = kullanici.UserPermissions.Count,
            EffectivePermissionCount = etkin.Count,
        };
    }
}
