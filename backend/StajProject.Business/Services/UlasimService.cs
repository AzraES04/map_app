using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NetTopologySuite.Geometries;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Validation;
using StajProject.DataAccess.Osrm;
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

    /// <summary>
    /// Rota motoru (Ödev 17). Arayüz üzerinden alınıyor: servis "rotayı kim
    /// hesaplıyor" bilmiyor, yalnızca sözleşmeyi tanıyor. Testler bunun
    /// yerine sahte bir istemci geçirerek OSRM olmadan çalışabiliyor.
    /// </summary>
    private readonly IOsrmClient _osrm;

    public UlasimService(
        IUlasimRepository repository,
        ICurrentUserService currentUser,
        IPermissionService permissionService,
        IGeoPermissionService geoPermission,
        IOsrmClient osrm)
    {
        _repository = repository;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _geoPermission = geoPermission;
        _osrm = osrm;
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

        // Ödev 17: yeni durak hattın şeklini değiştirdi → rota yenilensin.
        // Sıkıştırmadan SONRA çağrılıyor: imza sıraya bakıyor ve henüz
        // sıkıştırılmamış numaralarla üretilen imza, bir sonraki okumada
        // tutmaz ve rota durduk yere "eskimiş" görünürdü.
        await RotayiTazeleAsync(dto.GuzergahId);

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

        // Ödev 17: İKİ hattın da rotası etkilenmiş olabilir — durak birinden
        // çıkıp diğerine girdi. Yalnızca yeni güzergahı yenileseydik, eski
        // hattın rotası artık ona ait olmayan bir duraktan geçmeye devam
        // ederdi.
        //
        // Durağın yalnızca adı değiştiyse imza aynı kalıyor ve bu çağrı
        // OSRM'e hiç gitmiyor (bkz. RotayiTazeleAsync).
        await RotayiTazeleAsync(dto.GuzergahId);
        if (dto.GuzergahId != eskiGuzergah)
        {
            await RotayiTazeleAsync(eskiGuzergah);
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

            // Ödev 17: durak gitti, hat kısaldı → rota yenilensin.
            // İki durağın altına düşüldüyse RotayiTazeleAsync rotayı
            // TEMİZLİYOR; aksi hâlde haritada artık var olmayan bir durağa
            // giden hat asılı kalırdı.
            await RotayiTazeleAsync(mevcut.GuzergahId);
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

        // ÖDEV 17'NİN AÇIKÇA İSTEDİĞİ DAVRANIŞ:
        // "Güzergah üzerindeki durak sırası değiştiğinde OSRM'e yeni istek
        //  atılarak rota otomatik güncellenmelidir."
        //
        // Kullanıcının ayrıca "Rota Oluştur"a basması gerekmiyor. Basmak
        // zorunda kalsaydı, sürükle-bırak sonrası haritadaki hat ile durak
        // listesi ayrışmış hâlde kalır ve bunu fark etmek kullanıcıya
        // kalırdı.
        await RotayiTazeleAsync(guzergahId);

        return await GuzergahGetirAsync(guzergahId);
    }


    // ======================================================================
    //  Ödev 17 / Madde 1 — OSRM ROTASI
    // ======================================================================

    /// <summary>
    /// Güzergahın rotasını OSRM'den hesaplatıp veritabanına yazar
    /// ("Rota Oluştur" düğmesi).
    ///
    /// Elle çağrılan yol BU. Otomatik yenileme aynı işi
    /// <see cref="RotayiTazeleAsync"/> üzerinden yapıyor; aradaki tek fark
    /// hataların nasıl bildirildiği:
    ///
    ///   • Burada kullanıcı düğmeye BASTI — "olmadı" cevabını ve sebebini
    ///     görmeyi hak ediyor, o yüzden iş kuralı hatası fırlatılıyor.
    ///   • Otomatik yolda kullanıcı başka bir iş yapıyordu (durak taşıdı);
    ///     OSRM'in kapalı olması o işi engellememeli, o yüzden sessizce
    ///     geçiliyor.
    ///
    /// "Güzergah Yönetimi" yetkisi ister: rota hattın kalıcı bir özelliği ve
    /// hesaplatmak dış bir servise yük bindiriyor.
    /// </summary>
    public async Task<GuzergahDto?> RotaHesaplaAsync(
        int guzergahId,
        IReadOnlyList<RotaViaDto>? viaNoktalar = null)
    {
        // Yetki denetimi CONTROLLER'da: [YetkiGerekli(Yetkiler.GuzergahYonetimi)].
        // GuzergahEkle/Guncelle/Sil de aynı deseni izliyor — servis, yetkiyi
        // ikinci kez sormuyor.
        var guzergah = await _repository.GuzergahGetirAsync(guzergahId);
        if (guzergah is null)
        {
            return null;
        }

        if (!_osrm.Etkin)
        {
            throw new IsKuraliException(
                "Rota servisi (OSRM) kapalı. appsettings.json → Osrm:Enabled ayarına bakın.");
        }

        var duraklar = SiraliDuraklar(guzergah);

        if (duraklar.Count < 2)
        {
            throw new IsKuraliException(
                "Rota için en az 2 durak gerekli. Bu güzergahta " +
                $"{duraklar.Count} durak var.");
        }

        // ARA NOKTALAR (Ödev 18) — kullanıcının seçtiği alternatifi zorlamak için.
        //
        // Nokta listesi, duraklarla ARA NOKTALARIN birleşimi. Sıra önemli:
        // ara nokta, ait olduğu bacağın İKİ DURAĞI ARASINA giriyor. Sona
        // eklesek rota önce bütün durakları dolaşır, sonra oraya giderdi.
        var noktalar = viaNoktalar is { Count: > 0 }
            ? DuraklaraViaSerpistir(duraklar, viaNoktalar)
            : duraklar.Select(d => d.Geom.Coordinate).ToList();

        var sonuc = await _osrm.RotaHesaplaAsync(noktalar);

        if (sonuc is null)
        {
            // Rota yazılMIYOR: eldeki eski rota duruyor.
            //
            // Neden temizlemiyoruz? Kullanıcı "yeniden hesapla" dedi ve
            // olmadı. Eski rotayı silmek, elimizdeki en iyi bilgiyi bir
            // başarısızlık yüzünden yok etmek olurdu; harita da o an
            // boşalırdı. Eski rota duruyor, imza uyuşmuyorsa arayüz zaten
            // "güncel değil" diyor.
            throw new IsKuraliException(
                "OSRM rota hesaplayamadı. Servis çalışıyor mu ve durakların "
                + "bulunduğu bölge yüklü veri kapsamında mı, kontrol edin.");
        }

        await _repository.RotaYazAsync(
            guzergahId,
            sonuc.Cizgi,
            sonuc.MesafeMetre,
            sonuc.SureSaniye,
            RotaImzasiUret(duraklar));

        return await GuzergahGetirAsync(guzergahId);
    }

    /// <summary>
    /// Rotayı SESSİZCE yeniler — durak ekleme/güncelleme/silme ve sıralama
    /// değişikliklerinin ardından çağrılıyor (Ödev 17: "durak sırası
    /// değiştiğinde OSRM'e yeni istek atılarak rota otomatik güncellenmelidir").
    ///
    /// ---- NEDEN HATA FIRLATMIYOR? ----
    ///
    /// Kullanıcının yaptığı iş "durağı yukarı taşımak"tı. O iş
    /// VERİTABANINDA ZATEN BİTTİ. Şimdi OSRM'e ulaşılamıyor diye istisna
    /// fırlatırsak kullanıcı "sıralama kaydedilemedi" hatası görür — oysa
    /// kaydedildi. Dış bir servisin arızasını, kullanıcının kendi verisini
    /// kaybettiğine inandırmaya çeviremeyiz.
    ///
    /// Sessiz geçmenin bedeli: veritabanında güncelliğini yitirmiş bir rota
    /// kalabilir. Bu bedel <see cref="Guzergah.RotaImza"/> ile ödeniyor —
    /// imza eski dizilimi gösterdiği için arayüz durumu fark ediyor ve
    /// "rota güncel değil" diyor.
    ///
    /// ---- GEREKSİZ İSTEK ATMIYOR ----
    ///
    /// İmza değişmediyse OSRM'e hiç gidilmiyor. Örneğin yalnızca durağın ADI
    /// düzeltildiğinde geometri ve sıra aynı kalıyor; rota da aynı olacaktı.
    /// </summary>
    private async Task RotayiTazeleAsync(int guzergahId)
    {
        if (!_osrm.Etkin)
        {
            return;
        }

        var guzergah = await _repository.GuzergahGetirAsync(guzergahId);
        if (guzergah is null)
        {
            return;
        }

        var duraklar = SiraliDuraklar(guzergah);

        // 2'nin altına düşüldüyse rota artık anlamsız: temizliyoruz.
        // Bırakırsak haritada, artık var olmayan duraklara giden bir hat
        // çizili kalırdı.
        if (duraklar.Count < 2)
        {
            if (guzergah.Rota is not null)
            {
                await _repository.RotaYazAsync(guzergahId, null, null, null, null);
            }
            return;
        }

        var imza = RotaImzasiUret(duraklar);
        if (guzergah.Rota is not null && guzergah.RotaImza == imza)
        {
            return;   // dizilim değişmemiş, rota zaten güncel
        }

        // OTOMATİK YENİLEME ARA NOKTA KULLANMIYOR — bilerek.
        //
        // Kullanıcı bir alternatif seçtiğinde o seçim, O ANKİ durak dizilimi
        // için anlamlı. Durak eklenip taşındığında ya da sıra değiştiğinde
        // dizilim başkalaşıyor; eski ara noktayı zorlamak, artık ilgisi
        // kalmamış bir yerden geçmeye çalışan tuhaf bir rota üretirdi.
        //
        // Bu yüzden dizilim değişince rota, OSRM'in varsayılan en iyi yoluna
        // dönüyor ve kullanıcı isterse alternatifi yeniden seçiyor. Sessizce
        // yanlış bir rotayı korumaktansa, doğru bir varsayılana dönmek.
        var sonuc = await _osrm.RotaHesaplaAsync(
            duraklar.Select(d => d.Geom.Coordinate).ToList());

        if (sonuc is null)
        {
            return;   // eski rota yerinde kalıyor, imza uyuşmadığı için "eski" görünecek
        }

        await _repository.RotaYazAsync(
            guzergahId, sonuc.Cizgi, sonuc.MesafeMetre, sonuc.SureSaniye, imza);
    }


    /// <summary>
    /// Seçilen durağa GİDEN yolların alternatifleri.
    ///
    /// Bacak = bir önceki durak → seçilen durak. Gerekçe:
    /// <see cref="RotaAlternatifleriDto"/>.
    /// </summary>
    public async Task<RotaAlternatifleriDto?> DurakAlternatifleriAsync(int durakId)
    {
        var durak = await _repository.DurakGetirAsync(durakId);
        if (durak is null)
        {
            return null;
        }

        var guzergah = await _repository.GuzergahGetirAsync(durak.GuzergahId);
        if (guzergah is null)
        {
            return null;
        }

        var sirali = SiraliDuraklar(guzergah);
        var yeri = sirali.FindIndex(d => d.Id == durakId);

        var sonuc = new RotaAlternatifleriDto
        {
            GuzergahId = guzergah.Id,
            DurakId = durak.Id,
            DurakAdi = durak.Ad,
        };

        // İLK DURAK: öncesinde durak yok, dolayısıyla "buraya giden yol" diye
        // bir şey de yok. Boş liste dönüp susmak yerine sebebini söylüyoruz.
        if (yeri <= 0)
        {
            sonuc.Mesaj = sirali.Count < 2
                ? "Alternatif için hatta en az iki durak gerekiyor."
                : $"\"{durak.Ad}\" hattın ilk durağı — öncesinde bir durak yok.";
            return sonuc;
        }

        var onceki = sirali[yeri - 1];
        sonuc.OncekiDurakAdi = onceki.Ad;

        if (!_osrm.Etkin)
        {
            sonuc.Mesaj = "Rota servisi (OSRM) kapalı; alternatif hesaplanamıyor.";
            return sonuc;
        }

        var rotalar = await _osrm.AlternatifRotalarAsync(
            new[] { onceki.Geom.Coordinate, durak.Geom.Coordinate },
            enFazla: AlternatifSayisi);

        if (rotalar.Count == 0)
        {
            sonuc.Mesaj = "OSRM bu iki durak arasında yol bulamadı.";
            return sonuc;
        }

        // Fark, EN İYİYE göre hesaplanıyor: kullanıcı "bu yolu seçersem ne
        // kaybederim?" sorusunun cevabını görsün. Mutlak süreleri yan yana
        // koymak aynı bilgiyi vermiyor — 14 dk ile 17 dk arasındaki farkı
        // kullanıcının kafadan çıkarması gerekirdi.
        var enIyiSure = rotalar[0].SureSaniye;

        sonuc.Alternatifler = rotalar.Select((r, i) => new RotaAlternatifiDto
        {
            Sira = i,
            EnIyi = i == 0,
            Wkt = WktConverter.Write(r.Cizgi),
            MesafeMetre = r.MesafeMetre,
            SureSaniye = r.SureSaniye,
            ViaWkt = WktConverter.Write(OrtaNokta(r.Cizgi)),
            SureFarkiSaniye = r.SureSaniye - enIyiSure,
        }).ToList();

        if (sonuc.Alternatifler.Count == 1)
        {
            // Bu bir hata DEĞİL: iki nokta arasında gerçekten tek makul yol
            // olabiliyor. Söylemezsek kullanıcı "alternatifler nerede?" diye
            // düğmeye tekrar tekrar basar.
            sonuc.Mesaj = "OSRM bu bacak için tek makul yol buldu.";
        }

        return sonuc;
    }

    /// <summary>Kaç alternatif istenecek. Üçten fazlası listede okunmuyor.</summary>
    private const int AlternatifSayisi = 3;

    /// <summary>
    /// Çizginin ORTASINDAKİ nokta — alternatifi seçmek için kullanılan via.
    ///
    /// Uzunluğa göre değil, KÖŞE SAYISINA göre ortası alınıyor. Uzunluğa göre
    /// hesaplamak daha "doğru" görünürdü ama pratikte fark yok: via noktasının
    /// tek işi, o alternatifi diğerlerinden ayıran bir yere düşmek. Köşe
    /// ortası bunu sağlıyor ve ek bir uzunluk hesabı gerektirmiyor.
    /// </summary>
    private static Point OrtaNokta(LineString cizgi)
    {
        var k = cizgi.Coordinates;
        var orta = k[k.Length / 2];
        return new Point(orta.X, orta.Y) { SRID = WktConverter.Srid };
    }

    /// <summary>
    /// Ara noktaları, AİT OLDUKLARI BACAĞIN içine yerleştirir.
    ///
    /// Bir ara nokta "şu durağa gelirken buradan geç" demek. Hangi bacağa
    /// gireceği <see cref="RotaViaDto.DurakId"/> ile AÇIKÇA söyleniyor: via,
    /// o durağın HEMEN ÖNÜNE giriyor.
    ///
    /// ---- NEDEN GEOMETRİDEN TAHMİN ETMİYORUZ? ----
    ///
    /// İlk yazdığımda bacağı, via'nın iki durağa uzaklıklarının toplamı en
    /// küçük olan bacak diye buluyordum. Kulağa makul geliyor ama YANLIŞ
    /// çalışıyor ve canlıda yakaladım:
    ///
    ///   Batıkent(1) → Kızılay(2) → Ulus(3) hattında, Batıkent-Kızılay
    ///   bacağından alınmış bir via noktası için toplamlar
    ///     bacak 1→2 : 7014 + 6061 = 13075 m
    ///     bacak 2→3 : 6061 + 4706 = 10767 m   ← daha küçük!
    ///   çıkıyordu. Yani via, alındığı bacağa değil KOMŞU bacağa düşüyor ve
    ///   rota, seçilen alternatifi izlemek yerine 10 km dolanıyordu.
    ///
    /// Sebep basit: uzaklık toplamı, "doğru parçasına yakınlık" ölçüsü DEĞİL.
    /// Uçları birbirine yakın kısa bir bacak, via uzağında bile olsa küçük
    /// toplam verir. Doğru ölçü noktanın parçaya dik uzaklığıdır — ama onu
    /// yazmaya da gerek yok:
    ///
    /// BACAK ZATEN BİLİNİYOR. Alternatifler tek bir bacak için hesaplandı ve
    /// cevapta <c>DurakId</c> olarak gönderildi. Bildiğimiz bir şeyi
    /// geometriden yeniden tahmin etmek, tahminin yanılabileceği her yerde
    /// hata üretmekten başka bir işe yaramıyor.
    ///
    /// Sıra korunmadan sona eklenseydi rota önce bütün durakları dolaşır,
    /// sonra ara noktaya giderdi — yani seçilen alternatif değil, saçma bir
    /// zikzak çizilirdi.
    /// </summary>
    private static List<Coordinate> DuraklaraViaSerpistir(
        List<Durak> duraklar,
        IReadOnlyList<RotaViaDto> viaNoktalar)
    {
        // Durak id → o durağın ÖNÜNE girecek ara noktalar
        var oncesine = new Dictionary<int, List<Coordinate>>();

        foreach (var via in viaNoktalar)
        {
            Coordinate nokta;
            try
            {
                nokta = WktConverter.Read<Point>(via.Wkt).Coordinate;
            }
            catch (WktFormatException)
            {
                continue;   // bozuk ara nokta rotayı bozmasın, sessizce atlansın
            }

            // Hattın İLK durağı için via anlamsız: öncesinde bacak yok.
            // Yine de gelirse sessizce atlıyoruz — istisna fırlatmak,
            // kullanıcının göremediği bir ayrıntı yüzünden bütün rota
            // hesabını iptal etmek olurdu.
            var indeks = duraklar.FindIndex(d => d.Id == via.DurakId);
            if (indeks <= 0)
            {
                continue;
            }

            if (!oncesine.TryGetValue(via.DurakId, out var liste))
            {
                oncesine[via.DurakId] = liste = new List<Coordinate>();
            }
            liste.Add(nokta);
        }

        var sonuc = new List<Coordinate>();
        foreach (var durak in duraklar)
        {
            if (oncesine.TryGetValue(durak.Id, out var araNoktalar))
            {
                sonuc.AddRange(araNoktalar);
            }
            sonuc.Add(durak.Geom.Coordinate);
        }

        return sonuc;
    }

    /// <summary>Güzergahın duraklarını sırasıyla verir.</summary>
    private static List<Durak> SiraliDuraklar(Guzergah guzergah)
        => guzergah.Duraklar.OrderBy(d => d.Sira).ThenBy(d => d.Id).ToList();

    /// <summary>
    /// Durak diziliminin parmak izi — rotanın güncelliğini anlamanın yolu.
    ///
    /// İÇERİK: her durağın id'si ve koordinatı, SIRAYLA.
    ///   • Sıra değişirse metin değişir (id'lerin dizilişi farklı).
    ///   • Durak taşınırsa değişir (koordinat farklı).
    ///   • Durak eklenir/silinirse değişir (uzunluk farklı).
    ///   • Durağın ADI değişirse DEĞİŞMEZ — rota da değişmeyeceği için
    ///     boşuna OSRM isteği atılmıyor.
    ///
    /// Koordinatlar 6 basamağa yuvarlanıyor (≈11 cm). Ham double yazsaydık
    /// aynı noktanın farklı yuvarlama artıklarıyla okunması imzayı
    /// değiştirebilir ve rota durduk yere "eskimiş" görünürdü.
    ///
    /// InvariantCulture ZORUNLU: Türkçe kültürde ondalık ayırıcı virgül olur
    /// ve aynı dizilim, makinenin diline göre farklı imza üretirdi.
    ///
    /// Neden düz metin değil de SHA-256? 100 duraklı bir hatta düz metin
    /// birkaç KB tutar; kolon sabit 64 karakter olsun diye özetliyoruz.
    /// Kriptografik bir amaç yok — çakışma olasılığı zaten yok denecek kadar
    /// küçük ve sonucu yalnızca "rota eski mi?" uyarısı.
    /// </summary>
    internal static string RotaImzasiUret(IReadOnlyList<Durak> siraliDuraklar)
    {
        var metin = string.Join('|', siraliDuraklar.Select(d => string.Create(
            CultureInfo.InvariantCulture,
            $"{d.Id}:{d.Geom.X:F6},{d.Geom.Y:F6}")));

        var ozet = SHA256.HashData(Encoding.UTF8.GetBytes(metin));
        return Convert.ToHexString(ozet).ToLowerInvariant();
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

        // ---------- Ödev 17: rota ----------
        RotaWkt = guzergah.Rota is null ? null : WktConverter.Write(guzergah.Rota),
        RotaMesafeMetre = guzergah.RotaMesafeMetre,
        RotaSureSaniye = guzergah.RotaSureSaniye,
        RotaHesaplandi = guzergah.RotaHesaplandi,

        // Rota, MEVCUT durak dizilimi için mi hesaplanmış?
        //
        // Rota yoksa "güncel" diyoruz (false değil): ortada eskimiş bir şey
        // yok, sadece hiç hesaplanmamış. Arayüz o durumu zaten RotaWkt'nin
        // boş olmasından anlıyor; burada false dönseydi "rota güncel değil"
        // uyarısı hiç rota olmayan hatlarda da çıkardı.
        RotaGuncel = guzergah.Rota is null
            || guzergah.RotaImza == RotaImzasiUret(SiraliDuraklar(guzergah)),
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
