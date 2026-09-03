using NetTopologySuite.Geometries;

namespace StajProject.Entities;

// ============================================================================
//  TUR / ROTA MODÜLÜ — veri modeli
//
//  Dört tablo:
//
//      tour ──1──< waypoint                (turun sıralı durakları)
//        │
//        └──1──< tour_session ──1──< tour_session_participant ──> users
//
//  ÜÇ KAVRAM, ÜÇ AYRI TABLO — neden birleştirilmedi?
//
//    tour          ŞABLON'dur: bir kez tanımlanır, defalarca gezilir.
//    waypoint      şablonun İÇERİĞİ: hangi mekan, ne kadar kalınacak, hangi sırada.
//    tour_session  CANLI GEZİ: aynı şablonun bugünkü ve yarınki koşuları ayrı
//                  birer oturumdur. Canlı durumu (nerede olunduğu, hangi durakta
//                  kalındığı, kimlerin izlediği) şablona yazsaydık aynı tur iki
//                  gruba aynı anda yaptırılamaz ve geçmiş turların kaydı kalmazdı.
//
//  ROLLER OTURUMA AİTTİR, KULLANICIYA DEĞİL (bkz. <see cref="TourRole"/>):
//  aynı kişi bir oturumda Guide, başka bir oturumda Participant olabilir. Bu
//  yüzden rol users tablosunda bir kolon değil, KATILIM SATIRININ kolonu.
//  Sistem genelindeki "tur tanımlayabilir mi?" sorusu ise var olan yetki
//  mekanizmasında duruyor (Business/Auth/Yetkiler.cs → "Tur Yönetimi").
// ============================================================================

/// <summary>
/// Bir oturumdaki katılım rolü.
///
/// <c>Guide</c> oturumu YÖNETİR: başlatır, durağı ilerletir, konum yayınlar.
/// <c>Participant</c> İZLER: yalnızca yayını dinler.
///
/// Veritabanına METİN olarak yazılıyor (int değil): psql'den bakan biri "1"
/// değil "Guide" görüyor ve enum'a ileride araya bir üye eklendiğinde eski
/// satırların anlamı kaymıyor.
/// </summary>
public enum TourRole
{
    Guide,
    Participant,
}

/// <summary>
/// Oturumun yaşam döngüsü.
///
///   Planned   → oluşturuldu, henüz başlamadı (katılım kodu dağıtılabilir)
///   Live      → yayında
///   Paused    → rehber ara verdi; katılımcılar bağlı kalıyor
///   Completed → son durak tamamlandı
///   Cancelled → hiç başlamadan ya da yarıda bırakıldı
///
/// Neden tek bir "IsActive" bayrağı yetmedi? Üç farklı bitiş hâli
/// (tamamlandı / iptal / hiç başlamadı) bir boolean'a sığmıyor; sığdırmak
/// "bu tur gerçekten yapıldı mı?" sorusunu cevapsız bırakırdı.
/// </summary>
public enum TourSessionStatus
{
    Planned,
    Live,
    Paused,
    Completed,
    Cancelled,
}

/// <summary>
/// Durağın MEKAN TİPİ — simge ve süzgecin okuduğu alan.
///
/// NEDEN poi_category AĞACINA BAĞLANMADI? Kategori ağacı yöneticinin
/// düzenlediği, derinliği değişken bir SÖZLÜK; tur durağının tipi ise arayüzün
/// kod içinde bildiği KAPALI bir liste (her tipin kendi simgesi ve varsayılan
/// kalış süresi var). İkisini aynı yere bağlamak, bir kategoriyi silmenin tur
/// simgelerini bozması demek olurdu.
///
/// Durağın ayrıca bir POI kaydı varsa <see cref="Waypoint.PoiId"/> ile
/// bağlanıyor — tip ile kategori böylece birbirinden bağımsız kalıyor.
/// </summary>
public enum VenueType
{
    Other,
    Museum,
    Monument,
    ReligiousSite,
    Park,
    Viewpoint,
    Restaurant,
    Cafe,
    Shopping,
    Hotel,
    TransportHub,
}

