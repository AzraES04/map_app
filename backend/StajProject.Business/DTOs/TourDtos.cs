using System.ComponentModel.DataAnnotations;

namespace StajProject.Business.DTOs;

// ============================================================================
//  TUR MODÜLÜ — DTO'lar
//
//  Entity'ler dışarı çıkmıyor: Tour.Waypoints ↔ Waypoint.Tour ve
//  TourSession.Participants ↔ ...TourSession karşılıklı navigasyonları
//  doğrudan serileştirilse döngüsel JSON üretirdi. DTO tarafında ilişki TEK
//  YÖNLÜ taşınıyor — tur duraklarını biliyor, durak yalnızca turunun id'sini
//  (UlasimDtos'taki desenin aynısı).
//
//  Geometri WKT metni olarak taşınıyor (GeoJSON değil): projedeki bütün
//  uçlarda geometri böyle gidip geliyor, istemcide tek bir ayrıştırıcı var.
//
//  ENUM'LAR METİN OLARAK GİDİYOR ("Guide", "Museum"): istemcinin sayı
//  tablosunu kendi içinde tekrarlaması, iki tarafın sırası kaydığında sessizce
//  yanlış rol göstermesi demek olurdu.
// ============================================================================

// ---------------------------------------------------------------------------
//  Okuma DTO'ları
// ---------------------------------------------------------------------------

/// <summary>İstemciye giden tur durağı.</summary>
public class WaypointDto
{
    public int Id { get; set; }

    public int TourId { get; set; }

    /// <summary>Tur içindeki sıra (1'den başlar).</summary>
    public int Order { get; set; }

    /// <summary>Mekanın dış sağlayıcıdaki kimliği — "osm:node/123".</summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>Sistemdeki POI karşılığı; yoksa null.</summary>
    public int? PoiId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Mekan tipi — "Museum", "Cafe"... Simge bu alandan seçiliyor.</summary>
    public string VenueType { get; set; } = "Other";

    /// <summary>Planlanan kalış süresi (dakika).</summary>
    public int DwellMinutes { get; set; }

