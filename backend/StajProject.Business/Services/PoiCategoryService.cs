using StajProject.Business.DTOs;
using StajProject.Business.Poiler;
using StajProject.Business.Validation;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

public class PoiCategoryService : IPoiCategoryService
{
    /// <summary>
    /// Ağacın kabul edilen en büyük derinliği (kök = 1).
    ///
    /// Teknik bir sınır değil, arayüz kararı: açılır listede dördüncü seviye
    /// girinti okunmaz hâle geliyor ve "Yeme-İçme › Restoran › Kebapçı" zaten
    /// ödevin istediği ayrımı fazlasıyla karşılıyor. Sınır olmasaydı ağaç
    /// sessizce derinleşir, formlar kullanılamaz hâle gelirdi.
    /// </summary>
    private const int EnFazlaDerinlik = 3;

    /// <summary>Tam yolda kullanılan ayraç — "Yeme-İçme › Restoran".</summary>
    public const string YolAyraci = " › ";

    private readonly IPoiCategoryRepository _repository;
    private readonly IPoiRepository _poiRepository;

    /// <summary>
    /// Kategori değiştiğinde haritadaki stilleri de yenileyen servis
    /// (Ödev 13 iyileştirmesi). Ağaç ile GeoServer'ın stil listesi tek
    /// kaynaktan besleniyor; yönetici kategori eklediğinde simgesi de oluşuyor.
    ///
    /// NULL OLABİLİR. Testlerde ve GeoServer'sız kurulumda verilmiyor; kategori
    /// yönetiminin GeoServer'a bağımlı hâle gelmesini istemiyoruz. Null iken
    /// tek fark, stillerin elle yenilenmesi gerekmesi.
    /// </summary>
    private readonly IPoiStyleService? _stilServisi;

    public PoiCategoryService(
        IPoiCategoryRepository repository,
        IPoiRepository poiRepository,
        IPoiStyleService? stilServisi = null)
    {
        _repository = repository;
        _poiRepository = poiRepository;
        _stilServisi = stilServisi;
    }

    /// <summary>
    /// Ağaç değişti: stilleri yenile ama BAŞARISIZLIĞI YUTMA hakkını servise
    /// bırak (bkz. IPoiStyleService.SessizYenileAsync). Kategori zaten
    /// kaydedildi; GeoServer kapalı diye kullanıcıya "eklenemedi" demek
    /// yanlış bilgi olurdu.
    /// </summary>
    private Task StilleriYenileAsync()
        => _stilServisi?.SessizYenileAsync() ?? Task.CompletedTask;

    // ------------------------------------------------------------------
    //  Okuma
    // ------------------------------------------------------------------

    public async Task<List<PoiKategoriDto>> GetTreeAsync()
    {
        var kategoriler = await _repository.GetAllAsync();
        var sayilar = await _poiRepository.GetCountsByCategoryAsync();

        return AgacKur(kategoriler, sayilar);
    }

    public async Task<List<PoiKategoriDto>> GetSelectableAsync()
    {
        var kategoriler = await _repository.GetAllAsync();
        var sayilar = await _poiRepository.GetCountsByCategoryAsync();

        // ATA ZİNCİRİNİN TAMAMI aktif olmalı.
        //
        // Yalnızca kategorinin kendi IsActive'ine baksaydık, yönetici
        // "Yeme-İçme"yi pasife aldığında altındaki "Restoran" listede kalırdı;
        // kullanıcı açısından kapatılmış bir dalın içinden seçim yapmak
        // olurdu bu. Pasiflik daldan aşağı akıyor.
        var idIle = kategoriler.ToDictionary(k => k.Id);
        var secilebilir = kategoriler
            .Where(k => ZincirAktifMi(k, idIle))
            .ToList();

        // Düz liste ama SIRALI: her kök kendi alt ağacıyla art arda gelsin,
        // açılır listede girinti anlamlı okunsun.
        var sirali = new List<PoiKategoriDto>();

        void Gez(List<PoiKategoriDto> dugumler)
        {
            foreach (var dugum in dugumler)
            {
                var cocuklar = dugum.Cocuklar;
                dugum.Cocuklar = new List<PoiKategoriDto>();   // düz listede alt ağaç taşınmasın
                sirali.Add(dugum);
                Gez(cocuklar);
            }
        }

        Gez(AgacKur(secilebilir, sayilar));
        return sirali;
    }

