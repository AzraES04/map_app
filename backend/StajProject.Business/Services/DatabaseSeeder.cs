using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using StajProject.Business.Auth;
using StajProject.DataAccess;
using StajProject.Business.Geo;
using StajProject.Business.Mesai;
using StajProject.DataAccess.GeoServer;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

public class DatabaseSeeder : IDatabaseSeeder
{
    /// <summary>
    /// Gömülü il verisinin kaynak adı. Biçim:
    /// &lt;AssemblyAdı&gt;.&lt;Link yolundaki klasörler&gt;.&lt;dosya adı&gt;
    /// </summary>
    private const string IlVerisiKaynagi = "StajProject.Business.Data.tr-iller.geojson";

    private const string DemoKullanici = "admin";
    private const string DemoSifre = "staj123";

    // Ödev 5 / Madde 3'ü gösterebilmek için ikinci bir kullanıcı.
    // Sahiplik süzgecini kanıtlamanın tek yolu, farklı bir kullanıcıyla
    // giriş yapıp FARKLI bir harita görmektir.
    private const string IkinciKullanici = "ayse";
    private const string IkinciSifre = "staj123";

    /// <summary>
    /// ÜÇÜNCÜ demo kullanıcısı — "Kullanıcı" (User) rolünde (Ödev 13 / Madde 2).
    ///
    /// Ödev, POI aramasının "Kullanıcı rolüne de açık" olmasını istiyor. Uçta
    /// yetki özniteliği bulunmaması bunu KODDA sağlıyor ama EKRANDA
    /// kanıtlamıyor: iki demo hesabının ikisi de (Admin, Operatör) zaten
    /// arama yapabiliyordu, dolayısıyla "arama herkese açık mı?" sorusunun
    /// gösterilebilir bir cevabı yoktu.
    ///
    /// Bu hesap tam olarak o boşluğu kapatıyor. Girdiğinde:
    ///   • hiçbir çizim/POI aracı görünmüyor (yetkisi yok)
    ///   • yönetim bağlantısı yok
    ///   • ama arama barı çalışıyor, POI'ye zoom yapıp bilgi panelini açıyor
    /// Yani ödevin maddesi tek bir girişle gösterilebiliyor.
    /// </summary>
    private const string UcuncuKullanici = "mehmet";
    private const string UcuncuSifre = "staj123";

    /// <summary>
    /// Ödev 16 — ULAŞIM modülünün iki demo hesabı.
    ///
    /// Neden iki hesap daha? Ödev, ulaşım için AYRI Operatör ve Kullanıcı
    /// rolleri istiyor ve bu rollerin POI/çizim EKLEME yetkisi OLMAMASI
    /// gerekiyor. "Yetkisi yok" iddiası ancak o rolle giriş yapıp ekranı
    /// göstererek kanıtlanabilir — admin'de her şey açık olduğu için orada
    /// hiçbir kısıt görünmüyor. (Aynı gerekçeyle Ödev 13'te "mehmet"
    /// eklenmişti.)
    ///
    ///   kemal  → Ulaşım Operatörü : Durak Ekle aracı ve Güzergah Yönetimi VAR,
    ///                                 POI Ekle / çizim araçları YOK
    ///   zeynep → Ulaşım Kullanıcısı: hiçbir ekleme aracı yok; durakları,
    ///                                 güzergahları ve POI'leri yalnızca görüyor
    /// </summary>
    private const string UlasimOperatoruKullanici = "kemal";
    private const string UlasimKullanicisiKullanici = "zeynep";
    private const string UlasimSifre = "staj123";

    // ---------- Ödev 6 + 12: başlangıç rolleri ----------
    //
    // Ödev 12 / Madde 1 üç temel rol istiyor: Admin, Operatör, Kullanıcı.
    // Bunlar YENİ roller değil, var olan üç rolün ödevin verdiği adlarla
    // karşılığı — kapsamları zaten birebir örtüşüyordu:
    //     Yönetici → Admin, Editör → Operatör, Görüntüleyici → Kullanıcı
    // Yenilerini ayrıca eklemek panelde altı rol bırakırdı; ikisi diğerinin
    // eşi olan bir rol listesi, yetkilerin hangisinden geldiğini takip
    // etmeyi zorlaştırırdı. Mevcut veritabanındaki adları güncelleyen adım:
    // <see cref="RolleriYenidenAdlandirAsync"/>.
    //
    // Rol adı → o rolün yetkileri. Yalnızca rol İLK KEZ oluşturulurken
    // uygulanır; panelden yapılan düzenlemeler her açılışta geri alınmaz.
    private const string AdminRolu = "Admin";
    private const string OperatorRolu = "Operatör";
    private const string KullaniciRolu = "Kullanıcı";

    // Ödev 16: ulaşım modülünün kendi rolleri.
    private const string UlasimOperatoruRolu = "Ulaşım Operatörü";
    private const string UlasimKullanicisiRolu = "Ulaşım Kullanıcısı";

    /// <summary>
    /// Eski rol adı → yeni rol adı. Sırf ad değişikliği; yetkilere dokunulmaz,
    /// atamalar (user_roles) rolün id'sine bağlı olduğu için yerinde kalır.
    /// </summary>
    private static readonly (string Eski, string Yeni)[] RolAdiDegisiklikleri =
    {
        ("Yönetici", AdminRolu),
        ("Editör", OperatorRolu),
        ("Görüntüleyici", KullaniciRolu),
    };

