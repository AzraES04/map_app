namespace StajProject.Entities;

/// <summary>
/// Yenileme anahtarı — kısa ömürlü JWT'nin arkasındaki uzun ömürlü oturum.
///
/// ---- NEDEN GEREKTİ? ----
///
/// Erişim token'ı (JWT) bilerek kısa ömürlü: 10 dakika. Kısa olmasının sebebi
/// JWT'nin İPTAL EDİLEMEMESİ — sunucu onu doğrularken veritabanına bakmaz,
/// yalnızca imzayı kontrol eder. Yani çalınan bir token, siz kullanıcıyı
/// pasife alsanız bile süresi dolana kadar çalışır. O pencereyi 10 dakikada
/// tutmak doğru karar.
///
/// Ama tek başına 10 dakika, kullanıcıyı 10 dakikada bir giriş ekranına
/// atıyordu: yarım kalan poligon, doldurulmuş analiz paneli, hepsi gidiyordu.
/// "Süreyi uzatalım" yanlış çözüm olurdu — çalınan token'ın ömrünü uzatmak
/// demek.
///
/// Doğru çözüm iki anahtarı ayırmak:
///   erişim token'ı (JWT)  → 10 dk, veritabanına bakılmaz, HIZLI
///   yenileme anahtarı     → 7 gün, HER kullanımda veritabanına bakılır,
///                            İPTAL EDİLEBİLİR
///
/// ---- NEDEN DÜZ METİN DEĞİL, HASH SAKLANIYOR? ----
///
/// Bu tablo şifre tablosunun ikizi: içindeki değer, sahibinin yerine geçmeye
/// yetiyor. Düz metin saklasaydık, veritabanını okuyabilen biri (yedek
/// dosyası, SQL enjeksiyonu, bakış yetkisi olan bir çalışan) o an açık olan
/// TÜM oturumları devralırdı. Hash'te ise elindeki değerle giriş yapamaz:
/// hash'ten anahtarı geri üretemez.
///
/// Şifrelerde PBKDF2 kullanıyoruz ama burada SHA-256 yetiyor. Fark şu:
/// şifreyi insan seçer, tahmin edilebilir ve yavaş hash gerekir. Bu anahtar
/// ise 32 baytlık kriptografik rastgele veri — sözlük saldırısı diye bir şey
/// yok, kaba kuvvetle bulunamaz.
///
/// ---- DÖNDÜRME (ROTATION) VE YENİDEN KULLANIM TESPİTİ ----
///
/// Her yenilemede bu satır iptal edilir ve YENİ bir satır üretilir. Bu,
/// çalınan bir anahtarı süresiz kullanılabilir olmaktan çıkarır ve daha
/// önemlisi HIRSIZLIĞI GÖRÜNÜR yapar:
///
///   Anahtar çalındıysa aynı değer iki kez kullanılır — biri gerçek
///   kullanıcı, biri hırsız. İkincisi geldiğinde artık iptal edilmiş bir
///   anahtar sunulmuş olur. Bu normal kullanımda ASLA olmaz; tek açıklaması
///   anahtarın kopyalanmasıdır. O anda kullanıcının bütün oturumları
///   kapatılır (bkz. <c>AuthService.RefreshAsync</c>).
///
/// Döndürme olmasaydı hırsız da gerçek kullanıcı da aynı anahtarı yedi gün
/// boyunca sessizce paylaşırdı ve kimse fark etmezdi.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }

    /// <summary>Anahtarın sahibi.</summary>
    public int UserId { get; set; }

    /// <summary>
    /// Anahtarın SHA-256 özeti (64 karakter, küçük harf hex).
    /// Anahtarın kendisi yalnızca bir kez, üretildiği anda istemciye gider;
    /// sunucu bir daha onu göremez.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Anahtarın kendiliğinden geçersizleşeceği an (UTC).</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// İptal edildiği an (UTC); null ise hâlâ geçerli.
    ///
    /// Satır SİLİNMİYOR, işaretleniyor: yeniden kullanım tespiti tam da
    /// "iptal edilmiş bir anahtar tekrar sunuldu" durumunu yakalamaya
    /// dayanıyor. Silseydik o anahtar "hiç var olmamış" görünürdü ve
    /// hırsızlık, sıradan bir geçersiz anahtardan ayırt edilemezdi.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Döndürme sırasında bunun yerine geçen anahtarın özeti.
    /// Zinciri geriye doğru okumayı sağlar: hangi oturumun nereden türediği.
    /// </summary>
    public string? ReplacedByHash { get; set; }

    /// <summary>Neden iptal edildi? ("cikis", "dondurme", "yeniden-kullanim")</summary>
    public string? RevokedReason { get; set; }

    public User? User { get; set; }

    /// <summary>Şu an kullanılabilir mi? (iptal edilmemiş ve süresi dolmamış)</summary>
    public bool GecerliMi(DateTime an) => RevokedAt is null && ExpiresAt > an;
}
