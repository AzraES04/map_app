using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StajProject.DataAccess.Context;
using StajProject.DataAccess.GeoServer;
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
    /// <summary>
    /// "Bana GeoServer'ı değil, DOĞRUDAN veritabanını ver" demek için kullanılan
    /// hizmet anahtarı (.NET 8 keyed services).
    ///
    /// Ödev 8'den sonra <c>IGeometryRepository&lt;T&gt;</c> istendiğinde GeoServer'lı
    /// gerçekleme dönüyor. Bu doğru davranış — ama bir istisna var:
    /// <c>DatabaseSeeder</c>. Seeder uygulama AÇILIRKEN çalışır ve veritabanını
    /// hazırlar; okuması da yazması da veritabanına gitmeli. Aksi hâlde
    /// uygulamanın açılışı GeoServer'ın ayakta olmasına bağlanırdı — GeoServer
    /// kapalıyken proje hiç başlamazdı (bu, geliştirme sırasında bir kez yaşandı).
    /// </summary>
    public const string VeritabaniDeposu = "veritabani";

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
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // Ödev 6: dinamik yetkilendirme tabloları
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();

        // Ödev 7: coğrafi yetki (kullanıcı/rol bazlı çizim alanı)
        services.AddScoped<IGeoPermissionRepository, GeoPermissionRepository>();

        // Ödev 10: il sınırları — il ve bölge bazlı yetki tanımı
        services.AddScoped<IIlRepository, IlRepository>();

        // Ödev 12: hiyerarşik kategori sözlüğü. Kategori ağacı GeoServer'da
        // yayımlanmıyor (mekânsal veri değil, sözlük), okuması hep buradan.
        services.AddScoped<IPoiCategoryRepository, PoiCategoryRepository>();

        // Ödev 16: ulaşım modülü. GeoServer'a GİTMİYOR — durak listesi
        // sürükle-bırakla sürekli değişiyor ve güzergah çizgisi durakların
        // koordinatlarından istemcide üretiliyor; hazır bir WMS resmi bu iki
        // ihtiyacın ikisini de karşılayamıyor (gerekçe: IUlasimRepository).
        services.AddScoped<IUlasimRepository, UlasimRepository>();

        // Ödev 13 / Madde 1: POI artık GeoServer'da bir katman (vw_poi) ve
        // okuması oradan yapılıyor. Kayıt AddGeoServer içinde: çizim
        // tablolarındaki gibi Enabled bayrağına göre iki koldan biri seçiliyor.

        // Ödev 8: GeoServer bağlantısı ve okuma yolunun seçimi
        AddGeoServer(services, configuration);

        return services;   // zincirlenebilsin diye
    }

    /// <summary>
    /// Ödev 8 / Madde 2 — geometri okumaları hangi kaynaktan gelsin?
    ///
    /// Burası ödevin can alıcı noktası: <b>tek bir DI kaydı</b> değiştirilerek
    /// veri kaynağı PostGIS'ten GeoServer'a taşınıyor. Servis ve controller
    /// katmanlarında tek satır değişmiyor, çünkü ikisi de aynı
    /// <see cref="IGeometryRepository{TEntity}"/> arayüzünü görüyor.
    /// "Bağımlılık tersine çevirme" (dependency inversion) ilkesinin somut
    /// faydası tam olarak bu.
    /// </summary>
    private static void AddGeoServer(IServiceCollection services, IConfiguration configuration)
    {
        // Ayarlar okunmazsa (bölüm hiç yoksa) sınıfın kendi varsayılanları geçerli:
        // localhost:8080, workspace "staj", admin/geoserver.
        var ayarlar = configuration.GetSection("GeoServer").Get<GeoServerSettings>()
                      ?? new GeoServerSettings();

        services.AddSingleton(ayarlar);

        // Typed client: HttpClient'i IHttpClientFactory yönetir, biz sadece
        // GeoServerClient'i isteriz. Enabled kapalı olsa bile kaydediyoruz;
        // durum ekranı ("GeoServer bağlı mı?") ve WMS vekili buna ihtiyaç duyuyor.
        services.AddHttpClient<IGeoServerClient, GeoServerClient>();

        // Yazma yolu HER İKİ durumda da EF Core'dur. Somut tipi ayrıca
        // kaydediyoruz ki GeoServerGeometryRepository onu sarmalayabilsin.
        services.AddScoped<GeometryRepository<PointEntity>>();
        services.AddScoped<GeometryRepository<LineEntity>>();
        services.AddScoped<GeometryRepository<PolygonEntity>>();
        services.AddScoped<PoiRepository>();

        // Aynı depoların "veritabani" anahtarlı hâli — DatabaseSeeder bunu istiyor.
        // Her iki kolda da kayıtlı: seeder'ın davranışı GeoServer ayarından
        // BAĞIMSIZ olmalı.
        VeritabaniDeposuEkle<PointEntity>(services);
        VeritabaniDeposuEkle<LineEntity>(services);
        VeritabaniDeposuEkle<PolygonEntity>(services);

        // POI için de aynısı (Ödev 13). Seeder açılışta örnek POI'leri ekliyor
        // ve "tablo boş mu?" diye okuyor; o okuma GeoServer'a giderse
        // uygulamanın açılışı GeoServer'a bağlanır — üstelik seed sırasında
        // katman henüz yayımlanmamış bile olabilir.
        services.AddKeyedScoped<IPoiRepository>(
            VeritabaniDeposu,
            (sp, _) => sp.GetRequiredService<PoiRepository>());

        if (ayarlar.Enabled)
        {
            // ---- Ödevin istediği mimari (varsayılan) ----
            // Listeleme ve analiz GeoServer'ın WFS servisine gider.
            //
            // Fabrika (lambda) ile kaydediyoruz çünkü GeoServerGeometryRepository
            // yapıcısında IGeometryRepository<T> istiyor. Kısa yolu kullansaydık
            // (AddScoped<IGeometryRepository<T>, GeoServerGeometryRepository<T>>)
            // kayıt kendi kendini çözmeye çalışır, sonsuz döngüye girerdi.
            // Burada sarmalanacak depoyu SOMUT tiple veriyoruz.
            GeoServerRepositoryEkle<PointEntity>(services);
            GeoServerRepositoryEkle<LineEntity>(services);
            GeoServerRepositoryEkle<PolygonEntity>(services);

            // Ödev 13 / Madde 1: POI listeleme ve arama da WFS'ten.
            services.AddScoped<IPoiRepository>(sp =>
                new GeoServerPoiRepository(
                    sp.GetRequiredService<IGeoServerClient>(),
                    sp.GetRequiredService<PoiRepository>()));

            services.AddScoped<IAnalysisRepository, GeoServerAnalysisRepository>();
        }
        else
        {
            services.AddScoped<IPoiRepository, PoiRepository>();

            // ---- GeoServer:Enabled = false ----
            // Ödev 7'deki davranış: doğrudan EF Core + PostGIS.
            // GeoServer kurulu olmayan bir makinede çalışabilmek için.
            services.AddScoped<IGeometryRepository<PointEntity>, GeometryRepository<PointEntity>>();
            services.AddScoped<IGeometryRepository<LineEntity>, GeometryRepository<LineEntity>>();
            services.AddScoped<IGeometryRepository<PolygonEntity>, GeometryRepository<PolygonEntity>>();

            services.AddScoped<IAnalysisRepository, AnalysisRepository>();
        }
    }

    /// <summary>
    /// EF Core'lu depoyu <see cref="VeritabaniDeposu"/> anahtarıyla da kaydeder.
    /// </summary>
    private static void VeritabaniDeposuEkle<TEntity>(IServiceCollection services)
        where TEntity : GeometryEntityBase, new()
    {
        services.AddKeyedScoped<IGeometryRepository<TEntity>>(
            VeritabaniDeposu,
            (sp, _) => sp.GetRequiredService<GeometryRepository<TEntity>>());
    }

    /// <summary>
    /// Bir geometri tipi için "okuma GeoServer'dan, yazma EF Core'dan" kaydını yapar.
    /// </summary>
    private static void GeoServerRepositoryEkle<TEntity>(IServiceCollection services)
        where TEntity : GeometryEntityBase, new()
    {
        services.AddScoped<IGeometryRepository<TEntity>>(sp =>
            new GeoServerGeometryRepository<TEntity>(
                sp.GetRequiredService<IGeoServerClient>(),
                sp.GetRequiredService<GeometryRepository<TEntity>>()));
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
