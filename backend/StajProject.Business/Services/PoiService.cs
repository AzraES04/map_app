using NetTopologySuite.Geometries;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Mesai;
using StajProject.Business.Poiler;
using StajProject.Business.Validation;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

public class PoiService : IPoiService
{
    /// <summary>
    /// Aramanın çalışmaya başladığı en kısa metin (Ödev 13 / Madde 2).
    ///
    /// İki harf: "AŞ", "Dr" gibi kısaltmalarla arama yapılabilsin ama tek
    /// harflik bir sorgu bütün tabloyu getirmesin. Arayüz de aynı sınırı
    /// biliyor (poiApi.js) ve altında hiç istek atmıyor — sınır iki yerde
    /// yazılı ama SUNUCUDAKİ olan bağlayıcı, arayüzdeki sadece boş istekleri
    /// engelliyor.
    /// </summary>
    public const int EnAzAramaUzunlugu = 2;

    private readonly IPoiRepository _repository;
    private readonly IPoiCategoryRepository _kategoriRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;
    private readonly IGeoPermissionService _geoPermission;

    public PoiService(
        IPoiRepository repository,
        IPoiCategoryRepository kategoriRepository,
        ICurrentUserService currentUser,
        IPermissionService permissionService,
        IGeoPermissionService geoPermission)
    {
        _repository = repository;
        _kategoriRepository = kategoriRepository;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _geoPermission = geoPermission;
    }

    // ------------------------------------------------------------------
    //  Okuma
    // ------------------------------------------------------------------

    public async Task<List<PoiDto>> GetAllAsync()
    {
        // Süzgeç YOK — POI ortak veridir (bkz. IPoiService açıklaması).
        var poiler = await _repository.GetAllAsync();
        var yollar = await YollariHesaplaAsync();

        return poiler.Select(p => DtoyaCevir(p, yollar)).ToList();
    }

    public async Task<PoiDto?> GetByIdAsync(int id)
    {
        var poi = await _repository.GetByIdAsync(id);
        return poi is null ? null : DtoyaCevir(poi, await YollariHesaplaAsync());
    }

    /// <summary>
    /// Arama barının kaynağı (Ödev 13 / Madde 2).
    ///
    /// YETKİ İSTEMİYOR — bilerek. Ödev "bu arama özelliği Kullanıcı (User)
    /// rolüne de açık olmalıdır" diyor; zaten POI listeleme de süzülmüyor
    /// (POI ortak referans verisi). Arama, listenin süzülmüş hâlinden başka
    /// bir şey değil: listeleme herkese açıkken aramayı kapatmak korumasız
    /// bir kapının yanına kilitli bir kapı koymak olurdu.
    ///
    /// Çok kısa sorgular boş dönüyor: tek harf neredeyse bütün tabloyla
    /// eşleşir, kullanıcıya faydası olmayan bir liste için sunucuyu meşgul
    /// etmenin anlamı yok.
    /// </summary>
    public async Task<List<PoiAramaSonucuDto>> AraAsync(string? sorgu, int enFazla = 8)
    {
        var temiz = (sorgu ?? string.Empty).Trim();
        if (temiz.Length < EnAzAramaUzunlugu)
        {
            return new List<PoiAramaSonucuDto>();
        }

        // Üst sınırı burada da bağlıyoruz: istemci ?enFazla=100000 gönderse
        // bile arama kutusuna sığmayacak bir liste üretmiyoruz.
        var sinir = Math.Clamp(enFazla, 1, 25);

        var poiler = await _repository.AraAsync(temiz, sinir);
        var yollar = await YollariHesaplaAsync();

        // "Şu an" bir kez hesaplanıyor: liste içindeki kayıtların hepsi AYNI ana
        // göre değerlendirilsin. Her satırda yeniden okunsaydı, gece yarısını
        // geçen bir istekte iki satır iki farklı güne bakabilirdi.
        var simdi = TurkiyeZamani.Simdi();
        var bugunkuTatil = ResmiTatiller.Bul(DateOnly.FromDateTime(simdi));

        return poiler.Select(poi =>
        {
            var bulundu = yollar.TryGetValue(poi.KategoriId, out var kategori);
            var durum = MesaiPlani.Coz(poi.MesaiPlani)?.Durum(simdi, bugunkuTatil);

            return new PoiAramaSonucuDto
            {
                Id = poi.Id,
                Isim = poi.Isim,
                KategoriYolu = bulundu ? kategori.Yol : string.Empty,
                KokKategori = bulundu ? kategori.Kok : string.Empty,
                KategoriId = poi.KategoriId,
                Wkt = WktConverter.Write(poi.Geom),
                MesaiSaatleri = poi.MesaiSaatleri,
                // Planı olmayan kayıtta null kalıyor — "bilmiyoruz" ile
                // "kapalı"yı aynı değere indirmiyoruz.
                SuAnAcik = durum?.Acik,
                MesaiDurumu = durum?.Aciklama,
                IsActive = poi.IsActive,
            };
        }).ToList();
    }