/// <summary>
/// Tur ŞABLONU — genel bilgiler.
///
/// Canlı hiçbir bilgi taşımaz (nerede olunduğu, kimin izlediği): onların tamamı
/// <see cref="TourSession"/> tarafında. Böylece bir şablon aynı anda birden çok
/// grup tarafından gezilebiliyor.
/// </summary>
public class Tour : IAuditableEntity
{
    public int Id { get; set; }

    /// <summary>Turun adı — "Ankara Kale Turu".</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Haritadaki rengi (#rrggbb). <see cref="Guzergah.Renk"/> ile aynı gerekçe:
    /// aynı anda birkaç tur açıkken onları ayırmanın tek yolu renk.
    /// </summary>
    public string Color { get; set; } = "#7b5cd6";

    /// <summary>
    /// Turun PLANLANAN başlangıcı (UTC); serbest zamanlı şablonlarda null.
    ///
    /// Gerçekleşen başlangıç bu kolonda değil, <see cref="TourSession.StartedUtc"/>'de:
    /// plan ile gerçekleşen aynı kolona yazılsaydı gecikmeler görünmez olurdu.
    /// </summary>
    public DateTime? ScheduledStartUtc { get; set; }

    /// <summary>
    /// Turu tanımlayan rehber. Nullable + SetNull: tur ortak veridir, tanımlayan
    /// hesap silinse de yerinde kalmalı (<see cref="Poi.UserId"/> ile aynı kural).
    /// </summary>
    public int? GuideUserId { get; set; }

    public User? GuideUser { get; set; }

    /// <summary>
    /// Turun durakları. Sıra <see cref="Waypoint.Order"/> kolonunda; navigasyon
    /// koleksiyonu kendiliğinden sıralı gelmiyor, sorgular <c>OrderBy</c> ile okuyor.
    /// </summary>
    public ICollection<Waypoint> Waypoints { get; set; } = new List<Waypoint>();

    /// <summary>Bu şablonun bütün oturumları — canlı olanlar ve geçmiştekiler.</summary>
    public ICollection<TourSession> Sessions { get; set; } = new List<TourSession>();

    // ---------- Duraklardan üretilen rota ----------
    //
    // <see cref="Guzergah.Rota"/> ile aynı gerekçe: rota OSRM'den geliyor,
    // duraklardan tek başına türetilemiyor, hesabı saniyeler sürüyor. Bu yüzden
    // saklanıyor — ve hangi durak dizilimi için hesaplandığı RouteSignature ile
    // işaretleniyor ki bayat bir rota doğruymuş gibi çizilmesin.

    /// <summary>Yollara oturmuş tur rotası (EPSG:4326). Hesaplanmadıysa null.</summary>
    public LineString? Route { get; set; }

    /// <summary>Rotanın toplam mesafesi (metre).</summary>
    public double? RouteDistanceMeters { get; set; }

    /// <summary>
    /// Duraklar ARASINDA geçen tahmini yol süresi (saniye). Duraklardaki kalış
    /// süreleri DAHİL DEĞİL — turun toplam süresi bu değer ile
    /// <see cref="Waypoint.DwellMinutes"/> toplamının birleşimidir.
    /// </summary>
    public double? RouteDurationSeconds { get; set; }

    public DateTime? RouteCalculatedUtc { get; set; }

    /// <summary>
    /// Rotanın HANGİ durak dizilimi için hesaplandığının parmak izi (SHA-256 hex).
    /// Tutmuyorsa arayüz "rota güncel değil" diyor.
    /// Gerekçesi: <see cref="Guzergah.RotaImza"/>.
    /// </summary>
    public string? RouteSignature { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public DateTime? ModifiedDate { get; set; }
}

/// <summary>
/// Turun tek bir durağı: hangi mekan, nerede, ne kadar kalınacak.
/// </summary>
public class Waypoint : IAuditableEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Bağlı olduğu tur. ZORUNLU: tursuz bir durağın ne sırası ne de rotası
    /// tanımlı olurdu (<see cref="Durak.GuzergahId"/> ile aynı kural).
    /// </summary>
    public int TourId { get; set; }

