using NetTopologySuite.Geometries;
using StajProject.Business.Analiz;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Validation;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

/// <summary>
/// Ödev 14 — analiz panelinin iş katmanı.
///
/// Üç iş yapıyor, sırayla:
///   1) KURALLARI DOĞRULA  — kriter sayısı, ağırlık toplamı, alan seçimi
///   2) VERİYİ TOPLA       — hedef alanın geometrisi + alan içindeki POI'ler
///   3) HESABI ÇAĞIR       — <see cref="AgirlikliIsiIzgarasi"/>
///
/// Hesabın kendisi bilinçli olarak burada değil: o saf bir matematik ve
/// veritabanı bilmiyor, bu yüzden ayrı bir dosyada ve tek başına test
/// edilebilir. Bu sınıfın işi ORKESTRASYON.
/// </summary>
public class KonumAnaliziService : IKonumAnaliziService
{
    /// <summary>Ödev metni: "en az 2, en fazla 5 adet kategori bazlı kriter".</summary>
    public const int EnAzKriter = 2;
    public const int EnCokKriter = 5;

    /// <summary>Ödev metni: "tüm kriterlerin puanları toplamı tam olarak 100 olmalı".</summary>
    public const int ToplamAgirlik = 100;

    /// <summary>Kaç aday konum önerilecek.</summary>
    private const int AdaySayisi = 5;

    private readonly IPoiRepository _poiRepository;
    private readonly IPoiCategoryRepository _kategoriRepository;
    private readonly IIlRepository _ilRepository;

    public KonumAnaliziService(
        IPoiRepository poiRepository,
        IPoiCategoryRepository kategoriRepository,
        IIlRepository ilRepository)
    {
        _poiRepository = poiRepository;
        _kategoriRepository = kategoriRepository;
        _ilRepository = ilRepository;
    }

    public async Task<KonumAnaliziSonucuDto> CalistirAsync(KonumAnaliziRequestDto istek)
    {
        var kriterler = KriterleriDogrula(istek.Kriterler);
        var (alan, alanAdi) = await AlaniCozAsync(istek);

        // Kategori ağacı bir kez okunuyor; hem yol metinleri hem de
        // "bu kategori şu kriterin altında mı?" eşlemesi bundan türüyor.
        var kategoriler = await _kategoriRepository.GetAllAsync();
        var kategoriIdIle = kategoriler.ToDictionary(k => k.Id);

        foreach (var kriter in kriterler)
        {
            if (!kategoriIdIle.ContainsKey(kriter.KategoriId))
            {
                throw new IsKuraliException(
                    $"Id={kriter.KategoriId} olan POI kategorisi bulunamadı.");
            }
        }

        // Alt kategori → kriterin kendi kategorisi. "Yeme-İçme" seçildiğinde
        // Restoran/Kafe/Fırın POI'leri de o kriterin hanesine yazılsın diye.
        var kriterKategorileri = kriterler.Select(k => k.KategoriId).ToHashSet();
        var kriterEslemesi = KriterEslemesiKur(kategoriler, kriterKategorileri);

        // ---- Alan içindeki POI'ler ----
        //
        // Süzme VERİTABANINDA (PostGIS ST_Intersects) yapılıyor; "hepsini çek,
        // C#'ta ele" yolu 2500 kayıtla da çalışırdı ama analiz alanı küçüldükçe
        // taşınan verinin küçülmesi gereken bir yerde sabit kalırdı.
        // Ödev metni: "Analiz yalnızca seçilen alan içerisindeki POI'ler
        // üzerinde çalışmalıdır."
        var alandakiler = await _poiRepository.AlandakileriGetirAsync(alan);

        var analizPoileri = new List<AnalizPoisi>();
        var kriterSayaci = kriterKategorileri.ToDictionary(id => id, _ => 0);

        foreach (var poi in alandakiler)
        {
            // Kriterlerden hiçbirine girmeyen POI hesaba katılmıyor: kullanıcı
            // "eczane ve okul" dediyse hastanenin yüzeyi etkilemesi yanlış olurdu.
            if (!kriterEslemesi.TryGetValue(poi.KategoriId, out var kriterKategorisi)) continue;
            if (poi.Geom is null) continue;

            analizPoileri.Add(new AnalizPoisi(
                kriterKategorisi, poi.Isim, poi.Geom.X, poi.Geom.Y));

            kriterSayaci[kriterKategorisi]++;
        }

        // ---- Hesap ----
        var agirliklar = kriterler
            .Select(k => new AnalizAgirligi(k.KategoriId, k.Agirlik))
            .ToList();

        var izgara = AgirlikliIsiIzgarasi.Hesapla(alan, agirliklar, analizPoileri, AdaySayisi);

        var yollar = YollariHesapla(kategoriler);

        return new KonumAnaliziSonucuDto
        {
            AlanWkt = WktConverter.Write(alan),
            AlanAdi = alanAdi,
            AlanKm2 = AlanKm2(alan),
            ToplamPoi = analizPoileri.Count,
            Kriterler = kriterler.Select(k => new AnalizKriterSonucuDto
            {
                KategoriId = k.KategoriId,
                KategoriAdi = kategoriIdIle[k.KategoriId].Ad,
                KategoriYolu = yollar.TryGetValue(k.KategoriId, out var yol) ? yol : kategoriIdIle[k.KategoriId].Ad,
                Agirlik = k.Agirlik,
                PoiSayisi = kriterSayaci[k.KategoriId],
            }).ToList(),
            Izgara = new IsiIzgarasiDto
            {
                Extent = izgara.Extent,
                Sutun = izgara.Sutun,
                Satir = izgara.Satir,
                HucreMetre = Math.Round(izgara.HucreMetre, 1),
                EtkiYaricapiMetre = Math.Round(izgara.EtkiYaricapiMetre, 1),
                // Dört basamak, hücre başına ~6 karakter demek. Ham double
                // yazsaydık ("0.7231882094832131") aynı bilgi üç katı yer
                // tutardı; ekranda 0.0001'lik fark zaten görünmüyor.
                Degerler = izgara.Degerler.Select(d => d < 0 ? -1 : Math.Round(d, 4)).ToArray(),
                EnYuksekSkor = Math.Round(izgara.EnYuksekSkor, 4),
            },
            Adaylar = izgara.Adaylar.Select((a, i) => new AdayKonumDto
            {
                Sira = i + 1,
                Boylam = Math.Round(a.Boylam, 6),
                Enlem = Math.Round(a.Enlem, 6),
                Skor = Math.Round(a.Skor * 100, 1),
                Mesafeler = a.Mesafeler.Select(m => new AdayMesafesiDto
                {
                    KategoriId = m.KategoriId,
                    KategoriAdi = kategoriIdIle.TryGetValue(m.KategoriId, out var kat) ? kat.Ad : "?",
                    PoiAdi = m.PoiAdi,
                    MesafeMetre = m.MesafeMetre is null ? null : Math.Round(m.MesafeMetre.Value),
                }).ToList(),
            }).ToList(),
        };
    }

