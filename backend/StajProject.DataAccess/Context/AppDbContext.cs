using Microsoft.EntityFrameworkCore;
using StajProject.Entities;

namespace StajProject.DataAccess.Context;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Location> Locations => Set<Location>();
    public DbSet<User> Users => Set<User>();

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
    }
}
