using NetTopologySuite.Geometries;

namespace StajProject.Entities;

// ============================================================================
//  Ödev 12 / Madde 1: POI (Point of Interest — "ilgi noktası")
//
//  İki tablo:
//      poi_category ── HİYERARŞİK sınıflandırma (Yeme-İçme → Restoran, Kafe)
//      poi          ── haritadaki asıl kayıt; bir kategoriye bağlı
//
//  NEDEN tbl_point'e bir kolon eklemek yerine AYRI TABLO?
//  tbl_point "kullanıcının çizdiği serbest nokta"dır: sahibine özeldir, adı ve
//  rengi dışında bir anlamı yoktur. POI ise ORTAK REFERANS VERİSİDİR — operatör
//  girer, herkes görür, kategorisi ve mesai saati vardır. İkisini aynı tabloda
//  toplasaydık kolonların yarısı her satırda boş kalırdı ve "bu nokta POI mi
//  değil mi?" sorusu her sorguya sızardı.
// ============================================================================

/// <summary>
/// POI kategorisi — kendi kendine ATA-ÇOCUK ilişkisi kuran hiyerarşik sözlük.
///
/// <c>ParentId</c> aynı tablonun <c>Id</c>'sine bakar (self-referencing FK).
/// Kök kategorilerde null'dur: "Yeme-İçme" köktür, "Restoran" onun çocuğudur.
///
/// NEDEN iki ayrı tablo (ana kategori / alt kategori) değil?
/// Çünkü derinlik önceden bilinmiyor. İki tablo yapsaydık üçüncü seviye
/// istendiğinde ("Yeme-İçme → Restoran → Kebapçı") şema değiştirmek gerekirdi.
/// Tek tablo + parent_id ile derinlik VERİNİN sorunu olur, şemanın değil.
/// </summary>
public class PoiCategory : IAuditableEntity
{
    public int Id { get; set; }

    /// <summary>Görünen ad — "Yeme-İçme", "Restoran".</summary>
    public string Ad { get; set; } = string.Empty;

    public string? Aciklama { get; set; }

    /// <summary>
    /// Haritada bu kategorinin POI'lerini temsil eden simge (Ödev 15).
    ///
    /// Değer bir ANAHTAR: "fincan", "yatak", "eczane"… Çizimin kendisi
    /// Business katmanındaki <c>PoiIkonlari</c> kataloğunda duruyor.
    ///
    /// NEDEN SVG'NİN KENDİSİ SAKLANMIYOR?
    /// Kolonda ham SVG tutsaydık her kategori kendi çizimini taşırdı ve bir
    /// simgeyi düzeltmek bütün satırları güncellemeyi gerektirirdi; üstelik
    /// panelden gelen serbest SVG bir güvenlik yüzeyi (script gömülü SVG)
    /// açardı. Anahtar tutmak, "hangi simge" sorusunu veriye, "nasıl çizilir"
    /// sorusunu koda bırakıyor.
    ///
    /// NULL olabilir: kategoriye simge seçilmemiş demek. O durumda ATASININ
    /// simgesi, o da yoksa varsayılan harita iğnesi kullanılıyor — böylece
    /// yeni açılan bir alt kategori de haritada anlamlı görünüyor.
    /// </summary>
    public string? Ikon { get; set; }

    /// <summary>
    /// Üst kategori. Null ise bu bir KÖK kategoridir.
    ///
    /// Aynı tabloya bakan yabancı anahtar olduğu için veritabanı, olmayan bir
    /// ataya bağlanmayı zaten engelliyor. DÖNGÜYÜ (A'nın atası B, B'nin atası A)
    /// ise veritabanı engelleyemez — o kontrol servis katmanında.
    /// </summary>
    public int? ParentId { get; set; }

    public PoiCategory? Parent { get; set; }

    /// <summary>Bu kategorinin alt kategorileri.</summary>
    public ICollection<PoiCategory> Children { get; set; } = new List<PoiCategory>();

    /// <summary>Bu kategoriye bağlı POI'ler — kategori silinmeden önce sayılıyor.</summary>
    public ICollection<Poi> Poiler { get; set; } = new List<Poi>();

    /// <summary>Kaydın oluşturulma anı (UTC).</summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public DateTime? ModifiedDate { get; set; }
}