    // =====================================================================
    //  Doğrulama
    // =====================================================================

    /// <summary>
    /// Ödev metnindeki üç kuralı uygular: kriter sayısı 2–5, ağırlıklar
    /// toplamı tam 100, aynı kategori iki kez seçilemez.
    ///
    /// ÜÇÜNCÜ KURAL ÖDEVDE YAZMIYOR ama zorunlu: aynı kategori iki satırda
    /// 30 + 30 puanla seçilseydi o kategorinin yüzeyi iki kez toplanır,
    /// kullanıcı 60 puanlık tek bir kriter yazmış gibi olurdu — yani sessizce
    /// "çalışan" ama anlamsız bir sonuç. Sessiz yanlıştansa açık hata.
    /// </summary>
    private static List<AnalizKriteriDto> KriterleriDogrula(List<AnalizKriteriDto>? kriterler)
    {
        var liste = kriterler ?? new List<AnalizKriteriDto>();

        if (liste.Count < EnAzKriter || liste.Count > EnCokKriter)
        {
            throw new IsKuraliException(
                $"Analiz için en az {EnAzKriter}, en fazla {EnCokKriter} kriter seçilmelidir. " +
                $"Gönderilen kriter sayısı: {liste.Count}.");
        }

        if (liste.Any(k => k.Agirlik < 1 || k.Agirlik > 100))
        {
            throw new IsKuraliException("Her kriterin ağırlık puanı 1 ile 100 arasında olmalıdır.");
        }

        var toplam = liste.Sum(k => k.Agirlik);
        if (toplam != ToplamAgirlik)
        {
            throw new IsKuraliException(
                $"Kriter puanlarının toplamı tam olarak {ToplamAgirlik} olmalıdır. " +
                $"Şu anki toplam: {toplam}.");
        }

        if (liste.Select(k => k.KategoriId).Distinct().Count() != liste.Count)
        {
            throw new IsKuraliException("Aynı kategori birden fazla kritere verilemez.");
        }

        return liste;
    }