    /// <summary>Konum, WKT (EPSG:4326): "POINT (32.85 39.93)".</summary>
    public string Wkt { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>İstemciye giden tur şablonu — durakları sıralı hâlde içinde.</summary>
public class TourDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Haritadaki rengi (#rrggbb).</summary>
    public string Color { get; set; } = string.Empty;

    public DateTime? ScheduledStartUtc { get; set; }

    public int? GuideUserId { get; set; }

    /// <summary>Rehberin kullanıcı adı; hesap silinmişse null.</summary>
    public string? GuideUserName { get; set; }

    /// <summary>
    /// Duraklar SIRAYLA. Ayrı bir uçtan istetmiyoruz: tur listesi zaten
    /// duraklarıyla anlamlı (rota onlardan çiziliyor, yönetim ekranı
    /// sürükle-bırak listesini onlardan kuruyor) ve iki istek arasında sıranın
    /// değişmesi ihtimalini de ortadan kaldırıyor — GuzergahDto ile aynı karar.
    /// </summary>
    public List<WaypointDto> Waypoints { get; set; } = new();

    public int WaypointCount => Waypoints.Count;

    // ---------- Rota ----------

    /// <summary>Yollara oturmuş rota — WKT LINESTRING. Hesaplanmadıysa null.</summary>
    public string? RouteWkt { get; set; }

    public double? RouteDistanceMeters { get; set; }

    /// <summary>Duraklar arasındaki yol süresi (saniye); kalış süreleri hariç.</summary>
    public double? RouteDurationSeconds { get; set; }

    /// <summary>
    /// Rota, duraklara göre GÜNCEL mi? (imza karşılaştırmasının sonucu)
    ///
    /// Sunucu hesaplıyor, imzanın kendisini dışarı vermiyoruz: istemcinin
    /// yapacağı tek şey "güncel değil" uyarısını göstermek — imzayı taşımak
    /// aynı hesabı iki yerde yapmak olurdu.
    /// </summary>
    public bool RouteUpToDate { get; set; }

    /// <summary>Durakların kalış süreleri toplamı (dakika).</summary>
    public int TotalDwellMinutes => Waypoints.Sum(w => w.DwellMinutes);

    /// <summary>
    /// Turun tahmini toplam süresi (dakika) = yol süresi + kalış süreleri.
    /// Rota henüz hesaplanmadıysa yalnızca kalış sürelerini döner.
    /// </summary>
    public int EstimatedTotalMinutes =>
        TotalDwellMinutes + (int)Math.Round((RouteDurationSeconds ?? 0) / 60.0);

    /// <summary>
    /// Şu an CANLI bir oturumu varsa onun id'si; yoksa null.
    ///
    /// Liste ekranındaki "Katıl" düğmesi bu alana bakıyor. Oturumun tamamını
    /// buraya gömmedik: liste ekranı canlı ayrıntıyı göstermiyor ve her tur
    /// için oturum nesnesi taşımak listeyi gereksiz büyütürdü.
    /// </summary>
    public int? LiveSessionId { get; set; }

    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Oturumdaki tek bir katılımcı.</summary>
public class TourParticipantDto
{
    public int UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    /// <summary>"Guide" ya da "Participant".</summary>
    public string Role { get; set; } = string.Empty;

    public DateTime JoinedUtc { get; set; }

    /// <summary>Ayrıldıysa zamanı; hâlâ bağlıysa null.</summary>
    public DateTime? LeftUtc { get; set; }
}

/// <summary>
/// Canlı tur oturumunun ANLIK DURUMU.
///
/// TEK BİÇİM, İKİ YOL: hem SignalR yayınında hem REST cevaplarında bu DTO
/// gidiyor — <see cref="SimulasyonDurumDto"/> ile aynı gerekçe: haritaya konan
/// grup, verinin hangi kanaldan geldiğini bilmek zorunda değil.
/// </summary>
public class TourSessionDto
{
    public int Id { get; set; }

    public int TourId { get; set; }

    public string TourName { get; set; } = string.Empty;

    /// <summary>Turun rengi — grup ikonu turun rengiyle çiziliyor.</summary>
    public string Color { get; set; } = string.Empty;

    /// <summary>"Planned" | "Live" | "Paused" | "Completed" | "Cancelled".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Katılım kodu — YALNIZCA rehbere doldurulur, katılımcıya null gider.
    ///
    /// Kodun tamamı herkese gitseydi, tura katılan biri kodu başkalarına
    /// dağıtabilirdi; kimi çağıracağına rehber karar veriyor.
    /// </summary>
    public string? JoinCode { get; set; }

    public int GuideUserId { get; set; }

    public string GuideUserName { get; set; } = string.Empty;

    public DateTime? StartedUtc { get; set; }

    public DateTime? EndedUtc { get; set; }

    /// <summary>Grubun bulunduğu durak; henüz varılmadıysa null.</summary>
    public int? CurrentWaypointId { get; set; }

    public int? CurrentWaypointOrder { get; set; }

    public string? CurrentWaypointName { get; set; }

    public DateTime? CurrentWaypointArrivedUtc { get; set; }

    /// <summary>Bir SONRAKİ durak (son duraktaysa null) — arayüzde "sırada" satırı.</summary>
    public int? NextWaypointOrder { get; set; }

    public string? NextWaypointName { get; set; }

    /// <summary>Turun yüzde kaçı tamamlandı (0-100).</summary>
    public double ProgressPercent { get; set; }

    /// <summary>Rehberin son bilinen konumu; hiç yayın gelmediyse null.</summary>
    public double? LastLon { get; set; }

    public double? LastLat { get; set; }

    public DateTime? LastPositionUtc { get; set; }

    /// <summary>Şu an bağlı olan katılımcı sayısı (ayrılanlar sayılmaz).</summary>
    public int ParticipantCount { get; set; }

    /// <summary>
    /// İSTEĞİ YAPAN kullanıcının bu oturumdaki rolü: "Guide", "Participant"
    /// ya da katılmadıysa null.
    ///
    /// Arayüzün "yönetim düğmelerini göstereyim mi?" sorusunun cevabı. İstemci
    /// bunu GuideUserId ile kendi id'sini karşılaştırarak da bulabilirdi ama o
    /// hesap her ekranda tekrarlanırdı; üstelik rehber yetkisi ileride
    /// devredilirse tek bir kolonun karşılaştırması yanlış cevap verirdi.
    ///
    /// DİKKAT: bu alan GÖRÜNÜRLÜK içindir. Asıl kontrol sunucuda.
    /// </summary>
    public string? MyRole { get; set; }

    /// <summary>
    /// Katılımcı listesi — yalnızca oturum AYRINTISI istendiğinde doldurulur,
    /// canlı konum yayınında boş gider (her tikte bütün listeyi göndermek,
    /// değişmeyen veriyi saniyede bir tekrarlamak olurdu).
    /// </summary>
    public List<TourParticipantDto> Participants { get; set; } = new();
}

// ---------------------------------------------------------------------------
//  Yazma DTO'ları
// ---------------------------------------------------------------------------

/// <summary>Tur ekleme/güncelleme isteği.</summary>
public class TourSaveDto
{
    [Required(ErrorMessage = "Tur adı zorunludur.")]
    [MaxLength(200, ErrorMessage = "Tur adı en fazla 200 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000, ErrorMessage = "Açıklama en fazla 1000 karakter olabilir.")]
    public string? Description { get; set; }

    /// <summary>
    /// Renk "#rrggbb" biçiminde. Biçim kontrolü hem burada hem serviste:
    /// öznitelik istemciye alan bazlı hata veriyor, servisteki kontrol ise
    /// servisi doğrudan çağıran testlerde de geçerli (GuzergahSaveDto ile aynı).
    /// </summary>
    [Required(ErrorMessage = "Tur rengi zorunludur.")]
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Renk #rrggbb biçiminde olmalıdır.")]
    public string Color { get; set; } = "#7b5cd6";

    public DateTime? ScheduledStartUtc { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Yeni durak isteği (haritadaki "Durak Ekle" aracı).</summary>
public class WaypointCreateDto
{
    /// <summary>Hangi tura? ZORUNLU — 1-N ilişkisinin karşılığı.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Tur seçilmelidir.")]
    public int TourId { get; set; }

    [Required(ErrorMessage = "Durak adı zorunludur.")]
    [MaxLength(200, ErrorMessage = "Durak adı en fazla 200 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mekanın dış sağlayıcıdaki kimliği. Arama sonucundan geliyor; kullanıcı
    /// haritaya elle nokta koyduysa servis "manual:{guid}" üretiyor — kolon
    /// zorunlu, çünkü boş bırakılan bir kimlik "aynı mekan mı?" sorusunu
    /// cevapsız bırakır ve mükerrer duraklara kapı açar.
    /// </summary>
    [Required(ErrorMessage = "Mekan kimliği (PlaceID) zorunludur.")]
    [MaxLength(200, ErrorMessage = "Mekan kimliği en fazla 200 karakter olabilir.")]
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>Sistemdeki POI karşılığı — arama POI'lerden yapıldıysa dolu.</summary>
    public int? PoiId { get; set; }

    /// <summary>"Museum", "Cafe"... Tanınmayan değer <c>Other</c> sayılır.</summary>
    public string VenueType { get; set; } = "Other";

    /// <summary>
    /// Kalış süresi (dakika). Üst sınır bir günün altında: 24 saati aşan bir
    /// "durak", tur değil ayrı bir gündür.
    /// </summary>
    [Range(0, 1440, ErrorMessage = "Kalış süresi 0-1440 dakika arasında olmalıdır.")]
    public int DwellMinutes { get; set; }

    /// <summary>Konum — "POINT (32.85 39.93)". Tipi POINT olmak zorunda.</summary>
    [Required(ErrorMessage = "Durak konumu (WKT) zorunludur.")]
    public string Wkt { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Not en fazla 500 karakter olabilir.")]
    public string? Note { get; set; }

    /// <summary>
    /// Tur içindeki sıra. Boş bırakılırsa durak SONA ekleniyor — haritadan
    /// durak eklerken en doğal davranış bu (DurakCreateDto ile aynı karar).
    /// </summary>
    public int? Order { get; set; }
}

/// <summary>Durak güncelleme isteği.</summary>
public class WaypointUpdateDto
{
    [Required(ErrorMessage = "Durak adı zorunludur.")]
    [MaxLength(200, ErrorMessage = "Durak adı en fazla 200 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    public string VenueType { get; set; } = "Other";

    [Range(0, 1440, ErrorMessage = "Kalış süresi 0-1440 dakika arasında olmalıdır.")]
    public int DwellMinutes { get; set; }

    [MaxLength(500, ErrorMessage = "Not en fazla 500 karakter olabilir.")]
    public string? Note { get; set; }

    /// <summary>
    /// Boş bırakılırsa KONUM DEĞİŞMEZ (DurakUpdateDto ve PoiUpdateDto ile
    /// aynı desen). PlaceId burada YOK: mekanın kimliği değişmez — başka bir
    /// mekan demek, başka bir durak demektir.
    /// </summary>
    public string? Wkt { get; set; }
}

/// <summary>
/// Sürükle-bırak sonucunun sunucuya bildirimi.
///
/// Tek tek "şu durağın sırası 3 oldu" değil TOPLU LİSTE: bir durağı taşımak
/// aradaki bütün durakların sırasını kaydırıyor, tek tek gönderilseydi yarısı
/// yazıldığında liste tutarsız kalırdı (DurakSiralamaDto ile aynı gerekçe).
/// </summary>
public class WaypointReorderDto
{
    /// <summary>Durak id'leri, YENİ sırasıyla. Turun tüm duraklarını içermeli.</summary>
    [Required(ErrorMessage = "Sıralama listesi zorunludur.")]
    [MinLength(1, ErrorMessage = "Sıralama listesi boş olamaz.")]
    public List<int> WaypointIds { get; set; } = new();
}

/// <summary>Oturum açma isteği (rehber "Turu Başlat" dediğinde).</summary>
public class TourSessionCreateDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Tur seçilmelidir.")]
    public int TourId { get; set; }

    /// <summary>
    /// Oturum hemen yayına alınsın mı? false ise <c>Planned</c> durumunda
    /// açılıyor: rehber katılım kodunu önceden dağıtıp turu sonra başlatabilsin.
    /// </summary>
    public bool StartNow { get; set; } = true;
}

/// <summary>Katılma isteği — katılımcı kodu giriyor.</summary>
public class TourSessionJoinDto
{
    [Required(ErrorMessage = "Katılım kodu zorunludur.")]
    [MaxLength(12, ErrorMessage = "Katılım kodu en fazla 12 karakter olabilir.")]
    public string JoinCode { get; set; } = string.Empty;
}

/// <summary>
/// Rehberin konum bildirimi.
///
/// Rol İSTEMCİDEN GELMİYOR: sunucu isteği yapanın o oturumdaki rolüne bakıyor
/// ve Guide değilse reddediyor. Rolü gövdeye koysaydık, katılımcı kendini
/// Guide ilan edip grubu yanlış yere sürükleyebilirdi.
/// </summary>
public class TourPositionDto
{
    [Range(-180, 180, ErrorMessage = "Boylam -180 ile 180 arasında olmalıdır.")]
    public double Lon { get; set; }

    [Range(-90, 90, ErrorMessage = "Enlem -90 ile 90 arasında olmalıdır.")]
    public double Lat { get; set; }
}

/// <summary>
/// Oturumun durumunu değiştirme isteği (duraklat / sürdür / bitir / iptal)
/// ve durak ilerletme.
///
/// Tek uç, tek DTO: dört ayrı uç açsaydık her biri aynı yetki ve aynı durum
/// geçişi kontrolünü tekrarlardı.
/// </summary>
public class TourSessionUpdateDto
{
    /// <summary>Hedef durum: "Live" | "Paused" | "Completed" | "Cancelled".</summary>
    [Required(ErrorMessage = "Durum zorunludur.")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Grubun geçtiği durak. Verilirse oturum o durağa ilerliyor ve varış anı
    /// damgalanıyor; null ise yalnızca durum değişiyor.
    /// </summary>
    public int? CurrentWaypointId { get; set; }
}

// ============================================================================
//  MİSAFİR GÖRÜNÜMÜ — hesabı olmayan katılımcı için
// ============================================================================

/// <summary>
/// Paylaşılan bağlantıyı açan MİSAFİRİN gördüğü tur.
///
/// ---- NEDEN AYRI BİR DTO (TourSessionDto DEĞİL)? ----
/// Kullanıcı geri bildirimi: "kayıt yapmadan giriş yapmadan sadece verilen
/// kod kullanılarak misafir olarak görünsün ve ilgili tur bilgilerini
/// görebilsin". Önceki akış bağlantıyı açanı önce giriş ekranına, sonra da
/// haritanın tamamına götürüyordu — turu görmek için hesap açmak gerekiyordu.
///
/// Bu DTO, kimlik doğrulaması OLMADAN dışarı çıkan tek tur biçimi. O yüzden
/// içeriği ELEYEREK kuruldu — TourSessionDto'yu anonim uca vermek, orada
/// duran her alanı da herkese açmak olurdu:
///
///   ÇIKARILDI  katılımcı listesi   → gruptaki kişilerin kullanıcı adları;
///                                    kodu ele geçiren biri grubu görmemeli.
///   ÇIKARILDI  JoinCode            → misafir kodu zaten biliyor; cevapta
///                                    tekrar etmek onu paylaşmayı kolaylaştırır.
///   ÇIKARILDI  kullanıcı/oturum id → misafirin adresleyebileceği başka bir
///                                    kaynak kalmasın.
///   KALDI      rehberin ADI        → "kimin turundayım?" sorusunun cevabı;
///                                    gruba güven veren tek alan.
///
/// ---- KOD BİR PAROLA MI? ----
/// Kod 28 harflik alfabeden 6 karakter (~481 milyon olasılık) ve uç IP başına
/// dakikada 60 istekle sınırlı. Kaba kuvvetle bulmak yıllar sürer. Yine de
/// bir parola gibi davranmıyoruz: bu yüzden cevapta kişisel veri yok, yalnızca
/// turun kendisi var.
/// </summary>
public class MisafirTurDto
{
    /// <summary>
    /// Oturumun id'si.
    ///
    /// Eleme kuralına AYKIRI görünüyor ama değil: misafirin yoklama
    /// cevabını yazabilmesi için oturumu adresleyebilmesi gerekiyor ve
    /// alternatifi her cevapta kodu tekrar çözmekti. Tek başına bir işe
    /// yaramıyor — id'yle ulaşılan bütün uçlar kimlik istiyor.
    /// </summary>
    public int OturumId { get; set; }

    public string TourName { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;

    /// <summary>"Planned" | "Live" | "Paused" — kapanmış oturum hiç dönmüyor.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Rehberin görünen adı — "kimin turundayım?".</summary>
    public string GuideUserName { get; set; } = string.Empty;

    /// <summary>
    /// Rehberin TELEFON NUMARASI; girmemişse ya da tur canlı değilse null.
    ///
    /// ---- ELEME KURALINA İSTİSNA MI? ----
    /// Evet, ve bilerek. Yukarıdaki liste "misafirin adresleyebileceği bir
    /// şey kalmasın" diyor; bu alan tam tersine misafirin ULAŞABİLMESİ için
    /// var. Gerekçe: gruptan ayrılan kişinin ilk ihtiyacı rehberi aramak,
    /// uygulamada ise mesajlaşma yok.
    ///
    /// Riski üç yerden kısıyoruz:
    ///   • İSTEĞE BAĞLI — rehber numarasını hiç girmezse alan boş kalır.
    ///   • YALNIZCA CANLI turda — planlanmış ya da duraklatılmış oturumda
    ///     null; kodu eline geçiren biri "ileride" arayamaz.
    ///   • Yalnızca REHBERİN numarası — katılımcılarınki asla.
    /// </summary>
    public string? GuidePhone { get; set; }

    public DateTime? StartedUtc { get; set; }

    /// <summary>Grubun ŞU AN bulunduğu durak; henüz varılmadıysa null.</summary>
    public int? CurrentWaypointOrder { get; set; }

    public string? CurrentWaypointName { get; set; }

    public DateTime? CurrentWaypointArrivedUtc { get; set; }

    public int? NextWaypointOrder { get; set; }

    public string? NextWaypointName { get; set; }

    public double ProgressPercent { get; set; }

    /// <summary>Yollara oturmuş rota — misafir de haritada görsün.</summary>
    public string? RouteWkt { get; set; }

    public double? RouteDistanceMeters { get; set; }

    public double? RouteDurationSeconds { get; set; }

    /// <summary>Sıralı duraklar — programın tamamı misafire de açık.</summary>
    public List<MisafirDurakDto> Waypoints { get; set; } = new();

    // ---------- Rehberin canlı konumu ----------

    /// <summary>
    /// Rehberin son bildirdiği konum; yayın kapalıysa ya da hiç
    /// başlamadıysa null.
    ///
    /// Misafire açılan TEK kişisel veri bu ve gerekçesi var: turu takip
    /// etmenin kendisi bu. Rehber yayını istediği an durduruyor ve konum
    /// oturumla birlikte kapanıyor — kalıcı bir iz bırakmıyor.
    /// </summary>
    public double? GuideLon { get; set; }

    public double? GuideLat { get; set; }

    public DateTime? GuidePositionUtc { get; set; }

    /// <summary>
    /// AÇIK YOKLAMANIN SORUSU; yoklama yoksa null.
    ///
    /// Yalnızca soru dönüyor, SAYILAR DEĞİL: misafir kendi cevabını verir,
    /// grubun dökümü rehberin bilgisi. Sayıları da gönderseydik yoklama,
    /// gruptaki herkesin birbirini saydığı bir panoya dönerdi.
    /// </summary>
    public string? AcikYoklamaSorusu { get; set; }
}

/// <summary>
/// Rehberin CANLI KONUMU — gruba yayınlanan tek konum.
///
/// ---- NEDEN YALNIZCA REHBERİN KONUMU? ----
/// "Herkesin konumu" istenebilirdi ama o başka bir üründür: her katılımcıdan
/// sürekli GPS toplamak açık rıza, saklama süresi ve silme hakkı gerektirir.
/// Rehberin konumu ise turun KENDİSİNİN konumu — grup zaten onu takip
/// ediyor ve yayını başlatan da o.
///
/// Kesinlik (<see cref="DogrulukMetre"/>) taşınıyor: şehir içinde GPS 5-50
/// metre arasında değişiyor ve arayüz bunu bir daire olarak gösterebilsin.
/// </summary>
public class TurKonumDto
{
    [Range(-90, 90, ErrorMessage = "Enlem -90 ile 90 arasında olmalıdır.")]
    public double Lat { get; set; }

    [Range(-180, 180, ErrorMessage = "Boylam -180 ile 180 arasında olmalıdır.")]
    public double Lon { get; set; }

    /// <summary>Cihazın bildirdiği yatay doğruluk (metre); bilinmiyorsa null.</summary>
    public double? DogrulukMetre { get; set; }
}

/// <summary>
/// Misafirin gördüğü tek durak.
///
/// <see cref="WaypointDto"/>'nun kırpılmış hâli: id'ler ve PlaceId yok.
/// Misafirin bir durağı adresleyebilmesi gerekmiyor; adı, sırası, konumu ve
/// ne kadar kalınacağı yetiyor.
/// </summary>
public class MisafirDurakDto
{
    public int Order { get; set; }

    public string Name { get; set; } = string.Empty;

    public string VenueType { get; set; } = string.Empty;

    public int DwellMinutes { get; set; }

    public string Wkt { get; set; } = string.Empty;

    public string? Note { get; set; }
}

// ============================================================================
//  YOKLAMA — "şu an kimler burada?"
// ============================================================================

/// <summary>
/// Açık yoklamanın anlık durumu.
///
/// Sayılar ARAYÜZ İÇİN hazır: "15 kişinin 13'ü buradayım" cümlesini kurmak
/// için gereken her şey burada ve istemci ayrıca hesap yapmıyor. Aynı hesabın
/// iki yerde yapılması, iki yerde farklı yuvarlanması demektir.
/// </summary>
public class YoklamaDurumuDto
{
    public int OturumId { get; set; }

    /// <summary>Rehberin sorduğu soru — misafirin telefonunda başlık olarak çıkıyor.</summary>
    public string Soru { get; set; } = string.Empty;

    /// <summary>
    /// Rehberin bildirdiği kişi sayısı.
    ///
    /// Misafirler kayıtlı olmadığı için sunucu toplamı bilemiyor; "15 kişinin
    /// 13'ü" ifadesindeki 15 bu. Rehber sahada gruba bakıp giriyor.
    /// </summary>
    public int GrupBoyu { get; set; }

    public DateTime BaslangicUtc { get; set; }

    public int Buradayim { get; set; }

    public int Degilim { get; set; }

    /// <summary>
    /// ACİL diyenler — ayrı sayılıyor ve arayüzde kırmızı çıkıyor.
    ///
    /// "Değilim" ile aynı kefeye koysaydık, geride kalan biri ile yardım
    /// isteyen biri aynı sayıda erirdi; oysa rehberin ilk bakacağı sayı bu.
    /// </summary>
    public int Acil { get; set; }

    /// <summary>Henüz cevaplamayanlar (grup boyu − cevap sayısı).</summary>
    public int Cevapsiz { get; set; }

    /// <summary>
    /// Cevaplayan misafirin KENDİ cevabı; rehberin okumasında null.
    ///
    /// Misafirin ekranı "cevabınız alındı" diyebilsin diye var. Başkalarının
    /// cevabı DÖNMÜYOR: misafir yalnızca kendi durumunu görüyor, grubun
    /// dökümü rehberin bilgisi.
    /// </summary>
    public string? BenimCevabim { get; set; }

    /// <summary>
    /// Cevaplayanların DÖKÜMÜ — yalnızca REHBERE dönüyor.
    ///
    /// Misafirin cevabı BenimCevabim'de zaten var; ona başkalarının
    /// adını/telefonunu göstermek gizlilik ihlali olurdu. Controller bu
    /// alanı yalnızca rehber isteğinde dolduruyor
    /// (bkz. TurController.YoklamaBaslat / YoklamaDurumu), misafir
    /// isteğinde her zaman boş liste.
    ///
    /// Acil cevaplar EN ÜSTTE: rehberin ilk bakması gereken satırlar.
    /// </summary>
    public List<YoklamaKisiDto> Kisiler { get; set; } = new();
}

/// <summary>Yoklamaya cevap veren bir kişi — yalnızca rehberin gördüğü ayrıntı.</summary>
public class YoklamaKisiDto
{
    /// <summary>İsteğe bağlı — misafir boş bırakmış olabilir.</summary>
    public string? Ad { get; set; }

    /// <summary>"Buradayim" | "Degilim" | "Acil".</summary>
    public string Cevap { get; set; } = string.Empty;

    /// <summary>
    /// İsteğe bağlı — özellikle Acil cevapta anlamlı: rehber bu numarayı
    /// tıklayıp doğrudan arayabilir (tel: bağlantısı).
    /// </summary>
    public string? Telefon { get; set; }
}

/// <summary>Yoklama başlatma isteği.</summary>
public class YoklamaBaslatDto
{
    public string Soru { get; set; } = string.Empty;

    [Range(1, 500, ErrorMessage = "Grup sayısı 1 ile 500 arasında olmalıdır.")]
    public int GrupBoyu { get; set; } = 10;
}

/// <summary>Misafirin cevabı.</summary>
public class YoklamaCevapDto
{
    /// <summary>
    /// Misafirin tarayıcısında üretilen rastgele anahtar.
    ///
    /// KİMLİK DEĞİL: kim olduğunu söylemiyor, yalnızca "aynı tarayıcı"
    /// diyor. Amacı fikrini değiştiren misafirin sayıyı iki kez
    /// artırmasını önlemek.
    /// </summary>
    [Required(ErrorMessage = "Misafir anahtarı zorunludur.")]
    [StringLength(64, MinimumLength = 8)]
    public string MisafirAnahtari { get; set; } = string.Empty;

    /// <summary>"Buradayim" | "Degilim" | "Acil".</summary>
    [Required(ErrorMessage = "Cevap zorunludur.")]
    public string Cevap { get; set; } = string.Empty;

    /// <summary>İsteğe bağlı ad — rehberin listesinde görünür.</summary>
    [StringLength(60)]
    public string? Ad { get; set; }

    /// <summary>
    /// İsteğe bağlı telefon — özellikle Acil cevapta anlamlı: rehberin
    /// geri arayabilmesi için.
    /// </summary>
    [StringLength(30)]
    public string? Telefon { get; set; }
}
