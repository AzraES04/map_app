using Microsoft.EntityFrameworkCore;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Validation;
using StajProject.DataAccess.Repositories;

namespace StajProject.Business.Services;

public interface ICopKutusuService
{
    /// <summary>
    /// Çöp kutusunun tamamı — bütün türlerden silinmiş kayıtlar, özetiyle.
    ///
    /// LİSTE HERKESE AÇIK (giriş yapmış olmak yeterli). Geri ALMA yetkiye
    /// bağlı ve her kaydın <c>GeriAlinabilir</c> alanı bunu söylüyor.
    ///
    /// Neden listeyi de kısıtlamadık? Silinmiş bir kaydın adı, zaten
    /// silinmeden önce herkesin görebildiği bir bilgiydi; listeyi gizlemek
    /// yeni bir şey korumazdı ama "ne silindi?" sorusunu cevapsız bırakırdı.
    /// </summary>
    Task<CopKutusuDto> GetirAsync();

    /// <summary>
    /// Bir kaydı geri alır. Yetkisi yoksa <see cref="IsKuraliException"/>,
    /// kayıt yoksa false.
    /// </summary>
    Task<bool> GeriAlAsync(string tur, int id);

    /// <summary>
    /// Bir kaydı KALICI OLARAK siler — geri alma yok. Aynı yetki kuralı
    /// geçerli (silen yetki neyse, kalıcı silmek de onu ister).
    ///
    /// Kayıt başka bir tabloya referans veriliyorsa (örn. duraklı bir
    /// güzergah) <see cref="Validation.IsKuraliException"/> fırlatır —
    /// önce bağımlı kayıtların çözülmesi gerektiğini söyler.
    /// </summary>
    Task<bool> KaliciSilAsync(string tur, int id);

    /// <summary>
    /// SAKLAMA SÜRESİ DOLAN kayıtları kalıcı siler — otomatik temizlik.
    ///
    /// Kullanıcı adına değil, SİSTEM adına çalışıyor: bu yüzden yetki
    /// kontrolü YOK. Kontrol koysaydık, arka plan görevinin oturumu
    /// olmadığı için hiçbir kaydı silemezdi — ya da uydurma bir "sistem
    /// kullanıcısı" yaratmak gerekirdi ki o da yetki modelinde karşılığı
    /// olmayan bir kimlik olurdu.
    ///
    /// Bağlı kaydı olan (yabancı anahtar) kayıtlar ATLANIYOR, hata
    /// vermiyor: tek bir bağımlı kayıt yüzünden temizliğin tamamının
    /// durması, çöp kutusunun sonsuza kadar dolu kalması demekti.
    /// </summary>
    /// <returns>(silinen, atlanan) sayıları.</returns>
    Task<(int Silinen, int Atlanan)> SuresiDolanlariTemizleAsync();
}

/// <summary>
/// Çöp kutusu — silinen her kaydın geri getirilebildiği tek ekran.
///
/// ---- YETKİ KURALI ----
///
/// Geri alma, SİLMENİN TERSİ bir işlem; dolayısıyla silmek için hangi yetki
/// gerekiyorsa geri almak için de o gerekiyor. Yeni bir "Çöp Kutusu Yönetimi"
/// yetkisi UYDURULMADI: uydursaydık, o yetkiye sahip biri silemeyeceği bir
/// kaydı geri alabilir hâle gelirdi ve yetki sistemi kendi içinde tutarsız
/// olurdu.
///
/// Eşleme <see cref="TurYetkileri"/>'nde ve tek bir yerde duruyor.
/// </summary>
public class CopKutusuService : ICopKutusuService
{
    /// <summary>
    /// Tür → geri almak için gereken yetki.
    ///
    /// Kaynağı, o türü SİLEN ucun istediği yetki:
    ///   geometri  → Kayıt Silme      (GeometryControllerBase.Delete)
    ///   poi       → POI Yönetimi     (sahiplik alternatifi aşağıda)
    ///   kategori  → POI Yönetimi     (AdminPoiCategoriesController)
    ///   durak     → Güzergah Yönetimi
    ///   guzergah  → Güzergah Yönetimi
    ///   kullanıcı → Kullanıcı Yönetimi
    ///   rol       → Rol Yönetimi
    /// </summary>
    /// <summary>
    /// Çöp kutusunda bir kaydın KALMA SÜRESİ.
    ///
    /// Otuz gün: bir kaydın yanlışlıkla silindiğinin fark edilmesi için
    /// fazlasıyla yeterli (yıllık izin, uzun bir tatil bile bu aralığa
    /// giriyor), ama silinen verinin sonsuza kadar taşınmasını da
    /// engelliyor. Süre dolduğunda kayıt arka plan görevi tarafından
    /// kalıcı siliniyor (bkz. CopKutusuTemizleyici).
    ///
    /// Ayardan değil KODDAN geliyor: bu bir dağıtım tercihi değil, veri
    /// saklama kuralı. Ayara açsaydık her kurulumda farklı davranan ve
    /// kullanıcıya "30 gün" diye söz veremeyeceğimiz bir sistem olurdu.
    /// </summary>
    public static readonly TimeSpan SaklamaSuresi = TimeSpan.FromDays(30);