    /// <summary>
    /// Seçilen yer için kategori önerisi (Ödev 13 / Madde 4).
    ///
    /// İki adım: önce <see cref="KategoriEsleme"/> yer türünden/adından bir
    /// KATEGORİ YOLU tahmin ediyor ("Eğitim" › "Kütüphane"), sonra o yol
    /// veritabanındaki gerçek kategori ağacında ARANIYOR.
    ///
    /// İkinci adım şart: sözlük sabit, ağaç ise yönetici tarafından
    /// değiştirilebilir. Karşılığı olmayan bir öneriyi döndürseydik arayüz
    /// var olmayan bir kategoriyi seçmeye çalışırdı. Karşılık yoksa öneri de
    /// yok — form kategoriyi boş bırakıyor, kullanıcı kendisi seçiyor.
    ///
    /// Alt kategori bulunamaz ama kök bulunursa KÖK öneriliyor: "Eğitim"
    /// demek "Kütüphane" demekten daha az bilgi taşır ama yanlış değildir.
    /// </summary>
    public async Task<KategoriOneriDto?> KategoriOnerAsync(string? tur, string? sinif, string? isim)
    {
        var oneri = KategoriEsleme.Oner(tur, sinif, isim);
        if (oneri is null)
        {
            return null;
        }

        // Yalnızca SEÇİLEBİLİR (aktif, silinmemiş) kategoriler: pasif bir
        // kategoriyi önermek, kaydetmeye çalışınca reddedilen bir form üretirdi
        // (bkz. KategoriyiDogrulaAsync).
        var kategoriler = await _kategoriRepository.GetAllAsync();
        var secilebilir = kategoriler.Where(k => k.IsActive).ToList();

        var kok = secilebilir.FirstOrDefault(k =>
            k.ParentId is null && KategoriEsleme.Anahtar(k.Ad) == KategoriEsleme.Anahtar(oneri.Kok));

        if (kok is null)
        {
            return null;
        }

        var alt = secilebilir.FirstOrDefault(k =>
            k.ParentId == kok.Id && KategoriEsleme.Anahtar(k.Ad) == KategoriEsleme.Anahtar(oneri.Alt));

        var secilen = alt ?? kok;

        return new KategoriOneriDto
        {
            KategoriId = secilen.Id,
            KategoriAdi = secilen.Ad,
            ParentId = alt is null ? null : kok.Id,
            ParentAdi = alt is null ? null : kok.Ad,
            TamYol = alt is null ? kok.Ad : kok.Ad + PoiCategoryService.YolAyraci + alt.Ad,
            Gerekce = oneri.Gerekce,
        };
    }

    /// <summary>
    /// Bir yılın resmî tatilleri (Ödev 13 / Madde 3).
    ///
    /// Servis katmanında duruyor çünkü "resmî kurum tatilde kapalıdır" bir
    /// İŞ KURALIDIR. Controller listeyi üretmiyor, yalnızca taşıyor.
    /// </summary>
    public ResmiTatilListesiDto ResmiTatilleriGetir(int? yil)
    {
        var hedef = yil ?? DateTime.UtcNow.Year;

        return new ResmiTatilListesiDto
        {
            Yil = hedef,
            DiniBayramlarTanimli = ResmiTatiller.DiniBayramTablosuVarMi(hedef),
            Tatiller = ResmiTatiller.Yil(hedef).Select(t => new ResmiTatilDto
            {
                Tarih = t.Tarih.ToString("yyyy-MM-dd"),
                Ad = t.Ad,
                YarimGun = t.YarimGun,
            }).ToList(),
        };
    }