    public Tour? Tour { get; set; }

    /// <summary>
    /// Tur içindeki sıra (1'den başlar). Sürükle-bırakla değişen kolon budur;
    /// servis her yazma işleminden sonra sırayı 1..N olacak şekilde sıkıştırır
    /// (<see cref="Durak.Sira"/> ile aynı kural).
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Mekanın DIŞ SAĞLAYICIDAKİ kimliği (Google Place ID, OSM node/way id...).
    ///
    /// NEDEN SAKLANIYOR? Aynı mekan yeniden aranınca ikinci bir kayıt açılmasın,
    /// sağlayıcıdaki ad değişikliği ("Cafe X" → "X Kahve") aynı yeri iki farklı
    /// mekan yapmasın diye. Koordinat üzerinden eşleştirme bunu çözmezdi:
    /// sağlayıcı koordinatı birkaç metre güncellediğinde eşleşme kopardı.
    ///
    /// Sağlayıcı ÖN EKİYLE yazılıyor — "osm:node/123", "google:ChIJ..." — ki tek
    /// kolonda iki sağlayıcının kimlikleri çakışmasın.
    /// </summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>
    /// Durağın sistemdeki POI karşılığı — varsa.
    ///
    /// Nullable: bir tur durağı POI olmak zorunda değil (buluşma noktası, ara
    /// mola). SetNull: POI silinse de durak turda kalmalı, çünkü adı ve konumu
    /// kendi kolonlarında duruyor.
    /// </summary>
    public int? PoiId { get; set; }

    public Poi? Poi { get; set; }

    /// <summary>Durakta gösterilen ad — "Anıtkabir".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Mekan tipi; simge ve süzgeç bu alandan besleniyor.</summary>
    public VenueType VenueType { get; set; } = VenueType.Other;

    /// <summary>
    /// Planlanan KALIŞ SÜRESİ (dakika).
    ///
    /// Yol süresinden ayrı bir kolon: yol süresini OSRM hesaplıyor, kalış
    /// süresini rehber giriyor. Tek kolonda toplasaydık rota her yeniden
    /// hesaplandığında rehberin girdiği değer silinirdi.
    /// </summary>
    public int DwellMinutes { get; set; }

    /// <summary>Durağın konumu: POINT(boylam enlem), EPSG:4326.</summary>
    public Point Geom { get; set; } = default!;

    /// <summary>Rehberin notu — giriş ücreti, buluşma yeri, kapalı olduğu gün...</summary>
    public string? Note { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public DateTime? ModifiedDate { get; set; }
}

/// <summary>
/// CANLI TUR OTURUMU — bir şablonun tek bir koşusu.
///
/// Bir şablondan istenildiği kadar oturum açılabilir; her oturumun kendi
/// rehberi, kendi katılımcıları ve kendi ilerleme durumu vardır.
/// </summary>
public class TourSession : IAuditableEntity
{
    public int Id { get; set; }

    /// <summary>Hangi şablonun koşusu olduğu. ZORUNLU.</summary>
    public int TourId { get; set; }

    public Tour? Tour { get; set; }

    /// <summary>
    /// Oturumu yöneten rehber. ZORUNLU ve Restrict: rehbersiz bir canlı oturum
    /// yönetilemez, o yüzden bu bağ kopmamalı (kullanıcılar zaten fiziksel
    /// olarak değil soft delete ile siliniyor).
    ///
    /// Not: aynı kişi ayrıca <see cref="Participants"/> içinde
    /// <see cref="TourRole.Guide"/> satırıyla duruyor. Bu kolon "kim sorumlu"
    /// sorusunun tek ve zorunlu cevabı; katılım satırı ise o kişinin yayına
    /// bağlı olup olmadığını söylüyor. İkisi farklı sorular.
    /// </summary>
    public int GuideUserId { get; set; }

