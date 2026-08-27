using System.Text.Json;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace StajProject.DataAccess.GeoServer;

/// <summary>
/// GeoServer'ın WFS cevabını (GeoJSON) NetTopologySuite geometrilerine çevirir.
///
/// NEDEN ELDE YAZILDI?
/// Hazır bir paket var (NetTopologySuite.IO.GeoJSON4STJ). Kullanmadık çünkü:
///   1) Projede zaten NTS'in EF Core sürümü var; ikinci bir NTS bağımlılığı
///      sürüm çakışması riski taşıyor.
///   2) Bize sadece üç tip lazım (Point / LineString / Polygon) — tablolarımız
///      başka bir şey tutamıyor. Genel amaçlı bir okuyucunun tamamına
///      ihtiyacımız yok.
///   3) Sunumda "GeoJSON nasıl bir şey?" sorusuna kodu göstererek cevap
///      verilebiliyor.
///
/// GeoJSON'da koordinat sırası HER ZAMAN [boylam, enlem] yani [x, y]'dir
/// (RFC 7946). WKT ile aynı sıra; EPSG:4326'nın resmî eksen sırası
/// (enlem, boylam) burada GEÇERLİ DEĞİLDİR. Bu, GIS'te en sık yapılan
/// hatalardan biri — noktalar Ankara yerine Somali açıklarına düşer.
/// </summary>
public static class GeoJsonOkuyucu
{
    private static readonly GeometryFactory Fabrika =
        NtsGeometryServices.Instance.CreateGeometryFactory(GeoServerSettings.Srid);

    /// <summary>
    /// Bir GeoJSON FeatureCollection metnini kayıt listesine çevirir.
    /// </summary>
    /// <exception cref="GeoServerErisimException">
    /// Cevap GeoJSON değilse (örn. GeoServer XML hata raporu döndüyse).
    /// </exception>
    /// <param name="idZorunlu">
    /// <c>true</c> (varsayılan): kaydın id'si bulunamazsa hata fırlatılır —
    /// WFS'ten gelen bir kaydın kimliği olmak ZORUNDA, yoksa güncelleme ve
    /// silme hedefi kaybolur.
    /// <c>false</c>: id yoksa 0 bırakılır. Ödev 10'daki il sınırları dosyası
    /// gibi, kimliği başka bir alandan (plaka) gelen dış veriler için.
    /// </param>
    public static List<GeoServerFeature> Oku(string govde, bool idZorunlu = true)
    {
        JsonDocument belge;
        try
        {
            belge = JsonDocument.Parse(govde);
        }
        catch (JsonException ex)
        {
            // GeoServer hata durumunda çoğu zaman XML <ows:ExceptionReport> döner.
            // Ham gövdeyi kırpıp mesaja koyuyoruz: log'da sebebi görünsün.
            throw new GeoServerErisimException(
                $"GeoServer beklenen GeoJSON yerine başka bir şey döndü: {Kirp(govde)}", ex);
        }

        using (belge)
        {
            var kok = belge.RootElement;

            if (!kok.TryGetProperty("features", out var features) ||
                features.ValueKind != JsonValueKind.Array)
            {
                throw new GeoServerErisimException(
                    $"GeoServer cevabında 'features' dizisi yok: {Kirp(govde)}");
            }

            var sonuc = new List<GeoServerFeature>(features.GetArrayLength());

            foreach (var feature in features.EnumerateArray())
            {
                sonuc.Add(FeatureOku(feature, idZorunlu));
            }

            return sonuc;
        }
    }

    private static GeoServerFeature FeatureOku(JsonElement feature, bool idZorunlu)
    {
        // ---- properties: geometri dışındaki kolonlar ----
        var ozellikler = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        if (feature.TryGetProperty("properties", out var props) &&
            props.ValueKind == JsonValueKind.Object)
        {
            foreach (var alan in props.EnumerateObject())
            {
                // Clone(): JsonDocument using bloğundan çıkınca bellek serbest
                // bırakılıyor; klonlamazsak elimizde geçersiz JsonElement kalır.
                ozellikler[alan.Name] = alan.Value.Clone();
            }
        }

        // ---- id ----
        var id = IdBul(feature, ozellikler, idZorunlu);

        // ---- geometry ----
        Geometry? geometri = null;
        if (feature.TryGetProperty("geometry", out var geom) && geom.ValueKind == JsonValueKind.Object)
        {
            geometri = GeometriOku(geom);
        }

        return new GeoServerFeature
        {
            Id = id,
            Geometry = geometri,
            Ozellikler = ozellikler
        };
    }