    // ------------------------------------------------------------------
    //  Yazma
    // ------------------------------------------------------------------

    public async Task<PoiDto> CreateAsync(PoiCreateDto dto)
    {
        var isim = IsmiDogrula(dto.Isim);
        await KategoriyiDogrulaAsync(dto.KategoriId);

        // WKT metni POINT olmak zorunda; değilse WktFormatException → 400.
        var nokta = WktConverter.Read<Point>(dto.Wkt);

        // Ödev 7 ile aynı kural: POI de haritaya yapılan bir kayıttır, kullanıcıya
        // tanımlı çalışma alanının dışına konamaz. Kontrolü tekrar yazmıyoruz —
        // çizim uçlarıyla aynı servisi çağırıyoruz ki iki yol asla ayrışmasın.
        await _geoPermission.DogrulaAsync(_currentUser.RequireUserId(), nokta);

        var mesai = MesaiCoz(dto.MesaiPlani, dto.MesaiSaatleri);

        var olusan = await _repository.AddAsync(new Poi
        {
            Isim = isim,
            KategoriId = dto.KategoriId,
            MesaiSaatleri = mesai.Ozet,
            MesaiPlani = mesai.PlanJson,
            Geom = nokta,
            UserId = _currentUser.RequireUserId(),
            CreatedDate = DateTime.UtcNow,
        });

        return (await GetByIdAsync(olusan.Id))!;
    }

    public async Task<PoiDto?> UpdateAsync(int id, PoiUpdateDto dto)
    {
        var mevcut = await _repository.GetByIdAsync(id);
        if (mevcut is null)
        {
            return null;
        }

        await YetkiliMiAsync(mevcut, "düzenleme");

        var isim = IsmiDogrula(dto.Isim);
        await KategoriyiDogrulaAsync(dto.KategoriId);

        var nokta = mevcut.Geom;

        // Wkt boş gönderildiyse konuma dokunmuyoruz — yalnızca öznitelikler değişir.
        if (!string.IsNullOrWhiteSpace(dto.Wkt))
        {
            nokta = WktConverter.Read<Point>(dto.Wkt);

            // Güncellemede de alan kontrolü: aksi hâlde kullanıcı alan içine POI
            // koyup sonra sürükleyerek dışarı taşıyabilirdi.
            await _geoPermission.DogrulaAsync(_currentUser.RequireUserId(), nokta);
        }

        var mesai = MesaiCoz(dto.MesaiPlani, dto.MesaiSaatleri);

        var guncel = await _repository.UpdateAsync(new Poi
        {
            Id = id,
            Isim = isim,
            KategoriId = dto.KategoriId,
            MesaiSaatleri = mesai.Ozet,
            MesaiPlani = mesai.PlanJson,
            Geom = nokta,
        });

        return guncel is null ? null : DtoyaCevir(guncel, await YollariHesaplaAsync());
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var mevcut = await _repository.GetByIdAsync(id);
        if (mevcut is null)
        {
            return false;
        }

        await YetkiliMiAsync(mevcut, "silme");
        return await _repository.SoftDeleteAsync(id);
    }

    // Geri alma silinmiş kayıt üzerinde çalışır; repository onu sorgu filtresini
    // aşarak buluyor, bu yüzden buradan sahiplik kontrolü yapılamıyor
    // (GeometryService.RestoreAsync'te de aynı durum var). Ucun kendisi
    // "POI Yönetimi" yetkisi istiyor — bkz. PoiController.Restore.
    public Task<bool> RestoreAsync(int id) => _repository.RestoreAsync(id);

    public async Task<bool> SetActiveAsync(int id, bool isActive)
    {
        var mevcut = await _repository.GetByIdAsync(id);
        if (mevcut is null)
        {
            return false;
        }

        await YetkiliMiAsync(mevcut, "durum değiştirme");
        return await _repository.SetActiveAsync(id, isActive);
    }

    // ------------------------------------------------------------------
    //  Doğrulamalar
    // ------------------------------------------------------------------

