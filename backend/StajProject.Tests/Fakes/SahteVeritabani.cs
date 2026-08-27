using StajProject.Entities;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Bellekte yaşayan minik bir "veritabanı" (Ödev 6).
///
/// Kullanıcı, rol ve yetki repository'leri AYNI tabloları paylaşır — gerçekte de
/// öyle. Her sahte repository kendi listesini tutsaydı "kullanıcıya rol ata,
/// sonra rolün yetkilerini oku" gibi asıl ilginç senaryolar test edilemezdi.
/// </summary>
public class SahteVeritabani
{
    public List<User> Kullanicilar { get; } = new();
    public List<Role> Roller { get; } = new();
    public List<Permission> Yetkiler { get; } = new();

    public List<UserRole> KullaniciRolleri { get; } = new();
    public List<RolePermission> RolYetkileri { get; } = new();
    public List<UserPermission> KullaniciYetkileri { get; } = new();

    /// <summary>Ödev 7: kullanıcı/rol bazlı izinli çizim alanları.</summary>
    public List<GeoPermission> CografiYetkiler { get; } = new();

    /// <summary>Ödev 12: hiyerarşik POI kategori sözlüğü.</summary>
    public List<PoiCategory> PoiKategorileri { get; } = new();

    /// <summary>Ödev 12: haritadaki ilgi noktaları.</summary>
    public List<Poi> Poiler { get; } = new();

    /// <summary>Ödev 16: ulaşım hatları ve durakları (1-N).</summary>
    public List<Guzergah> Guzergahlar { get; } = new();
    public List<Durak> Duraklar { get; } = new();

    private int _sonrakiKullaniciId = 1;
    private int _sonrakiRolId = 1;
    private int _sonrakiYetkiId = 1;
    private int _sonrakiCografiYetkiId = 1;
    private int _sonrakiPoiKategoriId = 1;
    private int _sonrakiPoiId = 1;
    private int _sonrakiGuzergahId = 1;
    private int _sonrakiDurakId = 1;

    public int SonrakiKullaniciId() => _sonrakiKullaniciId++;
    public int SonrakiRolId() => _sonrakiRolId++;
    public int SonrakiYetkiId() => _sonrakiYetkiId++;
    public int SonrakiCografiYetkiId() => _sonrakiCografiYetkiId++;
    public int SonrakiPoiKategoriId() => _sonrakiPoiKategoriId++;
    public int SonrakiPoiId() => _sonrakiPoiId++;
    public int SonrakiGuzergahId() => _sonrakiGuzergahId++;
    public int SonrakiDurakId() => _sonrakiDurakId++;

    /// <summary>Testleri kısaltmak için: güzergahı ekler ve nesnesini döner.</summary>
    public Guzergah GuzergahEkle(string ad, string renk = "#2d7dd2", bool aktif = true, int? userId = null)
    {
        var guzergah = new Guzergah
        {
            Id = SonrakiGuzergahId(),
            Ad = ad,
            Renk = renk,
            IsActive = aktif,
            UserId = userId,
        };
        Guzergahlar.Add(guzergah);
        return guzergah;
    }

    /// <summary>
    /// Testleri kısaltmak için: kategoriyi ekler ve nesnesini döner.
    /// <paramref name="parentId"/> boşsa kök kategori olur.
    /// </summary>
    public PoiCategory PoiKategoriEkle(string ad, int? parentId = null, bool aktif = true)
    {
        var kategori = new PoiCategory
        {
            Id = SonrakiPoiKategoriId(),
            Ad = ad,
            ParentId = parentId,
            IsActive = aktif,
        };
        PoiKategorileri.Add(kategori);
        return kategori;
    }

    /// <summary>Testleri kısaltmak için: yetkiyi ekler ve nesnesini döner.</summary>
    public Permission YetkiEkle(string ad, string? aciklama = null)
    {
        var yetki = new Permission { Id = SonrakiYetkiId(), Name = ad, Description = aciklama };
        Yetkiler.Add(yetki);
        return yetki;
    }

    /// <summary>Rolü ve (verilmişse) yetkilerini ekler.</summary>
    public Role RolEkle(string ad, params int[] yetkiIdleri)
    {
        var rol = new Role { Id = SonrakiRolId(), Name = ad };
        Roller.Add(rol);

        foreach (var yetkiId in yetkiIdleri)
        {
            RolYetkileri.Add(new RolePermission { RoleId = rol.Id, PermissionId = yetkiId });
        }

        return rol;
    }

    /// <summary>
    /// Kullanıcıyı, üzerindeki rol ve doğrudan yetki navigasyonları DOLU hâlde döner.
    /// Gerçek repository Include ile aynısını yapıyor; testte de aynı şekli
    /// üretmezsek servis kodu boş listelerle çalışır ve testler yalan söylerdi.
    /// </summary>
    public User? KullaniciyiIliskileriyleGetir(int id)
    {
        var kullanici = Kullanicilar.FirstOrDefault(k => k.Id == id && !k.IsDeleted);
        if (kullanici is null)
        {
            return null;
        }

        kullanici.UserRoles = KullaniciRolleri
            .Where(kr => kr.UserId == id)
            .Select(kr => new UserRole
            {
                UserId = kr.UserId,
                RoleId = kr.RoleId,
                // Rolün YETKİLERİ de dolu gelmeli: gerçek repository Include ile
                // aynısını yapıyor, yetki birleştirmesi buna dayanıyor.
                Role = RolüYetkileriyleGetir(kr.RoleId),
            })
            .Where(kr => kr.Role is not null)     // silinmiş rol yetki üretmez
            .ToList();

        kullanici.UserPermissions = KullaniciYetkileri
            .Where(ky => ky.UserId == id)
            .Select(ky => new UserPermission
            {
                UserId = ky.UserId,
                PermissionId = ky.PermissionId,
                Permission = Yetkiler.FirstOrDefault(y => y.Id == ky.PermissionId),
            })
            .Where(ky => ky.Permission is not null)
            .ToList();

        return kullanici;
    }

    /// <summary>Rolü yetkileriyle birlikte döner (RoleRepository.Include karşılığı).</summary>
    public Role? RolüYetkileriyleGetir(int id)
    {
        var rol = Roller.FirstOrDefault(r => r.Id == id && !r.IsDeleted);
        if (rol is null)
        {
            return null;
        }

        rol.RolePermissions = RolYetkileri
            .Where(ry => ry.RoleId == id)
            .Select(ry => new RolePermission
            {
                RoleId = ry.RoleId,
                PermissionId = ry.PermissionId,
                Permission = Yetkiler.FirstOrDefault(y => y.Id == ry.PermissionId),
            })
            .Where(ry => ry.Permission is not null)
            .ToList();

        return rol;
    }
}
