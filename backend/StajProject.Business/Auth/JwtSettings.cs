namespace StajProject.Business.Auth;

/// <summary>
/// appsettings.json'daki "Jwt" bölümünün karşılığı.
/// </summary>
public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// ERİŞİM token'ının geçerlilik süresi (dakika). Bilerek kısa: 10 dk.
    ///
    /// Kısa olmasının sebebi JWT'nin İPTAL EDİLEMEMESİ. Sunucu onu
    /// doğrularken veritabanına bakmaz, yalnızca imzayı kontrol eder; yani
    /// çalınan bir token, kullanıcıyı pasife alsanız bile süresi dolana kadar
    /// çalışır. Bu süreyi UZATMAK, çalınan token'ın ömrünü uzatmak demek
    /// olurdu — çözüm <see cref="RefreshExpiryDays"/>.
    /// </summary>
    public int ExpiryMinutes { get; set; } = 15;

    /// <summary>
    /// YENİLEME anahtarının geçerlilik süresi (gün). Oturumun MUTLAK sonu.
    ///
    /// Uzun olabiliyor çünkü erişim token'ının aksine bu anahtar her
    /// kullanımda veritabanından doğrulanıyor: iptal edilebilir, döndürülüyor
    /// ve çalındığında fark edilebiliyor.
    ///
    /// Yedi gün "yeniden giriş istemeden bir hafta çalışabilmek" ile "kayıp
    /// bir cihazın sınırsız erişmesi" arasındaki denge. Sınırsız bırakmak
    /// (ömrünü hiç doldurmayan anahtar) tek başına büyük risk olurdu.
    /// </summary>
    public int RefreshExpiryDays { get; set; } = 7;
}
