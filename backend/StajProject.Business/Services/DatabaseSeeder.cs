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

    // Ödev 5 / Madde 3'ü gösterebilmek için ikinci bir kullanıcı.
    // Sahiplik süzgecini kanıtlamanın tek yolu, farklı bir kullanıcıyla
    // giriş yapıp FARKLI bir harita görmektir.
    private const string IkinciKullanici = "ayse";
    private const string IkinciSifre = "staj123";

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
        await IkinciKullaniciVerisiAsync();
    }

    private async Task KullaniciEkleAsync()
    {
        // Kullanıcı bazında kontrol: biri varken diğeri eklenebilsin.
        // (AnyAsync ile "hiç kullanıcı yok mu" diye bakmak, ikinci kullanıcıyı
        //  sonradan eklememizi imkânsız kılardı.)
        await KullaniciYoksaEkleAsync(DemoKullanici, DemoSifre);
        await KullaniciYoksaEkleAsync(IkinciKullanici, IkinciSifre);
    }

    private async Task<User> KullaniciYoksaEkleAsync(string kullaniciAdi, string sifre)
    {
        var mevcut = await _userRepository.GetByUsernameAsync(kullaniciAdi);
        if (mevcut is not null)
        {
            return mevcut;
        }

        var yeni = new User { Username = kullaniciAdi };
        yeni.PasswordHash = _passwordHasher.HashPassword(yeni, sifre);

        return await _userRepository.AddAsync(yeni);
    }

    /// <summary>
    /// İkinci kullanıcıya KENDİ çizimlerini verir (Ödev 5 / Madde 3 gösterimi).
    ///
    /// Boş harita da süzgeci kanıtlardı, ama "başka kullanıcı = başka veri"
    /// çok daha nettir: aynı uygulama, aynı ekran, tamamen farklı kayıtlar.
    /// Bölgeyi de bilerek ayırdık (Ege/Akdeniz), admin'in kayıtlarıyla
    /// karışmasın diye.
    /// </summary>
    private async Task IkinciKullaniciVerisiAsync()
    {
        var kullanici = await _userRepository.GetByUsernameAsync(IkinciKullanici);
        if (kullanici is null)
        {
            return;
        }

        // Bu kullanıcının HİÇ kaydı yoksa örnekleri yükle; varsa dokunma.
        var mevcutNoktalari = await _noktaRepo.GetAllAsync(kullanici.Id);
        if (mevcutNoktalari.Count > 0)
        {
            return;
        }

        foreach (var ornek in DemoVerisi.IkinciKullaniciNoktalari)
        {
            var entity = new PointEntity
            {
                Name = ornek.Ad,
                Description = ornek.Aciklama,
                Color = ornek.Renk,
                Geom = WktConverter.Read<Point>(ornek.Wkt),
                InsertedDate = DateTime.UtcNow,
                InsertedUserId = kullanici.Id,
            };
            await _noktaRepo.AddAsync(entity);
        }

        foreach (var ornek in DemoVerisi.IkinciKullaniciPoligonlari)
        {
            var entity = new PolygonEntity
            {
                Name = ornek.Ad,
                Description = ornek.Aciklama,
                Color = ornek.Renk,
                Geom = WktConverter.Read<Polygon>(ornek.Wkt),
                InsertedDate = DateTime.UtcNow,
                InsertedUserId = kullanici.Id,
            };
            await _poligonRepo.AddAsync(entity);
        }
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