    private static readonly Dictionary<string, string> TurYetkileri = new()
    {
        [CopKutusuRepository.Nokta] = Yetkiler.KayitSilme,
        [CopKutusuRepository.Cizgi] = Yetkiler.KayitSilme,
        [CopKutusuRepository.Poligon] = Yetkiler.KayitSilme,
        [CopKutusuRepository.Poi] = Yetkiler.PoiYonetimi,
        [CopKutusuRepository.Kategori] = Yetkiler.PoiYonetimi,
        [CopKutusuRepository.Durak] = Yetkiler.GuzergahYonetimi,
        [CopKutusuRepository.Guzergah] = Yetkiler.GuzergahYonetimi,
        [CopKutusuRepository.Kullanici] = Yetkiler.KullaniciYonetimi,
        [CopKutusuRepository.Rol] = Yetkiler.RolYonetimi,
    };

    /// <summary>Tür → insan için ad. Arayüzde sekme başlığı olarak görünüyor.</summary>
    private static readonly Dictionary<string, string> TurAdlari = new()
    {
        [CopKutusuRepository.Nokta] = "Nokta",
        [CopKutusuRepository.Cizgi] = "Çizgi",
        [CopKutusuRepository.Poligon] = "Alan",
        [CopKutusuRepository.Poi] = "POI",
        [CopKutusuRepository.Kategori] = "POI kategorisi",
        [CopKutusuRepository.Durak] = "Durak",
        [CopKutusuRepository.Guzergah] = "Güzergah",
        [CopKutusuRepository.Kullanici] = "Kullanıcı",
        [CopKutusuRepository.Rol] = "Rol",
    };

    private readonly ICopKutusuRepository _repository;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserService _currentUser;

    public CopKutusuService(
        ICopKutusuRepository repository,
        IPermissionService permissionService,
        ICurrentUserService currentUser)
    {
        _repository = repository;
        _permissionService = permissionService;
        _currentUser = currentUser;
    }

    public async Task<CopKutusuDto> GetirAsync()
    {
        var kayitlar = await _repository.ListeleAsync();
        var kullaniciId = _currentUser.RequireUserId();

        // Yetkiler TÜR BAŞINA bir kez soruluyor.
        //
        // Her kayıt için ayrı sormak, yüz silinmiş POI'de yüz kez aynı
        // veritabanı sorgusu demekti (N+1). Tür sayısı sabit ve dokuz.
        var yetkiler = new Dictionary<string, bool>();
        foreach (var (tur, yetki) in TurYetkileri)
        {
            yetkiler[tur] = await _permissionService.HasPermissionAsync(kullaniciId, yetki);
        }

        var ogeler = kayitlar.Select(k => new CopOgesiDto
        {
            Tur = k.Tur,
            TurAdi = TurAdlari.GetValueOrDefault(k.Tur, k.Tur),
            Id = k.Id,
            // Adsız kayıt mümkün (adı boş bırakılmış bir çizim). Boş bir satır
            // listede tıklanamaz bir boşluk gibi durur.
            Ad = string.IsNullOrWhiteSpace(k.Ad) ? "(isimsiz)" : k.Ad,
            Detay = k.Detay,
            SilinmeZamani = k.SilinmeZamani,
            Ekleyen = k.Ekleyen,
            GeriAlinabilir = yetkiler.GetValueOrDefault(k.Tur),
            // Kalan gün SUNUCUDA hesaplanıyor: istemcide hesaplasaydık
            // tarayıcının saati (ve saat dilimi) sonucu değiştirirdi —
            // "3 gün kaldı" diyen ekranla kaydı silen görev ayrışırdı.
            KalanGun = KalanGunHesapla(k.SilinmeZamani),
        }).ToList();

        return new CopKutusuDto
        {
            Ogeler = ogeler,
            // Özet, LİSTEDEN türetiliyor — ayrıca sayım sorgusu atmıyoruz.
            // İki kaynak olsaydı "özet 3 diyor ama listede 2 var" gibi bir
            // tutarsızlık mümkün olurdu.
            Ozet = ogeler
                .GroupBy(o => o.Tur)
                .Select(g => new CopOzetiDto
                {
                    Tur = g.Key,
                    TurAdi = g.First().TurAdi,
                    Adet = g.Count(),
                })
                .OrderByDescending(o => o.Adet)
                .ToList(),
        };
    }

