using NetTopologySuite.Geometries;
using StajProject.Business.Analiz;
using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.Business.Validation;
using StajProject.DataAccess.Repositories;

namespace StajProject.Business.Services;

/// <summary>
/// Toplu taşıma erişilebilirlik analizinin orkestrasyonu.
///
/// Alan çözme mantığı <see cref="KonumAnaliziService.AlaniCozAsync"/> ile
/// KASITLI OLARAK AYNI ama AYRI KOPYA: iki servis farklı hızlarda değişebilir
/// (biri POI kriterleri, diğeri durak/güzergah kavramlarıyla uğraşıyor) ve
/// ortak bir temel sınıf çıkarmak, aralarında gereksiz bir bağımlılık
/// kurardı. Kod kısa (otuz satır) — tekrarın maliyeti, yanlış bir soyutlamanın
/// maliyetinden düşük.
/// </summary>
public class ErisilebilirlikService : IErisilebilirlikService
{
    private readonly IUlasimRepository _ulasimRepository;
    private readonly IIlRepository _ilRepository;

    public ErisilebilirlikService(IUlasimRepository ulasimRepository, IIlRepository ilRepository)
    {
        _ulasimRepository = ulasimRepository;
        _ilRepository = ilRepository;
    }

    public async Task<ErisilebilirlikSonucuDto> CalistirAsync(ErisilebilirlikRequestDto istek)
    {
        var (alan, alanAdi) = await AlaniCozAsync(istek);

        var guzergahlar = await _ulasimRepository.GuzergahlariGetirAsync();

        var duraklar = guzergahlar
            .Where(g => !istek.YalnizcaAktif || g.IsActive)
            .SelectMany(g => g.Duraklar)
            .Where(d => !istek.YalnizcaAktif || d.IsActive)
            .Select(d => new ErisimDuragi(d.Id, d.Ad, d.Geom.X, d.Geom.Y))
            .ToList();

        var sonuc = TopluTasimaErisilebilirligi.Hesapla(alan, duraklar);

        return new ErisilebilirlikSonucuDto
        {
            AlanWkt = WktConverter.Write(alan),
            AlanAdi = alanAdi,
            DurakSayisi = duraklar.Count,
            IyiErisimYuzdesi = Math.Round(sonuc.IyiErisimOrani * 100, 2),
            Uyarilar = Uyarilar(istek, sonuc, duraklar.Count),
            Izgara = new IsiIzgarasiDto
            {
                Extent = sonuc.Extent,
                Sutun = sonuc.Sutun,
                Satir = sonuc.Satir,
                HucreMetre = Math.Round(sonuc.HucreMetre, 1),
                EtkiYaricapiMetre = TopluTasimaErisilebilirligi.SifirMesafeMetre,
                Degerler = sonuc.Degerler.Select(d => d < 0 ? -1 : Math.Round(d, 4)).ToArray(),
                EnYuksekSkor = Math.Round(sonuc.EnYuksekSkor, 4),
            },
        };
    }

    /// <summary>
    /// Sonucun yanına, onu doğru okutacak notlar.
    ///
    /// Canlıda görülen yanılgı: iki il seçilince "%0" çıktı ve kullanıcı
    /// analizi bozuk sandı. Sayı doğruydu — 25.000 km²'lik bir alanın
    /// kırsalı da sayılıyor ve sistemde o bölge için 15 durak var. Ama
    /// bunu söyleyen kimse yoktu. Sonuç geçerli olduğu için bunlar HATA
    /// değil, açıklama.
    /// </summary>
    private static List<string> Uyarilar(
        ErisilebilirlikRequestDto istek, ErisilebilirlikSonucu sonuc, int durakSayisi)
    {
        var uyarilar = new List<string>();

        if (durakSayisi == 0)
        {
            return uyarilar;   // panel zaten "durak yok" diyor
        }

        // Kaba hücre: 400 m eşiği, hücre kenarından küçükse ölçüm yaklaşıktır.
        if (sonuc.HucreMetre > TopluTasimaErisilebilirligi.IyiEsikMetre)
        {
            uyarilar.Add(
                $"Alan büyük olduğu için ızgara hücreleri yaklaşık {Math.Round(sonuc.HucreMetre / 50) * 50:0} m; " +
                $"{TopluTasimaErisilebilirligi.IyiEsikMetre:0} m eşiği bu çözünürlükte yaklaşık ölçülüyor.");
        }

        // İl seçildiyse oran ilin TAMAMI üzerinden — kırsal dahil.
        var ilSecildi = istek.IlPlakalari is { Count: > 0 };

        if (ilSecildi && sonuc.IyiErisimOrani < 0.05)
        {
            uyarilar.Add(
                "Oran ilin tamamı üzerinden hesaplanıyor; kırsal alanlar da sayıldığı için " +
                "düşük çıkması doğaldır. Sarı adacıklar durakların çevresini gösterir.");
        }

        // Az durak: sonuç "toplu taşıma yok" değil, "sistemde kayıt yok" demek olabilir.
        if (durakSayisi < 20)
        {
            uyarilar.Add(
                $"Bu bölge için sistemde yalnızca {durakSayisi} durak kayıtlı. " +
                "Analiz gerçek ağı değil, kayıtlı durakları ölçer.");
        }

        return uyarilar;
    }

    /// <summary>
    /// Hedef bölgeyi çözer: ya seçilen illerin birleşimi, ya çizilen poligon.
    /// KonumAnaliziService.AlaniCozAsync ile birebir aynı kural — gerekçe
    /// sınıf başlığında.
    /// </summary>
    private async Task<(Geometry Alan, string Ad)> AlaniCozAsync(ErisilebilirlikRequestDto istek)
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

        var iller = await _ilRepository.OzetGetirAsync();
        var adlar = iller
            .Where(i => plakalar.Contains(i.Id))
            .OrderBy(i => i.Id)
            .Select(i => i.Ad)
            .ToList();

        var ad = adlar.Count <= 3
            ? string.Join(", ", adlar)
            : $"{string.Join(", ", adlar.Take(3))} ve {adlar.Count - 3} il daha";

        return (birlesim, ad);
    }
}
