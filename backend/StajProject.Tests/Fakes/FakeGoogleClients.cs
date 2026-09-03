using NetTopologySuite.Geometries;
using StajProject.DataAccess.Google;
using StajProject.DataAccess.Yerel;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Sahte Places istemcisi.
///
/// ---- NEDEN GERÇEK GOOGLE'A BAĞLANMIYORUZ? ----
/// Testin doğrulaması gereken şey Google'ın doğru mekanı bulup bulmadığı
/// DEĞİL. Bizim sorumluluğumuz: kaç arama yapılıyor, 4.5 altındakiler
/// eleniyor mu, kalış süresi doğru atanıyor mu, bütçe aşılıyor mu.
/// Hiçbiri gerçek bir API anahtarı gerektirmiyor — üstelik gerçek servise
/// bağlansaydık test paketi ağ erişimine, faturalı bir anahtara ve o anki
/// mekan verisine bağlanırdı (aynı testler yarın farklı sonuç verirdi).
/// </summary>
public class FakePlacesClient : IOsmPlacesClient
{
    /// <summary>
    /// Havuz: (mekan, hangi ARAMA tipinin döndüreceği).
    ///
    /// İkisi ayrı çünkü gerçekte de ayrı: OSM'de bir anıt
    /// "tourist_attraction" süzgeciyle bulunuyor ama etiketleri onu "monument"
    /// diye tanıtıyor. Tek alanla taklit etseydik, kalış süresi ve kategori
    /// eşlemesini sınayan testler gerçekte olmayan bir kolaylık varsayardı.
    /// </summary>
    private readonly List<(GooglePlace Mekan, string? AramaTipi)> _havuz = new();

    /// <summary>Kaç ARAMA yapıldı? "İstek bütçesi" testlerinin ölçtüğü sayı.</summary>
    public int CagriSayisi { get; private set; }

    /// <summary>
    /// Kaç kez TOPLU çağrı geldi? Servisin aramaları tek seferde verdiğini
    /// (kaynağın bölme kararını kendisinin vermediğini) sınamak için.
    /// </summary>
    public int TopluCagriSayisi { get; private set; }

    /// <summary>Yapılan aramalar — sorgu metni ve yarıçap kontrolü için.</summary>
    public List<PlacesAramasi> Aramalar { get; } = new();

    public bool Etkin { get; set; } = true;

    /// <summary>
    /// Varsayılan true (Google gibi davranır). Anahtarsız kipi (OSM) sınayan
    /// testler false yapıp "puan süzgeci atlanıyor mu?" diye soruyor.
    /// </summary>
    public bool PuanVerisiVar { get; set; } = true;

    /// <summary>true ise her arama boş döner (servis kapalı / sonuç yok).</summary>
    public bool BosDon { get; set; }

    /// <summary>
    /// Bağlanırsa gerçek istemci gibi davranır: her istek önce bütçeden izin
    /// ister. Servisin raporladığı istek maliyeti buradan sayılıyor.
    /// </summary>
    public IIstekButcesi? Butce { get; set; }

    /// <summary>
    /// Havuza mekan ekler. Arama TİPE göre süzüyor: hangi aramanın hangi
    /// sonucu döndürdüğünü testte kurmak için.
    /// </summary>
    public FakePlacesClient Ekle(
        string id,
        string ad,
        double lat,
        double lon,
        double? puan,
        int degerlendirme,
        string tip,
        bool? vejetaryen = null,
        double? onem = null,
        string? aramaTipi = null,
        Dictionary<string, string>? etiketler = null)
    {
        // Önem verilmezse puandan türetiliyor: testlerin çoğu Google kipini
        // sınıyor ve orada sıralama zaten puan+oy sayısından geliyor.
        var hesaplananOnem = onem
            ?? (puan is null ? 0.3 : 0.6 * (puan.Value / 5.0)
                + 0.4 * Math.Clamp(Math.Log10(Math.Max(1, degerlendirme)) / 5.0, 0, 1));

        _havuz.Add((
            new GooglePlace(
                id, ad, lat, lon, puan, degerlendirme,
                new[] { tip }, tip, hesaplananOnem, vejetaryen, etiketler),
            aramaTipi ?? tip));
        return this;
    }

