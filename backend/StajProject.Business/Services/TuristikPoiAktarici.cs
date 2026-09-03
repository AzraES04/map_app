using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using StajProject.Business.DTOs;
using StajProject.Business.Tur;
using StajProject.Business.Validation;
using StajProject.DataAccess.Google;
using StajProject.DataAccess.Repositories;
using StajProject.DataAccess.Yerel;
using StajProject.Entities;

namespace StajProject.Business.Services;

/// <summary>
/// <see cref="ITuristikPoiAktarici"/> gerçeklemesi — kaynak OpenStreetMap.
///
/// ---- NEDEN IOsmPlacesClient (IPlacesClient DEĞİL)? ----
/// Arayüz (<see cref="IPlacesClient"/>) üzerinden çağırsaydık, Google anahtarı
/// tanımlı bir kurulumda aktarım GOOGLE'a giderdi: yüzlerce mekanı içe
/// aktarmak için faturalanan çağrılar. Üstelik Google'ın kullanım şartları
/// sonuçların kalıcı saklanmasına izin vermiyor — OSM'in ODbL lisansı ise
/// veriyi saklamaya ve türetmeye açıkça izin veriyor (kaynak gösterme
/// koşuluyla; bkz. POI açıklaması).
///
/// Yani buradaki bağımlılık BİLEREK somut: "içe aktarılan veri OSM'dendir"
/// bir uygulama detayı değil, hukuki ve maliyetsel bir karar.
/// </summary>
public class TuristikPoiAktarici : ITuristikPoiAktarici
{
    /// <summary>Kök kategori — turistik duraklar POI ağacında burada toplanıyor.</summary>
    private const string KokKategori = "Gezilecek Yer";

    /// <summary>
    /// Aktarım yarıçapı (metre).
    ///
    /// 25 km: Ankara'da Etimesgut'tan Mamak'a, İstanbul'da Bakırköy'den
    /// Kadıköy'e yetiyor. Daha büyük yarıçap şehir dışındaki köy camilerini
    /// de getirir ve listeyi seyreltirdi.
    /// </summary>
    private const int YaricapMetre = 25_000;

    /// <summary>
    /// Arama başına azami sonuç.
    ///
    /// Tur önerisi 20 ile yetiniyor; aktarım şehri doldurmak istiyor. 250 ×
    /// yedi arama = şehir başına en fazla ~1750 kayıt: harita bunu rahat
    /// çiziyor (GeoServer WMS zaten sunucuda basıyor), veritabanı için de
    /// önemsiz. Sınırsız bırakmak İstanbul'da on binlerce satır demekti ve
    /// vektör katmanı tıklama için hepsini belleğe alıyor.
    /// </summary>
    private const int AramaBasinaSonuc = 250;

    /// <summary>
    /// Aynı sayılma eşiği (metre). Bu mesafeden yakın ve AYNI ADLI bir kayıt
    /// varsa yeni satır açılmıyor.
    ///
    /// Neden isim + mesafe? OSM id'sini saklayacak bir kolon yok (poi tablosu
    /// Ödev 12'de tanımlandı) ve şema değiştirmek bu iş için ağır kaçardı.
    /// İsim tek başına da yetmezdi: "Merkez Camii" her şehirde var.
    /// </summary>
    private const double AyniSayilmaMetre = 100;

