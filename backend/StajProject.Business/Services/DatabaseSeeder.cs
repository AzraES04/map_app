using Microsoft.AspNetCore.Identity;
using NetTopologySuite.Geometries;
using StajProject.Business.Geo;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

public class DatabaseSeeder : IDatabaseSeeder
{
    private const string DemoKullanici = "admin";
    private const string DemoSifre = "staj123";

    private readonly IUserRepository _userRepository;
    private readonly IGeometryRepository<PointEntity> _noktaRepo;
    private readonly IGeometryRepository<LineEntity> _cizgiRepo;
    private readonly IGeometryRepository<PolygonEntity> _poligonRepo;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public DatabaseSeeder(
        IUserRepository userRepository,
        IGeometryRepository<PointEntity> noktaRepo,
        IGeometryRepository<LineEntity> cizgiRepo,
        IGeometryRepository<PolygonEntity> poligonRepo)
    {
        _userRepository = userRepository;
        _noktaRepo = noktaRepo;
        _cizgiRepo = cizgiRepo;
        _poligonRepo = poligonRepo;
    }

    public async Task SeedAsync()
    {
        await KullaniciEkleAsync();
        await DemoEnvanteriEkleAsync();
    }

    private async Task KullaniciEkleAsync()
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

    /// <summary>
    /// Örnek envanteri yükler — YALNIZCA ilgili tablo boşsa.
    ///
    /// Tablo bazında kontrol ediyoruz: kullanıcı kendi noktalarını çizmişse
    /// onlara dokunmadan, boş olan çizgi tablosuna örnek ekleyebiliyoruz.
    /// Bu kontrol olmasaydı her açılışta aynı kayıtlar tekrar tekrar eklenirdi.
    /// </summary>
    private async Task DemoEnvanteriEkleAsync()
    {
        await EkleAsync(_noktaRepo, DemoVerisi.Noktalar,
            wkt => new PointEntity { Geom = WktConverter.Read<Point>(wkt) });

        await EkleAsync(_cizgiRepo, DemoVerisi.Cizgiler,
            wkt => new LineEntity { Geom = WktConverter.Read<LineString>(wkt) });

        await EkleAsync(_poligonRepo, DemoVerisi.Poligonlar,
            wkt => new PolygonEntity { Geom = WktConverter.Read<Polygon>(wkt) });
    }

    /// <summary>
    /// Tek bir tabloyu doldurur. Geometri tipi her tabloda farklı olduğu için
    /// entity üretimini dışarıdan fabrika fonksiyonu olarak alıyoruz.
    /// </summary>
    private static async Task EkleAsync<TEntity>(
        IGeometryRepository<TEntity> repository,
        DemoVerisi.Ornek[] ornekler,
        Func<string, TEntity> uret)
        where TEntity : GeometryEntityBase
    {
        var mevcut = await repository.GetAllAsync();
        if (mevcut.Count > 0)
        {
            return;   // kullanıcının kendi verisi var, karışma
        }

        foreach (var ornek in ornekler)
        {
            var entity = uret(ornek.Wkt);
            entity.Name = ornek.Ad;
            entity.Description = ornek.Aciklama;
            entity.Color = ornek.Renk;
            entity.InsertedDate = DateTime.UtcNow;

            await repository.AddAsync(entity);
        }
    }
}
