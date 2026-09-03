using NetTopologySuite.Geometries;

namespace StajProject.DataAccess.Google;

/// <summary>Google'ın desteklediği seyahat kipleri.</summary>
public enum SeyahatKipi
{
    Yaya,
    Arac,
    ToplaTasima,
}

/// <summary>
/// İki durak arasındaki tek bir bacak.
/// </summary>
/// <param name="MesafeMetre">Bacağın uzunluğu.</param>
/// <param name="SureSaniye">Tahmini seyahat süresi (kalış süresi HARİÇ).</param>
public record DirectionsBacagi(double MesafeMetre, double SureSaniye);

/// <summary>
/// Directions cevabının işimize yarayan hâli.
/// </summary>
/// <param name="Sira">
/// ARA NOKTALARIN optimize edilmiş sırası: Google'ın <c>waypoint_order</c>
/// alanı. Değerler, isteğe verilen ara nokta listesindeki İNDİSLERdir —
/// [2,0,1] "önce üçüncü verdiğin, sonra birinci…" demek.
/// </param>
/// <param name="Bacaklar">Sıralanmış rotanın bacakları (durak sayısı - 1 adet).</param>
/// <param name="Cizgi">Rotanın tamamı (EPSG:4326); çözülemezse null.</param>
/// <param name="ToplamMetre">Bacakların toplamı.</param>
/// <param name="ToplamSaniye">Bacakların toplamı — kalış süreleri hariç.</param>
public record DirectionsRotasi(
    IReadOnlyList<int> Sira,
    IReadOnlyList<DirectionsBacagi> Bacaklar,
    LineString? Cizgi,
    double ToplamMetre,
    double ToplamSaniye);

/// <summary>
/// Google Directions ile konuşan istemci.
///
/// TEK İŞİ VAR: verilen noktaları en kısa gezilecek sıraya dizip rotayı
/// döndürmek. Sıralama işini Google'ın <c>optimize:true</c> parametresine
/// bırakıyoruz — bunun alternatifi mesafe matrisi çekip (N×N istek) gezgin
/// satıcı problemini kendimiz çözmekti. Tek istekte hem sıra hem rota
/// geliyor.
/// </summary>
public interface IDirectionsClient
{
    /// <summary>Anahtar girilmiş ve modül açık mı?</summary>
    bool Etkin { get; }

    /// <summary>
    /// Noktaları optimize edilmiş sıraya dizer ve rotayı hesaplar.
    ///
    /// İlk nokta BAŞLANGIÇ, son nokta VARIŞ olarak sabit kalır (Google'ın
    /// davranışı); yalnızca aradakiler yeniden sıralanır. Turun nereden
    /// başlayacağını sunucunun değiştirmemesi bilinçli: kullanıcı şehir
    /// merkezinden yola çıkmayı bekliyor.
    ///
    /// İSTİSNA FIRLATMAZ: hata durumunda null döner (bkz. IPlacesClient).
    /// </summary>
    /// <param name="noktalar">En az 2 nokta, sırayla ilk = başlangıç, son = varış.</param>
    Task<DirectionsRotasi?> SiraliRotaAsync(
        IReadOnlyList<Coordinate> noktalar,
        SeyahatKipi kip,
        CancellationToken iptal = default);
}