    /// <summary>
    /// Kaydın id'sini bulur.
    ///
    /// Önce <c>properties.id</c> kolonuna bakar (store'da "Expose primary keys"
    /// açıksa gelir). Yoksa GeoJSON'un feature id'sini çözer: GeoServer bunu
    /// "tablo.pk" biçiminde yazar → "tbl_point.5" → 5.
    /// </summary>
    private static int IdBul(JsonElement feature, Dictionary<string, JsonElement> ozellikler, bool zorunlu)
    {
        if (ozellikler.TryGetValue("id", out var idKolonu))
        {
            if (idKolonu.ValueKind == JsonValueKind.Number && idKolonu.TryGetInt32(out var sayi))
            {
                return sayi;
            }
            if (int.TryParse(idKolonu.ToString(), out var metinden))
            {
                return metinden;
            }
        }

        if (feature.TryGetProperty("id", out var featureId))
        {
            var metin = featureId.ValueKind == JsonValueKind.String
                ? featureId.GetString()
                : featureId.ToString();

            var nokta = metin?.LastIndexOf('.') ?? -1;
            if (metin is not null && nokta >= 0 && int.TryParse(metin[(nokta + 1)..], out var pk))
            {
                return pk;
            }
        }

        if (!zorunlu)
        {
            return 0;
        }

        throw new GeoServerErisimException(
            "WFS cevabında kaydın id'si bulunamadı. GeoServer'da PostGIS store " +
            "ayarlarında 'Expose primary keys' seçeneği işaretli olmalı.");
    }

    private static Geometry GeometriOku(JsonElement geom)
    {
        var tip = geom.GetProperty("type").GetString();
        var koordinatlar = geom.GetProperty("coordinates");

        return tip switch
        {
            "Point" => Fabrika.CreatePoint(KoordinatOku(koordinatlar)),
            "LineString" => Fabrika.CreateLineString(KoordinatDizisi(koordinatlar)),
            "Polygon" => PoligonOku(koordinatlar),

            // MultiPolygon: Ödev 10'daki il sınırlarında gerekiyor — 81 ilin
            // 17'si adalar ya da ayrık parçalar yüzünden çok parçalı
            // (İstanbul, Çanakkale, Muğla...). Çizim tablolarımızdan böyle bir
            // geometri GELMEZ; oradaki tip kısıtı kolonda zaten duruyor.
            "MultiPolygon" => Fabrika.CreateMultiPolygon(
                koordinatlar.EnumerateArray().Select(PoligonOku).ToArray()),

            _ => throw new GeoServerErisimException(
                $"Desteklenmeyen GeoJSON geometri tipi: {tip}")
        };
    }

    /// <summary>
    /// Poligon: koordinat halkalarının dizisi. İlk halka DIŞ sınır (shell),
    /// sonrakiler varsa DELİKler (holes). PostGIS'ten gelen verimizde delik
    /// yok ama standardın gereğini karşılıyoruz.
    /// </summary>
    private static Polygon PoligonOku(JsonElement halkalar)
    {
        var tumHalkalar = halkalar.EnumerateArray()
            .Select(h => Fabrika.CreateLinearRing(KoordinatDizisi(h)))
            .ToArray();

        if (tumHalkalar.Length == 0)
        {
            throw new GeoServerErisimException("Poligon geometrisinde hiç halka yok.");
        }

        return tumHalkalar.Length == 1
            ? Fabrika.CreatePolygon(tumHalkalar[0])
            : Fabrika.CreatePolygon(tumHalkalar[0], tumHalkalar[1..]);
    }

    private static Coordinate[] KoordinatDizisi(JsonElement dizi)
        => dizi.EnumerateArray().Select(KoordinatOku).ToArray();

    /// <summary>[boylam, enlem] çiftini Coordinate'a çevirir.</summary>
    private static Coordinate KoordinatOku(JsonElement cift)
    {
        var sayilar = cift.EnumerateArray().ToArray();

        if (sayilar.Length < 2)
        {
            throw new GeoServerErisimException("Koordinat çiftinde en az iki sayı olmalı.");
        }

        // [0] = X = boylam (longitude), [1] = Y = enlem (latitude)
        return new Coordinate(sayilar[0].GetDouble(), sayilar[1].GetDouble());
    }

    private static string Kirp(string metin)
    {
        var temiz = metin.Trim();
        return temiz.Length <= 300 ? temiz : temiz[..300] + "...";
    }
}
