using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// Tur şablonu ve canlı oturum veri erişimi.
///
/// ---- NEDEN TEK DEPO, İKİ AGREGAT DEĞİL? ----
/// Tur ve oturum ayrı tablolar ama HİÇBİR ZAMAN ayrı okunmuyorlar: bir
/// oturumun anlamı bağlı olduğu turun duraklarıyla ortaya çıkıyor (kaçıncı
/// duraktayız, kaç durak kaldı). İki depoya bölseydik servis her oturum
/// isteğinde ikisini ayrı ayrı çağırıp elle birleştirirdi — üstelik aynı
/// <c>DbContext</c> üzerinden. Ulaşım modülünde güzergah + durak için
/// verilen kararın aynısı.
/// </summary>
public interface ITurRepository
{
    // ---------------- Tur şablonu ----------------

    /// <summary>Turlar, DURAKLARI SIRALI hâlde.</summary>
    Task<List<Tour>> TurlariGetirAsync(int? kullaniciId = null);

    /// <summary>Tek tur (durakları sıralı); yoksa null.</summary>
    Task<Tour?> TurGetirAsync(int id);

    /// <summary>
    /// Turu duraklarıyla birlikte TEK işlemde ekler.
    ///
    /// Durakları ayrı ayrı eklemek, yarıda kalan bir kayıtta duraksız bir tur
    /// bırakırdı; EF ilişkili nesneleri aynı SaveChanges'te yazıyor.
    /// </summary>
    Task<Tour> TurEkleAsync(Tour tur);

    /// <summary>Fiziksel silme değil; is_deleted işaretlenir.</summary>
    Task<bool> TurSilAsync(int id);

    // ---------------- Canlı oturum ----------------

    /// <summary>
    /// Oturum; turu, durakları ve katılımcılarıyla. Yoksa null.
    /// </summary>
    Task<TourSession?> OturumGetirAsync(int id);

    /// <summary>
    /// KATILIM KODUNA göre oturum. Yalnızca KAPANMAMIŞ oturumlar aranır:
    /// biten bir turun kodu yeniden kullanılabildiği için (kısmi benzersiz
    /// index) kapanmışları da arasaydık eski bir tura katılmak mümkün olurdu.
    /// </summary>
    Task<TourSession?> OturumKodlaGetirAsync(string katilimKodu);

    /// <summary>Bir turun AÇIK oturumu var mı? (Planned / Live / Paused)</summary>
    Task<TourSession?> AcikOturumAsync(int turId);

    /// <summary>Kullanıcının katıldığı, hâlâ açık olan oturumlar.</summary>
    Task<List<TourSession>> KullanicininOturumlariAsync(int kullaniciId);

    Task<TourSession> OturumEkleAsync(TourSession oturum);

    /// <summary>
    /// Oturumu günceller (durum, bulunulan durak, konum, ilerleme).
    /// Kayıt yoksa null.
    /// </summary>
    Task<TourSession?> OturumGuncelleAsync(TourSession oturum);

    /// <summary>
    /// Katılım satırını ekler ya da var olanı geri açar.
    ///
    /// AYRILAN KİŞİ AYNI SATIRA DÖNÜYOR: yeni satır açsaydık bileşik anahtar
    /// (oturum + kullanıcı) ihlal edilirdi ve "kimler katıldı" listesi aynı
    /// kişiyi iki kez gösterirdi.
    /// </summary>
    Task<TourSessionParticipant> KatilimYazAsync(TourSessionParticipant katilim);

    /// <summary>Katılımcıyı ayrılmış işaretler (satır silinmez).</summary>
    Task<bool> AyrilAsync(int oturumId, int kullaniciId);

    /// <summary>Kodun AÇIK oturumlar arasında kullanımda olup olmadığı.</summary>
    Task<bool> KodKullanimdaAsync(string katilimKodu);
}
