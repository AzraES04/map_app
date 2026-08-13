using Microsoft.AspNetCore.Identity;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

public class DatabaseSeeder : IDatabaseSeeder
{
    private const string DemoKullanici = "admin";
    private const string DemoSifre = "staj123";

    private readonly IUserRepository _userRepository;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public DatabaseSeeder(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task SeedAsync()
    {
        // AnyAsync() query filter'ı yok sayar: soft delete ile silinmiş bir admin
        // varsa "hiç kullanıcı yok" sanıp yenisini yaratmayalım.
        if (await _userRepository.AnyAsync())
        {
            return;
        }

        var admin = new User { Username = DemoKullanici };
        admin.PasswordHash = _passwordHasher.HashPassword(admin, DemoSifre);

        await _userRepository.AddAsync(admin);
    }
}
