namespace StajProject.DataAccess.Google;

/// <summary>
/// Günlük dış servis isteği sayacı — kotanın son güvenlik ağı.
///
/// ---- NEDEN AYRI BİR SINIF? ----
///
/// Sayacı istemcilerin içine gömseydik iki istemci iki ayrı sayaç tutardı ve
/// "bugün Google'a kaç istek gitti?" sorusunun tek bir cevabı olmazdı.
/// Ayrıca test edilemezdi: bütçenin dolduğu durumu sınamak için gerçekten
/// bin istek atmak gerekirdi.
///
/// Ömrü SINGLETON: sayaç isteğe değil UYGULAMAYA ait. Scoped olsaydı her
/// HTTP isteğinde sıfırlanır, hiçbir şeyi sınırlamazdı.
/// </summary>
public interface IIstekButcesi
{
    /// <summary>
    /// Bir istek için izin ister. İzin verirse sayacı ARTIRIR.
    ///
    /// Neden "sor ve artır" tek metotta? İki adım olsaydı (kontrol et, sonra
    /// artır) aynı anda gelen iki istek ikisi de kontrolü geçip bütçeyi
    /// aşabilirdi.
    /// </summary>
    /// <returns>İstek atılabilir mi?</returns>
    bool IzinIste();

    /// <summary>Bugün kullanılan istek sayısı — günlük ekranı ve testler için.</summary>
    int BugunKullanilan { get; }

    /// <summary>Günlük tavan.</summary>
    int GunlukTavan { get; }
}