    private static string IsmiDogrula(string isim)
    {
        var temiz = isim.Trim();
        if (temiz.Length == 0)
        {
            throw new IsKuraliException("POI adı boş olamaz.");
        }

        return temiz;
    }

    /// <summary>
    /// Mesai bilgisini kaydedilecek İKİ kolona çevirir (Ödev 13 / Madde 3):
    /// yapısal plan (JSON) ve ondan üretilen okunur özet metin.
    ///
    /// ÖNCELİK PLANDA. Plan geldiyse özet metin ondan üretiliyor ve
    /// istemcinin gönderdiği metin YOK SAYILIYOR. Neden? İkisi de kabul
    /// edilseydi çelişebilirlerdi: plan "pazar kapalı" derken metin
    /// "7/24" diyebilirdi ve hangisinin doğru olduğunu kimse bilemezdi.
    /// Tek kaynak plan; metin onun türevi.
    ///
    /// Plan gelmediyse metin serbest bırakılıyor — Ödev 12'nin davranışı.
    /// Bu köprü, plan göndermeyen eski bir istemcinin (veya Swagger'dan
    /// elle atılan bir isteğin) çalışmaya devam etmesini sağlıyor.
    /// </summary>
    private static (string? Ozet, string? PlanJson) MesaiCoz(MesaiPlani? plan, string? metin)
    {
        if (plan is not null)
        {
            var dogrulanmis = plan.Dogrula();
            return (dogrulanmis.OzetMetin(), dogrulanmis.Serilestir());
        }

        // Boş metin yerine NULL — "değer yok"un veritabanındaki doğru karşılığı.
        return (string.IsNullOrWhiteSpace(metin) ? null : metin.Trim(), null);
    }

    /// <summary>
    /// Kategori var mı ve seçilebilir durumda mı?
    ///
    /// Pasif kategoriye yeni POI bağlanamaz: pasife almanın anlamı zaten
    /// "artık bu kategoriye kayıt girilmesin"dir. Mevcut POI'ler yerinde kalır.
    /// </summary>
    private async Task KategoriyiDogrulaAsync(int kategoriId)
    {
        var kategori = await _kategoriRepository.GetByIdAsync(kategoriId);
        if (kategori is null)
        {
            throw new IsKuraliException(
                "Seçilen kategori bulunamadı. Sayfayı yenileyip tekrar deneyin.");
        }

        if (!kategori.IsActive)
        {
            throw new IsKuraliException(
                $"\"{kategori.Ad}\" kategorisi pasif durumda; bu kategoriye yeni POI eklenemez.");
        }
    }

    /// <summary>
    /// Bu kaydı değiştirebilir miyim?
    ///
    ///   "POI Yönetimi" yetkisi varsa    → her POI'ye dokunabilir (yönetici)
    ///   "POI Ekleme" yetkisi + sahibiyse → kendi kaydına dokunabilir (operatör)
    ///   ikisi de değilse                 → reddedilir
    ///
    /// Kontrol neden öznitelikte değil de burada? Çünkü kural bir VEYA:
    /// [YetkiGerekli] tek bir yetki adı alır, "şu yetki VEYA (bu yetki ve
    /// sahiplik)" ifadesini kuramaz. Öznitelikle sadece "POI Ekleme" istesek
    /// yalnızca yönetim yetkisi olan bir yönetici kendi panelinden POI
    /// silemezdi; sadece "POI Yönetimi" istesek operatör kendi kaydını
    /// düzeltemezdi.
    ///
    /// Neden 403 değil de 400 (iş kuralı)? Reddedilen şey kimlik değil,
    /// isteğin HEDEFİ: kullanıcı POI düzenleyebiliyor, sadece BU kayıt
    /// başkasının. 403 "sen bu işi hiç yapamazsın" derdi — doğru olmazdı.
    /// </summary>
    private async Task YetkiliMiAsync(Poi poi, string eylem)
    {
        var kullaniciId = _currentUser.RequireUserId();

        if (await _permissionService.HasPermissionAsync(kullaniciId, Yetkiler.PoiYonetimi))
        {
            return;
        }

        var ekleyebilir = await _permissionService.HasPermissionAsync(kullaniciId, Yetkiler.PoiEkleme);
        if (!ekleyebilir)
        {
            throw new IsKuraliException(
                $"POI {eylem} işlemi için \"{Yetkiler.PoiEkleme}\" yetkisine sahip olmanız gerekiyor.");
        }

        if (poi.UserId != kullaniciId)
        {
            throw new IsKuraliException(
                "Bu POI'yi başka bir kullanıcı ekledi. Yalnızca kendi eklediğiniz POI'ler " +
                $"üzerinde {eylem} yapabilirsiniz.");
        }
    }

