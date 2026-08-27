using System.ComponentModel.DataAnnotations;

namespace StajProject.Business.DTOs;

// ============================================================================
//  Ödev 16 — akıllı ulaşım modülü DTO'ları
//
//  Entity'ler dışarı çıkmıyor: Guzergah.Duraklar ↔ Durak.Guzergah karşılıklı
//  navigasyonları doğrudan serileştirilse döngüsel JSON üretirdi (güzergah →
//  durak → güzergah → …). DTO tarafında ilişki TEK YÖNLÜ taşınıyor: güzergah
//  duraklarını biliyor, durak yalnızca güzergahının id'sini ve adını.
// ============================================================================

/// <summary>İstemciye giden durak kaydı.</summary>
public class DurakDto
{
    public int Id { get; set; }
    public string Ad { get; set; } = string.Empty;

    public int GuzergahId { get; set; }

    /// <summary>Güzergahın adı — durak listesinde ve bilgi kutucuğunda bağlam veriyor.</summary>
    public string GuzergahAdi { get; set; } = string.Empty;

    /// <summary>
    /// Güzergahın rengi. Durakla birlikte taşınıyor çünkü harita katmanı
    /// durağı hattının rengiyle çiziyor; istemcinin her durak için güzergah
    /// listesinde arama yapmasına gerek kalmıyor.
    /// </summary>
    public string GuzergahRengi { get; set; } = string.Empty;

    /// <summary>Güzergah içindeki sıra (1'den başlar).</summary>
    public int Sira { get; set; }

    /// <summary>Konum, WKT (EPSG:4326): "POINT (32.85 39.93)".</summary>
    public string Wkt { get; set; } = string.Empty;

    public string? Aciklama { get; set; }

    /// <summary>Ekleyen kullanıcının id'si; kullanıcı silinmişse null.</summary>
    public int? UserId { get; set; }

    /// <summary>Ekleyen kullanıcının adı — bilgi kutucuğunda gösteriliyor.</summary>
    public string? KullaniciAdi { get; set; }

    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>İstemciye giden güzergah kaydı — durakları sıralı hâlde içinde.</summary>
public class GuzergahDto
{
    public int Id { get; set; }
    public string Ad { get; set; } = string.Empty;

    /// <summary>Haritadaki rengi (#rrggbb).</summary>
    public string Renk { get; set; } = string.Empty;

    public string? Aciklama { get; set; }

    /// <summary>
    /// Durakları SIRAYLA. Ayrı bir uçtan istetmiyoruz: güzergah listesi zaten
    /// duraklarıyla birlikte anlamlı (harita hattı onlardan çiziliyor,
    /// yönetim ekranı sürükle-bırak listesini onlardan kuruyor) ve iki istek
    /// arasında sıranın değişmesi ihtimalini de ortadan kaldırıyor.
    /// </summary>
    public List<DurakDto> Duraklar { get; set; } = new();

    /// <summary>Durak sayısı — listede özet olarak gösteriliyor.</summary>
    public int DurakSayisi => Duraklar.Count;

    // ---------- Ödev 17 / Madde 1: OSRM rotası ----------

    /// <summary>
    /// OSRM'in hesapladığı, yollara oturmuş hat çizgisi — WKT LINESTRING
    /// (EPSG:4326). Rota henüz hesaplanmadıysa null.
    ///
    /// Arayüz null gelince Ödev 16'daki düz çizgiye düşüyor: hat yine
    /// görünüyor ama "kuş uçuşu" olduğu belli ediliyor.
    /// </summary>
    public string? RotaWkt { get; set; }

    /// <summary>Toplam sürüş mesafesi (metre).</summary>
    public double? RotaMesafeMetre { get; set; }

    /// <summary>
    /// Tahmini SÜRÜŞ süresi (saniye) — sefer süresi değil.
    /// Durak bekleme ve trafik hesaba katılmıyor.
    /// </summary>
    public double? RotaSureSaniye { get; set; }

    /// <summary>Rotanın en son hesaplandığı an (UTC).</summary>
    public DateTime? RotaHesaplandi { get; set; }

    /// <summary>
    /// Rota, MEVCUT durak dizilimi için mi hesaplandı?
    ///
    /// false olması tek bir şey demek: durak eklendi/taşındı/sırası değişti
    /// ama o an OSRM'e ulaşılamadığı için rota yenilenemedi. Arayüz bu
    /// durumda "rota güncel değil" uyarısı gösteriyor ve eski çizgiyi
    /// SOLGUN çiziyor — güncelmiş gibi göstermek, haritada sessizce yanlış
    /// bilgi vermek olurdu.
    ///
    /// Rota hiç YOKSA true döner: ortada eskimiş bir şey yok.
    /// </summary>
    public bool RotaGuncel { get; set; } = true;

