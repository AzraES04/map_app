using NetTopologySuite.Geometries;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

/// <summary>
/// Üç geometri tipinin ortak iş mantığı.
///
/// İki tip parametresi var:
///   TEntity   → hangi tabloya yazılacağı (PointEntity / LineEntity / PolygonEntity)
///   TGeometry → o tablonun kabul ettiği NTS geometri tipi (Point / LineString / Polygon)
///
/// TGeometry sayesinde "tbl_point'e POLYGON gönderilmesi" gibi hataları çevirinin
/// tam o anında yakalayıp anlamlı bir hata mesajı üretebiliyoruz.
/// </summary>
public class GeometryService<TEntity, TGeometry> : IGeometryService<TEntity>
    where TEntity : GeometryEntityBase, new()
    where TGeometry : Geometry
{
    private readonly IGeometryRepository<TEntity> _repository;
    private readonly ICurrentUserService _currentUser;

    public GeometryService(
        IGeometryRepository<TEntity> repository,
        ICurrentUserService currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    // Ödev 5: Harita açıldığında YALNIZCA giriş yapan kullanıcının çizimleri
    // listelenir. Süzme veritabanında yapılıyor; başkasının kaydı hiç gelmiyor,
    // "çekip sonra gizlemek" gibi sızıntıya açık bir yol izlenmiyor.
    public async Task<List<GeometryDto>> GetAllAsync()
    {
        var entities = await _repository.GetAllAsync(_currentUser.RequireUserId());
        return entities.Select(MapToDto).ToList();
    }

    public async Task<GeometryDto?> GetByIdAsync(int id)
    {
        var entity = await _repository.GetByIdAsync(id, _currentUser.RequireUserId());
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<GeometryDto> CreateAsync(GeometryCreateDto dto)
    {
        // 1) WKT metnini geometriye çevir + tipini doğrula + SRID'sini 4326 yap.
        //    Hatalıysa WktFormatException fırlar, controller onu 400'e çevirir.
        var geometry = WktConverter.Read<TGeometry>(dto.Wkt);

        // 2) Entity'yi kur. new() kısıtı sayesinde generic tipten nesne üretebiliyoruz.
        var entity = new TEntity
        {
            Name = dto.Name,
            Description = dto.Description,
            ImageUrl = DogrulaGorselAdresi(dto.ImageUrl),
            Color = NormalizeRenk(dto.Color),
            Geometry = geometry,
            InsertedDate = DateTime.UtcNow,
            InsertedUserId = _currentUser.RequireUserId()   // Ödev 5: sahiplik damgası
        };

        var created = await _repository.AddAsync(entity);
        return MapToDto(created);
    }

    public async Task<GeometryDto?> UpdateAsync(int id, GeometryUpdateDto dto)
    {
        // Sahiplik süzgeciyle getiriyoruz: başkasının kaydı "bulunamadı" olur.
        var existing = await _repository.GetByIdAsync(id, _currentUser.RequireUserId());
        if (existing is null)
        {
            return null;
        }

        existing.Name = dto.Name;
        existing.Description = dto.Description;
        existing.ImageUrl = DogrulaGorselAdresi(dto.ImageUrl);
        existing.Color = NormalizeRenk(dto.Color);

        // Wkt boş gönderildiyse geometriye dokunmuyoruz — sadece ad/açıklama güncellenir.
        if (!string.IsNullOrWhiteSpace(dto.Wkt))
        {
            existing.Geometry = WktConverter.Read<TGeometry>(dto.Wkt);
        }

        var updated = await _repository.UpdateAsync(existing);
        return updated is null ? null : MapToDto(updated);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // Sahibi değilse kayıt "bulunamadı" sayılır → başkasının çizimini silemez.
        if (await _repository.GetByIdAsync(id, _currentUser.RequireUserId()) is null)
        {
            return false;
        }

        return await _repository.SoftDeleteAsync(id);
    }

    // Geri alma silinmiş kayıt üzerinde çalışır; sahiplik kontrolü repository'de
    // IgnoreQueryFilters ile yapılamadığı için burada bilinçli olarak atlanıyor.
    public Task<bool> RestoreAsync(int id) => _repository.RestoreAsync(id);

    public async Task<bool> SetActiveAsync(int id, bool isActive)
    {
        if (await _repository.GetByIdAsync(id, _currentUser.RequireUserId()) is null)
        {
            return false;
        }

        return await _repository.SetActiveAsync(id, isActive);
    }

    /// <summary>
    /// Görsel adresini doğrular. Sadece http/https kabul ediyoruz.
    ///
    /// Neden? Kullanıcıdan gelen bir metni doğrudan &lt;img src&gt; içine koyuyoruz.
    /// Doğrulamasız bırakırsak "javascript:", "data:" veya "file:" gibi şemalar
    /// gelebilir. Modern tarayıcılar img'de javascript: çalıştırmasa da, kullanıcı
    /// girdisini şema seviyesinde sınırlamak temel bir savunma alışkanlığıdır.
    /// </summary>
    private static string? DogrulaGorselAdresi(string? adres)
    {
        if (string.IsNullOrWhiteSpace(adres))
        {
            return null;   // boş metin yerine NULL: "değer yok"un doğru karşılığı
        }

        var temiz = adres.Trim();

        if (!Uri.TryCreate(temiz, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new WktFormatException(
                "Görsel adresi geçersiz. http:// veya https:// ile başlayan tam bir adres girin.");
        }

        return temiz;
    }

    /// <summary>
    /// Rengi normalleştirir: boşluk kırpılır, küçük harfe indirilir, biçim doğrulanır.
    /// DTO'daki [RegularExpression] zaten kontrol ediyor ama servis kendi başına da
    /// güvenli olmalı — iş kuralı, kendisini çağıran katmana güvenmez.
    /// </summary>
    private static string? NormalizeRenk(string? renk)
    {
        if (string.IsNullOrWhiteSpace(renk))
        {
            return null;
        }

        var temiz = renk.Trim().ToLowerInvariant();

        if (!System.Text.RegularExpressions.Regex.IsMatch(temiz, "^#[0-9a-f]{6}$"))
        {
            throw new WktFormatException("Renk #RRGGBB biçiminde olmalıdır (örn. #23606e).");
        }

        return temiz;
    }

    /// <summary>Entity → DTO. Geometri burada WKT metnine dönüşür.</summary>
    private static GeometryDto MapToDto(TEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Description = entity.Description,
        Wkt = WktConverter.Write(entity.Geometry),
        GeometryType = entity.Geometry.GeometryType,
        ImageUrl = entity.ImageUrl,
        Color = entity.Color,
        InsertedDate = entity.InsertedDate,
        InsertedUserId = entity.InsertedUserId,
        ModifiedDate = entity.ModifiedDate,
        IsActive = entity.IsActive
    };
}
