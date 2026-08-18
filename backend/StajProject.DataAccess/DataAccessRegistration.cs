using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StajProject.DataAccess.Context;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.DataAccess;

/// <summary>
/// Veri erişim katmanının kendi DI kayıtları.
///
/// Katmanlı mimarinin gereği: her katman NE'ye ihtiyaç duyduğunu kendisi bilir.
/// Program.cs'in DbContext'ten, repository sınıflarından veya bağlantı
/// cümlesinden haberi olmasına gerek yok — sadece "veri katmanını ekle" der.
/// Yarın repository eklendiğinde Program.cs'e dokunulmayacak.
/// </summary>
public static class DataAccessRegistration
{
    public static IServiceCollection AddDataAccessLayer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // PostgreSQL + PostGIS (NetTopologySuite)
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.UseNetTopologySuite()));

        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        // Ödev 6: dinamik yetkilendirme tabloları
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();

        // Ödev 7: coğrafi yetki (kullanıcı/rol bazlı çizim alanı)
        services.AddScoped<IGeoPermissionRepository, GeoPermissionRepository>();

        // Geometri repository'leri: tek generic sınıf, üç tip argümanı
        services.AddScoped<IGeometryRepository<PointEntity>, GeometryRepository<PointEntity>>();
        services.AddScoped<IGeometryRepository<LineEntity>, GeometryRepository<LineEntity>>();
        services.AddScoped<IGeometryRepository<PolygonEntity>, GeometryRepository<PolygonEntity>>();

        services.AddScoped<IAnalysisRepository, AnalysisRepository>();

        return services;   // zincirlenebilsin diye
    }

    /// <summary>
    /// Bekleyen migration'ları uygular. Şema yönetimi veri katmanının işidir,
    /// bu yüzden Program.cs'te DbContext'e elle erişmek yerine burada duruyor.
    /// </summary>
    public static void MigrateDatabase(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
    }
}