    public Task<IReadOnlyList<GooglePlace>> AraAsync(
        IReadOnlyList<PlacesAramasi> aramalar,
        CancellationToken iptal = default)
    {
        TopluCagriSayisi++;

        var sonuc = new List<GooglePlace>();

        foreach (var arama in aramalar)
        {
            // Google istemcisi gibi davraniyoruz: her arama AYRI bir istek ve
            // ayri bir butce kalemi. (Overpass hepsini tek istekte birlestirir;
            // o kipin testleri OsmPlacesClient'a bakiyor.)
            CagriSayisi++;
            Aramalar.Add(arama);

            if (!Etkin || BosDon || Butce?.IzinIste() == false)
            {
                continue;
            }

            // Puan suzgeci SUNUCUDA uygulaniyor (minRating). Servisin kendi
            // ikinci kontrolu de oldugu icin, dusuk puanli bir kaydi buradan
            // dondurerek o kontrolu ayrica sinayabiliyoruz.
            sonuc.AddRange(_havuz
                .Where(h => arama.Tip is null
                    || string.Equals(h.AramaTipi, arama.Tip, StringComparison.OrdinalIgnoreCase))
                .Select(h => h.Mekan));
        }

        return Task.FromResult<IReadOnlyList<GooglePlace>>(
            sonuc
                .GroupBy(m => m.PlaceId, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList());
    }
}

/// <summary>
/// Sahte Directions istemcisi.
///
/// Sıralamayı gerçekten optimize ETMİYOR — "en yakın komşu" ile basit bir sıra
/// üretiyor. Testlerin sorusu "Google iyi mi sıralıyor" değil, "gelen sıra
/// doğru uygulanıyor mu ve bacak süreleri bütçeye doğru işleniyor mu".
/// </summary>
public class FakeDirectionsClient : IDirectionsClient
{
    /// <summary>Kaç rota isteği atıldı? Kota testlerinin ölçtüğü sayı.</summary>
    public int CagriSayisi { get; private set; }

    /// <summary>Son istekte gönderilen nokta sayısı.</summary>
    public int SonNoktaSayisi { get; private set; }

    /// <summary>Son istekteki seyahat kipi.</summary>
    public SeyahatKipi? SonKip { get; private set; }

    public bool Etkin { get; set; } = true;

    /// <summary>true ise her istek null döner (servis cevap vermiyor).</summary>
    public bool BasarisizOl { get; set; }

    /// <summary>Bağlanırsa her istek önce bütçeden izin ister (bkz. FakePlacesClient).</summary>
    public IIstekButcesi? Butce { get; set; }

    /// <summary>Her bacağın süresi (saniye). Testler bütçeyi bununla ayarlıyor.</summary>
    public double BacakSaniye { get; set; } = 600;   // 10 dakika

    /// <summary>Her bacağın uzunluğu (metre).</summary>
    public double BacakMetre { get; set; } = 2_000;

    public Task<DirectionsRotasi?> SiraliRotaAsync(
        IReadOnlyList<Coordinate> noktalar,
        SeyahatKipi kip,
        CancellationToken iptal = default)
    {
        CagriSayisi++;
        SonNoktaSayisi = noktalar.Count;
        SonKip = kip;

        if (!Etkin || BasarisizOl || noktalar.Count < 2 || Butce?.IzinIste() == false)
        {
            return Task.FromResult<DirectionsRotasi?>(null);
        }

        // Ara noktaları "en yakın komşu" sırasına diziyoruz: başlangıçtan
        // başlayıp her adımda en yakın ziyaret edilmemişe gidiyoruz.
        var araSayisi = noktalar.Count - 2;
        var sira = new List<int>();

        if (araSayisi > 0)
        {
            var kalan = Enumerable.Range(0, araSayisi).ToList();
            var gecerli = noktalar[0];

            while (kalan.Count > 0)
            {
                var enYakin = kalan
                    .OrderBy(i => gecerli.Distance(noktalar[i + 1]))
                    .First();

                sira.Add(enYakin);
                gecerli = noktalar[enYakin + 1];
                kalan.Remove(enYakin);
            }
        }

        var bacakSayisi = noktalar.Count - 1;
        var bacaklar = Enumerable
            .Range(0, bacakSayisi)
            .Select(_ => new DirectionsBacagi(BacakMetre, BacakSaniye))
            .ToList();

        var cizgi = new LineString(noktalar.Select(n => new Coordinate(n.X, n.Y)).ToArray())
        {
            SRID = 4326,
        };

        return Task.FromResult<DirectionsRotasi?>(new DirectionsRotasi(
            sira,
            bacaklar,
            cizgi,
            bacaklar.Sum(b => b.MesafeMetre),
            bacaklar.Sum(b => b.SureSaniye)));
    }
}

/// <summary>
/// Sahte istek bütçesi. Varsayılanı sınırsız; testler
/// <see cref="Tavan"/>'ı düşürerek "bütçe dolunca ne oluyor?" sorusunu soruyor.
/// </summary>
public class FakeIstekButcesi : IIstekButcesi
{
    public int Tavan { get; set; } = 1_000;

    public int BugunKullanilan { get; private set; }

    public int GunlukTavan => Tavan;

    public bool IzinIste()
    {
        if (BugunKullanilan >= Tavan) return false;
        BugunKullanilan++;
        return true;
    }
}