    // ------------------------------------------------------------------
    //  Dönüşüm
    // ------------------------------------------------------------------

    /// <summary>
    /// Kategori id → (ad, tam yol) sözlüğü.
    ///
    /// Kategori tablosu bir kez okunup yol bellekte kuruluyor. Alternatif,
    /// her POI için ata zincirini ayrı ayrı sorgulamaktı — kategori sayısı
    /// onlarla ölçülürken POI sayısı binlerle ölçülebilir, yani sorgu sayısı
    /// veriyle birlikte büyürdü (N+1).
    /// </summary>
    private async Task<Dictionary<int, (string Ad, string Yol, string Kok)>> YollariHesaplaAsync()
    {
        var kategoriler = await _kategoriRepository.GetAllAsync();
        var idIle = kategoriler.ToDictionary(k => k.Id);

        List<string> Zincir(PoiCategory kategori)
        {
            var parcalar = new List<string> { kategori.Ad };
            var gecerli = kategori;
            var guvenlik = 0;

            while (gecerli.ParentId is not null && idIle.TryGetValue(gecerli.ParentId.Value, out var ata))
            {
                parcalar.Insert(0, ata.Ad);
                gecerli = ata;

                // Veriye döngü sızmışsa sonsuza kadar dönmeyelim.
                if (++guvenlik > 64)
                {
                    break;
                }
            }

            return parcalar;
        }

        // Kök adı ayrıca taşınıyor (Ödev 13): arama sonucu ve harita stili
        // kategoriyi KÖKÜNE göre renklendiriyor — "Restoran" ile "Kafe" aynı
        // simgeyi paylaşıyor çünkü ikisi de Yeme-İçme. Yolu her seferinde
        // ayraçtan bölmek yerine zinciri zaten elimizdeyken ilk parçayı alıyoruz.
        return kategoriler.ToDictionary(
            k => k.Id,
            k =>
            {
                var zincir = Zincir(k);
                return (k.Ad, string.Join(PoiCategoryService.YolAyraci, zincir), zincir[0]);
            });
    }

    private static PoiDto DtoyaCevir(
        Poi poi, IReadOnlyDictionary<int, (string Ad, string Yol, string Kok)> yollar)
    {
        // Kategori sözlükte yoksa (yeni silinmişse) boş bırakmak yerine
        // görünür bir işaret koyuyoruz; sessiz boşluk hata gibi okunuyor.
        var bulundu = yollar.TryGetValue(poi.KategoriId, out var kategori);

        return new PoiDto
        {
            Id = poi.Id,
            Isim = poi.Isim,
            KategoriId = poi.KategoriId,
            KategoriAdi = bulundu ? kategori.Ad : "(kategori bulunamadı)",
            KategoriYolu = bulundu ? kategori.Yol : "(kategori bulunamadı)",
            MesaiSaatleri = poi.MesaiSaatleri,
            // Planı olmayan (Ödev 12'den kalma) kayıtlarda özet metinden
            // çözmeyi deniyoruz: düzenleme formu boş açılıp da kullanıcı
            // kaydettiğinde mevcut mesai bilgisi sessizce silinmesin.
            MesaiPlani = MesaiPlani.Coz(poi.MesaiPlani) ?? MesaiPlani.EskiMetindenCoz(poi.MesaiSaatleri),
            Wkt = WktConverter.Write(poi.Geom),
            UserId = poi.UserId,
            KullaniciAdi = poi.User?.Username,
            CreatedDate = poi.CreatedDate,
            ModifiedDate = poi.ModifiedDate,
            IsActive = poi.IsActive,
        };
    }
}
