using NetTopologySuite.Geometries;

namespace StajProject.Business.Ulasim;

/// <summary>
/// Simülasyonun BAŞLARKEN alınan anlık görüntüsü.
///
/// NEDEN ANLIK GÖRÜNTÜ? Simülasyon servisi bir SINGLETON: uygulama boyunca
/// yaşıyor ve zamanlayıcıdan besleniyor. Veritabanına erişen servisler ise
/// istek ömürlü (scoped). Singleton bir sınıfın scoped bir DbContext'i
/// tutması, ASP.NET'te klasik bir hatadır — bağlam ilk istekle birlikte
/// kapanır, sonraki tikler "disposed" hatası verir.
///
/// Bu yüzden veriyi OKUMAK scoped servisin (UlasimService) işi; singleton
/// yalnızca kopyayı saklıyor. Yan faydası: simülasyon sürerken hattın
/// durakları değiştirilse bile araç, başladığı rotayı tamamlıyor —
/// yarı yolda başka bir geometriye atlamıyor.
/// </summary>
public sealed class SimulasyonKaynak
{
    public required int GuzergahId { get; init; }

    public required string GuzergahAdi { get; init; }

    public required string Renk { get; init; }

    /// <summary>Aracın izleyeceği çizgi (EPSG:4326) — OSRM rotası ya da düz hat.</summary>
    public required IReadOnlyList<Coordinate> Yol { get; init; }

    /// <summary>Duraklar, hat üzerindeki oranlarıyla birlikte.</summary>
    public required IReadOnlyList<SimulasyonDurak> Duraklar { get; init; }

    /// <summary>Hattın toplam uzunluğu (metre).</summary>
    public required double ToplamMetre { get; init; }

    /// <summary>
    /// Simülasyonun ekranda süreceği zaman (saniye).
    ///
    /// GERÇEK sürüş süresi DEĞİL: 1863 km'lik bir hattı gerçek zamanlı
    /// oynatmak günler sürerdi ve jüri önünde gösterilemezdi. Sabit bir
    /// gösterim süresi seçiyoruz (varsayılan 60 sn, appsettings'ten
    /// değiştirilebilir); yüzde de zamanın oranı olduğu için ilerleme her
    /// hatta aynı hızda ve öngörülebilir oluyor.
    /// </summary>
    public required double ToplamSaniye { get; init; }

    public required int BaslatanKullaniciId { get; init; }

    public required string BaslatanKullanici { get; init; }
}

/// <summary>Bir durağın adı, sırası ve hat üzerindeki oranı (0-1).</summary>
/// <param name="Sira">Durağın hattaki sırası (1'den başlar).</param>
/// <param name="Ad">Durak adı.</param>
/// <param name="Oran">Hattın başından itibaren oran — 0 ilk, 1 son.</param>
public sealed record SimulasyonDurak(int Sira, string Ad, double Oran);
