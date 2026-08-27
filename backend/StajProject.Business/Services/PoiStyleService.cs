using Microsoft.Extensions.Logging;
using StajProject.Business.DTOs;
using StajProject.Business.Poiler;
using StajProject.DataAccess.GeoServer;
using StajProject.DataAccess.Repositories;

namespace StajProject.Business.Services;

public class PoiStyleService : IPoiStyleService
{
    /// <summary>
    /// Ödev 13'ün İLK uygulamasından kalan, elle yazılmış stil adları.
    ///
    /// O sürümde stiller kök kategoriye göre sabit yazılıydı. Artık kategori
    /// tablosundan üretiliyorlar; bu adların karşılığı yok. Yenileme sırasında
    /// siliniyorlar ki GeoServer'ın stil listesinde çalışmayan artıklar
    /// kalmasın — birisi onları katmana bağlarsa harita sessizce yanlış
    /// çizerdi.
    /// </summary>
    private static readonly string[] EskiStiller =
    {
        "poi_yeme_icme", "poi_konaklama", "poi_saglik", "poi_egitim",
    };

    private readonly IPoiCategoryRepository _kategoriRepository;
    private readonly IGeoServerClient _geoServer;
    private readonly ILogger<PoiStyleService> _logger;

    public PoiStyleService(
        IPoiCategoryRepository kategoriRepository,
        IGeoServerClient geoServer,
        ILogger<PoiStyleService> logger)
    {
        _kategoriRepository = kategoriRepository;
        _geoServer = geoServer;
        _logger = logger;
    }

    public async Task<List<PoiStilDto>> ListeleAsync()
        => (await UretAsync()).Select(DtoyaCevir).ToList();

    public async Task<PoiStilYenilemeDto> YenileAsync(CancellationToken iptal = default)
    {
        var stiller = await UretAsync();

        // ---- 1) SİMGE DOSYALARINI yaz (Ödev 15) ----
        //
        // SLD'LERDEN ÖNCE olmak zorunda: stil yazıldığı anda GeoServer onu
        // ayrıştırıyor ve bir WMS isteği hemen gelebilir. Dosya henüz yoksa
        // o istek simgesiz çizilir; sıra tersine çevrilirse bu, "bazen
        // görünüyor bazen görünmüyor" diye teşhis edilmesi zor bir hataya
        // dönüşürdü.
        //
        // Aynı dosya birden çok stile hizmet etmiyor (kategori başına bir
        // dosya) ama yine de Distinct: yedek stil ile bir kategori aynı adı
        // paylaşırsa iki kez yazmanın anlamı yok.
        foreach (var (dosya, svg) in stiller
                     .GroupBy(x => x.SvgDosyaAdi)
                     .Select(g => (g.Key, g.First().Svg)))
        {
            await _geoServer.StilKaynagiYazAsync(dosya, svg, "image/svg+xml", iptal);
        }

        // ---- 2) Stilleri yaz ----
        foreach (var stil in stiller)
        {
            await _geoServer.StilYazAsync(stil.StilAdi, stil.Sld, iptal);
        }

        // ---- 3) Katmana bağla ----
        //
        // Sıra ÖNEMLİ: WMS'te sonraki stil öncekinin üstüne çiziliyor ve
        // istemci katmanı bu listedeki sırayla tekrarlıyor. Kökten yaprağa
        // giden sıra, alt kategorilerin köklerinin üstünde kalmasını sağlıyor.
        var katman = _geoServer.Ayarlar.KatmanAdi(GeoServerKatmanlari.Poi);
        await _geoServer.KatmanaStilBaglaAsync(katman, stiller.Select(s => s.StilAdi), iptal);

        // ---- 4) Karşılığı kalmayanları sil ----
        //
        // Silme EN SONA bırakıldı. Önce silseydik, yeni stiller yazılmadan
        // önce gelen bir WMS isteği katmanı stilsiz bulurdu.
        var silinen = await ArtiklariTemizleAsync(stiller.Select(s => s.StilAdi).ToHashSet(), iptal);

        _logger.LogInformation(
            "POI stilleri yenilendi: {Yazilan} yazıldı, {Silinen} silindi.",
            stiller.Count, silinen);

        return new PoiStilYenilemeDto
        {
            Stiller = stiller.Select(DtoyaCevir).ToList(),
            Yazilan = stiller.Count,
            Silinen = silinen,
        };
    }

