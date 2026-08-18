using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StajProject.Entities;

namespace StajProject.DataAccess.Context;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Location> Locations => Set<Location>();
    public DbSet<User> Users => Set<User>();

    // Ödev 3 / Görev 2: her çizim tipi kendi tablosuna
    public DbSet<PointEntity> Points => Set<PointEntity>();
    public DbSet<LineEntity> Lines => Set<LineEntity>();
    public DbSet<PolygonEntity> Polygons => Set<PolygonEntity>();

    // Ödev 6 / Madde 2: dinamik yetkilendirme
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();

    // ---------- Ödev 3 / Görev 1: ModifiedDate otomatik güncelleme ----------
    // Her kayıt işleminden ÖNCE devreye girer. Böylece "modified_date yazmayı unuttum"
    // diye bir durum kalmaz; kural tek yerde, merkezî olarak uygulanır.
    //
    // NEDEN parametresiz SaveChanges() değil de bool alan aşırı yükleme?
    // DbContext içinde zincir şöyle: SaveChanges() -> SaveChanges(true)
    //                                SaveChangesAsync(ct) -> SaveChangesAsync(true, ct)
    // Yani BÜTÜN yollar bu iki metotta birleşiyor. Parametresizleri override etseydik,
    // birisi doğrudan db.SaveChanges(false) çağırdığında kuralımız sessizce atlanırdı.

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// ChangeTracker: EF'in "bu istekte hangi nesne ne durumda?" defteri.
    /// Added / Modified / Deleted / Unchanged durumlarını burada okuyabiliyoruz.
    /// </summary>
    private void ApplyAuditRules()
    {
        // IAuditableEntity uygulayan HER entity: User, PointEntity, LineEntity, PolygonEntity...
        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            // Sadece GÜNCELLENEN kayıtlar; yeni eklenen kaydın modified_date'i null kalmalı.
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedDate = DateTime.UtcNow;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PostGIS eklentisini EF Core'a tanıt
        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.Entity<Location>(entity =>
        {
            entity.ToTable("locations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(1000);
            entity.Property(e => e.Geom).HasColumnName("geom").HasColumnType("geometry(Point, 4326)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            // ---------- Ödev 3 / Görev 1: durum takibi kolonları ----------

            entity.Property(e => e.IsDeleted)
                  .HasColumnName("is_deleted")
                  .HasDefaultValue(false);

            entity.Property(e => e.IsActive)
                  .HasColumnName("is_active")
                  .HasDefaultValue(true)
                  // Sentinel: EF, CLR varsayılanına eşit değerleri INSERT'e hiç yazmaz, DB default'u devreye girer.
                  // bool'un CLR varsayılanı false olduğu için, sentinel'i true'ya çekmezsek
                  // "IsActive = false" olan bir kullanıcı sessizce true kaydedilirdi. (EF Core 8 özelliği)
                  .HasSentinel(true);

            entity.Property(e => e.ModifiedDate)
                  .HasColumnName("modified_date");

            // Username unique olmalı — AMA soft delete ile birlikte düşün:
            // "ayse" silinip is_deleted=true olduysa, yeni bir "ayse" açılabilmeli.
            // Bu yüzden index'i kısmi (partial) yapıyoruz: sadece silinmemiş satırlar için benzersizlik.
            entity.HasIndex(e => e.Username)
                  .IsUnique()
                  .HasFilter("is_deleted = false");

            // Global query filter: bundan sonra Users üzerinden yapılan HER sorguya
            // EF otomatik olarak bu WHERE koşulunu ekler. Silinmiş kullanıcıyı görmek istersek
            // bilinçli olarak .IgnoreQueryFilters() dememiz gerekir.
            entity.HasQueryFilter(u => !u.IsDeleted);
        });

        // ---------- Ödev 3 / Görev 2-3: geometri tabloları ----------
        //
        // Üç tablo da aynı ortak kolonlara sahip; tek fark geometri tipinde.
        // Ortak kısmı aşağıdaki yerel fonksiyonda topluyoruz (kod tekrarı yok),
        // geometri kolonunu ise her tabloda ayrı yazıyoruz çünkü PostGIS tipi farklı.

        modelBuilder.Entity<PointEntity>(entity =>
        {
            ConfigureGeometryTable(entity, "tbl_point");
            entity.Property(e => e.Geom)
                  .HasColumnName("geom")
                  .HasColumnType("geometry(Point, 4326)")
                  .IsRequired();
            entity.HasIndex(e => e.Geom).HasMethod("gist");
        });

        modelBuilder.Entity<LineEntity>(entity =>
        {
            ConfigureGeometryTable(entity, "tbl_line");
            entity.Property(e => e.Geom)
                  .HasColumnName("geom")
                  .HasColumnType("geometry(LineString, 4326)")
                  .IsRequired();
            entity.HasIndex(e => e.Geom).HasMethod("gist");
        });

        modelBuilder.Entity<PolygonEntity>(entity =>
        {
            ConfigureGeometryTable(entity, "tbl_polygon");
            entity.Property(e => e.Geom)
                  .HasColumnName("geom")
                  .HasColumnType("geometry(Polygon, 4326)")
                  .IsRequired();
            entity.HasIndex(e => e.Geom).HasMethod("gist");
        });

        ConfigureAuthorization(modelBuilder);
    }

    // ---------- Ödev 6 / Madde 2: rol / yetki tabloları ----------
    //
    // Beş tablo: roles, permissions ve aralarındaki üç bağlantı tablosu.
    // Bağlantı tablolarında BİLEŞİK anahtar kullanıyoruz (örn. user_id + role_id):
    // aynı çiftin iki kez eklenmesi böylece veritabanı seviyesinde imkânsız olur.
    private static void ConfigureAuthorization(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(entity =>
        {
            ConfigureLookupTable(entity, "roles");
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            ConfigureLookupTable(entity, "permissions");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(e => new { e.UserId, e.RoleId });
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.InsertedDate).HasColumnName("inserted_date");

            // Cascade: kullanıcı/rol satırı FİZİKSEL olarak silinirse atama da gitsin.
            // Uygulamada soft delete kullanıyoruz, yani bu yol normalde çalışmaz;
            // yine de veritabanını tutarsız satırlarla baş başa bırakmıyoruz.
            entity.HasOne(e => e.User).WithMany(u => u.UserRoles)
                  .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Role).WithMany(r => r.UserRoles)
                  .HasForeignKey(e => e.RoleId).OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.RoleId);   // "bu rolde kaç kullanıcı var?" sorgusu için

            // Sahibi (kullanıcı veya rol) soft delete edilmişse atama da görünmesin.
            // Bu filtre olmasaydı silinmiş bir rol, kullanıcının yetkilerini
            // beslemeye devam ederdi.
            entity.HasQueryFilter(e => !e.User!.IsDeleted && !e.Role!.IsDeleted);
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(e => new { e.RoleId, e.PermissionId });
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.PermissionId).HasColumnName("permission_id");
            entity.Property(e => e.InsertedDate).HasColumnName("inserted_date");

            entity.HasOne(e => e.Role).WithMany(r => r.RolePermissions)
                  .HasForeignKey(e => e.RoleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Permission).WithMany(p => p.RolePermissions)
                  .HasForeignKey(e => e.PermissionId).OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.PermissionId);

            entity.HasQueryFilter(e => !e.Role!.IsDeleted && !e.Permission!.IsDeleted);
        });

        modelBuilder.Entity<UserPermission>(entity =>
        {
            entity.ToTable("user_permissions");
            entity.HasKey(e => new { e.UserId, e.PermissionId });
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.PermissionId).HasColumnName("permission_id");
            entity.Property(e => e.InsertedDate).HasColumnName("inserted_date");

            entity.HasOne(e => e.User).WithMany(u => u.UserPermissions)
                  .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Permission).WithMany(p => p.UserPermissions)
                  .HasForeignKey(e => e.PermissionId).OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.PermissionId);

            entity.HasQueryFilter(e => !e.User!.IsDeleted && !e.Permission!.IsDeleted);
        });
    }

    /// <summary>
    /// roles ve permissions tabloları birebir aynı iskelete sahip
    /// (id / name / description + durum kolonları), o yüzden tek yerden kuruluyor.
    /// users tablosundaki desenin aynısı: kısmi benzersiz index + soft delete filtresi.
    /// </summary>
    private static void ConfigureLookupTable<TEntity>(EntityTypeBuilder<TEntity> entity, string tableName)
        where TEntity : AuthorizationEntityBase
    {
        entity.ToTable(tableName);
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
        entity.Property(e => e.InsertedDate).HasColumnName("inserted_date");

        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true).HasSentinel(true);
        entity.Property(e => e.ModifiedDate).HasColumnName("modified_date");

        // users tablosundaki mantığın aynısı: silinen "Editör" rolünün adı
        // yeniden kullanılabilsin diye benzersizlik yalnızca yaşayan satırlarda.
        entity.HasIndex(e => e.Name).IsUnique().HasFilter("is_deleted = false");

        entity.HasQueryFilter(e => !e.IsDeleted);
    }

    /// <summary>
    /// Üç geometri tablosunun ORTAK yapılandırması.
    /// Generic olduğu için PointEntity/LineEntity/PolygonEntity üçünde de çalışır.
    /// </summary>
    private static void ConfigureGeometryTable<TEntity>(EntityTypeBuilder<TEntity> entity, string tableName)
        where TEntity : GeometryEntityBase
    {
        entity.ToTable(tableName);
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(1000);
        entity.Property(e => e.ImageUrl).HasColumnName("image_url").HasMaxLength(500);
        entity.Property(e => e.Color).HasColumnName("color").HasMaxLength(7);   // "#RRGGBB"
        entity.Property(e => e.InsertedDate).HasColumnName("inserted_date");
        entity.Property(e => e.InsertedUserId).HasColumnName("inserted_user_id");

        // Sahibe göre süzme sorgusu bu index'i kullanır (Ödev 5)
        entity.HasIndex(e => e.InsertedUserId);

        // Durum takibi kolonları — users tablosundakiyle birebir aynı desen
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true).HasSentinel(true);
        entity.Property(e => e.ModifiedDate).HasColumnName("modified_date");

        entity.HasQueryFilter(e => !e.IsDeleted);
    }
}
