using NetTopologySuite.Geometries;

namespace StajProject.Entities;

/// <summary>
/// Türkiye'nin 81 ili ve sınır geometrisi (Ödev 10).
///
/// NEDEN AYRI BİR TABLO?
/// Coğrafi yetki artık üç yoldan tanımlanabiliyor: elle çizim, il seçimi ve
/// bölge seçimi. Son ikisi için gerçek idari sınırlar gerekiyor — kaba
/// dikdörtgenler "Ankara'ya yetki verdim" demenin karşılığı olmazdı.
///
/// Bu tablo REFERANS VERİDİR: kullanıcı üretmez, uygulama değiştirmez.
/// Bu yüzden soft delete / aktiflik kolonları yok; <see cref="GeoPermission"/>
/// gibi bir kural da değil, sadece coğrafyanın kendisi.
/// </summary>
public class Il
{
    /// <summary>
    /// Plaka kodu (1–81) — birincil anahtar.
    ///
    /// Kendi ürettiğimiz bir kimlik yerine plakayı kullanıyoruz: zaten benzersiz,
    /// zaten herkesin bildiği, ve veri dosyası yeniden yüklendiğinde aynı ile
    /// aynı numara düşüyor. Otomatik artan bir id kullansaydık her yeniden
    /// yüklemede numaralar kayabilir, kayıtlı yetkiler yanlış ile bağlanabilirdi.
    /// </summary>
    public int Id { get; set; }

    /// <summary>İl adı — "Ankara", "Şanlıurfa".</summary>
    public string Ad { get; set; } = string.Empty;

    /// <summary>
    /// Coğrafi bölge — "Marmara", "Ege", "Akdeniz", "İç Anadolu", "Karadeniz",
    /// "Doğu Anadolu", "Güneydoğu Anadolu".
    ///
    /// Bölge sınırları ayrı bir geometri olarak TUTULMUYOR: bölge, o bölgedeki
    /// illerin birleşimidir. Ayrı tutsaydık iki veri kaynağı birbirinden
    /// kayabilir, il sınırıyla bölge sınırı çakışmayabilirdi.
    /// </summary>
    public string Bolge { get; set; } = string.Empty;

    /// <summary>
    /// İl sınırı (EPSG:4326).
    ///
    /// Tip <see cref="Geometry"/>, <see cref="Polygon"/> değil: illerin 17'si
    /// adalar ya da ayrık parçalar yüzünden MultiPolygon (örn. İstanbul,
    /// Çanakkale, Muğla). Polygon dayatsaydık bu iller hiç yüklenemezdi.
    /// </summary>
    public Geometry Geom { get; set; } = default!;
}