    public async Task<bool> GeriAlAsync(string tur, int id)
    {
        if (!TurYetkileri.TryGetValue(tur, out var gerekenYetki))
        {
            throw new IsKuraliException($"Bilinmeyen kayıt türü: \"{tur}\".");
        }

        var kullaniciId = _currentUser.RequireUserId();

        if (!await _permissionService.HasPermissionAsync(kullaniciId, gerekenYetki))
        {
            // Mesaj hangi yetkinin gerektiğini SÖYLÜYOR. Gizlemek, kullanıcıyı
            // "neden olmuyor?" diye yöneticiye göndermekten başka işe
            // yaramazdı; yetki adları zaten rol ekranında herkese görünür.
            throw new IsKuraliException(
                $"Bu kaydı geri almak için \"{gerekenYetki}\" yetkisi gerekiyor.");
        }

        return await _repository.GeriAlAsync(tur, id);
    }

    public async Task<bool> KaliciSilAsync(string tur, int id)
    {
        if (!TurYetkileri.TryGetValue(tur, out var gerekenYetki))
        {
            throw new IsKuraliException($"Bilinmeyen kayıt türü: \"{tur}\".");
        }

        var kullaniciId = _currentUser.RequireUserId();

        if (!await _permissionService.HasPermissionAsync(kullaniciId, gerekenYetki))
        {
            throw new IsKuraliException(
                $"Bu kaydı kalıcı silmek için \"{gerekenYetki}\" yetkisi gerekiyor.");
        }

        try
        {
            return await _repository.KaliciSilAsync(tur, id);
        }
        // İKİ İSTİSNA TİPİ: gerçek veritabanında (PostgreSQL) yabancı anahtar
        // ihlali DbUpdateException olarak geliyor; EF'in InMemory sağlayıcısı
        // (testlerde) aynı durumu InvalidOperationException ile bildiriyor.
        // İkisi de aynı anlama geliyor: "bu kayda hâlâ referans veren başka
        // kayıtlar var."
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            // Yabancı anahtar ihlali: bu kayda hâlâ referans veren başka
            // kayıtlar var (örn. duraklı bir güzergah, POI'li bir kategori).
            // Ham veritabanı hatasını göstermek yerine ne yapılması
            // gerektiğini söylüyoruz.
            throw new IsKuraliException(
                $"{TurAdlari.GetValueOrDefault(tur, tur)} kalıcı silinemedi: " +
                "başka kayıtlar hâlâ ona bağlı. Önce onları silin ya da " +
                "farklı bir kayda taşıyın.");
        }
    }

    public async Task<(int Silinen, int Atlanan)> SuresiDolanlariTemizleAsync()
    {
        var sinir = DateTime.UtcNow - SaklamaSuresi;

        // Listeyi olduğu gibi alıp süzüyoruz: çöp kutusu doğası gereği
        // küçük bir liste (silinmiş kayıtlar) ve depoya dokuz tür için
        // ayrı bir toplu silme yolu yazmak, var olan ve YABANCI ANAHTAR
        // davranışı zaten sınanmış tek kayıtlık yolu ikinci kez
        // gerçeklemek olurdu.
        var kayitlar = await _repository.ListeleAsync();

        // SilinmeZamani NULL olanlar dokunulmadan bırakılıyor: zamanı
        // bilinmeyen bir kaydın süresinin dolduğunu iddia edemeyiz
        // (Ödev 3 öncesinden kalan kayıtlarda bu alan boş olabiliyor).
        var suresiDolanlar = kayitlar
            .Where(k => k.SilinmeZamani is not null && k.SilinmeZamani < sinir)
            .ToList();

        var silinen = 0;
        var atlanan = 0;

        foreach (var kayit in suresiDolanlar)
        {
            try
            {
                if (await _repository.KaliciSilAsync(kayit.Tur, kayit.Id))
                {
                    silinen++;
                }
            }
            // Bağlı kayıt varsa (yabancı anahtar) bu kayıt bu turda
            // silinemiyor. Sessizce atlanıyor: bağımlı kayıtların kendi
            // süresi de dolduğunda sıra ona da gelecek.
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
            {
                atlanan++;
            }
        }

        return (silinen, atlanan);
    }

    /// <summary>
    /// Kalıcı silinmesine kaç gün kaldı? Zamanı bilinmiyorsa null.
    ///
    /// Yukarı yuvarlanıyor (Ceiling): 0,2 gün kalmışsa kullanıcı "1 gün"
    /// görsün, "0 gün" değil — sıfır, kaydın çoktan gitmiş olduğunu
    /// düşündürürdü. Süresi geçmiş ama henüz temizlenmemiş kayıtlarda
    /// (görev günde bir çalışıyor) 0 dönüyor: "bugün silinecek".
    /// </summary>
    private static int? KalanGunHesapla(DateTime? silinmeZamani)
    {
        if (silinmeZamani is null)
        {
            return null;
        }

        var kalan = silinmeZamani.Value + SaklamaSuresi - DateTime.UtcNow;
        return kalan <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(kalan.TotalDays);
    }
}