    public int? UserId { get; set; }
    public string? KullaniciAdi { get; set; }

    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Güzergah ekleme/güncelleme isteği.</summary>
public class GuzergahSaveDto
{
    [Required(ErrorMessage = "Güzergah adı zorunludur.")]
    [MaxLength(150, ErrorMessage = "Güzergah adı en fazla 150 karakter olabilir.")]
    public string Ad { get; set; } = string.Empty;

    /// <summary>
    /// Renk "#rrggbb" biçiminde. Biçim kontrolü hem burada (RegularExpression)
    /// hem serviste var: öznitelik istemciye hızlı ve alan bazlı bir hata
    /// veriyor, servisteki kontrol ise servisi doğrudan çağıran testlerde de
    /// geçerli (aynı ikili savunma projenin geri kalanında da var).
    /// </summary>
    [Required(ErrorMessage = "Güzergah rengi zorunludur.")]
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Renk #rrggbb biçiminde olmalıdır.")]
    public string Renk { get; set; } = "#2d7dd2";

    [MaxLength(500, ErrorMessage = "Açıklama en fazla 500 karakter olabilir.")]
    public string? Aciklama { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Yeni durak isteği (haritadaki "Durak Ekle" aracı).</summary>
public class DurakCreateDto
{
    [Required(ErrorMessage = "Durak adı zorunludur.")]
    [MaxLength(150, ErrorMessage = "Durak adı en fazla 150 karakter olabilir.")]
    public string Ad { get; set; } = string.Empty;

    /// <summary>
    /// Hangi güzergaha? ZORUNLU — 1-N ilişkisinin karşılığı. Arayüzde bu bir
    /// açılır liste (ödev metni: "mevcut güzergahlar dropdown üzerinden
    /// seçilsin").
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Güzergah seçilmelidir.")]
    public int GuzergahId { get; set; }

    /// <summary>Konum — "POINT (32.85 39.93)". Tipi POINT olmak zorunda.</summary>
    [Required(ErrorMessage = "Durak konumu (WKT) zorunludur.")]
    public string Wkt { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Açıklama en fazla 500 karakter olabilir.")]
    public string? Aciklama { get; set; }

    /// <summary>
    /// Güzergahtaki sıra. Boş bırakılırsa durak SONA ekleniyor — haritadan
    /// durak eklerken en doğal davranış bu; araya sokmak isteyen kullanıcı
    /// zaten sürükle-bırakla taşıyor.
    /// </summary>
    public int? Sira { get; set; }
}

/// <summary>Durak güncelleme isteği.</summary>
public class DurakUpdateDto
{
    [Required(ErrorMessage = "Durak adı zorunludur.")]
    [MaxLength(150, ErrorMessage = "Durak adı en fazla 150 karakter olabilir.")]
    public string Ad { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Güzergah seçilmelidir.")]
    public int GuzergahId { get; set; }

    [MaxLength(500, ErrorMessage = "Açıklama en fazla 500 karakter olabilir.")]
    public string? Aciklama { get; set; }

    /// <summary>
    /// Boş bırakılırsa KONUM DEĞİŞMEZ; yalnızca ad/güzergah/açıklama
    /// güncellenir (GeometryUpdateDto ve PoiUpdateDto ile aynı desen).
    /// </summary>
    public string? Wkt { get; set; }
}

/// <summary>
/// Sürükle-bırak sonucunun sunucuya bildirimi (Ödev 16 / Madde 2).
///
/// NEDEN TEK TEK "şu durağın sırası 3 oldu" DEĞİL DE TOPLU LİSTE?
/// Bir durağı yukarı taşımak aradaki bütün durakların sırasını kaydırıyor.
/// Tek tek gönderseydik yarısı yazılıp yarısı yazılmadığında liste tutarsız
/// kalırdı; üstelik her taşımada N istek atılırdı. Sunucu, gelen id
/// sırasını tek işlemde 1..N olarak yazıyor.
/// </summary>
public class DurakSiralamaDto
{
    /// <summary>Durak id'leri, YENİ sırasıyla. Güzergahın tüm duraklarını içermeli.</summary>
    [Required(ErrorMessage = "Sıralama listesi zorunludur.")]
    [MinLength(1, ErrorMessage = "Sıralama listesi boş olamaz.")]
    public List<int> DurakIdleri { get; set; } = new();
}
