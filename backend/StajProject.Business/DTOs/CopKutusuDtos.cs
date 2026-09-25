namespace StajProject.Business.DTOs;

// ============================================================================
//  ÇÖP KUTUSU
//
//  Proje Ödev 3'ten beri SOFT DELETE kullanıyor: silinen hiçbir kayıt
//  veritabanından gitmiyor, yalnızca is_deleted = true yapılıyor. Yani veri
//  ZATEN duruyordu — ama arayüzden görmenin ve geri getirmenin bir yolu yoktu.
//
//  İki uç istisnaydı: geometri ve POI'de "restore" ucu vardı ama silinenleri
//  LİSTELEYEN hiçbir şey olmadığı için o uçları çağırmak, silinen kaydın
//  id'sini bir yerden bilmeyi gerektiriyordu. Pratikte erişilemezlerdi.
//
//  Bu ekran o boşluğu kapatıyor: tek bir yerde bütün silinmiş kayıtlar,
//  türüne bakmadan.
// ============================================================================

/// <summary>
/// Çöp kutusundaki tek bir kayıt.
///
/// Farklı tablolardan gelen kayıtlar TEK BİR BİÇİMDE taşınıyor. Her tür için
/// ayrı DTO ve ayrı uç yapmak da mümkündü ama arayüz o zaman yedi ayrı liste
/// çizmek ve yedi ayrı "geri al" düğmesi yönetmek zorunda kalırdı — oysa
/// kullanıcının sorusu tek: "ne sildim, geri alabilir miyim?"
/// </summary>
public class CopOgesiDto
{
    /// <summary>
    /// Kayıt türünün makine adı: "nokta", "cizgi", "poligon", "poi",
    /// "kategori", "durak", "guzergah", "kullanici", "rol".
    ///
    /// Geri alma ucu bunu yol parçası olarak alıyor.
    /// </summary>
    public string Tur { get; set; } = string.Empty;

    /// <summary>İnsan için tür adı — "Nokta", "POI", "Durak"…</summary>
    public string TurAdi { get; set; } = string.Empty;

    public int Id { get; set; }

    /// <summary>Kaydın adı. Adsız kayıtlar için "(isimsiz)" yazılıyor.</summary>
    public string Ad { get; set; } = string.Empty;

    /// <summary>
    /// Bağlam: POI'de kategori, durakta güzergah, geometride açıklama.
    ///
    /// Aynı adda iki kayıt olabiliyor ("Merkez" adında iki durak) ve
    /// kullanıcının hangisini geri aldığını bilmesi gerekiyor.
    /// </summary>
    public string? Detay { get; set; }

    /// <summary>
    /// Silinme anı (UTC).
    ///
    /// ---- NEDEN AYRI BİR deleted_date KOLONU YOK? ----
    ///
    /// <c>ModifiedDate</c> kullanılıyor ve bu bir kısayol DEĞİL, doğru cevap:
    /// soft delete'in kendisi bir GÜNCELLEMEDİR ve <c>ApplyAuditRules</c> o
    /// güncellemede ModifiedDate'i basıyor. Silinmiş bir kayıt ise sorgu
    /// filtreleri yüzünden başka hiçbir yerden güncellenemiyor — yani
    /// silinmiş bir kaydın SON değişikliği, silinmesidir.
    ///
    /// Ayrı kolon eklemek dokuz tabloda şema değişikliği demekti ve aynı
    /// bilgiyi ikinci kez saklamaktan başka bir şey getirmezdi.
    ///
    /// Ödev 3 öncesinden kalan, hiç güncellenmemiş kayıtlarda null olabilir.
    /// </summary>
    public DateTime? SilinmeZamani { get; set; }

    /// <summary>Kaydı ekleyen kullanıcı; bilinmiyorsa null.</summary>
    public string? Ekleyen { get; set; }

    /// <summary>
    /// Giriş yapan kullanıcı bu kaydı geri alabilir mi?
    ///
    /// Liste HERKESE aynı geliyor ama düğme herkeste açık değil. Yetkiyi
    /// istemcide hesaplamak yerine sunucudan göndermek, arayüzün yetki
    /// kurallarını ikinci kez (ve yanlış) uygulamasını engelliyor.
    /// </summary>
    public bool GeriAlinabilir { get; set; }

    /// <summary>
    /// Kalıcı silinmesine kaç gün kaldı (0 = bugün silinecek).
    /// Silinme zamanı bilinmiyorsa null — o kayıt otomatik silinmiyor.
    ///
    /// Sunucuda hesaplanıyor: kalan süreyi tarayıcının saatinden
    /// hesaplasaydık, saati kaymış bir makinede ekrandaki gün sayısı
    /// kaydı silen görevle ayrışırdı.
    /// </summary>
    public int? KalanGun { get; set; }
}

/// <summary>
/// Çöp kutusu özeti — tür başına kaç kayıt var.
/// Arayüz süzgeç sekmelerini bundan çiziyor.
/// </summary>
public class CopOzetiDto
{
    public string Tur { get; set; } = string.Empty;
    public string TurAdi { get; set; } = string.Empty;
    public int Adet { get; set; }
}

/// <summary>Çöp kutusunun tamamı.</summary>
public class CopKutusuDto
{
    public List<CopOzetiDto> Ozet { get; set; } = new();
    public List<CopOgesiDto> Ogeler { get; set; } = new();
}
