using NetTopologySuite.Geometries;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Validation;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;
using Bolgeler = StajProject.Business.Geo.Bolgeler;

namespace StajProject.Business.Services;

public class GeoPermissionService : IGeoPermissionService
{
    private readonly IGeoPermissionRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IIlRepository _ilRepository;
    private readonly IGeometryRepository<PolygonEntity> _poligonRepository;
    private readonly ICurrentUserService _currentUser;

    public GeoPermissionService(
        IGeoPermissionRepository repository,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IIlRepository ilRepository,
        IGeometryRepository<PolygonEntity> poligonRepository,
        ICurrentUserService currentUser)
    {
        _repository = repository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _ilRepository = ilRepository;
        _poligonRepository = poligonRepository;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Yetki alanı olarak seçilebilecek KAYITLI poligonlar (Ödev 11).
    ///
    /// Sahiplik süzgeci UYGULANMIYOR (userId: null): yönetici, yetkiyi
    /// tanımlarken sistemdeki herhangi bir alanı referans alabilmeli.
    /// Kendi çizimleriyle sınırlasaydık, başka bir yöneticinin çizdiği
    /// "İstanbul metropol alanı" kullanılamazdı.
    /// </summary>
    public Task<List<GeometryDto>> GetSecilebilirAlanlarAsync()
        => _poligonRepository.GetAllAsync().ContinueWith(t => t.Result
            .Select(p => new GeometryDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Wkt = WktConverter.Write(p.Geometry),
                GeometryType = p.Geometry.GeometryType,
                Color = p.Color,
                InsertedDate = p.InsertedDate,
                InsertedUserId = p.InsertedUserId,
                IsActive = p.IsActive,
            })
            .ToList());

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

        // Ödev 10: alan üç yoldan tanımlanabiliyor. Hangisi geldiyse ondan
        // geometriyi üret; sonrası her üçü için aynı.
        var alan = await AlanGeometrisiAsync(dto);

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

    /// <summary>
    /// İsteğe göre alan geometrisini üretir (Ödev 10).
    ///
    /// Üç yol var ve YALNIZCA BİRİ dolu olmalı. "Hepsini kabul edip birleştirsek
    /// olmaz mıydı?" — olurdu ama kullanıcı ne tanımladığını göremezdi:
    /// haritada çizim yapıp sonra bir de il seçen yöneticinin beklentisi
    /// belirsiz olurdu. Belirsizliği kabul etmek yerine reddediyoruz.
    /// </summary>
    private async Task<Geometry> AlanGeometrisiAsync(GeoPermissionCreateDto dto)
    {
        var cizim = !string.IsNullOrWhiteSpace(dto.Wkt);
        var ilSecimi = dto.IlPlakalari is { Count: > 0 };
        var bolgeSecimi = dto.Bolgeler is { Count: > 0 };
        var poligonSecimi = dto.PoligonIdleri is { Count: > 0 };

        var secilenYolSayisi = (cizim ? 1 : 0) + (ilSecimi ? 1 : 0)
                             + (bolgeSecimi ? 1 : 0) + (poligonSecimi ? 1 : 0);

        if (secilenYolSayisi == 0)
        {
            throw new IsKuraliException(
                "Alan tanımlanmadı. Haritaya bir alan çizin; il, bölge ya da kayıtlı alan seçin.");
        }

        if (secilenYolSayisi > 1)
        {
            throw new IsKuraliException(
                "Alan tek bir yolla tanımlanmalı: çizim, il, bölge ya da kayıtlı alan.");
        }

        if (cizim)
        {
            // Elle çizimde tip kısıtı hâlâ geçerli: kullanıcı bir ALAN çiziyor,
            // çizgi ya da nokta değil. WktConverter tipi doğrular ve SRID'yi
            // 4326'ya sabitler.
            return WktConverter.Read<Polygon>(dto.Wkt);
        }

        if (ilSecimi)
        {
            // Distinct: arayüz aynı ili iki kez göndermiş olsa bile birleşim
            // aynı; ama gereksiz geometri taşımanın anlamı yok.
            var plakalar = dto.IlPlakalari!.Distinct().ToList();

            return await _ilRepository.IllerinBirlesimiAsync(plakalar)
                ?? throw new IsKuraliException(
                    "Seçilen iller bulunamadı. İl sınırları yüklenmiş mi?");
        }

        if (bolgeSecimi)
        {
            return await BolgeleriBirlestirAsync(dto.Bolgeler!);
        }

        return await PoligonlariBirlestirAsync(dto.PoligonIdleri!);
    }

    /// <summary>
    /// Seçilen bölgelerin illerini tek bir alanda birleştirir (Ödev 11 — çoklu bölge).
    ///
    /// Bölgeler AYRIK olduğu için sonuç neredeyse her zaman MultiPolygon olur;
    /// <c>geo_permissions.geom</c> bu yüzden <c>geometry(Geometry, 4326)</c>.
    /// </summary>
    private async Task<Geometry> BolgeleriBirlestirAsync(List<string> istenen)
    {
        var adlar = new List<string>();

        foreach (var ham in istenen.Select(b => b.Trim()).Where(b => b.Length > 0).Distinct())
        {
            if (!Bolgeler.Gecerli(ham))
            {
                throw new IsKuraliException(
                    $"Bilinmeyen bölge: \"{ham}\". Geçerli bölgeler: {string.Join(", ", Bolgeler.Tumu)}.");
            }

            adlar.Add(Bolgeler.Normalize(ham));
        }

        if (adlar.Count == 0)
        {
            throw new IsKuraliException("Hiç bölge seçilmedi.");
        }

        Geometry? birlesim = null;

        foreach (var ad in adlar.Distinct())
        {
            var bolge = await _ilRepository.BolgeGeometrisiAsync(ad)
                ?? throw new IsKuraliException(
                    $"\"{ad}\" bölgesinde il bulunamadı. İl sınırları yüklenmiş mi?");

            birlesim = birlesim is null ? bolge : birlesim.Union(bolge);
        }

        birlesim!.SRID = WktConverter.Srid;
        return birlesim;
    }

    /// <summary>
    /// Seçilen KAYITLI poligonları birleştirir (Ödev 11).
    ///
    /// Geometri veritabanındaki kayıttan okunuyor, istemcinin gönderdiği
    /// WKT'den değil: yönetici listeden bir alan seçtiğinde kaydedilen sınır,
    /// ekranda gördüğü kaydın sınırının BİREBİR aynısı olmalı.
    /// </summary>
    private async Task<Geometry> PoligonlariBirlestirAsync(List<int> idler)
    {
        var istenen = idler.Distinct().ToHashSet();

        // Sahiplik süzgeci yok: yönetici herhangi bir kayıtlı alanı referans alabilir.
        var poligonlar = (await _poligonRepository.GetAllAsync())
            .Where(p => istenen.Contains(p.Id))
            .ToList();

        if (poligonlar.Count == 0)
        {
            throw new IsKuraliException("Seçilen kayıtlı alanlar bulunamadı.");
        }

        Geometry birlesim = poligonlar[0].Geometry;
        for (var i = 1; i < poligonlar.Count; i++)
        {
            birlesim = birlesim.Union(poligonlar[i].Geometry);
        }

        birlesim.SRID = WktConverter.Srid;
        return birlesim;
    }

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
