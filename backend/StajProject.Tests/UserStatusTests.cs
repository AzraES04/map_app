using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.DataAccess.Context;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// Ödev 3 / Görev 1 testleri: is_deleted, is_active ve modified_date kolonlarının
/// sadece "var olması" değil, DOĞRU DAVRANMASI test ediliyor.
///
/// Burada FakeRepository yerine EF Core'un InMemory sağlayıcısını kullanıyoruz. Sebep:
/// test etmek istediğimiz şeylerin (global query filter, SaveChanges override'ı)
/// tamamı AppDbContext'in içinde yaşıyor. Sahte repository yazsaydık kendi yazdığımız
/// sahte davranışı test etmiş olurduk — yani hiçbir şeyi.
/// </summary>
public class UserStatusTests
{
    /// <summary>
    /// Her test için sıfırdan, izole bir veritabanı. Guid sayesinde testler
    /// birbirinin verisini görmez (xUnit testleri paralel çalışabilir).
    /// </summary>
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static User NewUser(string username = "ayse") => new()
    {
        Username = username,
        PasswordHash = "dummy-hash"
    };

    // ---------------- modified_date ----------------

    [Fact]
    public async Task YeniKullanici_ModifiedDate_NullKalir()
    {
        using var db = CreateContext();

        db.Users.Add(NewUser());
        await db.SaveChangesAsync();

        var user = await db.Users.SingleAsync();
        Assert.Null(user.ModifiedDate);      // hiç güncellenmedi → damga yok
        Assert.False(user.IsDeleted);        // varsayılan: silinmemiş
        Assert.True(user.IsActive);          // varsayılan: aktif
    }

    [Fact]
    public async Task KullaniciGuncellenince_ModifiedDate_OtomatikDolar()
    {
        using var db = CreateContext();
        db.Users.Add(NewUser());
        await db.SaveChangesAsync();

        var oncesi = DateTime.UtcNow;

        var user = await db.Users.SingleAsync();
        user.Username = "ayse.yilmaz";       // herhangi bir değişiklik yeter
        await db.SaveChangesAsync();

        Assert.NotNull(user.ModifiedDate);
        Assert.Equal(DateTimeKind.Utc, user.ModifiedDate!.Value.Kind);   // Npgsql timestamptz şartı
        Assert.True(user.ModifiedDate >= oncesi);
    }

    // ---------------- is_deleted + global query filter ----------------

    [Fact]
    public async Task SoftDelete_KaydiSilmez_SadeceIsaretler()
    {
        using var db = CreateContext();
        var repo = new UserRepository(db);

        db.Users.Add(NewUser());
        await db.SaveChangesAsync();
        var id = (await db.Users.SingleAsync()).Id;

        var sonuc = await repo.SoftDeleteAsync(id);

        Assert.True(sonuc);

        // Filtreyi yok sayarak bakınca satır HÂLÂ ORADA — fiziksel silme yok.
        var silinen = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == id);
        Assert.True(silinen.IsDeleted);
        Assert.False(silinen.IsActive);          // silinen hesap pasif de olmalı
        Assert.NotNull(silinen.ModifiedDate);    // güncelleme damgası otomatik atıldı
    }

    [Fact]
    public async Task SoftDeleteSonrasi_NormalSorgu_KullaniciyiGormez()
    {
        using var db = CreateContext();
        var repo = new UserRepository(db);

        db.Users.Add(NewUser());
        await db.SaveChangesAsync();
        await repo.SoftDeleteAsync((await db.Users.SingleAsync()).Id);

        Assert.Empty(await db.Users.ToListAsync());                    // filtre devrede
        Assert.Single(await db.Users.IgnoreQueryFilters().ToListAsync()); // veri duruyor
        Assert.Null(await repo.GetByUsernameAsync("ayse"));            // repo da göremiyor
    }

    [Fact]
    public async Task SoftDelete_AyniIdIcinIkinciKez_FalseDoner()
    {
        using var db = CreateContext();
        var repo = new UserRepository(db);
        db.Users.Add(NewUser());
        await db.SaveChangesAsync();
        var id = (await db.Users.SingleAsync()).Id;

        Assert.True(await repo.SoftDeleteAsync(id));
        Assert.False(await repo.SoftDeleteAsync(id));   // artık filtre onu gizliyor
    }

    // ---------------- is_active ----------------

    [Fact]
    public async Task SetActiveAsync_HesabiPasifYapar_KayitGorunurKalir()
    {
        using var db = CreateContext();
        var repo = new UserRepository(db);
        db.Users.Add(NewUser());
        await db.SaveChangesAsync();
        var id = (await db.Users.SingleAsync()).Id;

        var sonuc = await repo.SetActiveAsync(id, false);

        Assert.True(sonuc);
        var user = await db.Users.SingleAsync();   // pasif kullanıcı SİLİNMİŞ değil, görünür
        Assert.False(user.IsActive);
        Assert.False(user.IsDeleted);
    }

    // ---------------- AuthService entegrasyonu ----------------

    private static AuthService CreateAuthService(AppDbContext db)
    {
        var settings = new JwtSettings
        {
            Key = "StajProject-Test-Imza-Anahtari-En-Az-32-Karakter!",
            Issuer = "test",
            Audience = "test",
            ExpiryMinutes = 10
        };

        return new AuthService(new UserRepository(db), new RefreshTokenRepository(db), Options.Create(settings));
    }

    /// <summary>Testte de gerçek hash algoritmasını kullanıyoruz ki doğrulama gerçekten çalışsın.</summary>
    private static User NewUserWithPassword(string username, string password)
    {
        var user = new User { Username = username };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        return user;
    }

    [Fact]
    public async Task Login_AktifKullanici_TokenDoner()
    {
        using var db = CreateContext();
        db.Users.Add(NewUserWithPassword("admin", "staj123"));
        await db.SaveChangesAsync();

        var sonuc = await CreateAuthService(db)
            .LoginAsync(new LoginRequestDto { Username = "admin", Password = "staj123" });

        Assert.NotNull(sonuc);
        Assert.False(string.IsNullOrWhiteSpace(sonuc!.Token));
        Assert.Equal("admin", sonuc.Username);
    }

    [Fact]
    public async Task Login_PasifKullanici_SifreDogruOlsaBile_Reddedilir()
    {
        using var db = CreateContext();
        var user = NewUserWithPassword("admin", "staj123");
        user.IsActive = false;                       // hesap askıya alınmış
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sonuc = await CreateAuthService(db)
            .LoginAsync(new LoginRequestDto { Username = "admin", Password = "staj123" });

        Assert.Null(sonuc);
    }

    [Fact]
    public async Task Login_SoftDeleteEdilmisKullanici_Reddedilir()
    {
        using var db = CreateContext();
        db.Users.Add(NewUserWithPassword("admin", "staj123"));
        await db.SaveChangesAsync();
        await new UserRepository(db).SoftDeleteAsync((await db.Users.SingleAsync()).Id);

        var sonuc = await CreateAuthService(db)
            .LoginAsync(new LoginRequestDto { Username = "admin", Password = "staj123" });

        Assert.Null(sonuc);
    }
}