    private static readonly (string Ad, string Aciklama, string[] Yetkiler)[] BaslangicRolleri =
    {
        (AdminRolu, "Tüm yetkilere sahip; kullanıcı, rol ve POI yönetimini yapar.", new[]
        {
            Auth.Yetkiler.NoktaEkleme, Auth.Yetkiler.CizgiEkleme, Auth.Yetkiler.PoligonEkleme,
            Auth.Yetkiler.KayitGuncelleme, Auth.Yetkiler.KayitSilme, Auth.Yetkiler.AnalizCalistirma,
            Auth.Yetkiler.KullaniciYonetimi, Auth.Yetkiler.RolYonetimi,
            Auth.Yetkiler.CografiYetkiTanimlama,
            Auth.Yetkiler.PoiEkleme, Auth.Yetkiler.PoiYonetimi,
        }),
        (OperatorRolu, "Haritada çizim ve POI girişi yapar; kendi kayıtlarını düzenler ve siler.", new[]
        {
            Auth.Yetkiler.NoktaEkleme, Auth.Yetkiler.CizgiEkleme, Auth.Yetkiler.PoligonEkleme,
            Auth.Yetkiler.KayitGuncelleme, Auth.Yetkiler.KayitSilme, Auth.Yetkiler.AnalizCalistirma,
            // Ödev 12: POI ekleme aracı OPERATÖRÜN işi. Kategori ağacına
            // dokunamaz — o "POI Yönetimi" yetkisiyle Admin'de.
            Auth.Yetkiler.PoiEkleme,
        }),
        (KullaniciRolu, "Yalnızca görüntüler; çizim ve POI girişi yapamaz.", new[]
        {
            Auth.Yetkiler.AnalizCalistirma,
        }),

        // ---------- Ödev 16: ulaşım modülü rolleri ----------
        //
        // ÖDEV NOTU: "Bu yeni rolleri eklerken daha önce yapılan POI ve Harita
        // Çizim araçlarını bu roldekiler ekleme işlemlerini yapmasınlar yetkileri
        // olmasın, eklenen POI leri görüntüleyebilsinler."
        //
        // Bu yüzden aşağıdaki listelerde ne NoktaEkleme/CizgiEkleme/PoligonEkleme
        // ne de PoiEkleme var. GÖRÜNTÜLEME için yetki gerekmiyor: POI listeleme
        // ucu (GET /api/poi) bilinçli olarak yetkisiz — POI ortak referans
        // verisidir (bkz. IPoiService). Yani "görebilsinler" koşulu, listeye bir
        // yetki EKLEYEREK değil, hiçbir şey eklemeyerek sağlanıyor.
        (UlasimOperatoruRolu,
            "Ulaşım modülü: durak ekler, güzergah yönetir. Çizim ve POI ekleyemez.", new[]
        {
            Auth.Yetkiler.DurakEkleme,
            Auth.Yetkiler.GuzergahYonetimi,
        }),

        // Hiç yetkisi olmayan bir rol — ve bu bir eksiklik değil, TANIMIN kendisi.
        // Durakları, güzergahları ve POI'leri görmek için giriş yapmış olmak
        // yeterli; ekleme/düzenleme uçlarının hepsi bir yetki özniteliğiyle
        // korunuyor. Boş liste, paneldeki rol ekranında da açıkça görünüyor.
        (UlasimKullanicisiRolu,
            "Ulaşım modülü: durakları, güzergahları ve POI'leri yalnızca görüntüler.",
            Array.Empty<string>()),
    };

    /// <summary>
    /// Başlangıç rollerinin SALT OKUNUR görünümü — yalnızca testler için.
    ///
    /// NEDEN AÇILDI? Ödev 16'nın notu, ulaşım rollerinin POI ve çizim
    /// yetkisi TAŞIMAMASINI istiyor. Bu bir DAVRANIŞ değil bir TANIM kuralı:
    /// servisi çağırarak sınanamıyor, tanımın kendisine bakmak gerekiyor.
    /// Açılmasaydı "yetki eklenmediğini" ancak gözle kontrol edebilirdik ve
    /// ileride biri listeye bir satır eklediğinde kimse fark etmezdi.
    ///
    /// Liste zaten <c>static readonly</c> ve içindeki tüm değerler değişmez
    /// (record benzeri değerli demetler); dışarı vermek bir durum sızıntısı
    /// oluşturmuyor.
    /// </summary>
    public static IReadOnlyList<(string Ad, string Aciklama, string[] Yetkiler)> BaslangicRolleriTest
        => BaslangicRolleri;

    /// <summary>
    /// VAR OLAN rollere sonradan eklenmesi gereken yetkiler.
    ///
    /// Neden gerekli? <see cref="RolleriEkleAsync"/> yalnızca rolü İLK KEZ
    /// oluştururken yetki dağıtıyor — panelden yapılan düzenlemeler her
    /// açılışta geri alınmasın diye. Ama bu kuralın bir yan etkisi var:
    /// projeye yeni bir modül eklendiğinde (Ödev 12'nin POI'si gibi) rol
    /// zaten var olduğu için yeni yetki hiç kimseye ulaşmıyor ve modül,
    /// çalışan bir kurulumda sessizce erişilemez kalıyor. Bu tam olarak
    /// <see cref="AdminRolunuTamamlaAsync"/>'in Admin için çözdüğü sorun;
    /// burada aynı çözüm diğer roller için ADI GEÇEN yetkilerle sınırlı
    /// tutuluyor.
    ///
    /// Yalnızca EKLER, hiçbir zaman kaldırmaz: yöneticinin panelden verdiği
    /// başka yetkiler yerinde kalır.
    /// </summary>
    private static readonly (string Rol, string[] Yetkiler)[] SonradanEklenenYetkiler =
    {
        // Ödev 12: POI girişi operatörün işi. Kategori ağacı ("POI Yönetimi")
        // bilinçli olarak verilmiyor — o Admin'de kalıyor.
        (OperatorRolu, new[] { Auth.Yetkiler.PoiEkleme }),
    };

