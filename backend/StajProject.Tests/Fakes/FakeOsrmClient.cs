using NetTopologySuite.Geometries;
using StajProject.DataAccess.Osrm;

namespace StajProject.Tests.Fakes;

/// <summary>
/// Sahte OSRM istemcisi (Ödev 17).
///
/// ---- NEDEN GERÇEK OSRM'E BAĞLANMIYORUZ? ----
///
/// Testin doğrulaması gereken şey OSRM'in doğru rota bulup bulmadığı DEĞİL —
/// o OSRM'in kendi işi ve zaten kendi testleri var. Bizim sorumluluğumuz:
///   • rota ne zaman isteniyor, ne zaman istenmiyor,
///   • OSRM cevap vermediğinde ne oluyor,
///   • imza ne zaman değişiyor.
/// Bunların hiçbiri gerçek bir yol ağı gerektirmiyor.
///
/// Üstelik gerçek OSRM'e bağlansaydık test paketi Docker'ın ayakta olmasına,
/// GB'larca OSM verisine ve ağ erişimine bağlanırdı; CI'de hiç çalışmazdı.
///
/// Varsayılan davranış: verilen noktaları DÜZ birleştirip döner. Gerçek bir
/// rota gibi görünmez ama testlerin sorduğu sorular için yeterli.
/// </summary>
public class FakeOsrmClient : IOsrmClient
{
    /// <summary>Kaç kez rota istendi? "Gereksiz istek atmıyor" testlerinin ölçtüğü sayı.</summary>
    public int CagriSayisi { get; private set; }

    /// <summary>Son istekte gönderilen noktalar — sıranın doğru gittiğini sınamak için.</summary>
    public IReadOnlyList<Coordinate>? SonNoktalar { get; private set; }

    /// <summary>false yapılırsa istemci "OSRM kapalı" gibi davranır.</summary>
    public bool Etkin { get; set; } = true;

    /// <summary>
    /// true yapılırsa her istek null döner — "OSRM ayakta değil / rota
    /// bulunamadı" durumunun taklidi.
    /// </summary>
    public bool BasarisizOl { get; set; }

    public Task<OsrmRotaSonucu?> RotaHesaplaAsync(
        IReadOnlyList<Coordinate> noktalar,
        CancellationToken iptal = default)
    {
        CagriSayisi++;
        SonNoktalar = noktalar;

        if (!Etkin || BasarisizOl || noktalar.Count < 2)
        {
            return Task.FromResult<OsrmRotaSonucu?>(null);
        }

        // Mesafe, noktalar arası derece farkının kabaca metreye çevrilmiş
        // hâli. Gerçekçi olması gerekmiyor; testler yalnızca "bir sayı
        // yazıldı mı" diye bakıyor.
        var cizgi = new LineString(noktalar.Select(n => new Coordinate(n.X, n.Y)).ToArray())
        {
            SRID = 4326,
        };

        return Task.FromResult<OsrmRotaSonucu?>(
            new OsrmRotaSonucu(cizgi, cizgi.Length * 111_000, cizgi.Length * 111_000 / 12.5));
    }

    public Task<bool> AyaktaMiAsync(CancellationToken iptal = default)
        => Task.FromResult(Etkin && !BasarisizOl);
}
