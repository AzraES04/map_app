using System.Globalization;
using NetTopologySuite.Geometries;
using StajProject.DataAccess.GeoServer;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// Kesişim analizini GeoServer üzerinden yürüten gerçekleme (Ödev 8 / Madde 2).
///
/// EF'li kardeşi (<see cref="AnalysisRepository"/>) sorguyu
/// <c>ST_Intersects(@alan, geom)</c> olarak PostGIS'e gönderiyordu. Burada
/// aynı soruyu GeoServer'ın CQL diliyle soruyoruz:
///
///     INTERSECTS(geom, POLYGON((...)))
///
/// GeoServer bunu kendi bağlandığı PostGIS'te yine <c>ST_Intersects</c>'e
/// çevirir — yani hesap gene veritabanında, GIST index kullanılarak yapılır.
/// Değişen tek şey isteğin kimin üzerinden geçtiği. Envanterin tamamını
/// backend'e çekip C#'ta kesişim hesaplamıyoruz; o yol hem yavaş olurdu hem de
/// mekânsal index'i işe yaramaz hâle getirirdi.
///
/// CQL'de <c>INTERSECTS</c>, ST_Intersects gibi "en ufak temas bile sayılır"
/// anlamındadır — ödevin "tamamen kapsanması gerekmez" şartının karşılığı.
/// </summary>
public class GeoServerAnalysisRepository : IAnalysisRepository
{
    private readonly IGeoServerClient _geoServer;

    public GeoServerAnalysisRepository(IGeoServerClient geoServer)
    {
        _geoServer = geoServer;
    }

    public Task<List<PointEntity>> KesisenNoktalarAsync(Polygon alan)
        => KesisenleriGetirAsync<PointEntity>(GeoServerKatmanlari.Nokta, alan);

    public Task<List<LineEntity>> KesisenCizgilerAsync(Polygon alan)
        => KesisenleriGetirAsync<LineEntity>(GeoServerKatmanlari.Cizgi, alan);

    public Task<List<PolygonEntity>> KesisenPoligonlarAsync(Polygon alan, int? haricTutulanId = null)
    {
        // Kaydedilmiş bir poligonun analizinde kendisini sonuçta görmek anlamsız:
        // bir geometri her zaman kendisiyle kesişir.
        var ekKosul = haricTutulanId is null
            ? null
            : $"id <> {haricTutulanId.Value.ToString(CultureInfo.InvariantCulture)}";

        return KesisenleriGetirAsync<PolygonEntity>(GeoServerKatmanlari.Poligon, alan, ekKosul);
    }

    /// <summary>
    /// Üç katman için ortak gövde: CQL süzgecini kurar, WFS'ten çeker, entity'ye çevirir.
    /// </summary>
    private async Task<List<TEntity>> KesisenleriGetirAsync<TEntity>(
        string katman,
        Polygon alan,
        string? ekKosul = null)
        where TEntity : GeometryEntityBase, new()
    {
        // CQL geometri değişmezi WKT metnidir — projede zaten her yerde WKT
        // kullandığımız için ek bir çeviriye gerek yok.
        //
        // "geom" kolon adı: AppDbContext'te üç tablo da HasColumnName("geom") diyor.
        //
        // EKSEN SIRASI: buradaki koordinatlar (boylam, enlem) sırasında yazılıyor
        // ve GeoServer de onları öyle okuyor — çünkü GeoServerClient WFS 1.0.0
        // kullanıyor. Sürüm 2.0.0 olsaydı GeoServer aynı metni (enlem, boylam)
        // sanır, sorgu hata vermeden bambaşka bir yeri sorardı. Gerekçenin tamamı
        // GeoServerClient.OzellikGetirAsync içinde yazılı.
        //
        // DİKKAT: Bu metne "SRID=4326;" gibi bir önek EKLENMEMELİ. CQL_FILTER'da
        // noktalı virgül "filtre listesi" ayıracıdır; önek filtreyi ikiye böler ve
        // GeoServer "Could not parse CQL filter list" der.
        //
        // "is_deleted = false" koşulu da YOK: Ödev 9'dan sonra o kural katmanın
        // SQL View'ında. Silinmiş kayıt bu katmandan hiç çıkmıyor.
        var suzgec = $"INTERSECTS(geom, {alan.AsText()})";

        if (ekKosul is not null)
        {
            suzgec += $" AND {ekKosul}";
        }


        var features = await _geoServer.OzellikGetirAsync(
            katman,
            cqlFilter: suzgec,
            siralama: "id A");   // A = ascending; EF gerçeklemesindeki OrderBy(e => e.Id)

        return features.Select(f => FeatureDonusturucu.Cevir<TEntity>(f, katman)).ToList();
    }
}