    public User? GuideUser { get; set; }

    public TourSessionStatus Status { get; set; } = TourSessionStatus.Planned;

    /// <summary>
    /// Katılım kodu — katılımcının turu bulmak için girdiği kısa metin ("K7QF2M").
    ///
    /// Neden oturumun id'si değil? Id ardışıktır: komşu numaraları deneyen biri
    /// başkasının turuna düşebilirdi. Kod rastgele ve yalnızca KAPANMAMIŞ
    /// oturumlar arasında benzersiz (kapanan oturumun kodu yeniden kullanılabilir).
    /// </summary>
    public string JoinCode { get; set; } = string.Empty;

    /// <summary>Yayının gerçekten başladığı an (UTC). Planned iken null.</summary>
    public DateTime? StartedUtc { get; set; }

    /// <summary>Oturumun kapandığı an (UTC) — tamamlandı ya da iptal edildi.</summary>
    public DateTime? EndedUtc { get; set; }

    /// <summary>
    /// Grubun şu an bulunduğu durak; ilk durağa varılmadıysa null. SetNull:
    /// durak turdan çıkarılsa da oturum kaydı ayakta kalmalı.
    /// </summary>
    public int? CurrentWaypointId { get; set; }

    public Waypoint? CurrentWaypoint { get; set; }

    /// <summary>Bu durağa varış anı — kalış süresi sayacı buradan işliyor.</summary>
    public DateTime? CurrentWaypointArrivedUtc { get; set; }

    /// <summary>
    /// Rehberin son bilinen konumu (EPSG:4326) ve onun zaman damgası.
    ///
    /// Canlı yayın SignalR üzerinden gidiyor; bu kolon SON DEĞERİ saklıyor ki
    /// tura sonradan katılan biri ilk yayın mesajını beklemeden haritada grubu
    /// görsün. Konum GEÇMİŞİ tutulmuyor: her tik bir satır olsaydı tablo saatte
    /// binlerce satır büyürdü ve hiçbir ekran geçmişi istemiyor.
    /// </summary>
    public Point? LastPosition { get; set; }

    public DateTime? LastPositionUtc { get; set; }

    /// <summary>Turun yüzde kaçı tamamlandı (0-100) — arayüzdeki ilerleme çubuğu.</summary>
    public double ProgressPercent { get; set; }

    /// <summary>Bu oturuma bağlı katılım satırları (rehberinki dahil).</summary>
    public ICollection<TourSessionParticipant> Participants { get; set; }
        = new List<TourSessionParticipant>();

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public DateTime? ModifiedDate { get; set; }
}

/// <summary>
/// Bir kullanıcının bir oturumdaki KATILIMI ve ROLÜ.
///
/// Anahtar bileşik (tour_session_id + user_id): aynı kişinin aynı oturuma iki
/// kez yazılması böylece veritabanı seviyesinde imkânsız — user_roles ve
/// role_permissions bağlantı tablolarındaki desenin aynısı.
/// </summary>
public class TourSessionParticipant
{
    public int TourSessionId { get; set; }

    public TourSession? TourSession { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Bu OTURUMDAKİ rol. Guide yönetir, Participant izler. Rol oturum başına
    /// tutuluyor: aynı kişi başka bir turda katılımcı olabilir.
    /// </summary>
    public TourRole Role { get; set; } = TourRole.Participant;

    public DateTime JoinedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ayrılma anı; hâlâ bağlıysa null.
    ///
    /// Satır SİLİNMİYOR: "kimler katıldı" bilgisi tur bittikten sonra da anlamlı
    /// (yoklama). Ayrıca geri dönen kişi aynı satıra dönüyor, ikinci bir satır
    /// açılmıyor.
    /// </summary>
    public DateTime? LeftUtc { get; set; }
}
