using System.Text.RegularExpressions;
using NetTopologySuite.Geometries;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Validation;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

public class UlasimService : IUlasimService
{
    /// <summary>
    /// Renk biçimi — "#rrggbb".
    ///
    /// Aynı kontrol DTO'da <c>[RegularExpression]</c> olarak da var. Tekrar
    /// değil, iki farklı kapı: öznitelik HTTP isteğini alan bazlı bir hatayla
    /// karşılıyor, buradaki kural servisi doğrudan çağıran testlerde ve
    /// ileride eklenecek başka çağıranlarda da geçerli.
    /// </summary>
    private static readonly Regex RenkBicimi = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    private readonly IUlasimRepository _repository;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;
    private readonly IGeoPermissionService _geoPermission;

    public UlasimService(
        IUlasimRepository repository,
        ICurrentUserService currentUser,
        IPermissionService permissionService,
        IGeoPermissionService geoPermission)
    {
        _repository = repository;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _geoPermission = geoPermission;
    }

    // ======================================================================
    //  Güzergah
    // ======================================================================

    public async Task<List<GuzergahDto>> GuzergahlariGetirAsync()
        => (await _repository.GuzergahlariGetirAsync()).Select(DtoyaCevir).ToList();

    public async Task<GuzergahDto?> GuzergahGetirAsync(int id)
    {
        var guzergah = await _repository.GuzergahGetirAsync(id);
        return guzergah is null ? null : DtoyaCevir(guzergah);
    }

    public async Task<GuzergahDto> GuzergahEkleAsync(GuzergahSaveDto dto)
    {
        var ad = AdiDogrula(dto.Ad, "Güzergah");
        var renk = RengiDogrula(dto.Renk);
        await AdBenzersizMiAsync(ad, haricId: null);

        var olusan = await _repository.GuzergahEkleAsync(new Guzergah
        {
            Ad = ad,
            Renk = renk,
            Aciklama = dto.Aciklama?.Trim(),
            IsActive = dto.IsActive,
            UserId = _currentUser.RequireUserId(),
            CreatedDate = DateTime.UtcNow,
        });

        return (await GuzergahGetirAsync(olusan.Id))!;
    }

    public async Task<GuzergahDto?> GuzergahGuncelleAsync(int id, GuzergahSaveDto dto)
    {
        var mevcut = await _repository.GuzergahGetirAsync(id);
        if (mevcut is null)
        {
            return null;
        }

        var ad = AdiDogrula(dto.Ad, "Güzergah");
        var renk = RengiDogrula(dto.Renk);
        await AdBenzersizMiAsync(ad, haricId: id);

        var guncel = await _repository.GuzergahGuncelleAsync(new Guzergah
        {
            Id = id,
            Ad = ad,
            Renk = renk,
            Aciklama = dto.Aciklama?.Trim(),
            IsActive = dto.IsActive,
        });

        return guncel is null ? null : DtoyaCevir(guncel);
    }

    public async Task<bool> GuzergahSilAsync(int id)
    {
        var mevcut = await _repository.GuzergahGetirAsync(id);
        if (mevcut is null)
        {
            return false;
        }

        // DOLU GÜZERGAH SİLİNEMEZ.
        //
        // Şemada ilişki Restrict; ama biz soft delete kullandığımız için
        // veritabanı bunu engelleyemez (satır fiziksel olarak silinmiyor).
        // Kural burada: durakları sessizce sahipsiz bırakmak yerine önce
        // taşınmalarını/silinmelerini istiyoruz — kararı kullanıcı veriyor.
        // (PoiCategoryService.DeleteAsync ile birebir aynı gerekçe.)
        var durakSayisi = mevcut.Duraklar.Count;
        if (durakSayisi > 0)
        {
            throw new IsKuraliException(
                $"\"{mevcut.Ad}\" güzergahına bağlı {durakSayisi} durak var. " +
                "Güzergah silinemez; önce durakları silin veya başka bir güzergaha taşıyın.");
        }

        return await _repository.GuzergahSilAsync(id);
    }

    // ======================================================================
    //  Durak
    // ======================================================================

    public async Task<List<DurakDto>> DuraklariGetirAsync()
        // Metot grubu (Select(DtoyaCevir)) YAZILAMIYOR: DtoyaCevir'in ikinci
        // parametresi isteğe bağlı olduğu için derleyici bunu Select'in
        // "(öğe, indeks)" aşırı yüklüyle karıştırıyor. Lambda niyeti açık
        // yazıyor: güzergahı durakın kendi navigasyonundan alacak.
        => (await _repository.DuraklariGetirAsync()).Select(d => DtoyaCevir(d)).ToList();

