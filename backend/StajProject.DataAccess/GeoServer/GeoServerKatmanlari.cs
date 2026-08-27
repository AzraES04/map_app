using StajProject.Entities;

namespace StajProject.DataAccess.GeoServer;

/// <summary>
/// Entity tipi ↔ GeoServer katmanı eşlemesi.
///
/// Ödev 9 / Madde 1'den sonra katmanlar TABLOYA DEĞİL, birer <b>SQL View</b>'a
/// bakıyor:
///
/// <code>SELECT ... FROM tbl_point WHERE is_deleted = false</code>
///
/// Bu yüzden adlar da değişti (<c>tbl_point</c> → <c>vw_point</c>). Adın
/// "vw" ile başlaması kasıtlı: katmanın arkasında bir tablo değil bir sorgu
/// olduğu isimden anlaşılsın.
///
/// Kazancı: soft delete kuralı artık <b>katmanın içinde</b>. Silinmiş bir
/// kayıt GeoServer'ın hiçbir servisinden — WFS, WMS, ısı haritası, lejant —
/// çıkamaz. Önceden bu kuralı her istekte CQL süzgeciyle biz gönderiyorduk;
/// tek bir yerde unutulsa silinmiş veri sızardı.
///
/// Adlar <c>geoserver/gs-yapilandir.ps1</c> içindeki <c>$Katmanlar</c>
/// listesiyle BİREBİR aynı olmak zorunda.
/// </summary>
public static class GeoServerKatmanlari
{
    public const string Nokta = "vw_point";
    public const string Cizgi = "vw_line";
    public const string Poligon = "vw_polygon";

    /// <summary>
    /// POI katmanı (Ödev 13 / Madde 1).
    ///
    /// Diğer üçünden FARKLI bir SQL View'a bakıyor: <c>poi</c> tablosunu
    /// kategori ağacıyla ve ekleyen kullanıcıyla BİRLEŞTİRİYOR. Sebep,
    /// stillerin kategoriye göre ayrışması: SLD'nin "bu nokta Sağlık mı
    /// Eğitim mi?" sorusunu sorabilmesi için kategori adının katmanda bir
    /// kolon olarak bulunması şart, yabancı anahtar (kategori_id) yetmez.
    ///
    /// Görünümün SQL'i <c>geoserver/gs-yapilandir.ps1</c> içinde.
    /// </summary>
    public const string Poi = "vw_poi";

    /// <summary>Üç çizim katmanı, sabit sırada — WMS ve durum ucu bunu kullanıyor.</summary>
    public static readonly IReadOnlyList<string> Tumu = new[] { Nokta, Cizgi, Poligon };

    /// <summary>
    /// WMS vekilinin iletmeyi kabul ettiği katmanlar (Ödev 8'deki beyaz liste).
    ///
    /// Çizim katmanları + POI. Vekil bu listede olmayan bir katman adını
    /// reddediyor; olmasaydı backend GeoServer'ın tamamına açık bir kapı
    /// hâline gelirdi (bkz. GeoServerController).
    /// </summary>
    public static readonly IReadOnlyList<string> WmsIzinli = new[] { Nokta, Cizgi, Poligon, Poi };

    private static readonly Dictionary<Type, string> Eslesme = new()
    {
        [typeof(PointEntity)] = Nokta,
        [typeof(LineEntity)] = Cizgi,
        [typeof(PolygonEntity)] = Poligon,
    };

    /// <summary>Entity tipinin hangi katmana karşılık geldiğini söyler.</summary>
    public static string Bul(Type entityTipi)
        => Eslesme.TryGetValue(entityTipi, out var ad)
            ? ad
            : throw new InvalidOperationException(
                $"{entityTipi.Name} için GeoServer katmanı tanımlı değil.");
}
