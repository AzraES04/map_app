namespace StajProject.Business.Validation;

/// <summary>
/// Bağımlı olunan DIŞ bir servis kullanılamıyor: yapılandırılmamış, kapalı,
/// ulaşılamıyor ya da kotası dolmuş.
///
/// ---- NEDEN <see cref="IsKuraliException"/> DEĞİL? ----
/// İş kuralı ihlali "gönderdiğin veri yanlış" der ve 400'e çevrilir; burada
/// kullanıcının verisinde bir sorun YOK, sunucu tarafındaki bir bağımlılık
/// şu an hizmet veremiyor. Doğru cevap 503: "geçici, sonra dene". İkisini
/// aynı istisnaya bindirseydik, arayüz "formunu düzelt" diyen bir hata
/// gösterirdi — düzeltilecek bir şey yokken.
///
/// Mesaj İSTEMCİYE GÖSTERİLİYOR çünkü eyleme dönüştürülebilir olacak şekilde
/// yazılıyor ("GoogleMaps:ApiKey tanımlı değil"). GeoServerErisimException'da
/// verilen kararın aynısı.
/// </summary>
public class DisServisException : Exception
{
    public DisServisException(string mesaj) : base(mesaj)
    {
    }

    public DisServisException(string mesaj, Exception ic) : base(mesaj, ic)
    {
    }
}