    public async Task<DurakDto?> DurakGetirAsync(int id)
    {
        var durak = await _repository.DurakGetirAsync(id);
        return durak is null ? null : DtoyaCevir(durak);
    }

    public async Task<DurakDto> DurakEkleAsync(DurakCreateDto dto)
    {
        var ad = AdiDogrula(dto.Ad, "Durak");
        await GuzergahiDogrulaAsync(dto.GuzergahId);

        // WKT metni POINT olmak zorunda; değilse WktFormatException → 400.
        var nokta = WktConverter.Read<Point>(dto.Wkt);

        // Ödev 7 kuralı: durak da haritaya yapılan bir kayıttır, kullanıcıya
        // tanımlı çalışma alanının dışına konamaz. Kontrolü tekrar yazmıyoruz,
        // çizim ve POI uçlarıyla AYNI servisi çağırıyoruz ki üç yol ayrışmasın.
        await _geoPermission.DogrulaAsync(_currentUser.RequireUserId(), nokta);

        // Sıra verilmediyse SONA ekle. Haritadan durak koyarken en doğal
        // davranış bu; araya sokmak isteyen zaten sürükle-bırakla taşıyor.
        var sira = dto.Sira ?? (await _repository.SonSiraAsync(dto.GuzergahId) + 1);

        var olusan = await _repository.DurakEkleAsync(new Durak
        {
            Ad = ad,
            GuzergahId = dto.GuzergahId,
            Sira = Math.Max(1, sira),
            Geom = nokta,
            Aciklama = dto.Aciklama?.Trim(),
            UserId = _currentUser.RequireUserId(),
            CreatedDate = DateTime.UtcNow,
        });

        // Araya eklenmiş olabilir (Sira elle verildiyse) → sırayı sıkıştır.
        await SiralariDuzeltAsync(dto.GuzergahId);

        return (await DurakGetirAsync(olusan.Id))!;
    }

    public async Task<DurakDto?> DurakGuncelleAsync(int id, DurakUpdateDto dto)
    {
        var mevcut = await _repository.DurakGetirAsync(id);
        if (mevcut is null)
        {
            return null;
        }

        await YetkiliMiAsync(mevcut, "düzenleme");

        var ad = AdiDogrula(dto.Ad, "Durak");
        await GuzergahiDogrulaAsync(dto.GuzergahId);

        var eskiGuzergah = mevcut.GuzergahId;
        var nokta = mevcut.Geom;

        // Wkt boş gönderildiyse konuma dokunmuyoruz — yalnızca öznitelikler
        // değişir (PoiService.UpdateAsync ile aynı desen).
        if (!string.IsNullOrWhiteSpace(dto.Wkt))
        {
            nokta = WktConverter.Read<Point>(dto.Wkt);
            await _geoPermission.DogrulaAsync(_currentUser.RequireUserId(), nokta);
        }

        // BAŞKA GÜZERGAHA TAŞINIYORSA sıra yeniden hesaplanmalı: eski
        // güzergahtaki numarası yeni güzergahta anlamsız (hatta çakışıyor).
        var sira = mevcut.Sira;
        if (dto.GuzergahId != eskiGuzergah)
        {
            sira = await _repository.SonSiraAsync(dto.GuzergahId) + 1;
        }

        var guncel = await _repository.DurakGuncelleAsync(new Durak
        {
            Id = id,
            Ad = ad,
            GuzergahId = dto.GuzergahId,
            Sira = sira,
            Geom = nokta,
            Aciklama = dto.Aciklama?.Trim(),
        });

        // Taşıma olduysa ESKİ güzergahta boşluk kaldı; ikisini de sıkıştır.
        await SiralariDuzeltAsync(dto.GuzergahId);
        if (dto.GuzergahId != eskiGuzergah)
        {
            await SiralariDuzeltAsync(eskiGuzergah);
        }

        return guncel is null ? null : (await DurakGetirAsync(id));
    }

    public async Task<bool> DurakSilAsync(int id)
    {
        var mevcut = await _repository.DurakGetirAsync(id);
        if (mevcut is null)
        {
            return false;
        }

        await YetkiliMiAsync(mevcut, "silme");

        var silindi = await _repository.DurakSilAsync(id);

        if (silindi)
        {
            // Silinen durak araya boşluk bıraktı (1,2,4,5) → 1..N'e sıkıştır.
            // Boşluk bırakmak zararsız görünüyor ama arayüzde "3. durak"
            // yazan sayı ile listedeki konum ayrışırdı.
            await SiralariDuzeltAsync(mevcut.GuzergahId);
        }

        return silindi;
    }

