using StajProject.Business.Geo;
using StajProject.Business.Services;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Testlerde "giriş yapmış kullanıcı"yı taklit eder.
///
/// Arayüzü Business katmanında tutmamızın karşılığı burada görülüyor:
/// gerçek gerçekleme HttpContext'e bağlı, ama iş katmanı yalnızca arayüzü
/// tanıdığı için testte tek satırlık bir sahte sınıfla değiştirilebiliyor.
/// </summary>
public class FakeCurrentUserService : ICurrentUserService
{
    public FakeCurrentUserService(int? userId = 1) => UserId = userId;

    public int? UserId { get; set; }

    /// <summary>Ödev 19: simülasyonu başlatanın adı. Testlerde sabit.</summary>
    public string? UserName { get; set; } = "test-kullanici";

    public int RequireUserId()
        => UserId ?? throw new WktFormatException("Bu işlem için giriş yapmış olmanız gerekiyor.");
}