    /// <summary>
    /// OSM tip anahtarı → (kategori adı, ikon anahtarı).
    ///
    /// Yeme-içme AYRI kategorilerde ve "Gezilecek Yer" altında: seed'den gelen
    /// "Restoran"/"Kafe" kategorileri SENTETİK veri taşıyor (şablon adlar).
    /// Gerçek mekanları oraya karıştırsaydık kullanıcı hangi kaydın gerçek
    /// olduğunu ayırt edemezdi.
    /// </summary>
    private static readonly Dictionary<string, (string Kategori, string Ikon)> Esleme =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["museum"] = ("Müze", "muze"),
            ["art_gallery"] = ("Müze", "muze"),
            ["monument"] = ("Tarihi Yer", "anit"),
            ["tourist_attraction"] = ("Tarihi Yer", "anit"),
            ["park"] = ("Park", "agac"),
            ["mosque"] = ("İbadet Yeri", "pin"),
            ["church"] = ("İbadet Yeri", "pin"),
            ["restaurant"] = ("Yöresel Lezzet", "catal-bicak"),
            ["cafe"] = ("Kahve & Tatlı", "fincan"),
        };

    /// <summary>
    /// Aktarımda kullanılan aramalar.
    ///
    /// Yedi arama TEK Overpass sorgusunda birleşiyor (bkz. IPlacesClient.AraAsync),
    /// yani şehir başına bir istek.
    /// </summary>
    private static readonly (string Tip, string Sorgu)[] Aramalar =
    {
        ("museum", "müze"),
        ("tourist_attraction", "tarihi yer"),
        ("art_gallery", "sanat galerisi"),
        ("park", "park"),
        ("mosque", "tarihi cami"),
        ("restaurant", "yöresel restoran"),
        ("cafe", "kafe"),
    };





    private readonly IOsmPlacesClient _osm;
    private readonly IPoiRepository _poiler;
    private readonly IPoiCategoryRepository _kategoriler;
    private readonly IIlRepository _iller;
    private readonly IPoiStyleService? _stiller;
    private readonly ILogger<TuristikPoiAktarici> _logger;

    public TuristikPoiAktarici(
        IOsmPlacesClient osm,
        IPoiRepository poiler,
        IPoiCategoryRepository kategoriler,
        IIlRepository iller,
        ILogger<TuristikPoiAktarici> logger,
        // İSTEĞE BAĞLI: GeoServer kapalı bir kurulumda da aktarım çalışmalı.
        // Üretimde kayıtlı olduğu için her zaman geliyor; testlerde
        // verilmediğinde stil yenileme adımı sessizce atlanıyor
        // (PoiCategoryService'teki aynı desen).
        IPoiStyleService? stiller = null)
    {
        _osm = osm;
        _poiler = poiler;
        _kategoriler = kategoriler;
        _iller = iller;
        _stiller = stiller;
        _logger = logger;
    }

    public async Task<TuristikAktarimSonucuDto> AktarAsync(
        IReadOnlyList<int> ilPlakalari,
        CancellationToken iptal = default)
    {
        if (ilPlakalari.Count == 0)
        {
            throw new IsKuraliException("En az bir şehir seçilmelidir.");
        }

        if (ilPlakalari.Any(p => p is < 1 or > 81))
        {
            throw new IsKuraliException("Geçersiz plaka kodu.");
        }

        if (!_osm.Etkin)
        {
            throw new DisServisException(
                "OpenStreetMap kaynağı kapalı (YerelTurKaynagi:Enabled).");
        }

        var kategoriIdleri = await KategorileriHazirlaAsync();
        var sonuc = new TuristikAktarimSonucuDto();

        // Mevcut POI'ler bir kez okunuyor: her aday için ayrı sorgu atmak,
        // yüzlerce mekan için yüzlerce veritabanı gidiş-gelişi olurdu.
        var mevcutlar = await _poiler.GetAllAsync();

        foreach (var plaka in ilPlakalari.Distinct())
        {
            var eklenen = await SehriAktarAsync(plaka, kategoriIdleri, mevcutlar, sonuc, iptal);

            _logger.LogInformation(
                "Turistik POI aktarımı: {Plaka} plakası için {Sayi} kayıt eklendi.",
                plaka, eklenen);
        }

        if (sonuc.Eklenen > 0)
        {
            await StilleriYenileAsync(sonuc);
        }

        return sonuc;
    }

    // ------------------------------------------------------------------
    //  Kategoriler
    // ------------------------------------------------------------------

    /// <summary>
    /// Turistik kategori ağacını hazırlar; varsa dokunmaz.
    ///
    /// Yönetici bu kategorileri panelden yeniden adlandırmış olabilir — o
    /// yüzden ada göre ARANıyor ve bulunamazsa açılıyor. Her aktarımda
    /// silip yeniden yaratmak, yöneticinin düzenlemesini ezerdi.
    /// </summary>
    private async Task<Dictionary<string, int>> KategorileriHazirlaAsync()
    {
        var kok = await _kategoriler.GetByNameAsync(KokKategori, null)
                  ?? await _kategoriler.AddAsync(new PoiCategory
                  {
                      Ad = KokKategori,
                      Aciklama = "OpenStreetMap'ten içe aktarılan turistik duraklar.",
                      Ikon = "anit",
                      CreatedDate = DateTime.UtcNow,
                  });

        var sonuc = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (kategoriAdi, ikon) in Esleme.Values.Distinct())
        {
            var kategori = await _kategoriler.GetByNameAsync(kategoriAdi, kok.Id)
                           ?? await _kategoriler.AddAsync(new PoiCategory
                           {
                               Ad = kategoriAdi,
                               ParentId = kok.Id,
                               Ikon = ikon,
                               CreatedDate = DateTime.UtcNow,
                           });

            sonuc[kategoriAdi] = kategori.Id;
        }

        return sonuc;
    }

    // ------------------------------------------------------------------
    //  Şehir aktarımı
    // ------------------------------------------------------------------

    private async Task<int> SehriAktarAsync(
        int plaka,
        IReadOnlyDictionary<string, int> kategoriIdleri,
        List<Poi> mevcutlar,
        TuristikAktarimSonucuDto sonuc,
        CancellationToken iptal)
    {
        var merkez = SehirMerkezleri.Bul(plaka);

        if (merkez is null)
        {
            sonuc.Uyarilar.Add($"{plaka} plakası için şehir merkezi tanımlı değil, atlandı.");
            return 0;
        }

        var il = (await _iller.OzetGetirAsync()).FirstOrDefault(i => i.Id == plaka);
        var sehirAdi = il?.Ad ?? plaka.ToString();

        var aramalar = Aramalar
            .Select(a => new PlacesAramasi(
                Sorgu: a.Sorgu,
                Tip: a.Tip,
                Lat: merkez.Value.Lat,
                Lon: merkez.Value.Lon,
                YaricapMetre: YaricapMetre,
                // Puan süzgeci OSM'de anlamsız; alan sözleşme gereği dolu.
                EnAzPuan: 0,
                EnFazlaSonuc: AramaBasinaSonuc))
            .ToList();

        var mekanlar = await _osm.AraAsync(aramalar, iptal);

        if (mekanlar.Count == 0)
        {
            sonuc.Uyarilar.Add(
                $"{sehirAdi} için OpenStreetMap'ten sonuç alınamadı " +
                "(servis yoğun olabilir, birkaç dakika sonra tekrar deneyin).");
            return 0;
        }

        var yeniler = new List<Poi>();

        foreach (var mekan in mekanlar)
        {
            var tip = mekan.Tipler.FirstOrDefault(t => Esleme.ContainsKey(t));

            if (tip is null || !Esleme.TryGetValue(tip, out var eslesme)
                || !kategoriIdleri.TryGetValue(eslesme.Kategori, out var kategoriId))
            {
                sonuc.Atlanan++;
                continue;
            }

            // OSM tip anahtarını ("restaurant") tur mekan tipine çeviriyoruz:
            // kalite süzgeci tur önerisiyle AYNI imzayı kullanıyor, iki
            // çağıranın kuralı ayrışmasın.
            var mekanTipi = tip switch
            {
                "restaurant" => VenueType.Restaurant,
                "cafe" => VenueType.Cafe,
                _ => VenueType.Other,
            };

            if (!MekanKalitesi.YemeIcmeUygunMu(mekan, mekanTipi, puanVerisiVar: false))
            {
                sonuc.Atlanan++;
                continue;
            }

            var nokta = new Point(mekan.Lon, mekan.Lat) { SRID = 4326 };

            if (ZatenVar(mevcutlar, yeniler, mekan.Ad, nokta))
            {
                sonuc.Atlanan++;
                continue;
            }

            var poi = new Poi
            {
                Isim = mekan.Ad,
                KategoriId = kategoriId,
                Geom = nokta,
                // Kaynak gösterme ODbL lisansının koşulu; mesai alanı zaten
                // serbest metin taşıyor ve popup'ta görünüyor.
                MesaiSaatleri = "Kaynak: OpenStreetMap (ODbL)",
                CreatedDate = DateTime.UtcNow,
            };

            yeniler.Add(poi);
            sonuc.KategoriBazinda[eslesme.Kategori] =
                sonuc.KategoriBazinda.GetValueOrDefault(eslesme.Kategori) + 1;
        }

        if (yeniler.Count > 0)
        {
            // Tek işlemde: yüzlerce kayıt için tek tek AddAsync çağırmak
            // yüzlerce SaveChanges demek olurdu (bkz. IPoiRepository).
            await _poiler.TopluEkleAsync(yeniler);
            mevcutlar.AddRange(yeniler);
        }

        sonuc.Eklenen += yeniler.Count;
        sonuc.Sehirler[sehirAdi] = yeniler.Count;

        return yeniler.Count;
    }

    /// <summary>
    /// Yeni kategoriler için GeoServer stillerini üretir.
    ///
    /// ---- NEDEN AKTARIMIN PARÇASI? ----
    /// Haritadaki POI katmanı KATEGORİ BAŞINA bir SLD ile çiziliyor. Yeni
    /// kategori açılıp stili üretilmezse kayıtlar veritabanında durur ama
    /// haritada hiç görünmez — kullanıcı için "aktarım çalışmadı" demektir.
    /// Yönetim panelindeki "Harita stillerini yenile" düğmesi aynı işi
    /// yapıyor; aktarımın bunu kendiliğinden yapması, iki adımı bir adıma
    /// indiriyor.
    ///
    /// Sessiz sürüm çağrılıyor: GeoServer kapalıysa aktarım BAŞARISIZ
    /// sayılmamalı, kayıtlar zaten yazıldı.
    /// </summary>
    private async Task StilleriYenileAsync(TuristikAktarimSonucuDto sonuc)
    {
        if (_stiller is null) return;

        try
        {
            await _stiller.SessizYenileAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Aktarım sonrası stil yenileme başarısız.");
            sonuc.Uyarilar.Add(
                "Kayıtlar eklendi ama harita stilleri yenilenemedi; " +
                "POI Yönetimi ekranından \"Harita stillerini yenile\" deneyin.");
        }
    }

    /// <summary>
    /// Aynı adlı bir kayıt yakında duruyor mu?
    ///
    /// Mesafe karşılaştırması DERECE cinsinden yapılıyor (yaklaşık): 100 metre
    /// Türkiye enlemlerinde ~0.0009°. Kesin bir jeodezik hesap gerekmiyor —
    /// eşik zaten kaba ve amaç mükerrer kaydı elemek.
    /// </summary>
    private static bool ZatenVar(
        IEnumerable<Poi> mevcutlar,
        IEnumerable<Poi> yeniler,
        string ad,
        Point nokta)
    {
        var esik = AyniSayilmaMetre / 111_000.0;

        bool Yakin(Poi p) =>
            string.Equals(p.Isim, ad, StringComparison.OrdinalIgnoreCase)
            && p.Geom is not null
            && Math.Abs(p.Geom.X - nokta.X) < esik
            && Math.Abs(p.Geom.Y - nokta.Y) < esik;

        return mevcutlar.Any(Yakin) || yeniler.Any(Yakin);
    }
}