    // ---------- Ödev 12: başlangıç POI kategorileri ----------
    //
    // Ağaç, ödev metnindeki örneği (Yeme-İçme → Restoran, Kafe) içerecek
    // şekilde kuruldu ve üç kök daha eklendi: tek kökle hiyerarşinin
    // "kardeş dallar" tarafı gösterilemezdi.
    /// <summary>
    /// Başlangıç kategori ağacı ve her düğümün SIMGESİ (Ödev 15).
    ///
    /// Anahtarlar <c>PoiIkonlari</c> kataloguyla birebir eşleşmeli; eşleşmeyen
    /// bir anahtar yazılırsa kategori yine oluşuyor ama haritada varsayılan
    /// iğneyle çiziliyor (sessiz kalmasın diye testi var).
    ///
    /// KÖKE DE SİMGE VERİLİYOR: alt kategorisi olmayan ya da sonradan açılan
    /// bir çocuk, atasının simgesini miras alıyor (bkz. PoiIkonlari.EtkinAnahtar).
    /// Böylece "Yeme-İçme › Kebapçı" açıldığı anda haritada çatal-bıçakla
    /// görünüyor, kimsenin bir şey seçmesi gerekmiyor.
    /// </summary>
    private static readonly (string Kok, string KokIkon, (string Ad, string Ikon)[] Altlar)[]
        BaslangicKategorileri =
    {
        ("Yeme-İçme", "catal-bicak", new[]
        {
            ("Restoran", "catal-bicak"), ("Kafe", "fincan"), ("Fırın", "ekmek"),
        }),
        ("Konaklama", "yatak", new[]
        {
            ("Otel", "yatak"), ("Pansiyon", "ev"),
        }),
        ("Sağlık", "kalp", new[]
        {
            ("Hastane", "hastane"), ("Eczane", "eczane"),
        }),
        ("Eğitim", "mezuniyet", new[]
        {
            ("Okul", "mezuniyet"), ("Kütüphane", "kitap"),
        }),
    };

    /// <summary>
    /// Örnek POI'ler. Hepsi Ankara'da: ikinci kullanıcının (Operatör rolündeki
    /// "ayse") çizim alanı rolünden gelen "Ankara çalışma sahası"nı kapsıyor,
    /// yani seed verisi kendi coğrafi yetki kuralıyla tutarlı.
    ///
    /// Ödev 13 / Madde 3'ten sonra her örneğin GÜN GÜN mesai planı var. Dört
    /// kip de temsil ediliyor ki ilk açılışta ekran boş bir örnekle değil,
    /// özelliğin tamamıyla karşılasın:
    ///   • hafta sonu farklı saatli (restoran)
    ///   • pazar kapalı (eczane)
    ///   • 7/24 (kafe)
    ///   • resmî kurum: hafta içi sabit, hafta sonu ve resmî tatilde kapalı
    ///     (Millî Kütüphane — Madde 4'teki örneğin ta kendisi)
    /// </summary>
    private static readonly (string Isim, string Kategori, MesaiPlani Plan, string Wkt)[] BaslangicPoileri =
    {
        ("Kızılay Kebap Salonu", "Restoran",
            MesaiPlaniKur("11:00", "23:00", haftaSonu: ("12:00", "23:30")),
            "POINT (32.8541 39.9208)"),

        ("Kuğulu Park Kafe", "Kafe",
            SurekliPlan(),
            "POINT (32.8628 39.9042)"),

        ("Tunalı Eczanesi", "Eczane",
            MesaiPlaniKur("09:00", "19:00", haftaSonu: ("10:00", "14:00"), pazarAcik: false),
            "POINT (32.8571 39.9010)"),

        ("Millî Kütüphane", "Kütüphane",
            MesaiPlani.ResmiKurum(),
            "POINT (32.8355 39.9345)"),
    };

    /// <summary>Hafta içi tek saat, hafta sonu (isteğe bağlı) başka saat.</summary>
    private static MesaiPlani MesaiPlaniKur(
        string acilis, string kapanis, (string Acilis, string Kapanis)? haftaSonu = null, bool pazarAcik = true)
    {
        var plan = MesaiPlani.Varsayilan();

        foreach (var gun in plan.Gunler)
        {
            if (gun.Gun <= 5)
            {
                gun.Acik = true;
                gun.Acilis = acilis;
                gun.Kapanis = kapanis;
                continue;
            }

            var acik = haftaSonu is not null && (gun.Gun == 6 || pazarAcik);
            gun.Acik = acik;
            gun.Acilis = acik ? haftaSonu!.Value.Acilis : null;
            gun.Kapanis = acik ? haftaSonu!.Value.Kapanis : null;
        }

        return plan;
    }

    private static MesaiPlani SurekliPlan()
    {
        var plan = MesaiPlani.Varsayilan();
        plan.Tip = MesaiTipi.Surekli;
        return plan;
    }

    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IGeoPermissionRepository _geoPermissionRepository;
    private readonly IIlRepository _ilRepository;
    private readonly IPoiRepository _poiRepository;

