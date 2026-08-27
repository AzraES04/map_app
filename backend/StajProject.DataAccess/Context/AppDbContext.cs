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

    /// <summary>Eksik 5: yenileme anahtarları (kısa ömürlü JWT'nin arkasındaki oturum).</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

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

    // Ödev 7 / Madde 2: coğrafi yetki (kullanıcı/rol bazlı çizim alanı)
    public DbSet<GeoPermission> GeoPermissions => Set<GeoPermission>();

    // Ödev 10: 81 ilin sınırları — il ve bölge bazlı yetki tanımının kaynağı
    public DbSet<Il> Iller => Set<Il>();

    // Ödev 12: POI (ilgi noktası) ve hiyerarşik kategori sözlüğü
    public DbSet<PoiCategory> PoiKategorileri => Set<PoiCategory>();
    public DbSet<Poi> Poiler => Set<Poi>();

    // Ödev 16: akıllı ulaşım modülü — güzergah 1 ─< N durak
    public DbSet<Guzergah> Guzergahlar => Set<Guzergah>();
    public DbSet<Durak> Duraklar => Set<Durak>();

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

            // Ödev 10: kayıt olan kullanıcı yönetici onayı bekler.
            // Varsayılan TRUE: mevcut kullanıcılar ve panelden açılanlar
            // onaylı sayılır; yalnızca kayıt ucu bunu bilerek false yazar.
            entity.Property(e => e.IsApproved)
                  .HasColumnName("is_approved")
                  .HasDefaultValue(true)
                  // IsActive'deki sentinel gerekçesinin aynısı: bool'un CLR
                  // varsayılanı false olduğu için, sentinel true'ya çekilmezse
                  // "IsApproved = false" olan kayıt sessizce true yazılırdı.
                  .HasSentinel(true);

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

        // ---------- Ödev 10: il sınırları ----------
        modelBuilder.Entity<Il>(entity =>
        {
            entity.ToTable("iller");
            entity.HasKey(e => e.Id);

            // ValueGeneratedNever: id PLAKA kodudur, veritabanı üretmez.
            // Bunu söylemezsek EF kolonu identity yapar ve seed'in yazdığı
            // plaka numaraları yok sayılır.
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

            entity.Property(e => e.Ad).HasColumnName("ad").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Bolge).HasColumnName("bolge").HasMaxLength(50).IsRequired();

            // Geometry (Polygon değil): 81 ilin 17'si MultiPolygon.
            entity.Property(e => e.Geom)
                  .HasColumnName("geom")
                  .HasColumnType("geometry(Geometry, 4326)")
                  .IsRequired();

            entity.HasIndex(e => e.Geom).HasMethod("gist");
            entity.HasIndex(e => e.Bolge);        // "bu bölgedeki iller" sorgusu
            entity.HasIndex(e => e.Ad).IsUnique();
        });

        ConfigureAuthorization(modelBuilder);
        ConfigurePoi(modelBuilder);
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

        // ---------- Eksik 5: yenileme anahtarları ----------
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            // SHA-256 hex çıktısı her zaman tam 64 karakter — sabit uzunluk
            // kolonun yanlış bir şey (örn. anahtarın kendisi) taşımasını zorlaştırır.
            entity.Property(e => e.TokenHash)
                  .HasColumnName("token_hash").HasMaxLength(64).IsRequired();

            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            entity.Property(e => e.ReplacedByHash)
                  .HasColumnName("replaced_by_hash").HasMaxLength(64);
            entity.Property(e => e.RevokedReason)
                  .HasColumnName("revoked_reason").HasMaxLength(40);

            // Her istekte "bu özet kimin?" diye aranıyor: benzersiz indeks hem
            // aramayı hızlandırıyor hem de aynı özetin iki satırda durmasını
            // veritabanı düzeyinde imkânsız kılıyor — yeniden kullanım tespiti
            // "tek eşleşme" varsayımına dayandığı için bu bir güvenlik kısıtı.
            entity.HasIndex(e => e.TokenHash).IsUnique();

            // "Bu kullanıcının açık oturumlarını kapat" sorgusu için.
            entity.HasIndex(e => e.UserId);

            // Kullanıcı FİZİKSEL olarak silinirse anahtarları da gitsin:
            // sahipsiz kalan bir anahtar, kime ait olduğu bilinmeyen bir
            // oturum demektir.
            entity.HasOne(e => e.User).WithMany()
                  .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);

            // Soft delete edilmiş kullanıcının anahtarları görünmesin: hesabı
            // silinen biri, elindeki anahtarla oturumunu yenileyememeli.
            entity.HasQueryFilter(e => !e.User!.IsDeleted);
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

        modelBuilder.Entity<GeoPermission>(entity =>
        {
            entity.ToTable("geo_permissions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.InsertedDate).HasColumnName("inserted_date");
            entity.Property(e => e.InsertedUserId).HasColumnName("inserted_user_id");

            // Ödev 10: tip artık Polygon değil GEOMETRY. İl/bölge seçimiyle
            // tanımlanan alanlar MultiPolygon olabiliyor; kolon tipi Polygon
            // kalsaydı PostGIS bu kayıtları reddederdi.
            entity.Property(e => e.Geom)
                  .HasColumnName("geom")
                  .HasColumnType("geometry(Geometry, 4326)")
                  .IsRequired();

            // Alan sorguları "bu nokta içeride mi?" diye soracak — mekânsal index şart.
            entity.HasIndex(e => e.Geom).HasMethod("gist");

            entity.Property(e => e.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true).HasSentinel(true);
            entity.Property(e => e.ModifiedDate).HasColumnName("modified_date");

            // Sahip silinirse kuralı da götür: sahipsiz bir "izinli alan" satırı
            // kimseye uygulanmaz, sadece tabloda çöp olarak durur.
            entity.HasOne(e => e.User).WithMany()
                  .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Role).WithMany()
                  .HasForeignKey(e => e.RoleId).OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.RoleId);

            // YA kullanıcı YA rol — ikisi birden ya da ikisi de boş olamaz.
            // Kuralı veritabanına yazıyoruz: servis katmanı da kontrol ediyor ama
            // tek savunma hattına güvenmek, ileride başka bir yoldan (script, elle
            // INSERT) tutarsız satır girmesine kapı bırakırdı.
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_geo_permissions_tek_sahip",
                "(user_id IS NOT NULL AND role_id IS NULL) OR (user_id IS NULL AND role_id IS NOT NULL)"));

            entity.HasQueryFilter(e => !e.IsDeleted);
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

    // ---------- Ödev 12: POI ve hiyerarşik kategori ----------
    //
    // İki tablo, iki farklı zorluk:
    //   poi_category → kendi kendine bakan yabancı anahtar (ata-çocuk)
    //   poi          → PostGIS nokta + iki farklı silme davranışı
    private static void ConfigurePoi(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PoiCategory>(entity =>
        {
            entity.ToTable("poi_category");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Ad).HasColumnName("ad").HasMaxLength(150).IsRequired();
            entity.Property(e => e.Aciklama).HasColumnName("aciklama").HasMaxLength(500);

            // Ödev 15: kategoriye özgü simge. Kolon bir ANAHTAR tutuyor
            // ("fincan", "eczane"), çizimin kendisini değil — gerekçe:
            // PoiCategory.Ikon. 40 karakter, en uzun anahtarın çok üstünde.
            entity.Property(e => e.Ikon).HasColumnName("ikon").HasMaxLength(40);

            entity.Property(e => e.ParentId).HasColumnName("parent_id");
            entity.Property(e => e.CreatedDate).HasColumnName("created_date");

            entity.Property(e => e.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true).HasSentinel(true);
            entity.Property(e => e.ModifiedDate).HasColumnName("modified_date");

            // ATA-ÇOCUK: aynı tabloya bakan yabancı anahtar.
            //
            // Restrict (Cascade DEĞİL) bilinçli: Cascade olsaydı bir kategoriyi
            // fiziksel silmek bütün alt ağacı sessizce götürürdü. Zaten soft
            // delete kullanıyoruz; çocuğu olan kategoriyi silme denemesi
            // servis katmanında anlamlı bir mesajla reddediliyor.
            entity.HasOne(e => e.Parent)
                  .WithMany(e => e.Children)
                  .HasForeignKey(e => e.ParentId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.ParentId);

            // "Aynı ata altında aynı ad iki kez olmasın" kuralı SERVİSTE.
            //
            // Neden veritabanı index'i değil? PostgreSQL'de benzersiz index
            // NULL'ları birbirinden farklı sayar; kök kategorilerde parent_id
            // NULL olduğu için (NULL, 'Yeme-İçme') çifti iki kez yazılabilirdi.
            // Kuralın yarısını uygulayan bir index, hiç uygulamayandan daha
            // yanıltıcı olurdu — bu yüzden index yalnızca ARAMA için.
            entity.HasIndex(e => new { e.ParentId, e.Ad });

            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        modelBuilder.Entity<Poi>(entity =>
        {
            entity.ToTable("poi");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Isim).HasColumnName("isim").HasMaxLength(200).IsRequired();
            entity.Property(e => e.KategoriId).HasColumnName("kategori_id");
            entity.Property(e => e.MesaiSaatleri).HasColumnName("mesai_saatleri").HasMaxLength(200);

            // Ödev 13 / Madde 3: gün gün mesai planı.
            //
            // Kolon tipi JSONB (JSON değil): PostgreSQL jsonb'yi ayrıştırılmış
            // biçimde saklar, yani bozuk JSON veritabanı seviyesinde reddedilir
            // ve ileride "salı açık olanlar" gibi bir sorgu index'lenebilir.
            // Uzunluk sınırı YOK — sınır koymak, gün sayısı arttığında
            // (öğle arası, sezonluk saat) sessizce kesilen veri demek olurdu.
            //
            // C# tarafı string: Entities katmanı Business'taki MesaiPlani
            // tipini göremez (bağımlılık yönü), Npgsql metni jsonb'ye
            // kendiliğinden çeviriyor.
            entity.Property(e => e.MesaiPlani).HasColumnName("mesai_plani").HasColumnType("jsonb");

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.CreatedDate).HasColumnName("created_date");

            entity.Property(e => e.Geom)
                  .HasColumnName("geom")
                  .HasColumnType("geometry(Point, 4326)")
                  .IsRequired();

            // "Bu alanın içindeki POI'ler" ve harita sorguları için mekânsal index.
            entity.HasIndex(e => e.Geom).HasMethod("gist");

            entity.Property(e => e.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true).HasSentinel(true);
            entity.Property(e => e.ModifiedDate).HasColumnName("modified_date");

            // Kategori ZORUNLU ve Restrict: dolu bir kategoriyi silmek,
            // POI'leri kategorisiz (yani listelenemez) bırakırdı.
            entity.HasOne(e => e.Kategori)
                  .WithMany(k => k.Poiler)
                  .HasForeignKey(e => e.KategoriId)
                  .OnDelete(DeleteBehavior.Restrict);

            // Ekleyen kullanıcı İSTEĞE BAĞLI ve SetNull: POI ortak veridir,
            // ekleyen hesap ortadan kalksa da nokta haritada kalmalı.
            // (geo_permissions'daki Cascade'in tersi — orada kayıt sahibine
            // AİT bir kuraldı, burada sahibinden bağımsız bir veri.)
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.KategoriId);
            entity.HasIndex(e => e.UserId);

            // Kategori süzgeci de filtreye dahil: kategori zorunlu bir ilişki
            // olduğu için, silinmiş bir kategoriye bağlı POI'nin listelenmesi
            // "kategorisi olmayan POI" gibi tutarsız bir sonuç üretirdi.
            // (Servis zaten dolu kategorinin silinmesini engelliyor; bu ikinci hat.)
            entity.HasQueryFilter(e => !e.IsDeleted && !e.Kategori!.IsDeleted);
        });

        ConfigureUlasim(modelBuilder);
    }

    // ----------------------------------------------------------------------
    //  Ödev 16 — ulaşım modülü
    // ----------------------------------------------------------------------

    /// <summary>
    /// Güzergah ve durak tabloları (1-N).
    ///
    /// Ayrı bir metotta çünkü OnModelCreating zaten çok uzun; modül bazlı
    /// bölmek "hangi tablo hangi ödevden geldi" sorusunu da okunur kılıyor.
    /// </summary>
    private static void ConfigureUlasim(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Guzergah>(entity =>
        {
            entity.ToTable("guzergah");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Ad).HasColumnName("ad").HasMaxLength(150).IsRequired();

            // Renk "#rrggbb" — tam 7 karakter. Uzunluk sınırı biçimi garanti
            // etmiyor (biçim kontrolü serviste), ama kolonun ne taşıdığını
            // şema üzerinden de belli ediyor.
            entity.Property(e => e.Renk).HasColumnName("renk").HasMaxLength(7).IsRequired();

            entity.Property(e => e.Aciklama).HasColumnName("aciklama").HasMaxLength(500);
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.CreatedDate).HasColumnName("created_date");
            entity.Property(e => e.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true).HasSentinel(true);
            entity.Property(e => e.ModifiedDate).HasColumnName("modified_date");

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.SetNull);

            // ---------- Ödev 17 / Madde 1: OSRM rotası ----------

            // LineString, Point DEĞİL: hat bir çizgi. Tipi şemada sabitlemek,
            // yanlışlıkla başka bir geometri yazılmasını veritabanı düzeyinde
            // imkânsız kılıyor (diğer geometri tablolarındaki kuralın aynısı).
            entity.Property(e => e.Rota)
                  .HasColumnName("rota")
                  .HasColumnType("geometry(LineString, 4326)");

            entity.Property(e => e.RotaMesafeMetre).HasColumnName("rota_mesafe_metre");
            entity.Property(e => e.RotaSureSaniye).HasColumnName("rota_sure_saniye");
            entity.Property(e => e.RotaHesaplandi).HasColumnName("rota_hesaplandi");

            // İmza SHA-256 hex özeti — sabit 64 karakter.
            entity.Property(e => e.RotaImza).HasColumnName("rota_imza").HasMaxLength(64);

            // Mekânsal index: "bu alandan geçen hatlar" sorgusu ileride
            // gerekirse hazır olsun; duraklardaki gist index'iyle aynı gerekçe.
            entity.HasIndex(e => e.Rota).HasMethod("gist");

            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        modelBuilder.Entity<Durak>(entity =>
        {
            entity.ToTable("durak");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Ad).HasColumnName("ad").HasMaxLength(150).IsRequired();
            entity.Property(e => e.GuzergahId).HasColumnName("guzergah_id");
            entity.Property(e => e.Sira).HasColumnName("sira");
            entity.Property(e => e.Aciklama).HasColumnName("aciklama").HasMaxLength(500);
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.CreatedDate).HasColumnName("created_date");
            entity.Property(e => e.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true).HasSentinel(true);
            entity.Property(e => e.ModifiedDate).HasColumnName("modified_date");

            entity.Property(e => e.Geom)
                  .HasColumnName("geom")
                  .HasColumnType("geometry(Point, 4326)")
                  .IsRequired();

            // "Bu alandaki duraklar" ve harita sorguları için mekânsal index.
            entity.HasIndex(e => e.Geom).HasMethod("gist");

            // 1-N'İN ŞEMADAKİ KARŞILIĞI.
            //
            // Restrict (Cascade DEĞİL): dolu bir güzergahı silmek durakları
            // sessizce götürürdü. Zaten soft delete kullanıyoruz; dolu güzergahın
            // silinmesi serviste anlamlı bir mesajla reddediliyor — kategori
            // ağacındaki kuralın (PoiCategory) aynısı.
            entity.HasOne(e => e.Guzergah)
                  .WithMany(g => g.Duraklar)
                  .HasForeignKey(e => e.GuzergahId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.SetNull);

            // Bir güzergahın durakları SIRAYLA okunuyor; bileşik index hem
            // süzmeyi hem sıralamayı tek taramada karşılıyor.
            entity.HasIndex(e => new { e.GuzergahId, e.Sira });

            // Güzergah süzgeci de filtreye dahil: silinmiş bir güzergaha bağlı
            // durağın listelenmesi "güzergahı olmayan durak" gibi tutarsız bir
            // sonuç üretirdi (poi → poi_category filtresiyle aynı desen).
            entity.HasQueryFilter(e => !e.IsDeleted && !e.Guzergah!.IsDeleted);
        });
    }

}
