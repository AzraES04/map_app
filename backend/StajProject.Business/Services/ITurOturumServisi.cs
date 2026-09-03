using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// TUR ŞABLONU ve CANLI OTURUM yönetimi.
///
/// ---- AKIŞ ----
///   1. Rehber öneriyi KAYDEDER          → tur şablonu (kalıcı)
///   2. Şablondan OTURUM AÇAR            → katılım kodu üretilir
///   3. Kodu/bağlantıyı gruba GÖNDERİR
///   4. Katılımcılar kodla KATILIR       → aynı oturumu izlerler
///   5. Rehber durakları İLERLETİR       → herkesin ekranı güncellenir
///
/// ---- NEDEN ÖNCE KAYDETMEK GEREKİYOR? ----
/// Öneri bir cevaptır, kayıt değil (id = 0). Paylaşılabilir bir bağlantı ise
/// SUNUCUDA duran bir şeye işaret etmek zorunda: katılımcı bağlantıyı
/// açtığında turun durakları veritabanından okunuyor. Öneriyi doğrudan
/// paylaşsaydık, bağlantı yalnızca onu üreten tarayıcıda anlamlı olurdu.
/// </summary>
public interface ITurOturumServisi
{
    // ---------------- Tur şablonu ----------------

    /// <summary>
    /// Rota önerisini kalıcı tur şablonuna çevirir. Rehber = isteği yapan.
    /// </summary>
    Task<TourDto> TuruKaydetAsync(TurKaydetDto istek);

    /// <summary>Kayıtlı turlar (durakları sıralı).</summary>
    Task<List<TourDto>> TurlariGetirAsync();

    /// <summary>Tek tur; yoksa null.</summary>
    Task<TourDto?> TurGetirAsync(int id);

    /// <summary>Turu siler (soft delete). Yalnızca turu tanımlayan rehber.</summary>
    Task<bool> TurSilAsync(int id);

    // ---------------- Canlı oturum ----------------

    /// <summary>
    /// Turdan canlı oturum açar ve KATILIM KODU üretir.
    ///
    /// Aynı turun açık bir oturumu varsa yenisi açılmıyor; var olan dönüyor.
    /// İki oturum açık olsaydı katılımcının hangisine katıldığı koda bakmadan
    /// anlaşılmaz, rehber de iki gruba birden yayın yaptığını sanırdı.
    /// </summary>
    Task<TourSessionDto> OturumAcAsync(TourSessionCreateDto istek);

    /// <summary>
    /// Katılım koduyla oturuma katılır (rol: Participant).
    ///
    /// Kod bulunamazsa <see cref="Validation.IsKuraliException"/> — kapanmış
    /// oturumların kodu yeniden kullanılabildiği için "kod yanlış" ile "tur
    /// bitmiş" aynı cevabı veriyor: kullanıcı için ikisi de "bu bağlantı
    /// artık çalışmıyor" demek.
    /// </summary>
    Task<TourSessionDto> OturumaKatilAsync(TourSessionJoinDto istek);

    /// <summary>
    /// MİSAFİR GÖRÜNÜMÜ — kimlik doğrulaması olmadan, yalnızca katılım koduyla.
    ///
    /// Katılım SATIRI YAZMAZ: misafirin bir kullanıcı kaydı yok, dolayısıyla
    /// "kim katıldı" listesine giremez. Bu bilinçli — kullanıcı isteği
    /// "kayıt yapmadan giriş yapmadan sadece kod ile misafir olarak görsün,
    /// onun dışında herhangi bir etkisi olmasın" idi.
    ///
    /// Kapanmış (Completed/Cancelled) oturumda null döner: paylaşılan bağlantı
    /// tur bitince ölmeli, yoksa eski bir bağlantı aylar sonra da turun
    /// programını göstermeye devam ederdi.
    /// </summary>
    Task<MisafirTurDto?> MisafirGorunumuAsync(string katilimKodu);

    /// <summary>
    /// Rehberin CANLI KONUMUNU yazar.
    ///
    /// Yalnızca o oturumun rehberi çağırabilir: konum, turun konumu olarak
    /// yayınlanıyor ve başka birinin yazması grubu yanlış yere baktırırdı.
    ///
    /// Durum güncellemesinden (OturumGuncelleAsync) AYRI: konum saniyeler
    /// aralığıyla geliyor ve durum geçişi kontrollerinden geçmesi hem
    /// gereksiz hem de yanlış olurdu.
    /// </summary>
    Task<TourSessionDto> KonumBildirAsync(int oturumId, TurKonumDto konum);

    /// <summary>
    /// Konum yayınını durdurur (kayıtlı konumu siler).
    ///
    /// Yayını kapatmak KONUMU DA SİLİYOR: yalnızca "yayın kapalı" bayrağı
    /// tutsaydık son konum veritabanında kalırdı ve rehber onu sildiğini
    /// sanırdı.
    /// </summary>
    Task<TourSessionDto> KonumYayininiDurdurAsync(int oturumId);

    /// <summary>
    /// Çağıranın O OTURUMUN REHBERİ olduğunu doğrular; değilse fırlatır.
    ///
    /// Yoklama uçları bunu kullanıyor: yoklamanın kendisi bellekte duran
    /// ayrı bir servis (IYoklamaServisi) ve orada veritabanı erişimi yok,
    /// dolayısıyla "bu oturumun rehberi kim?" sorusunu soramaz.
    /// </summary>
    Task RehberDogrulaAsync(int oturumId);

    /// <summary>
    /// Katılım kodundan oturum id'si; kod geçersizse ya da tur kapandıysa null.
    ///
    /// Misafir uçları için: misafir oturum id'sini bilmiyor ve bilmemeli
    /// (bkz. MisafirTurDto), ama yoklama cevabı bir oturuma yazılmak zorunda.
    /// </summary>
    Task<int?> MisafirOturumIdAsync(string katilimKodu);

    /// <summary>Oturumun anlık durumu; yoksa null.</summary>
    Task<TourSessionDto?> OturumGetirAsync(int id);

    /// <summary>İsteği yapan kullanıcının katıldığı AÇIK oturumlar.</summary>
    Task<List<TourSessionDto>> OturumlarimAsync();

    /// <summary>
    /// Oturumu ilerletir: durum değişikliği ve/veya bulunulan durak.
    /// YALNIZCA REHBER çağırabilir; katılımcı denerse iş kuralı hatası.
    /// </summary>
    Task<TourSessionDto> OturumGuncelleAsync(int id, TourSessionUpdateDto istek);

    /// <summary>Oturumdan ayrılır (katılım satırı silinmez, damgalanır).</summary>
    Task<bool> OturumdanAyrilAsync(int id);
}
