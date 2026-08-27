using NetTopologySuite.Geometries;

namespace StajProject.Entities;

// ============================================================================
//  Ödev 16 — AKILLI ULAŞIM MODÜLÜ
//
//  İki tablo, aralarında 1-N ilişki:
//
//      guzergah  ── 1 ────< N ──  durak
//
//  "Her bir durak bir güzergaha ait" (ödev metni). Yani ilişki durak
//  tarafından taşınıyor: <see cref="Durak.GuzergahId"/> ZORUNLU bir yabancı
//  anahtar.
//
//  NEDEN N-N DEĞİL?
//  Gerçek hayatta bir durak birden çok hattın uğrağı olabilir ve o model
//  araya bir bağlantı tablosu (guzergah_durak) ister. Ödev açıkça 1-N
//  istiyor ve bunun somut bir faydası var: DURAĞIN SIRASI güzergaha göre
//  değişen bir bilgidir. 1-N'de sıra durağın kendi kolonu olabiliyor
//  (<see cref="Durak.Sira"/>); N-N'de sıranın bağlantı tablosunda durması
//  gerekirdi ve sürükle-bırak sıralaması iki tabloya birden dokunurdu.
//
//  NEDEN tbl_point ya da poi DEĞİL?
//  tbl_point kullanıcının kişisel çizimi (Ödev 5: herkes kendininkini görür).
//  poi ortak referans verisi ama KATEGORİYE bağlı, sırası yok, bir hatta ait
//  değil. Durak üçüncü bir şey: ortak veri + güzergaha ait + sıralı. Üçünü
//  aynı tabloda toplamak, kolonların çoğunu her satırda boş bırakırdı —
//  Poi.cs başlığındaki gerekçenin aynısı.
// ============================================================================

/// <summary>
/// Bir ulaşım hattı (Ödev 16 / Madde 1).
///
/// Güzergahın kendi geometrisi YOK: hattın çizgisi, duraklarının
/// <see cref="Durak.Sira"/> sırasına göre birleştirilmesiyle çıkıyor.
///
/// NEDEN AYRI BİR LineString KOLONU TUTULMUYOR?
/// Tutsaydık iki kaynak olurdu: durak listesi ve çizgi. Sürükle-bırakla sıra
/// değiştiğinde ya da bir durak taşındığında ikisi birbirinden kayabilirdi ve
/// haritada "duraklar burada ama çizgi şurada" gibi bir çelişki doğardı.
/// Çizgi TÜRETİLMİŞ bir değer olduğu için tek doğru yeri hesaplandığı an.
/// (Aynı ilke Ödev 10'da bölge sınırları için de uygulandı: bölge, illerinin
/// birleşimidir, ayrı bir geometri değil.)
/// </summary>
public class Guzergah : IAuditableEntity
{
    public int Id { get; set; }

    /// <summary>Hattın adı — "M1 Metro", "126 Ulus-Çayyolu".</summary>
    public string Ad { get; set; } = string.Empty;

    /// <summary>
    /// Haritadaki rengi (#rrggbb).
    ///
    /// Güzergahın KENDİ özelliği, kategoriden türetilmiyor: aynı anda birkaç
    /// hat açıkken onları ayırmanın tek yolu renk. Ödev metni de renk
    /// belirlemeyi açıkça istiyor.
    /// </summary>
    public string Renk { get; set; } = "#2d7dd2";

    public string? Aciklama { get; set; }

    /// <summary>
    /// Bu güzergahın durakları. Sıra <see cref="Durak.Sira"/> kolonunda;
    /// navigasyon koleksiyonu kendiliğinden sıralı gelmiyor, sorgular
    /// <c>OrderBy(d =&gt; d.Sira)</c> ile okuyor.
    /// </summary>
    public ICollection<Durak> Duraklar { get; set; } = new List<Durak>();

    /// <summary>
    /// Güzergahı tanımlayan kullanıcı. Nullable ve SetNull: hat ortak veridir,
    /// tanımlayan hesap silinse de yerinde kalmalı (Poi.UserId ile aynı kural).
    /// </summary>
    public int? UserId { get; set; }

    public User? User { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public DateTime? ModifiedDate { get; set; }
}

/// <summary>
/// Güzergah üzerindeki tek bir durak (Ödev 16 / Madde 1).
/// </summary>
public class Durak : IAuditableEntity
{
    public int Id { get; set; }

    /// <summary>Durağın adı — "Kızılay", "Ulus".</summary>
    public string Ad { get; set; } = string.Empty;

    /// <summary>
    /// Bağlı olduğu güzergah. ZORUNLU — ödev metni: "Her bir durak bir
    /// güzergaha ait olacak şekilde 1-N ilişki kurun."
    ///
    /// Güzergahsız bir durak haritada hangi renkle çizileceği ve hangi hattın
    /// kaçıncı durağı olduğu bilinmeyen bir kayıt olurdu.
    /// </summary>
    public int GuzergahId { get; set; }

    public Guzergah? Guzergah { get; set; }

    /// <summary>
    /// Güzergah İÇİNDEKİ sıra numarası (1'den başlar).
    ///
    /// Sürükle-bırakla değiştirilen şey bu kolon. Sıralamayı "eklenme
    /// tarihine göre" bırakmak da mümkündü ama o zaman araya durak eklemek
    /// imkânsız olurdu: yeni kayıt hep sona düşerdi.
    ///
    /// Boşluklu olabilir mi? Servis her yazma işleminden sonra sırayı
    /// 1..N olacak şekilde SIKIŞTIRIYOR (bkz. UlasimService.SiralariDuzelt).
    /// Boşluk bırakmak "araya ekleme"yi ucuzlatırdı ama arayüzde
    /// "3. durak" yazan sayı ile listedeki konum ayrışırdı.
    /// </summary>
    public int Sira { get; set; }

    /// <summary>Durağın konumu: POINT(boylam enlem), EPSG:4326.</summary>
    public Point Geom { get; set; } = default!;

    /// <summary>Bilgi kutucuğunda gösterilen serbest not — peron, aktarma bilgisi…</summary>
    public string? Aciklama { get; set; }

    /// <summary>Durağı ekleyen kullanıcı; hesap silinirse null olur.</summary>
    public int? UserId { get; set; }

    public User? User { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public DateTime? ModifiedDate { get; set; }
}
