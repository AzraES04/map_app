using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using StajProject.Business.Auth;
using StajProject.Business.Services;
using StajProject.Entities;

namespace StajProject.Business;

/// <summary>
/// İş katmanının kendi DI kayıtları.
/// Program.cs artık hangi servisin hangi sınıfla karşılandığını bilmiyor;
/// sadece "iş katmanını ekle" diyor.
/// </summary>
public static class BusinessRegistration
{
    public static IServiceCollection AddBusinessLayer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // appsettings.json'daki "Jwt" bölümü → JwtSettings nesnesi
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<IDatabaseSeeder, DatabaseSeeder>();
        services.AddScoped<IAnalysisService, AnalysisService>();

        // Ödev 14: konum analizi — alan seçimi + ağırlıklı kriterlerle ısı haritası
        services.AddScoped<IKonumAnaliziService, KonumAnaliziService>();

        // Ödev 6: yönetim paneli ve dinamik yetkilendirme
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IUserAdminService, UserAdminService>();

        // Ödev 7: coğrafi yetki — hem yönetim hem çizim sırasındaki alan kontrolü
        services.AddScoped<IGeoPermissionService, GeoPermissionService>();

        // Ödev 10: il / bölge referans verisi
        services.AddScoped<IIlService, IlService>();

        // Ödev 12: POI yönetimi ve hiyerarşik kategori sözlüğü
        services.AddScoped<IPoiService, PoiService>();
        services.AddScoped<IPoiCategoryService, PoiCategoryService>();

        // Ödev 16: akıllı ulaşım modülü — güzergah ve durak yönetimi
        services.AddScoped<IUlasimService, UlasimService>();

        // Ödev 13 iyileştirmesi: POI stilleri kategori tablosundan üretiliyor.
        // PoiCategoryService bunu isteğe bağlı bağımlılık olarak alıyor —
        // kayıtlı olduğu için üretimde her zaman geliyor, testlerde
        // verilmediğinde kategori yönetimi GeoServer'sız çalışmaya devam ediyor.
        services.AddScoped<IPoiStyleService, PoiStyleService>();

        // Geometri servisleri: aynı generic sınıf, üç farklı tip çifti.
        // Controller "IGeometryService<PointEntity>" isteyince konteyner
        // GeometryService<PointEntity, Point> üretir.
        services.AddScoped<IGeometryService<PointEntity>, GeometryService<PointEntity, Point>>();
        services.AddScoped<IGeometryService<LineEntity>, GeometryService<LineEntity, LineString>>();
        services.AddScoped<IGeometryService<PolygonEntity>, GeometryService<PolygonEntity, Polygon>>();

        return services;
    }

    /// <summary>Başlangıç verisini oluşturur (iş kuralı Business katmanında yaşıyor).</summary>
    public static async Task SeedDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IDatabaseSeeder>();
        await seeder.SeedAsync();
    }
}
