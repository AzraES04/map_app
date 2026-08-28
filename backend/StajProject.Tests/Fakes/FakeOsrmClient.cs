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

    /// <summary>Kaç kez ALTERNATİF istendi?</summary>
    public int AlternatifCagriSayisi { get; private set; }

    /// <summary>
    /// Kaç alternatif üretilsin? Gerçek OSRM her zaman çok yol vermiyor —
    /// bazen tek makul yol var. Testlerin o durumu da sınayabilmesi için
    /// ayarlanabilir.
    /// </summary>
    public int UretilecekAlternatif { get; set; } = 3;

    public Task<IReadOnlyList<OsrmRotaSonucu>> AlternatifRotalarAsync(
        IReadOnlyList<Coordinate> noktalar,
        int enFazla = 3,
        CancellationToken iptal = default)
    {
        AlternatifCagriSayisi++;
        SonNoktalar = noktalar;

        if (!Etkin || BasarisizOl || noktalar.Count < 2)
        {
            return Task.FromResult<IReadOnlyList<OsrmRotaSonucu>>(Array.Empty<OsrmRotaSonucu>());
        }

        var adet = Math.Min(UretilecekAlternatif, enFazla);
        var sonuc = new List<OsrmRotaSonucu>();

        for (var i = 0; i < adet; i++)
        {
            // Her alternatif biraz DAHA UZUN ve biraz FARKLI bir yol.
            // Sapma i'ye göre büyüyor: gerçek OSRM'de de alternatifler en
            // iyiden uzaklaştıkça daha dolambaçlı oluyor. Hepsi aynı olsaydı
            // "en iyisi otomatik seçiliyor mu?" sorusu sınanamazdı.
            var sapma = i * 0.01;
            var koordinatlar = noktalar
                .Select(n => new Coordinate(n.X + sapma, n.Y + sapma))
                .ToList();

            // Ortada bir köşe: via noktası hesabı çizginin ORTASINI alıyor,
            // iki noktalı bir çizgide orta = son nokta olurdu ve
            // alternatifler birbirinden ayrılmazdı.
            koordinatlar.Insert(1, new Coordinate(
                (noktalar[0].X + noktalar[^1].X) / 2 + sapma,
                (noktalar[0].Y + noktalar[^1].Y) / 2 + sapma));

            var cizgi = new LineString(koordinatlar.ToArray()) { SRID = 4326 };

            sonuc.Add(new OsrmRotaSonucu(
                cizgi,
                cizgi.Length * 111_000 * (1 + i * 0.1),
                cizgi.Length * 111_000 / 12.5 * (1 + i * 0.15)));
        }

        return Task.FromResult<IReadOnlyList<OsrmRotaSonucu>>(sonuc);
    }

    public Task<bool> AyaktaMiAsync(CancellationToken iptal = default)
        => Task.FromResult(Etkin && !BasarisizOl);
}