    /// <summary>
    /// POI stillerini kategori tablosundan üreten servis (Ödev 13 iyileştirmesi).
    /// Seed sonunda BİR KEZ, sessizce çalıştırılıyor — gerekçe SeedAsync'te.
    /// </summary>
    private readonly IPoiStyleService _poiStilServisi;
    private readonly IPoiCategoryRepository _poiKategoriRepository;
    private readonly IGeometryRepository<PointEntity> _noktaRepo;
    private readonly IGeometryRepository<LineEntity> _cizgiRepo;
    private readonly IGeometryRepository<PolygonEntity> _poligonRepo;
    private readonly PasswordHasher<User> _passwordHasher = new();

    /// <summary>
    /// Geometri depoları <b>anahtarlı</b> isteniyor (Ödev 8).
    ///
    /// Anahtarsız istenseydi GeoServer'lı gerçekleme gelirdi ve uygulama
    /// AÇILIŞTA GeoServer'a bağımlı hâle gelirdi: GeoServer kapalıyken
    /// "örnek envanter var mı?" sorusu bile cevaplanamaz, proje hiç başlamazdı.
    /// Seeder veritabanını HAZIRLAYAN koddur; doğrudan veritabanıyla konuşmalı.
    /// </summary>
    public DatabaseSeeder(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IGeoPermissionRepository geoPermissionRepository,
        IIlRepository ilRepository,
        // Ödev 13'ten sonra POI okuması da GeoServer'a gidiyor; seeder aynı
        // gerekçeyle (aşağıdaki açıklama) veritabanı deposunu istiyor.
        [FromKeyedServices(DataAccessRegistration.VeritabaniDeposu)] IPoiRepository poiRepository,
        IPoiCategoryRepository poiKategoriRepository,
        [FromKeyedServices(DataAccessRegistration.VeritabaniDeposu)] IGeometryRepository<PointEntity> noktaRepo,
        [FromKeyedServices(DataAccessRegistration.VeritabaniDeposu)] IGeometryRepository<LineEntity> cizgiRepo,
        [FromKeyedServices(DataAccessRegistration.VeritabaniDeposu)] IGeometryRepository<PolygonEntity> poligonRepo,
        IPoiStyleService poiStilServisi)
    {
        _poiStilServisi = poiStilServisi;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _geoPermissionRepository = geoPermissionRepository;
        _ilRepository = ilRepository;
        _poiRepository = poiRepository;
        _poiKategoriRepository = poiKategoriRepository;
        _noktaRepo = noktaRepo;
        _cizgiRepo = cizgiRepo;
        _poligonRepo = poligonRepo;
    }

