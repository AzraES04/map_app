using NetTopologySuite.Geometries;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.Business.Validation;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Testlerde coğrafi yetkiyi taklit eder (Ödev 7 / Madde 2).
///
/// Varsayılan davranış SERBEST: eski testler alan kısıtı diye bir şey
/// olmadığını varsayarak yazıldı, öyle de kalmalı. Kısıtı incelemek isteyen
/// test <see cref="IzinliAlan"/> alanına bir poligon koyar.
/// </summary>
public class FakeGeoPermissionService : IGeoPermissionService
{
    /// <summary>null ise kısıt yok; doluysa geometriler bu alanın içinde kalmalı.</summary>
    public Geometry? IzinliAlan { get; set; }

    /// <summary>Kaç kez doğrulama çağrıldı? "Kontrol gerçekten yapılıyor mu?" testi için.</summary>
    public int DogrulamaSayisi { get; private set; }

    public Task DogrulaAsync(int userId, Geometry geometri)
    {
        DogrulamaSayisi++;

        if (IzinliAlan is null || IzinliAlan.Covers(geometri))
        {
            return Task.CompletedTask;
        }

        throw new IsKuraliException("Çizim, size tanımlı alanın dışında kalıyor.");
    }

    // ---- Yönetim tarafı testlerde kullanılmıyor ----
    public Task<List<GeoPermissionDto>> GetAllAsync() => Task.FromResult(new List<GeoPermissionDto>());
    public Task<List<GeoPermissionDto>> GetForUserAsync(int userId) => GetAllAsync();
    public Task<List<GeoPermissionDto>> GetForRoleAsync(int roleId) => GetAllAsync();
    public Task<GeoPermissionDto> CreateAsync(GeoPermissionCreateDto dto) => throw new NotSupportedException();
    public Task<List<GeometryDto>> GetSecilebilirAlanlarAsync() => Task.FromResult(new List<GeometryDto>());
    public Task<bool> DeleteAsync(int id) => Task.FromResult(false);
    public Task<CalismaAlaniDto> GetCalismaAlanimAsync() => Task.FromResult(new CalismaAlaniDto());
}
