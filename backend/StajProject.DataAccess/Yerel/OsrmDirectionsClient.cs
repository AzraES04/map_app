using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using StajProject.DataAccess.Google;
using StajProject.DataAccess.Osrm;

namespace StajProject.DataAccess.Yerel;

/// <summary>
/// OSRM tabanlı sıralama ve rota — ANAHTARSIZ KİP.
///
/// <see cref="IDirectionsClient"/>'ı uyguluyor: iş katmanı Google Directions
/// ile bunun arasındaki farkı görmüyor.
///
/// ---- SIRALAMAYI KİM ÇÖZÜYOR? ----
/// OSRM'in <c>/trip</c> servisi (bkz. <see cref="OsrmTrip"/>) — Google'ın
/// <c>optimize:true</c> parametresinin yerelde çalışan karşılığı. Ödev 17'den
/// beri zaten Docker'da duran bir servis; ek kurulum, ek maliyet yok.
///
/// ---- OSRM DE KAPALIYSA? ----
/// Öneri düşmüyor: sıra EN YAKIN KOMŞU sezgiseliyle burada hesaplanıyor,
/// mesafeler kuş uçuşu, süreler kip başına ortalama hızdan. Sonuç optimal
/// değil ve çizgisi de yok — ama "tur önerisi hiç çalışmıyor" ile "tur
/// önerisi kabaca çalışıyor" arasındaki fark, kurulumun yarısı eksik bir
/// makinede demoyu ayakta tutan şey. Tahmini olduğu cevapta işaretleniyor
/// (<see cref="DirectionsRotasi.Cizgi"/> null geliyor ve iş katmanı
/// "rota çizilemedi" diyebiliyor).
/// </summary>
public class OsrmDirectionsClient : IDirectionsClient
{
    /// <summary>
    /// Yedek hesapta kullanılan ortalama hızlar (km/sa).
    ///
    /// Business katmanındaki katalogla aynı değerler ama oradan OKUNMUYOR:
    /// DataAccess, Business'a bağımlı olamaz (bağımlılık yönü). Kopya olması
    /// kabul edilebilir çünkü ikisi de yalnızca KABA bir tahmin üretiyor.
    /// </summary>
    private static readonly Dictionary<SeyahatKipi, double> Hizlar = new()
    {
        [SeyahatKipi.Yaya] = 4.5,
        [SeyahatKipi.Arac] = 30,
        [SeyahatKipi.ToplaTasima] = 18,
    };

    /// <summary>Kuş uçuşunu gerçek yola yaklaştıran katsayı (şehir içi ızgara).</summary>
    private const double YolKatsayisi = 1.3;

    private readonly HttpClient _http;
    private readonly OsrmSettings _ayarlar;
    private readonly ILogger<OsrmDirectionsClient> _logger;

    public OsrmDirectionsClient(
        HttpClient http,
        OsrmSettings ayarlar,
        ILogger<OsrmDirectionsClient> logger)
    {
        _http = http;
        _ayarlar = ayarlar;
        _logger = logger;

        _http.Timeout = TimeSpan.FromSeconds(ayarlar.TimeoutSeconds);
    }

    /// <summary>
    /// Her zaman etkin: OSRM kapalı olsa bile yedek hesap çalışıyor.
    ///
    /// Google istemcisinde bu bayrak "anahtar var mı?" sorusuydu ve false
    /// olduğunda öneri hiç üretilemiyordu. Burada öyle bir durum yok — bu
    /// yüzden false dönmek, çalışabilecek bir öneriyi engellemek olurdu.
    /// </summary>
    public bool Etkin => true;