/// <summary>
/// Haritadaki tek bir ilgi noktası (Ödev 12 / Madde 1).
///
/// Kolon adları ödev metnindeki adlandırmayı izliyor: <c>isim</c>,
/// <c>kategori_id</c>, <c>mesai_saatleri</c> ve standart durum kolonları
/// <c>is_active</c> / <c>is_deleted</c> / <c>created_date</c> / <c>user_id</c>.
///
/// Not: geometri tabloları (tbl_point vb.) izleme kolonlarını
/// <c>inserted_date</c> / <c>inserted_user_id</c> adlarıyla taşıyor. Burada
/// ödevin verdiği adlar kullanıldı; iki tablonun kolon adları farklı olsa da
/// ANLAMLARI aynı ve ikisi de aynı <see cref="IAuditableEntity"/> sözleşmesini
/// uyguluyor, yani ModifiedDate damgası her ikisinde de merkezî olarak basılıyor.
/// </summary>
public class Poi : IAuditableEntity
{
    public int Id { get; set; }

    /// <summary>POI'nin adı — "Hacı Arif Bey Lokantası".</summary>
    public string Isim { get; set; } = string.Empty;

    /// <summary>
    /// Bağlı olduğu kategori. ZORUNLU: kategorisiz bir POI, listelerde
    /// süzülemez ve haritada hangi simgeyle çizileceği bilinemez.
    /// </summary>
    public int KategoriId { get; set; }

    public PoiCategory? Kategori { get; set; }

    /// <summary>
    /// Mesai saatlerinin İNSAN OKUYACAĞI özeti — "Pzt-Cum 09:00-18:00 ·
    /// Cmt 10:00-14:00 · Paz kapalı" ya da "7/24".
    ///
    /// Ödev 12'de bu alan tek başınaydı ve kullanıcı doğrudan buraya yazıyordu.
    /// Ödev 13 / Madde 3 gün gün tanım isteyince yapısal bilgi
    /// <see cref="MesaiPlani"/> kolonuna taşındı; bu kolon artık ELLE
    /// GİRİLMİYOR, plandan üretiliyor (bkz. Business/Mesai/MesaiPlani.OzetMetin).
    ///
    /// Neden hâlâ duruyor? Listeler, harita popup'ı ve GeoServer'ın SQL View'ı
    /// mesaiyi tek satırda göstermek istiyor. Hepsinin JSON çözmesi yerine
    /// özeti bir kez üretip saklıyoruz. Türetilmiş bir kolon olduğu için de
    /// ikisi çelişemez: plan değişince özet de aynı işlemde yeniden yazılıyor.
    /// </summary>
    public string? MesaiSaatleri { get; set; }

    /// <summary>
    /// Haftalık çalışma planı — JSON (jsonb kolonu), Ödev 13 / Madde 3.
    ///
    /// Şeması Business katmanındaki <c>MesaiPlani</c> sınıfıdır: yedi günün
    /// açık/kapalı durumu ve saatleri, "resmî tatillerde kapalı" bayrağı ve
    /// kip ("haftalik" / "surekli" / "resmi").
    ///
    /// NEDEN ENTITY'DE METİN OLARAK DURUYOR DA TİPLİ DEĞİL?
    /// Entities katmanı hiçbir katmana bağımlı değil (bkz. README → Mimari);
    /// buraya Business'taki MesaiPlani tipini koymak bağımlılık yönünü ters
    /// çevirirdi. Kolon ham JSON taşıyor, anlamlandırma Business'ta yapılıyor —
    /// aynı desen WKT'de de var: veritabanı metni tutar, kuralı iş katmanı bilir.
    ///
    /// Ödev 12'den kalan kayıtlarda NULL: o kayıtların yalnızca özet metni var.
    /// </summary>
    public string? MesaiPlani { get; set; }

    /// <summary>Konum: POINT(boylam enlem), EPSG:4326.</summary>
    public Point Geom { get; set; } = default!;

    /// <summary>
    /// POI'yi ekleyen kullanıcı. Admin panelindeki "ekleyen kullanıcı"
    /// sütunu bu kolondan besleniyor.
    ///
    /// Nullable: kullanıcı fiziksel olarak silinirse POI'nin de silinmesi
    /// yanlış olurdu — POI ortak veridir, sahibi değişse de yerinde kalmalı.
    /// O yüzden yabancı anahtar davranışı SetNull ("ekleyen bilinmiyor").
    /// </summary>
    public int? UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Kaydın oluşturulma anı (UTC).</summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public DateTime? ModifiedDate { get; set; }
}