    public async Task SeedAsync()
    {
        // Ödev 10: il sınırları EN BAŞTA. Referans veri; hem coğrafi yetki
        // tanımları hem de yönetim ekranı buna dayanıyor.
        await IlleriYukleAsync();

        await KullaniciEkleAsync();

        // Ödev 6: önce yetki sözlüğü, sonra roller, en sonda kullanıcı-rol ataması.
        // Sıra önemli: rol yetkilerini bağlayabilmek için yetkilerin id'si gerekiyor.
        await YetkileriEkleAsync();

        // Ödev 12: adları GÜNCELLEMEK, rolleri oluşturmaktan ÖNCE gelmeli.
        // Sonra çalışsaydı RolleriEkleAsync "Admin" adında rol bulamayıp
        // yenisini oluşturur, ardından yeniden adlandırma "Admin zaten var"
        // diyerek eski "Yönetici"yi olduğu yerde bırakırdı — iki rol.
        await RolleriYenidenAdlandirAsync();

        await RolleriEkleAsync();
        await AdminRolunuTamamlaAsync();
        await EksikRolYetkileriniEkleAsync();
        await RolAtamalariniYapAsync();

        await DemoEnvanteriEkleAsync();
        await IkinciKullaniciVerisiAsync();

        // Ödev 12: önce kategori sözlüğü, sonra POI'ler. Sıra zorunlu —
        // POI kategorisiz eklenemiyor, kategori id'leri buradan geliyor.
        await PoiKategorileriniEkleAsync();
        await PoiKategoriIkonlariniTamamlaAsync();
        await PoiOrnekleriniEkleAsync();

        // Ödev 14: konum analizinin çalışabileceği hacimde POI verisi.
        // İl sınırlarını kullandığı için IlleriYukleAsync'ten SONRA olmak
        // zorunda; kategori id'lerine ihtiyaç duyduğu için de kategorilerden.
        await AnalizPoiSetiniEkleAsync();

        // Ödev 7: çalışma alanı tanımları. İkinci kullanıcının verisi
        // yüklendikten SONRA çalışmalı — alanlar o veriyi kapsayacak şekilde
        // seçildi, önce çalışırsa kapsadığı bir şey olmaz.
        await CografiYetkiEkleAsync();

        // Ödev 13 iyileştirmesi: kategoriler yerine oturduktan SONRA POI
        // stillerini GeoServer'a yaz.
        //
        // NEDEN SEED'İN İÇİNDE? Sıfırdan kurulan bir makinede kimse
        // "Stilleri yenile" düğmesine basmadan haritanın doğru görünmesini
        // istiyoruz. Kategoriler bu metotta oluşuyor; stiller onlardan
        // türüyor, yani doğru sıra burası.
        //
        // NEDEN SESSİZ? Uygulamanın AÇILIŞI GeoServer'a bağlanamaz. Sunucu
        // kapalıysa (ya da katman henüz yayımlanmadıysa) proje yine de
        // açılmalı; eksik kalan tek şey haritadaki simgeler olur ve yönetici
        // panelden tamamlayabilir. Bu, seeder'ın veritabanı deposunu
        // anahtarla istemesiyle aynı ilkenin devamı.
        await _poiStilServisi.SessizYenileAsync();
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
    /// Ödev 12 / Madde 1: üç temel rolün adını günceller (Yönetici → Admin,
    /// Editör → Operatör, Görüntüleyici → Kullanıcı).
    ///
    /// Neden yeni rol eklemek yerine YENİDEN ADLANDIRMA?
    /// Rol atamaları (user_roles), rol yetkileri (role_permissions) ve coğrafi
    /// yetki tanımları (geo_permissions.role_id) hep rolün ID'sine bağlı.
    /// Yeni rol açıp eskisini silseydik bu üç tablodaki bağların hepsini elle
    /// taşımak gerekirdi; tek bir UPDATE ile ad değiştiğinde ise bağların
    /// hiçbirine dokunulmuyor — kullanıcılar rollerinde, alanlar yerinde kalıyor.
    ///
    /// Hedef ad ZATEN VARSA dokunulmuyor: ikinci kez çalıştığında (her açılışta
    /// çalışıyor) yapacak bir iş kalmıyor, ayrıca yönetici panelden yeni bir
    /// "Admin" rolü açtıysa onun üstüne yazılmıyor.
    /// </summary>
    private async Task RolleriYenidenAdlandirAsync()
    {
        foreach (var (eskiAd, yeniAd) in RolAdiDegisiklikleri)
        {
            if (await _roleRepository.GetByNameAsync(yeniAd) is not null)
            {
                continue;   // ad zaten güncel (ya da elle açılmış bir rol var)
            }

            var rol = await _roleRepository.GetByNameAsync(eskiAd);
            if (rol is null)
            {
                continue;   // eski rol de yok — temiz kurulum, RolleriEkleAsync oluşturacak
            }

            // Açıklama ve aktiflik OLDUĞU GİBİ geçiyor: burada yapılan iş
            // sadece ad değişikliği. UpdateAsync verilen alanların hepsini
            // yazdığı için mevcut değerleri geri göndermek zorundayız.
            await _roleRepository.UpdateAsync(new Entities.Role
            {
                Id = rol.Id,
                Name = yeniAd,
                Description = rol.Description,
                IsActive = rol.IsActive,
            });
        }
    }

    /// <summary>
    /// Başlangıç rollerini oluşturur. Rol zaten varsa YETKİLERİNE DOKUNULMAZ:
    /// jüri gösteriminde panelden "Operatör"e yeni bir yetki eklendiğinde,
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
    /// Admin rolüne, sistemde tanımlı AMA rolde eksik olan yetkileri ekler.
    ///
    /// Bu, "var olan role dokunma" kuralının BİLİNÇLİ tek istisnası. Sebebi şu:
    /// projeye yeni bir yetki eklendiğinde (Ödev 7'deki "Coğrafi Yetki Tanımlama"
    /// gibi) Admin rolü zaten var olduğu için seed ona dokunmaz; sonuç olarak
    /// yeni özelliğe sistemdeki HİÇ KİMSE erişemez ve düzeltmenin tek yolu
    /// veritabanına elle müdahale olurdu.
    ///
    /// Yalnızca EKLER, hiçbir zaman kaldırmaz. Operatör ve Kullanıcı rollerine
    /// dokunulmaz — onların kapsamı bilinçli olarak dardır.
    /// </summary>
    private async Task AdminRolunuTamamlaAsync()
    {
        var rol = await _roleRepository.GetByNameAsync(AdminRolu);
        if (rol is null)
        {
            return;   // az önce oluşturulduysa zaten tam yetkili
        }

        var tumYetkiIdleri = (await _permissionRepository.GetAllAsync())
            .Select(y => y.Id)
            .ToList();

        var mevcutIdler = rol.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
        if (tumYetkiIdleri.All(mevcutIdler.Contains))
        {
            return;   // eksik yok, gereksiz yazma yapma
        }

        await _roleRepository.SetPermissionsAsync(rol.Id, tumYetkiIdleri);
    }

    /// <summary>
    /// <see cref="SonradanEklenenYetkiler"/> listesindeki eksikleri tamamlar.
    ///
    /// Rol yoksa (temiz kurulum) hiçbir şey yapmaz: o durumda rolü
    /// <see cref="RolleriEkleAsync"/> zaten tam yetkiyle oluşturuyor.
    /// </summary>
    private async Task EksikRolYetkileriniEkleAsync()
    {
        var tumYetkiler = await _permissionRepository.GetAllAsync();

        foreach (var (rolAdi, yetkiAdlari) in SonradanEklenenYetkiler)
        {
            var rol = await _roleRepository.GetByNameAsync(rolAdi);
            if (rol is null)
            {
                continue;
            }

            var mevcutIdler = rol.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();

            var eklenecekler = tumYetkiler
                .Where(y => yetkiAdlari.Contains(y.Name) && !mevcutIdler.Contains(y.Id))
                .Select(y => y.Id)
                .ToList();

            if (eklenecekler.Count == 0)
            {
                continue;   // eksik yok, gereksiz yazma yapma
            }

            // Birleşim yazıyoruz: SetPermissionsAsync listeyi OLDUĞU GİBİ
            // uyguluyor, yalnızca eksikleri gönderseydik rolün diğer
            // yetkileri silinirdi.
            await _roleRepository.SetPermissionsAsync(rol.Id, mevcutIdler.Concat(eklenecekler).ToList());
        }
    }

    /// <summary>
    /// Demo kullanıcılarını rollerine bağlar:
    /// admin → Admin, ayşe → Operatör, mehmet → Kullanıcı.
    ///
    /// Üç hesap, ödevin istediği üç rolün TAMAMINI ekranda gösterilebilir
    /// kılıyor. Üçüncüsü olmadan "Kullanıcı rolü ne görüyor?" sorusunun
    /// cevabı yalnızca kodda kalıyordu (bkz. <see cref="UcuncuKullanici"/>).
    ///
    /// Yalnızca kullanıcının HİÇ rolü yoksa atama yapılır. Aksi hâlde panelden
    /// "admin'in rolünü değiştir" denemesi her yeniden başlatmada geri alınırdı.
    /// admin'e Admin rolünün verilmesi ayrıca zorunlu: yönetim uçları
    /// "Kullanıcı Yönetimi" yetkisi istiyor, o da bu rolden geliyor.
    /// </summary>
    private async Task RolAtamalariniYapAsync()
    {
        await RolAtaAsync(DemoKullanici, AdminRolu);
        await RolAtaAsync(IkinciKullanici, OperatorRolu);
        await RolAtaAsync(UcuncuKullanici, KullaniciRolu);

        // Ödev 16
        await RolAtaAsync(UlasimOperatoruKullanici, UlasimOperatoruRolu);
        await RolAtaAsync(UlasimKullanicisiKullanici, UlasimKullanicisiRolu);
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
        await KullaniciYoksaEkleAsync(UcuncuKullanici, UcuncuSifre);

        // Ödev 16: ulaşım modülünün iki demo hesabı — rollerin
        // "yetkisi yok" tarafını ekranda gösterebilmek için.
        await KullaniciYoksaEkleAsync(UlasimOperatoruKullanici, UlasimSifre);
        await KullaniciYoksaEkleAsync(UlasimKullanicisiKullanici, UlasimSifre);
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

    // ---------- Ödev 10: il sınırları ----------

    /// <summary>
    /// 81 ilin sınırlarını yükler — YALNIZCA tablo boşsa.
    ///
    /// Veri kaynağı gömülü bir GeoJSON dosyası (Apache 2.0 lisanslı, bkz.
    /// README). Dosya yolu aramak yerine gömülü kaynak kullanılmasının sebebi:
    /// çalışma dizini geliştirme ve yayın ortamında farklı, "dosya bulunamadı"
    /// hatası sessizce oluşurdu.
    ///
    /// Bölge bilgisi dosyada YOK; plaka koduna göre <see cref="Bolgeler"/>
    /// tablosundan atanıyor.
    /// </summary>
    private async Task IlleriYukleAsync()
    {
        if (await _ilRepository.SayAsync() > 0)
        {
            return;
        }

        var govde = GomuluKaynagiOku(IlVerisiKaynagi);
        if (govde is null)
        {
            // Sessizce geçiyoruz: il verisi olmadan da uygulama çalışır,
            // yalnızca il/bölge seçimi kullanılamaz. Açılışı engellemek
            // orantısız olurdu.
            return;
        }

        // idZorunlu: false — bu dosyada kaydın kimliği "id" alanında değil,
        // "number" (plaka) özelliğinde.
        var kayitlar = GeoJsonOkuyucu.Oku(govde, idZorunlu: false);

        var iller = kayitlar
            .Select(k => new Il
            {
                Id = k.Tamsayi("number") ?? 0,
                Ad = k.Metin("name") ?? string.Empty,
                Geom = k.Geometry!,
            })
            .Where(i => i.Id is >= 1 and <= 81 && i.Ad.Length > 0 && i.Geom is not null)
            .ToList();

        foreach (var il in iller)
        {
            il.Bolge = Bolgeler.BolgeBul(il.Id);
            il.Geom.SRID = WktConverter.Srid;
        }

        if (iller.Count > 0)
        {
            await _ilRepository.EkleAsync(iller);
        }
    }

    /// <summary>Derlemeye gömülü metin kaynağını okur; yoksa null.</summary>
    private static string? GomuluKaynagiOku(string ad)
    {
        using var akis = typeof(DatabaseSeeder).Assembly.GetManifestResourceStream(ad);
        if (akis is null)
        {
            return null;
        }

        using var okuyucu = new StreamReader(akis);
        return okuyucu.ReadToEnd();
    }

    // ---------- Ödev 7: coğrafi yetki tohumlaması ----------

    /// <summary>
    /// Kullanıcı ve rol bazlı çizim alanlarını yükler — YALNIZCA tablo tamamen boşsa.
    ///
    /// Neden seed'e kondu? Bu kural veri olmadan GÖSTERİLEMEZ: tanım yoksa
    /// "kısıt yok" demektir ve kullanıcı her yere çizebilir. Yeni kurulan bir
    /// makinede ya da veritabanı sıfırlandığında Ödev 7 sessizce görünmez
    /// hâle geliyordu; artık başlangıç verisinin parçası.
    ///
    /// İKİ FARKLI SAHİP TİPİ bilerek seçildi. <c>geo_permissions</c> tablosunda
    /// "YA kullanıcı YA rol" CHECK kısıtı var; ikisini birden tohumlamak hem
    /// kısıtın iki dalını da örnekliyor hem de çalışma alanının BİRLEŞİM
    /// olduğunu ekranda iki ayrı çerçeve olarak gösteriyor.
    ///
    /// Alanlar AYRIK seçildi (Ege/Akdeniz ile Ankara): üst üste binselerdi
    /// birleşim kuralı tek çerçeve gibi görünür, anlatılamazdı.
    /// </summary>
    private async Task CografiYetkiEkleAsync()
    {
        // Tabloda tek bir satır bile varsa dokunma: panelden tanımlanmış
        // kuralları her açılışta yeniden üretmek ya da ezmek kabul edilemez.
        if ((await _geoPermissionRepository.GetAllAsync()).Count > 0)
        {
            return;
        }

        var kullanici = await _userRepository.GetByUsernameAsync(IkinciKullanici);
        var rol = await _roleRepository.GetByNameAsync(OperatorRolu);

        // Ayşe'nin örnek verisi Ege/Akdeniz kıyısında; alan onu kapsıyor.
        if (kullanici is not null)
        {
            await CografiYetkiYazAsync(
                "Ege–Akdeniz kıyı şeridi",
                userId: kullanici.Id,
                roleId: null,
                wkt: "POLYGON ((27.6 35.7, 31.9 35.7, 31.9 37.5, 27.6 37.5, 27.6 35.7))");
        }

        // Rolden gelen alan: Operatör rolündeki HERKES buraya da çizebilir.
        if (rol is not null)
        {
            await CografiYetkiYazAsync(
                "Ankara çalışma sahası",
                userId: null,
                roleId: rol.Id,
                wkt: "POLYGON ((32.3 39.5, 33.4 39.5, 33.4 40.3, 32.3 40.3, 32.3 39.5))");
        }
    }

    private Task CografiYetkiYazAsync(string ad, int? userId, int? roleId, string wkt)
        => _geoPermissionRepository.AddAsync(new GeoPermission
        {
            Name = ad,
            UserId = userId,
            RoleId = roleId,
            // WktConverter tipi doğrular ve SRID'yi 4326'ya sabitler.
            Geom = WktConverter.Read<Polygon>(wkt),
            InsertedDate = DateTime.UtcNow,
        });

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

    // ---------- Ödev 12: POI kategorileri ve örnek POI'ler ----------

    /// <summary>
    /// Hiyerarşik kategori sözlüğünü yükler — YALNIZCA tablo tamamen boşsa.
    ///
    /// Neden seed'e kondu? Kategori olmadan POI EKLENEMEZ (kategori zorunlu
    /// alan). Boş bir kurulumda operatör POI aracını açtığında karşısına boş
    /// bir açılır liste çıkar ve modül denenemez hâlde kalırdı. Yönetici
    /// panelden istediğini ekleyip silebiliyor; buradaki sadece başlangıç.
    ///
    /// Tabloda tek satır bile varsa dokunulmuyor: panelden düzenlenmiş bir
    /// ağacın her açılışta yeniden üretilmesi ya da ezilmesi kabul edilemez.
    /// </summary>
    private async Task PoiKategorileriniEkleAsync()
    {
        if ((await _poiKategoriRepository.GetAllAsync()).Count > 0)
        {
            return;
        }

        foreach (var (kokAd, kokIkon, altlar) in BaslangicKategorileri)
        {
            var kok = await _poiKategoriRepository.AddAsync(new PoiCategory
            {
                Ad = kokAd,
                Ikon = kokIkon,
                CreatedDate = DateTime.UtcNow,
            });

            foreach (var (altAd, altIkon) in altlar)
            {
                await _poiKategoriRepository.AddAsync(new PoiCategory
                {
                    Ad = altAd,
                    Ikon = altIkon,
                    // Ata-çocuk bağı: kökün id'si ancak KAYDEDİLDİKTEN sonra
                    // belli oluyor, o yüzden alt kategoriler ikinci turda.
                    ParentId = kok.Id,
                    CreatedDate = DateTime.UtcNow,
                });
            }
        }
    }

    /// <summary>
    /// SIMGESİ OLMAYAN başlangıç kategorilerine simge atar (Ödev 15).
    ///
    /// NEDEN AYRI BİR ADIM? <see cref="PoiKategorileriniEkleAsync"/> yalnızca
    /// tablo TAMAMEN BOŞSA çalışıyor — panelden düzenlenmiş bir ağacı her
    /// açılışta ezmesin diye. Ama bu, Ödev 12'den beri çalışan bir
    /// kurulumda yeni gelen <c>ikon</c> kolonunun sonsuza kadar boş kalması
    /// demek: bütün POI'ler varsayılan iğneyle çizilirdi ve ödev, "yeni
    /// kurulumda çalışıyor ama bende çalışmıyor" diye görünürdü.
    /// Aynı sorunun rol tarafındaki çözümü: <see cref="EksikRolYetkileriniEkleAsync"/>.
    ///
    /// YALNIZCA BOŞ OLANI DOLDURUR: yöneticinin panelden seçtiği bir simge
    /// asla geri alınmıyor.
    /// </summary>
    private async Task PoiKategoriIkonlariniTamamlaAsync()
    {
        var kategoriler = await _poiKategoriRepository.GetAllAsync();
        if (kategoriler.Count == 0)
        {
            return;
        }

        // Ad → simge sözlüğü: kökler ve alt kategoriler birlikte.
        // Ağaçtaki YERİNE değil ADINA bakıyoruz — yönetici "Eczane"yi başka
        // bir kökün altına taşımış olabilir, simgesi yine geçerli.
        var ikonlar = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var (kok, kokIkon, altlar) in BaslangicKategorileri)
        {
            ikonlar[kok] = kokIkon;
            foreach (var (ad, ikon) in altlar)
            {
                ikonlar[ad] = ikon;
            }
        }

        foreach (var kategori in kategoriler)
        {
            if (!string.IsNullOrWhiteSpace(kategori.Ikon)) continue;
            if (!ikonlar.TryGetValue(kategori.Ad, out var ikon)) continue;

            kategori.Ikon = ikon;
            await _poiKategoriRepository.UpdateAsync(kategori);
        }

        // Stilleri BURADA yenilemiyoruz: SeedAsync zaten en sonda
        // _poiStilServisi.SessizYenileAsync() çağırıyor ve o çağrı güncel
        // simgeleri okuyor. İki kez yenilemek her açılışta boşuna bir tur
        // GeoServer yazması demek olurdu.
    }

    /// <summary>
    /// Örnek POI'leri yükler — YALNIZCA tablo boşsa.
    ///
    /// Sahibi ikinci kullanıcı ("ayse", Operatör rolünde): POI ekleme
    /// operatörün işi olduğu için örnek verinin de operatöre ait olması,
    /// admin panelindeki "ekleyen kullanıcı" sütununu ilk açılışta anlamlı
    /// kılıyor. Kullanıcı yoksa POI de eklenmiyor — sahipsiz örnek veri,
    /// göstermek istediğimiz şeyin tam tersini gösterirdi.
    /// </summary>
    private async Task PoiOrnekleriniEkleAsync()
    {
        if ((await _poiRepository.GetAllAsync()).Count > 0)
        {
            return;
        }

        var kullanici = await _userRepository.GetByUsernameAsync(IkinciKullanici);
        if (kullanici is null)
        {
            return;
        }

        // Kategoriyi ADIYLA arıyoruz: id'ler veritabanına göre değişir,
        // ada göre eşleme her kurulumda aynı sonucu verir.
        var kategoriler = await _poiKategoriRepository.GetAllAsync();

        foreach (var (isim, kategoriAdi, plan, wkt) in BaslangicPoileri)
        {
            var kategori = kategoriler.FirstOrDefault(k => k.Ad == kategoriAdi);
            if (kategori is null)
            {
                continue;   // kategori ağacı elle değiştirilmişse o POI'yi atla
            }

            // Doğrulanmış plan + ondan üretilen özet metin. Özeti elle yazmıyoruz:
            // seed verisi de uygulamanın kendi kuralından geçsin ki ekrandaki
            // metinle kullanıcının gireceği metin aynı biçimde olsun.
            var dogrulanmis = plan.Dogrula();

            await _poiRepository.AddAsync(new Poi
            {
                Isim = isim,
                KategoriId = kategori.Id,
                MesaiSaatleri = dogrulanmis.OzetMetin(),
                MesaiPlani = dogrulanmis.Serilestir(),
                Geom = WktConverter.Read<Point>(wkt),
                UserId = kullanici.Id,
                CreatedDate = DateTime.UtcNow,
            });
        }
    }

    // ---------- Ödev 14: analiz veri seti ----------

    /// <summary>
    /// Konum analizi için hacimli POI verisi üretir (Ödev 14).
    ///
    /// Ödev metni: "Bu analiz için POI verisi oluşturun, ne kadar çok veri
    /// olursa o kadar iyi sonuç verecektir." Isı haritası bir YOĞUNLUK
    /// yüzeyi olduğu için dört örnek POI ile anlamlı bir sonuç çıkmıyor;
    /// üretimin gerekçesi ve yöntemi <see cref="AnalizPoiUretici"/> başlığında.
    ///
    /// NE ZAMAN ÇALIŞIR? Tabloda <see cref="AnalizPoiUretici.UretimEsigi"/>
    /// sayısından az POI varsa. "Tablo boş mu?" demiyoruz çünkü Ödev 12'nin
    /// dört örnek POI'si zaten orada; "her açılışta" da demiyoruz çünkü ikinci
    /// açılışta veri iki katına çıkardı. Eşik, üretilen setin çok altında ve
    /// örnek verinin çok üstünde: tam bir kez çalışıyor.
    ///
    /// İL VERİSİ YOKSA sessizce hiçbir şey yapmıyor. Testlerdeki sahte il
    /// deposu boş geliyor; üretim orada çalışsaydı POI testleri, hiç
    /// beklemedikleri binlerce kayıtla karşılaşırdı.
    /// </summary>
    private async Task AnalizPoiSetiniEkleAsync()
    {
        if ((await _poiRepository.GetAllAsync()).Count >= AnalizPoiUretici.UretimEsigi)
        {
            return;
        }

        var iller = await _ilRepository.SinirlariGetirAsync();
        if (iller.Count == 0)
        {
            return;
        }

        // Kategori ADIYLA eşleniyor (örnek POI'lerdeki kuralın aynısı):
        // id'ler kuruluma göre değişir, ad her kurulumda aynıdır.
        //
        // Aynı ad birden çok kez geçebilir mi? Kategori servisi, aynı ata
        // altında aynı ada izin vermiyor; farklı atalar altında ("Yeme-İçme ›
        // Diğer" ile "Konaklama › Diğer") verebiliyor. Bu yüzden ilkini
        // alıyoruz — profillerdeki adlar zaten başlangıç ağacındaki benzersiz
        // yaprak adları.
        var kategoriIdleri = new Dictionary<string, int>();
        foreach (var kategori in await _poiKategoriRepository.GetAllAsync())
        {
            kategoriIdleri.TryAdd(kategori.Ad, kategori.Id);
        }

        var uretilenler = AnalizPoiUretici.Uret(iller, kategoriIdleri);
        if (uretilenler.Count == 0)
        {
            return;
        }

        // Toplu ekleme: tek SaveChanges. Tek tek eklemek binlerce ağ
        // gidiş-gelişi demek olurdu ve ilk açılışı dakikalara çıkarırdı.
        await _poiRepository.TopluEkleAsync(uretilenler);
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
