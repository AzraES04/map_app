using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// Yenileme anahtarlarının veritabanı erişimi (Eksik 5).
///
/// Bu arayüzde bilerek "GetAll" yok: anahtar listesi hiçbir ekranda
/// gösterilmiyor ve gösterilmemeli. Depo yalnızca kimlik doğrulama akışının
/// ihtiyaç duyduğu dört işi yapıyor.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>Yeni anahtar satırı ekler.</summary>
    Task<RefreshToken> AddAsync(RefreshToken token);

    /// <summary>
    /// Özete göre anahtarı bulur — kullanıcısı ve ROLLERİYLE birlikte.
    ///
    /// İlişkiler yükleniyor çünkü yenileme sırasında yeni bir JWT üretmeden
    /// önce hesabın hâlâ uygun olduğunu (silinmemiş, aktif, onaylı) kontrol
    /// ediyoruz. Ayrı sorgu atmak, aynı satırı iki kez okumak olurdu.
    ///
    /// Süresi dolmuş ve iptal edilmiş anahtarlar da DÖNER: "geçersiz" ile
    /// "hiç yok" ayrımını çağıran taraf yapar. Yeniden kullanım tespiti tam
    /// da iptal edilmiş bir satırı bulabilmeye dayanıyor.
    /// </summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash);

    /// <summary>Değişiklikleri kaydeder (iptal, döndürme zinciri).</summary>
    Task SaveAsync();

    /// <summary>
    /// Bir kullanıcının AÇIK olan tüm anahtarlarını iptal eder ve kaç satır
    /// etkilendiğini döner.
    ///
    /// İki yerde çağrılıyor: yeniden kullanım tespit edildiğinde (bütün
    /// oturumları kapat) ve hesap pasife alındığında.
    /// </summary>
    Task<int> RevokeAllForUserAsync(int userId, string sebep);
}