    public async Task SessizYenileAsync()
    {
        try
        {
            await YenileAsync();
        }
        catch (GeoServerErisimException ex)
        {
            // Beklenen durum: GeoServer kapalı. Kategori işlemi başarılı,
            // yalnızca haritadaki simgesi henüz oluşmadı.
            _logger.LogWarning(ex,
                "Kategori değişti ama POI stilleri yenilenemedi. " +
                "GeoServer açıldığında yönetim panelinden 'Stilleri yenile' çalıştırılmalı.");
        }
        catch (Exception ex)
        {
            // Beklenmeyen durum: yine de kategori işlemini geri almıyoruz,
            // ama bunu uyarı değil HATA olarak günlüğe yazıyoruz.
            _logger.LogError(ex, "POI stilleri yenilenirken beklenmeyen hata.");
        }
    }

    // ------------------------------------------------------------------

    private async Task<List<PoiStili>> UretAsync()
        => PoiStilUretici.Uret(await _kategoriRepository.GetAllAsync());

    /// <summary>
    /// Bizim ürettiğimiz ada uyan ama artık listede olmayan stilleri siler.
    ///
    /// YALNIZCA KENDİ ÖN EKİMİZE dokunuyoruz. Aynı çalışma alanında ısı
    /// haritası stilleri (isi_haritasi, isi_deger) ve elle eklenmiş stiller de
    /// duruyor; "listede yoksa sil" demek onları da götürürdü.
    /// </summary>
    private async Task<int> ArtiklariTemizleAsync(IReadOnlySet<string> gecerliler, CancellationToken iptal)
    {
        var mevcutlar = await _geoServer.StilleriListeleAsync(iptal);

        var silinecekler = mevcutlar
            .Where(ad => BizimMi(ad) && !gecerliler.Contains(ad))
            .ToList();

        var silinen = 0;

        foreach (var ad in silinecekler)
        {
            try
            {
                await _geoServer.StilSilAsync(ad, iptal);
                silinen++;
            }
            catch (GeoServerErisimException ex)
            {
                // Tek bir artık stil silinemedi diye bütün yenilemeyi
                // başarısız saymıyoruz: asıl iş (yeni stillerin yazılması)
                // zaten tamamlandı.
                _logger.LogWarning(ex, "Artık POI stili silinemedi: {Stil}", ad);
            }
        }

        return silinen;
    }

    private static bool BizimMi(string stilAdi)
        => stilAdi.StartsWith(PoiStilUretici.StilOneki, StringComparison.Ordinal)
           || stilAdi == PoiStilUretici.YedekStil
           || EskiStiller.Contains(stilAdi);

    private static PoiStilDto DtoyaCevir(PoiStili stil) => new()
    {
        Stil = stil.StilAdi,
        KategoriId = stil.KategoriId,
        Ad = stil.Ad,
        TamYol = stil.TamYol,
        Renk = stil.Renk,
        Sekil = stil.Sekil,
        Ikon = stil.Ikon,

        // Çizim parçaları da DTO'ya giriyor: arayüz haritadaki OpenLayers
        // simgesini ve lejantı bunlardan kuruyor, yani frontend'de path
        // verisinin kopyası yok. GeoServer'ın çizdiği SVG ile arayüzün
        // çizdiği simge AYNI kaynaktan (PoiIkonlari) türüyor.
        IkonParcalari = PoiIkonlari.BulYaDaVarsayilan(stil.Ikon).Parcalar
            .Select(p => new IkonParcasiDto { D = p.D, Beyaz = p.Beyaz })
            .ToList(),
    };
}
