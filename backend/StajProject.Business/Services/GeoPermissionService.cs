using NetTopologySuite.Geometries;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Validation;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

public class GeoPermissionService : IGeoPermissionService
{
    private readonly IGeoPermissionRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ICurrentUserService _currentUser;

    public GeoPermissionService(
        IGeoPermissionRepository repository,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ICurrentUserService currentUser)
    {
        _repository = repository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _currentUser = currentUser;
    }

    // ------------------------------------------------------------------
    //  Yönetim tarafı
    // ------------------------------------------------------------------

    public async Task<List<GeoPermissionDto>> GetAllAsync()
        => (await _repository.GetAllAsync()).Select(DtoyaCevir).ToList();

    public async Task<List<GeoPermissionDto>> GetForUserAsync(int userId)
        => (await _repository.GetByUserAsync(userId)).Select(DtoyaCevir).ToList();

    public async Task<List<GeoPermissionDto>> GetForRoleAsync(int roleId)
        => (await _repository.GetByRoleAsync(roleId)).Select(DtoyaCevir).ToList();

    public async Task<GeoPermissionDto> CreateAsync(GeoPermissionCreateDto dto)
    {
        // YA kullanıcı YA rol. Veritabanında CHECK kısıtı da var ama oradan
        // gelen hata kullanıcıya gösterilebilir bir cümle değil.
        if ((dto.UserId is null) == (dto.RoleId is null))
        {
            throw new IsKuraliException("Alan ya bir kullanıcıya ya da bir role tanımlanmalıdır (ikisi birden olamaz).");
        }

        var ad = dto.Name.Trim();
        if (ad.Length == 0)
        {
            throw new IsKuraliException("Alan adı boş olamaz.");
        }

        // Sahip gerçekten var mı? Yoksa yabancı anahtar hatası 500'e dönüşürdü.
        if (dto.UserId is not null && await _userRepository.GetByIdAsync(dto.UserId.Value) is null)
        {
            throw new IsKuraliException("Alan tanımlanacak kullanıcı bulunamadı.");
        }

        if (dto.RoleId is not null && await _roleRepository.GetByIdAsync(dto.RoleId.Value) is null)
        {
            throw new IsKuraliException("Alan tanımlanacak rol bulunamadı.");
        }

        // WKT → Polygon. Tip uyuşmazlığı ve bozuk metin burada yakalanır;
        // ayrıca SRID 4326'ya sabitlenir (bkz. WktConverter).
        var alan = WktConverter.Read<Polygon>(dto.Wkt);

        var kayit = await _repository.AddAsync(new GeoPermission
        {
            Name = ad,
            UserId = dto.UserId,
            RoleId = dto.RoleId,
            Geom = alan,
            InsertedDate = DateTime.UtcNow,
            InsertedUserId = _currentUser.UserId,
        });

        // Sahip adlarının dolu gelmesi için ilişkileriyle yeniden okuyoruz.
        var tam = await _repository.GetByIdAsync(kayit.Id);
        return DtoyaCevir(tam ?? kayit);
    }

    public Task<bool> DeleteAsync(int id) => _repository.SoftDeleteAsync(id);

    // ------------------------------------------------------------------
    //  Uygulama tarafı — asıl kural
    // ------------------------------------------------------------------

    public async Task<CalismaAlaniDto> GetCalismaAlanimAsync()
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return new CalismaAlaniDto { Kisitli = false };
        }

        var alanlar = await _repository.GetEffectiveForUserAsync(userId.Value);

        return new CalismaAlaniDto
        {
            Kisitli = alanlar.Count > 0,
            Alanlar = alanlar.Select(a => new CalismaAlaniParcasiDto
            {
                Id = a.Id,
                Name = a.Name,
                Wkt = WktConverter.Write(a.Geom),
                Kaynak = a.RoleId is not null ? "rol" : "doğrudan",
            }).ToList(),
        };
    }

    public async Task DogrulaAsync(int userId, Geometry geometri)
    {
        var alanlar = await _repository.GetEffectiveForUserAsync(userId);

        // KISIT TANIMLANMAMIŞSA SERBEST.
        // Tersini seçseydik (tanım yoksa hiçbir yere çizemez), coğrafi yetki
        // modülü açıldığı anda bütün mevcut kullanıcılar çizim yapamaz hâle
        // gelirdi. Kısıtlama, konması gereken bir kural olarak tasarlandı.
        if (alanlar.Count == 0)
        {
            return;
        }

        // Alanların BİRLEŞİMİ geçerli: iki bitişik alan tanımlıysa, tam
        // sınırlarından geçen bir çizgi de kabul edilmeli. Tek tek bakıp
        // "herhangi biri kapsıyor mu" deseydik böyle bir çizim reddedilirdi.
        Geometry izinliAlan = alanlar[0].Geom;
        for (var i = 1; i < alanlar.Count; i++)
        {
            izinliAlan = izinliAlan.Union(alanlar[i].Geom);
        }

        // Covers, Contains'ten farklı olarak SINIRI da içeride sayar.
        // Kullanıcı alanın tam kenarına nokta koyduğunda reddetmek,
        // açıklanması zor ve haksız bir davranış olurdu.
        if (izinliAlan.Covers(geometri))
        {
            return;
        }

        var adlar = string.Join(", ", alanlar.Select(a => $"\"{a.Name}\""));
        throw new IsKuraliException(
            $"Çizim, size tanımlı alanın dışında kalıyor. Yalnızca {adlar} " +
            "alanı içine çizim yapabilirsiniz.");
    }

    // ------------------------------------------------------------------

    private static GeoPermissionDto DtoyaCevir(GeoPermission kayit) => new()
    {
        Id = kayit.Id,
        Name = kayit.Name,
        UserId = kayit.UserId,
        Username = kayit.User?.Username,
        RoleId = kayit.RoleId,
        RoleName = kayit.Role?.Name,
        Wkt = WktConverter.Write(kayit.Geom),
        InsertedDate = kayit.InsertedDate,
        IsActive = kayit.IsActive,
    };
}
