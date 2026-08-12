using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.Tests.Fakes;
using Xunit;

namespace StajProject.Tests;

public class LocationServiceTests
{
    private static LocationService CreateService() => new(new FakeLocationRepository());

    [Fact]
    public async Task CreateAsync_KoordinatlariDogruPointeCevirir()
    {
        // Arrange
        var service = CreateService();
        var dto = new LocationCreateDto
        {
            Name = "Anıtkabir",
            Description = "Ankara",
            Longitude = 32.8597,
            Latitude = 39.9334
        };

        // Act
        var created = await service.CreateAsync(dto);

        // Assert — X=boylam, Y=enlem eşleşmesi doğru mu?
        Assert.Equal(32.8597, created.Longitude, precision: 4);
        Assert.Equal(39.9334, created.Latitude, precision: 4);
        Assert.Equal("Anıtkabir", created.Name);
        Assert.True(created.Id > 0);
    }

    [Fact]
    public async Task CreateAsync_SonrasindaGetAll_KaydiIcerir()
    {
        var service = CreateService();
        await service.CreateAsync(new LocationCreateDto { Name = "A", Longitude = 1, Latitude = 2 });
        await service.CreateAsync(new LocationCreateDto { Name = "B", Longitude = 3, Latitude = 4 });

        var all = await service.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, l => l.Name == "A");
        Assert.Contains(all, l => l.Name == "B");
    }

    [Fact]
    public async Task GetByIdAsync_OlmayanId_NullDoner()
    {
        var service = CreateService();

        var result = await service.GetByIdAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_VarolanKaydiSiler()
    {
        var service = CreateService();
        var created = await service.CreateAsync(new LocationCreateDto { Name = "Silinecek", Longitude = 10, Latitude = 20 });

        var deleted = await service.DeleteAsync(created.Id);
        var remaining = await service.GetAllAsync();

        Assert.True(deleted);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task DeleteAsync_OlmayanKayit_FalseDoner()
    {
        var service = CreateService();

        var deleted = await service.DeleteAsync(123);

        Assert.False(deleted);
    }
}