    /// <summary>
    /// Hedef bölgeyi çözer: ya seçilen illerin birleşimi, ya çizilen poligon.
    /// </summary>
    private async Task<(Geometry Alan, string Ad)> AlaniCozAsync(KonumAnaliziRequestDto istek)
    {
        var plakalar = istek.IlPlakalari?.Distinct().ToList() ?? new List<int>();
        var cizimVar = !string.IsNullOrWhiteSpace(istek.Wkt);

        if (plakalar.Count > 0 && cizimVar)
        {
            throw new IsKuraliException(
                "Hedef bölge ya il listesinden ya da haritaya çizilerek seçilmelidir; ikisi birden gönderilemez.");
        }

        if (plakalar.Count == 0 && !cizimVar)
        {
            throw new IsKuraliException(
                "Analiz için hedef bölge seçilmelidir: il listesinden seçim yapın ya da haritada bir alan çizin.");
        }

        if (cizimVar)
        {
            // Tip kontrolü Geometry üzerinden: kullanıcı poligon çiziyor ama
            // MultiPolygon da kabul edilebilir olmalı (ileride "birden çok
            // parça çiz" istenirse uç değişmesin). Nokta/çizgi ise alan
            // kaplamadığı için analiz edilemez.
            var cizilen = WktConverter.Read<Geometry>(istek.Wkt);

            if (cizilen is not (Polygon or MultiPolygon))
            {
                throw new WktFormatException(
                    $"Analiz alanı POLYGON olmalıdır; gelen tip: {cizilen.GeometryType}.");
            }

            return (cizilen, "Haritada çizilen alan");
        }

        var birlesim = await _ilRepository.IllerinBirlesimiAsync(plakalar);
        if (birlesim is null)
        {
            throw new IsKuraliException("Seçilen plakalara karşılık gelen il bulunamadı.");
        }

        // Ad için illerin kendi listesini okuyoruz: istemcinin gönderdiği
        // sıraya değil, plaka sırasına göre yazılıyor ki aynı seçim her
        // seferinde aynı başlığı üretsin.
        var iller = await _ilRepository.OzetGetirAsync();
        var adlar = iller
            .Where(i => plakalar.Contains(i.Id))
            .OrderBy(i => i.Id)
            .Select(i => i.Ad)
            .ToList();

        // Uzun listeler paneli taşırmasın: ilk üç il + "ve N il daha".
        var ad = adlar.Count <= 3
            ? string.Join(", ", adlar)
            : $"{string.Join(", ", adlar.Take(3))} ve {adlar.Count - 3} il daha";

        return (birlesim, ad);
    }

    // =====================================================================
    //  Kategori ağacı yardımcıları
    // =====================================================================

    /// <summary>
    /// "Bu POI kategorisi hangi kriterin altında?" sözlüğü.
    ///
    /// Her kategori için ata zinciri yukarı doğru yürünüyor; zincirde bir
    /// kriter kategorisine rastlanırsa eşleme kurulur. En YAKIN ata kazanıyor:
    /// hem "Yeme-İçme" hem "Restoran" ayrı kriterler olarak seçilseydi bir
    /// restoran POI'si "Restoran" kriterine yazılırdı. (Bu durum zaten
    /// mümkün değil — iki kriter aynı POI'yi paylaşamasın diye ata-torun
    /// çiftinin ikisi birden seçilirse eşleme yalnızca yaprağı sayar.)
    /// </summary>
    private static Dictionary<int, int> KriterEslemesiKur(
        List<PoiCategory> kategoriler, HashSet<int> kriterKategorileri)
    {
        var idIle = kategoriler.ToDictionary(k => k.Id);
        var esleme = new Dictionary<int, int>();

        foreach (var kategori in kategoriler)
        {
            var gecerli = kategori;
            var guvenlik = 0;

            while (true)
            {
                if (kriterKategorileri.Contains(gecerli.Id))
                {
                    esleme[kategori.Id] = gecerli.Id;
                    break;
                }

                if (gecerli.ParentId is null || !idIle.TryGetValue(gecerli.ParentId.Value, out var ata))
                {
                    break;
                }

                gecerli = ata;

                // Veriye döngü sızmışsa sonsuza kadar dönmeyelim
                // (PoiService.YollariHesaplaAsync ile aynı güvenlik).
                if (++guvenlik > 64) break;
            }
        }

        return esleme;
    }

    /// <summary>Kategori id → tam yol ("Sağlık › Eczane").</summary>
    private static Dictionary<int, string> YollariHesapla(List<PoiCategory> kategoriler)
    {
        var idIle = kategoriler.ToDictionary(k => k.Id);

        return kategoriler.ToDictionary(k => k.Id, k =>
        {
            var parcalar = new List<string> { k.Ad };
            var gecerli = k;
            var guvenlik = 0;

            while (gecerli.ParentId is not null && idIle.TryGetValue(gecerli.ParentId.Value, out var ata))
            {
                parcalar.Insert(0, ata.Ad);
                gecerli = ata;
                if (++guvenlik > 64) break;
            }

            return string.Join(PoiCategoryService.YolAyraci, parcalar);
        });
    }

    /// <summary>
    /// Alanın yaklaşık yüzölçümü (km²).
    ///
    /// <c>Geometry.Area</c> derece-kare döndürüyor — coğrafi bir büyüklük
    /// değil. Ortalama enlemde bir derecenin metre karşılığıyla çarparak
    /// yaklaşık km²'ye çeviriyoruz. Kesin sonuç için geometriyi eşit alanlı
    /// bir projeksiyona (örn. EPSG:5070 muadili) taşımak gerekirdi; bu sayı
    /// ekranda "analiz alanı ~24.500 km²" diye BAĞLAM veriyor, hesaba
    /// girmiyor, o yüzden yaklaşık olması yeterli.
    /// </summary>
    private static double AlanKm2(Geometry alan)
    {
        var zarf = alan.EnvelopeInternal;
        var ortaEnlem = (zarf.MinY + zarf.MaxY) / 2.0;

        var kmDereceX = 111.320 * Math.Cos(ortaEnlem * Math.PI / 180.0);
        const double kmDereceY = 110.540;

        return Math.Round(alan.Area * kmDereceX * kmDereceY, 1);
    }
}