    // ------------------------------------------------------------------
    //  Yazma
    // ------------------------------------------------------------------

    public async Task<PoiKategoriDto> CreateAsync(PoiKategoriSaveDto dto)
    {
        var ad = AdiDogrula(dto.Ad);
        var kategoriler = await _repository.GetAllAsync();

        AtayiDogrula(kategoriler, dto.ParentId, duzenlenenId: null);
        KardeslerdeBenzersizMi(kategoriler, ad, dto.ParentId, haricId: null);

        var olusan = await _repository.AddAsync(new PoiCategory
        {
            Ad = ad,
            Aciklama = dto.Aciklama?.Trim(),
            Ikon = IkonuDogrula(dto.Ikon),
            ParentId = dto.ParentId,
            IsActive = dto.IsActive,
            CreatedDate = DateTime.UtcNow,
        });

        await StilleriYenileAsync();

        return (await BulAsync(olusan.Id))!;
    }

    public async Task<PoiKategoriDto?> UpdateAsync(int id, PoiKategoriSaveDto dto)
    {
        var mevcut = await _repository.GetByIdAsync(id);
        if (mevcut is null)
        {
            return null;
        }

        var ad = AdiDogrula(dto.Ad);
        var kategoriler = await _repository.GetAllAsync();

        AtayiDogrula(kategoriler, dto.ParentId, duzenlenenId: id);
        KardeslerdeBenzersizMi(kategoriler, ad, dto.ParentId, haricId: id);

        var guncel = await _repository.UpdateAsync(new PoiCategory
        {
            Id = id,
            Ad = ad,
            Aciklama = dto.Aciklama?.Trim(),
            Ikon = IkonuDogrula(dto.Ikon),
            ParentId = dto.ParentId,
            IsActive = dto.IsActive,
        });

        // Ad veya aktiflik değişmiş olabilir: stil başlığı ve süzgeç
        // kapsamı ikisine de bağlı.
        await StilleriYenileAsync();

        return guncel is null ? null : await BulAsync(id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var mevcut = await _repository.GetByIdAsync(id);
        if (mevcut is null)
        {
            return false;
        }

        var kategoriler = await _repository.GetAllAsync();
        if (kategoriler.Any(k => k.ParentId == id))
        {
            // Alt ağacı sessizce silmek, yönetim ekranında tek tıkla veri kaybı
            // demek olurdu. Önce çocukların taşınmasını/silinmesini istiyoruz;
            // karar kullanıcının.
            throw new IsKuraliException(
                $"\"{mevcut.Ad}\" kategorisinin alt kategorileri var. " +
                "Önce onları silin veya başka bir kategoriye taşıyın.");
        }

        var sayilar = await _poiRepository.GetCountsByCategoryAsync();
        if (sayilar.TryGetValue(id, out var poiSayisi) && poiSayisi > 0)
        {
            throw new IsKuraliException(
                $"\"{mevcut.Ad}\" kategorisine bağlı {poiSayisi} POI var. " +
                "Kategori silinemez; önce POI'leri başka bir kategoriye taşıyın.");
        }

        var silindi = await _repository.SoftDeleteAsync(id);

        if (silindi)
        {
            // Silinen kategorinin stili de GeoServer'dan kalkmalı; kalsaydı
            // stil listesinde çalışmayan bir artık olarak dururdu.
            await StilleriYenileAsync();
        }

        return silindi;
    }

    // ------------------------------------------------------------------
    //  Doğrulamalar
    // ------------------------------------------------------------------

    /// <summary>
    /// Simge anahtarını doğrular. Boş/null geçerli ("seçilmedi"), tanınmayan
    /// anahtar 400.
    ///
    /// NEDEN SESSİZCE YOK SAYILMIYOR? Yönetici bir simge seçtiğini sanıp
    /// haritada onu hiç göremezdi — ve sebebi hiçbir yerde yazmıyor olurdu.
    /// Bu, projedeki "sessiz yanlıştansa açık hata" kuralının aynısı.
    /// </summary>
    private static string? IkonuDogrula(string? anahtar)
    {
        var temiz = anahtar?.Trim();
        if (string.IsNullOrEmpty(temiz)) return null;

        if (!PoiIkonlari.GecerliMi(temiz))
        {
            throw new IsKuraliException(
                $"Tanınmayan simge: \"{temiz}\". Geçerli simgeler: GET /api/poi/ikonlar.");
        }

        return temiz;
    }

    private static string AdiDogrula(string ad)
    {
        var temiz = ad.Trim();
        if (temiz.Length == 0)
        {
            throw new IsKuraliException("Kategori adı boş olamaz.");
        }

        return temiz;
    }

    /// <summary>
    /// Ata seçimini doğrular: var mı, kendisi mi, kendi torunu mu, derinlik aşılıyor mu?
    ///
    /// DÖNGÜ KONTROLÜ kritik: "Yeme-İçme"nin atası "Restoran" yapılırsa ikisi
    /// birbirini gösterir; yol hesaplayan her döngü sonsuza kadar döner ve
    /// uygulama kilitlenir. Veritabanının yabancı anahtarı bunu göremez —
    /// her iki satır da geçerli bir id'ye işaret ediyor.
    /// </summary>
    private static void AtayiDogrula(List<PoiCategory> kategoriler, int? parentId, int? duzenlenenId)
    {
        if (parentId is null)
        {
            return;   // kök kategori
        }

        var ata = kategoriler.FirstOrDefault(k => k.Id == parentId);
        if (ata is null)
        {
            throw new IsKuraliException(
                "Seçilen üst kategori bulunamadı. Sayfayı yenileyip tekrar deneyin.");
        }

        if (duzenlenenId is not null)
        {
            if (parentId == duzenlenenId)
            {
                throw new IsKuraliException("Bir kategori kendisinin üst kategorisi olamaz.");
            }

            // Torunlarından biri ata seçilirse de döngü oluşur.
            if (Torunlar(kategoriler, duzenlenenId.Value).Contains(parentId.Value))
            {
                throw new IsKuraliException(
                    "Bir kategori, kendi alt kategorilerinden birinin altına taşınamaz (döngü oluşurdu).");
            }
        }

        // Derinlik: atanın seviyesi + 1; taşınan düğümün alt ağacı da onunla kayar.
        var ataSeviyesi = Seviye(kategoriler, ata);
        var altAgacDerinligi = duzenlenenId is null
            ? 0
            : AltAgacDerinligi(kategoriler, duzenlenenId.Value);

        if (ataSeviyesi + 1 + altAgacDerinligi > EnFazlaDerinlik)
        {
            throw new IsKuraliException(
                $"Kategori ağacı en fazla {EnFazlaDerinlik} seviye derinleşebilir.");
        }
    }

    /// <summary>
    /// Aynı ata altında aynı ad iki kez olmasın.
    ///
    /// Bu kural veritabanı index'iyle kurulmadı: PostgreSQL benzersiz index'te
    /// NULL'ları farklı sayar, kök kategorilerde parent_id NULL olduğu için
    /// index kuralın yalnızca yarısını uygulardı (bkz. AppDbContext.ConfigurePoi).
    /// FARKLI atalar altında aynı ada izin var — "Yeme-İçme › Diğer" ile
    /// "Konaklama › Diğer" birbirini engellememeli.
    /// </summary>
    private static void KardeslerdeBenzersizMi(
        List<PoiCategory> kategoriler, string ad, int? parentId, int? haricId)
    {
        var cakisan = kategoriler.FirstOrDefault(k =>
            k.ParentId == parentId &&
            k.Id != haricId &&
            string.Equals(k.Ad, ad, StringComparison.OrdinalIgnoreCase));

        if (cakisan is not null)
        {
            var yer = parentId is null ? "kök seviyede" : "aynı üst kategori altında";
            throw new IsKuraliException($"\"{ad}\" adında bir kategori {yer} zaten var.");
        }
    }

    // ------------------------------------------------------------------
    //  Ağaç yardımcıları
    // ------------------------------------------------------------------

    private async Task<PoiKategoriDto?> BulAsync(int id)
    {
        var kategoriler = await _repository.GetAllAsync();
        var sayilar = await _poiRepository.GetCountsByCategoryAsync();

        PoiKategoriDto? Ara(List<PoiKategoriDto> dugumler)
        {
            foreach (var dugum in dugumler)
            {
                if (dugum.Id == id)
                {
                    return dugum;
                }

                var bulunan = Ara(dugum.Cocuklar);
                if (bulunan is not null)
                {
                    return bulunan;
                }
            }

            return null;
        }

        return Ara(AgacKur(kategoriler, sayilar));
    }

    /// <summary>
    /// Düz listeden ağaç kurar. Kökler döner; her düğümde tam yol, seviye ve
    /// POI sayısı dolu olur.
    ///
    /// Kök tanımı dikkatli: parent_id dolu AMA atası listede yoksa (ata pasif
    /// süzgeciyle elenmiş olabilir) o düğüm de kök sayılmalı — yoksa ağaçtan
    /// sessizce düşerdi.
    /// </summary>
    private static List<PoiKategoriDto> AgacKur(
        List<PoiCategory> kategoriler, IReadOnlyDictionary<int, int> poiSayilari)
    {
        var idIle = kategoriler.ToDictionary(k => k.Id);
        var atayaGore = kategoriler.ToLookup(k => k.ParentId);

        List<PoiKategoriDto> Dugumler(IEnumerable<PoiCategory> kaynak, int seviye, string onEk)
            => kaynak
                .OrderBy(k => k.Ad, StringComparer.CurrentCulture)
                .Select(k =>
                {
                    var yol = onEk.Length == 0 ? k.Ad : onEk + YolAyraci + k.Ad;
                    return new PoiKategoriDto
                    {
                        Id = k.Id,
                        Ad = k.Ad,
                        Aciklama = k.Aciklama,
                        Ikon = k.Ikon,
                        EtkinIkon = PoiIkonlari.EtkinAnahtar(k, idIle, x => x.Ikon, x => x.ParentId),
                        ParentId = k.ParentId,
                        TamYol = yol,
                        Seviye = seviye,
                        IsActive = k.IsActive,
                        CreatedDate = k.CreatedDate,
                        ModifiedDate = k.ModifiedDate,
                        PoiSayisi = poiSayilari.TryGetValue(k.Id, out var sayi) ? sayi : 0,
                        Cocuklar = Dugumler(atayaGore[k.Id], seviye + 1, yol),
                    };
                })
                .ToList();

        // Kök = atası olmayan VEYA atası bu listede bulunmayan düğüm.
        var koklerKaynak = kategoriler
            .Where(k => k.ParentId is null || !idIle.ContainsKey(k.ParentId.Value));

        return Dugumler(koklerKaynak, 0, string.Empty);
    }

    /// <summary>Kategorinin ve bütün atalarının aktif olup olmadığı.</summary>
    private static bool ZincirAktifMi(PoiCategory kategori, IReadOnlyDictionary<int, PoiCategory> idIle)
    {
        var gecerli = kategori;
        var guvenlik = 0;

        while (true)
        {
            if (!gecerli.IsActive)
            {
                return false;
            }

            if (gecerli.ParentId is null || !idIle.TryGetValue(gecerli.ParentId.Value, out var ata))
            {
                return true;
            }

            gecerli = ata;

            // Veriye döngü sızmışsa (elle INSERT) sonsuz döngüye girmeyelim.
            if (++guvenlik > 64)
            {
                return false;
            }
        }
    }

    /// <summary>Kökten sayarak kaçıncı seviye? Kök = 1.</summary>
    private static int Seviye(List<PoiCategory> kategoriler, PoiCategory kategori)
    {
        var idIle = kategoriler.ToDictionary(k => k.Id);
        var seviye = 1;
        var gecerli = kategori;

        while (gecerli.ParentId is not null && idIle.TryGetValue(gecerli.ParentId.Value, out var ata))
        {
            gecerli = ata;
            if (++seviye > 64)
            {
                break;   // bozuk veriye karşı emniyet
            }
        }

        return seviye;
    }

    /// <summary>Bir düğümün altındaki en uzun dalın uzunluğu (yaprakta 0).</summary>
    private static int AltAgacDerinligi(List<PoiCategory> kategoriler, int id)
    {
        var cocuklar = kategoriler.Where(k => k.ParentId == id).ToList();
        if (cocuklar.Count == 0)
        {
            return 0;
        }

        return 1 + cocuklar.Max(c => AltAgacDerinligi(kategoriler, c.Id));
    }

    /// <summary>Bir düğümün bütün torunlarının id'leri.</summary>
    private static HashSet<int> Torunlar(List<PoiCategory> kategoriler, int id)
    {
        var sonuc = new HashSet<int>();
        var sira = new Queue<int>();
        sira.Enqueue(id);

        while (sira.Count > 0)
        {
            var gecerli = sira.Dequeue();
            foreach (var cocuk in kategoriler.Where(k => k.ParentId == gecerli))
            {
                if (sonuc.Add(cocuk.Id))
                {
                    sira.Enqueue(cocuk.Id);
                }
            }
        }

        return sonuc;
    }
}
