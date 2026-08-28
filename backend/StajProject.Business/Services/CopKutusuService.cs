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
}