    public async Task<GuzergahDto?> SiralamaGuncelleAsync(int guzergahId, DurakSiralamaDto dto)
    {
        var guzergah = await _repository.GuzergahGetirAsync(guzergahId);
        if (guzergah is null)
        {
            return null;
        }

        var gelen = dto.DurakIdleri ?? new List<int>();

        if (gelen.Distinct().Count() != gelen.Count)
        {
            throw new IsKuraliException("Sıralama listesinde aynı durak birden fazla kez var.");
        }

        // Listedeki her id GERÇEKTEN bu güzergahın durağı olmalı.
        //
        // Kontrol olmasaydı başka bir hattın durağının id'sini göndermek o
        // durağın sırasını sessizce bozardı — repository yabancı id'yi zaten
        // atlıyor ama "istek kabul edildi" cevabı yanıltıcı olurdu.
        var gecerliIdler = guzergah.Duraklar.Select(d => d.Id).ToHashSet();
        var yabanci = gelen.Where(id => !gecerliIdler.Contains(id)).ToList();

        if (yabanci.Count > 0)
        {
            throw new IsKuraliException(
                $"Şu durak id'leri bu güzergaha ait değil: {string.Join(", ", yabanci)}.");
        }

        if (gelen.Count != gecerliIdler.Count)
        {
            throw new IsKuraliException(
                $"Sıralama listesi güzergahın BÜTÜN duraklarını içermeli. " +
                $"Beklenen: {gecerliIdler.Count}, gelen: {gelen.Count}.");
        }

        await _repository.SiralariYazAsync(guzergahId, gelen);

        return await GuzergahGetirAsync(guzergahId);
    }

    // ======================================================================
    //  Doğrulama
    // ======================================================================

    private static string AdiDogrula(string ad, string tur)
    {
        var temiz = (ad ?? string.Empty).Trim();
        if (temiz.Length == 0)
        {
            throw new IsKuraliException($"{tur} adı boş olamaz.");
        }

        return temiz;
    }

    private static string RengiDogrula(string renk)
    {
        var temiz = (renk ?? string.Empty).Trim();
        if (!RenkBicimi.IsMatch(temiz))
        {
            throw new IsKuraliException(
                $"Güzergah rengi \"#rrggbb\" biçiminde olmalıdır. Gelen: \"{renk}\".");
        }

        // Küçük harfe sabitliyoruz: "#2D7DD2" ile "#2d7dd2" aynı renk ama
        // arayüzde renk seçicinin değeriyle karşılaştırılıyor ve büyük harfli
        // gelen değer seçicide "seçili değil" görünürdü.
        return temiz.ToLowerInvariant();
    }

    /// <summary>
    /// Aynı adda ikinci bir güzergah açılmasını engeller.
    ///
    /// NEDEN? Durak formundaki açılır listede hat ADIYLA seçiliyor; iki
    /// "126 Ulus" satırı arasında hangisinin doğru olduğunu kimse bilemez.
    /// Veritabanı index'i yerine servis kuralı: soft delete yüzünden benzersiz
    /// index kısmi olmak zorunda ve kural zaten burada okunur duruyor
    /// (PoiCategoryService'teki "kardeşlerde benzersiz" kuralıyla aynı tercih).
    /// </summary>
    private async Task AdBenzersizMiAsync(string ad, int? haricId)
    {
        var mevcutlar = await _repository.GuzergahlariGetirAsync();

        var cakisma = mevcutlar.Any(g =>
            g.Id != haricId
            && string.Equals(g.Ad, ad, StringComparison.CurrentCultureIgnoreCase));

        if (cakisma)
        {
            throw new IsKuraliException($"\"{ad}\" adında bir güzergah zaten var.");
        }
    }

    private async Task GuzergahiDogrulaAsync(int guzergahId)
    {
        var guzergah = await _repository.GuzergahGetirAsync(guzergahId);

        if (guzergah is null)
        {
            throw new IsKuraliException($"Id={guzergahId} olan güzergah bulunamadı.");
        }

        if (!guzergah.IsActive)
        {
            throw new IsKuraliException(
                $"\"{guzergah.Ad}\" güzergahı pasif durumda; yeni durak eklenemez.");
        }
    }

