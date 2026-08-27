using System.Globalization;
using System.Text.Json;
using NetTopologySuite.Geometries;

namespace StajProject.DataAccess.GeoServer;

/// <summary>
/// WFS cevabından okunan TEK bir kayıt (GeoJSON dilinde "feature").
///
/// GeoJSON'da bir feature iki parçadan oluşur:
///   "geometry"   → koordinatlar
///   "properties" → tablonun geometri dışındaki kolonları (name, color, ...)
///
/// Bu sınıf o iki parçayı bir arada tutar ve kolonları TİPLİ okumak için
/// küçük yardımcılar sunar. Neden yardımcı? GeoJSON'da her şey JSON'dur:
/// <c>inserted_user_id</c> sayı, <c>is_deleted</c> mantıksal,
/// <c>inserted_date</c> ise metin olarak gelir. Bu çevrimleri her çağrı
/// yerinde tekrar yazmak yerine tek yerde topluyoruz — ve <b>eksik kolon</b>
/// durumunda çökmek yerine null dönüyoruz (GeoServer'da bir kolon
/// gizlenirse uygulama tamamen durmasın).
/// </summary>
public sealed class GeoServerFeature
{
    /// <summary>
    /// Kaydın veritabanı id'si.
    ///
    /// İki kaynaktan gelebilir: store'da "Expose primary keys" açıksa
    /// <c>properties.id</c> kolonu olarak, kapalıysa yalnızca feature id
    /// metninde ("tbl_point.5"). <see cref="GeoJsonOkuyucu"/> ikisini de dener.
    /// </summary>
    public int Id { get; init; }

    /// <summary>EPSG:4326 koordinatlarıyla geometri. Kolon boşsa null.</summary>
    public Geometry? Geometry { get; init; }

    /// <summary>Geometri dışındaki kolonlar, kolon adıyla anahtarlanmış.</summary>
    public IReadOnlyDictionary<string, JsonElement> Ozellikler { get; init; }
        = new Dictionary<string, JsonElement>();

    /// <summary>Kolonu metin olarak okur. Yoksa veya null ise null döner.</summary>
    public string? Metin(string kolon)
    {
        if (!Bul(kolon, out var deger)) return null;
        return deger.ValueKind == JsonValueKind.String ? deger.GetString() : deger.ToString();
    }

    /// <summary>Kolonu tam sayı olarak okur. Yoksa/boşsa null.</summary>
    public int? Tamsayi(string kolon)
    {
        if (!Bul(kolon, out var deger)) return null;

        if (deger.ValueKind == JsonValueKind.Number && deger.TryGetInt32(out var sayi))
        {
            return sayi;
        }

        // Bazı sürücüler sayıyı metin olarak yazabiliyor — o durumu da karşılıyoruz.
        return int.TryParse(deger.ToString(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var metinden)
            ? metinden
            : null;
    }

    /// <summary>Kolonu mantıksal olarak okur; yoksa <paramref name="varsayilan"/> döner.</summary>
    public bool Mantiksal(string kolon, bool varsayilan = false)
    {
        if (!Bul(kolon, out var deger)) return varsayilan;

        return deger.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(deger.GetString(), out var m) ? m : varsayilan,
            _ => varsayilan
        };
    }

    /// <summary>
    /// Kolonu tarihe çevirir.
    ///
    /// GeoServer zaman kolonlarını ISO-8601 olarak yazar ("2026-08-19T10:11:12Z").
    /// <c>DateTimeStyles.AdjustToUniversal</c> ile sonucu UTC'ye sabitliyoruz:
    /// projenin geri kalanı (InsertedDate, ModifiedDate) UTC ile çalışıyor,
    /// makinenin yerel saat dilimine göre kayan bir değer sessiz hatalar üretir.
    /// </summary>
    public DateTime? Tarih(string kolon)
    {
        var metin = Metin(kolon);
        if (string.IsNullOrWhiteSpace(metin)) return null;

        return DateTime.TryParse(metin, CultureInfo.InvariantCulture,
                                 DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                 out var tarih)
            ? DateTime.SpecifyKind(tarih, DateTimeKind.Utc)
            : null;
    }

    /// <summary>Kolon var mı ve değeri null dışında mı?</summary>
    private bool Bul(string kolon, out JsonElement deger)
    {
        if (Ozellikler.TryGetValue(kolon, out deger) && deger.ValueKind != JsonValueKind.Null)
        {
            return true;
        }

        deger = default;
        return false;
    }
}