    public async Task<DirectionsRotasi?> SiraliRotaAsync(
        IReadOnlyList<Coordinate> noktalar,
        SeyahatKipi kip,
        CancellationToken iptal = default)
    {
        if (noktalar.Count < 2)
        {
            return null;
        }

        // ---- 1. Yol: OSRM /trip ----
        //
        // KİP YOK SAYILIYOR: osrm-routed tek profille (bu kurulumda "driving")
        // derlenmiş veriyle çalışıyor; yaya profili ayrı bir veri hazırlığı
        // ister. Sıralama açısından fark küçük — şehir içinde en yakın durak
        // iki kipte de aynı. Süreler ise iş katmanında kip katsayısıyla
        // düzeltiliyor (bkz. TurKatalogu.TopluTasimaSureCarpani).
        var gezi = await OsrmTrip.HesaplaAsync(_http, _ayarlar, _logger, noktalar, iptal);

        if (gezi is not null)
        {
            return new DirectionsRotasi(
                Sira: gezi.Sira,
                Bacaklar: gezi.Bacaklar
                    .Select(b => new DirectionsBacagi(b.MesafeMetre, b.SureSaniye))
                    .ToList(),
                Cizgi: gezi.Cizgi,
                ToplamMetre: gezi.ToplamMetre,
                ToplamSaniye: gezi.ToplamSaniye);
        }

        // ---- 2. Yol: yedek sezgisel ----
        _logger.LogWarning("OSRM sıralama vermedi; en yakın komşu sezgiseline düşülüyor.");
        return YedekSirala(noktalar, kip);
    }

    /// <summary>
    /// EN YAKIN KOMŞU: başlangıçtan başla, her adımda en yakın ziyaret
    /// edilmemiş noktaya git; son nokta sabit kalsın.
    ///
    /// Optimal değil (gezgin satıcı problemi için bilinen en basit sezgisel)
    /// ama şehir içi bir turda kabul edilebilir sonuç veriyor ve HİÇBİR dış
    /// servise ihtiyaç duymuyor.
    /// </summary>
    private static DirectionsRotasi YedekSirala(IReadOnlyList<Coordinate> noktalar, SeyahatKipi kip)
    {
        var araSayisi = noktalar.Count - 2;
        var sira = new List<int>();

        if (araSayisi > 0)
        {
            var kalan = Enumerable.Range(0, araSayisi).ToList();
            var gecerli = noktalar[0];

            while (kalan.Count > 0)
            {
                var enYakin = kalan
                    .OrderBy(i => MesafeMetre(gecerli, noktalar[i + 1]))
                    .First();

                sira.Add(enYakin);
                gecerli = noktalar[enYakin + 1];
                kalan.Remove(enYakin);
            }
        }

        // Bacak süreleri, SIRALANMIŞ dizilim üzerinden hesaplanıyor: sırayı
        // uygulamadan ölçseydik iş katmanının bütçe hesabı başka bir rotanın
        // sürelerini kullanırdı.
        var dizilim = new List<Coordinate> { noktalar[0] };
        dizilim.AddRange(sira.Select(i => noktalar[i + 1]));
        dizilim.Add(noktalar[^1]);

        var hiz = Hizlar.TryGetValue(kip, out var h) ? h : Hizlar[SeyahatKipi.Yaya];
        var bacaklar = new List<DirectionsBacagi>();

        for (var i = 1; i < dizilim.Count; i++)
        {
            var metre = MesafeMetre(dizilim[i - 1], dizilim[i]) * YolKatsayisi;
            var saniye = metre / 1000.0 / hiz * 3600.0;

            bacaklar.Add(new DirectionsBacagi(metre, saniye));
        }

        // Çizgi YOK (null): elimizde yol ağı bilgisi olmadığı için
        // çizebileceğimiz tek şey kuş uçuşu bir çizgi olurdu ve o, haritada
        // gerçek bir rota sanılırdı. Arayüz null gelince yalnızca durakları
        // gösteriyor.
        return new DirectionsRotasi(
            sira,
            bacaklar,
            Cizgi: null,
            ToplamMetre: bacaklar.Sum(b => b.MesafeMetre),
            ToplamSaniye: bacaklar.Sum(b => b.SureSaniye));
    }

    /// <summary>İki koordinat arası kuş uçuşu mesafe (metre) — haversine.</summary>
    private static double MesafeMetre(Coordinate a, Coordinate b)
    {
        const double yaricap = 6_371_000;
        var rad = (double derece) => derece * Math.PI / 180;

        var dLat = rad(b.Y - a.Y);
        var dLon = rad(b.X - a.X);

        var h = Math.Pow(Math.Sin(dLat / 2), 2)
              + Math.Cos(rad(a.Y)) * Math.Cos(rad(b.Y)) * Math.Pow(Math.Sin(dLon / 2), 2);

        return 2 * yaricap * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