    /// <summary>
    /// "Bu durağa dokunabilir miyim?" — POI'deki kuralın ulaşım karşılığı.
    ///
    /// Güzergah Yönetimi yetkisi olan herkese açık (hat sorumlusu bütün
    /// durakları düzenler); yoksa Durak Ekleme yetkisi VE kaydın sahibi
    /// olmak gerekiyor.
    /// </summary>
    private async Task YetkiliMiAsync(Durak durak, string eylem)
    {
        var kullaniciId = _currentUser.RequireUserId();

        if (await _permissionService.HasPermissionAsync(kullaniciId, Yetkiler.GuzergahYonetimi))
        {
            return;
        }

        var ekleyebilir = await _permissionService.HasPermissionAsync(kullaniciId, Yetkiler.DurakEkleme);
        if (!ekleyebilir)
        {
            throw new IsKuraliException(
                $"Durak {eylem} işlemi için \"{Yetkiler.DurakEkleme}\" yetkisine sahip olmanız gerekiyor.");
        }

        if (durak.UserId != kullaniciId)
        {
            throw new IsKuraliException(
                "Bu durağı başka bir kullanıcı ekledi. Yalnızca kendi eklediğiniz duraklar " +
                $"üzerinde {eylem} yapabilirsiniz.");
        }
    }

    /// <summary>
    /// Güzergahtaki sıra numaralarını 1..N olacak şekilde SIKIŞTIRIR.
    ///
    /// Silme ve araya ekleme boşluk bırakıyor (1, 2, 4, 5). Boşluk teknik
    /// olarak zararsız — sıralama yine doğru — ama arayüz numarayı ekranda
    /// gösteriyor ve "4. durak" yazan satırın listede üçüncü sırada olması
    /// kullanıcıyı yanıltırdı.
    /// </summary>
    private async Task SiralariDuzeltAsync(int guzergahId)
    {
        var guzergah = await _repository.GuzergahGetirAsync(guzergahId);
        if (guzergah is null) return;

        var siraliIdler = guzergah.Duraklar
            .OrderBy(d => d.Sira)
            .ThenBy(d => d.Id)      // eşit sırada kararlı bir düzen: id
            .Select(d => d.Id)
            .ToList();

        if (siraliIdler.Count == 0) return;

        await _repository.SiralariYazAsync(guzergahId, siraliIdler);
    }

    // ======================================================================
    //  Dönüşüm
    // ======================================================================

    private static GuzergahDto DtoyaCevir(Guzergah guzergah) => new()
    {
        Id = guzergah.Id,
        Ad = guzergah.Ad,
        Renk = guzergah.Renk,
        Aciklama = guzergah.Aciklama,
        UserId = guzergah.UserId,
        KullaniciAdi = guzergah.User?.Username,
        CreatedDate = guzergah.CreatedDate,
        ModifiedDate = guzergah.ModifiedDate,
        IsActive = guzergah.IsActive,
        Duraklar = guzergah.Duraklar
            .OrderBy(d => d.Sira)
            .Select(d => DtoyaCevir(d, guzergah))
            .ToList(),
    };

    /// <summary>
    /// Durak → DTO.
    /// </summary>
    /// <param name="guzergah">
    /// Güzergah AYRI parametre: <c>durak.Guzergah</c> navigasyonu her sorguda
    /// dolu gelmiyor (güzergah listesi duraklarını Include ederken çocuğun
    /// ata navigasyonunu doldurmuyor). Elimizdeki güzergahı geçmek, null
    /// kontrolüyle boş ad/renk döndürmekten daha doğru.
    /// </param>
    private static DurakDto DtoyaCevir(Durak durak, Guzergah? guzergah = null)
    {
        var hat = guzergah ?? durak.Guzergah;

        return new DurakDto
        {
            Id = durak.Id,
            Ad = durak.Ad,
            GuzergahId = durak.GuzergahId,
            GuzergahAdi = hat?.Ad ?? "(güzergah bulunamadı)",
            GuzergahRengi = hat?.Renk ?? "#7a7f87",
            Sira = durak.Sira,
            Wkt = WktConverter.Write(durak.Geom),
            Aciklama = durak.Aciklama,
            UserId = durak.UserId,
            KullaniciAdi = durak.User?.Username,
            CreatedDate = durak.CreatedDate,
            ModifiedDate = durak.ModifiedDate,
            IsActive = durak.IsActive,
        };
    }
}
