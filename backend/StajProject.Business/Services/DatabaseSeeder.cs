using Microsoft.AspNetCore.Identity;
using NetTopologySuite.Geometries;
using StajProject.Business.Auth;
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

    // ---------- Ödev 6: başlangıç rolleri ----------
    // Rol adı → o rolün yetkileri. Yalnızca rol İLK KEZ oluşturulurken uygulanır;
    // panelden yapılan düzenlemeler her açılışta geri alınmaz.
    private const string YoneticiRolu = "Yönetici";
    private const string EditorRolu = "Editör";
    private const string GoruntuleyiciRolu = "Görüntüleyici";

    private static readonly (string Ad, string Aciklama, string[] Yetkiler)[] BaslangicRolleri =
    {
        (YoneticiRolu, "Tüm yetkilere sahip; kullanıcı ve rolleri yönetir.", new[]
        {
            Auth.Yetkiler.NoktaEkleme, Auth.Yetkiler.CizgiEkleme, Auth.Yetkiler.PoligonEkleme,
            Auth.Yetkiler.KayitGuncelleme, Auth.Yetkiler.KayitSilme, Auth.Yetkiler.AnalizCalistirma,
            Auth.Yetkiler.KullaniciYonetimi, Auth.Yetkiler.RolYonetimi,
        }),
        (EditorRolu, "Harita üzerinde çizim yapar, kendi kayıtlarını düzenler ve siler.", new[]
        {
            Auth.Yetkiler.NoktaEkleme, Auth.Yetkiler.CizgiEkleme, Auth.Yetkiler.PoligonEkleme,
            Auth.Yetkiler.KayitGuncelleme, Auth.Yetkiler.KayitSilme, Auth.Yetkiler.AnalizCalistirma,
        }),
        (GoruntuleyiciRolu, "Yalnızca görüntüler; çizim yapamaz.", new[]
        {
            Auth.Yetkiler.AnalizCalistirma,
        }),
    };

    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IGeometryRepository<PointEntity> _noktaRepo;
    private readonly IGeometryRepository<LineEntity> _cizgiRepo;
    private readonly IGeometryRepository<PolygonEntity> _poligonRepo;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public DatabaseSeeder(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IGeometryRepository<PointEntity> noktaRepo,
        IGeometryRepository<LineEntity> cizgiRepo,
        IGeometryRepository<PolygonEntity> poligonRepo)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _noktaRepo = noktaRepo;
        _cizgiRepo = cizgiRepo;
        _poligonRepo = poligonRepo;
    }

    public async Task SeedAsync()
    {
        await KullaniciEkleAsync();

        // Ödev 6: önce yetki sözlüğü, sonra roller, en sonda kullanıcı-rol ataması.
        // Sıra önemli: rol yetkilerini bağlayabilmek için yetkilerin id'si gerekiyor.
        await YetkileriEkleAsync();
        await RolleriEkleAsync();
        await RolAtamalariniYapAsync();

        await DemoEnvanteriEkleAsync();
        await IkinciKullaniciVerisiAsync();
    }

    // ---------- Ödev 6: yetki / rol tohumlaması ----------

    /// <summary>
    /// Kodun tanıdığı yetkileri (bkz. <see cref="Auth.Yetkiler"/>) permissions
    /// tablosuna yazar. Var olanlara dokunmaz — açıklaması panelden
    /// değiştirilmişse geri alınmaz.
    ///
    /// Yetkiyi kodun bilmesi şart: "Point Ekleme" satırının bir anlamı olması için
    /// bir yerde o adı arayan bir kontrol olmalı. Bu yüzden yetki listesi seed'le
    /// gelir; rollere DAĞITIMI ise tamamen panelden yapılır.
    /// </summary>
    private async Task YetkileriEkleAsync()
    {
        foreach (var (ad, aciklama) in Auth.Yetkiler.Tumu)
        {
            if (await _permissionRepository.GetByNameAsync(ad) is not null)
            {
                continue;
            }

            await _permissionRepository.AddAsync(new Permission
            {
                Name = ad,
                Description = aciklama,
                InsertedDate = DateTime.UtcNow,
            });
        }
    }

    /// <summary>
    /// Başlangıç rollerini oluşturur. Rol zaten varsa YETKİLERİNE DOKUNULMAZ:
    /// jüri gösteriminde panelden "Editör"e yeni bir yetki eklendiğinde,
    /// sunucu yeniden başlayınca değişikliğin silinmesi kabul edilemez.
    /// </summary>
    private async Task RolleriEkleAsync()
    {
        var yetkiler = await _permissionRepository.GetAllAsync();

        foreach (var (ad, aciklama, yetkiAdlari) in BaslangicRolleri)
        {
            if (await _roleRepository.GetByNameAsync(ad) is not null)
            {
                continue;
            }

            var rol = await _roleRepository.AddAsync(new Role
            {
                Name = ad,
                Description = aciklama,
                InsertedDate = DateTime.UtcNow,
            });

            var yetkiIdleri = yetkiler
                .Where(y => yetkiAdlari.Contains(y.Name))
                .Select(y => y.Id)
                .ToList();

            await _roleRepository.SetPermissionsAsync(rol.Id, yetkiIdleri);
        }
    }

    /// <summary>
    /// Demo kullanıcılarını rollerine bağlar: admin → Yönetici, ayşe → Editör.
    ///
    /// Yalnızca kullanıcının HİÇ rolü yoksa atama yapılır. Aksi hâlde panelden
    /// "admin'in rolünü değiştir" denemesi her yeniden başlatmada geri alınırdı.
    /// admin'e Yönetici rolünün verilmesi ayrıca zorunlu: yönetim uçları
    /// "Kullanıcı Yönetimi" yetkisi istiyor, o da bu rolden geliyor.
    /// </summary>
    private async Task RolAtamalariniYapAsync()
    {
        await RolAtaAsync(DemoKullanici, YoneticiRolu);
        await RolAtaAsync(IkinciKullanici, EditorRolu);
    }

    private async Task RolAtaAsync(string kullaniciAdi, string rolAdi)
    {
        var kullanici = await _userRepository.GetByUsernameAsync(kullaniciAdi);
        var rol = await _roleRepository.GetByNameAsync(rolAdi);
        if (kullanici is null || rol is null)
        {
            return;
        }

        // GetByUsernameAsync ilişkileri yüklemiyor; rol durumunu tam kayıttan okuyoruz.
        var iliskileriyle = await _userRepository.GetByIdAsync(kullanici.Id);
        if (iliskileriyle is null || iliskileriyle.UserRoles.Count > 0)
        {
            return;
        }

        await _userRepository.SetRolesAsync(kullanici.Id, new[] { rol.Id });
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
