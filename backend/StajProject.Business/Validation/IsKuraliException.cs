namespace StajProject.Business.Validation;

/// <summary>
/// İş kuralı ihlali — "bu kullanıcı adı zaten var", "kendi hesabını silemezsin" gibi.
///
/// Neden ayrı bir tip? Bu hatalar SUNUCU hatası değil, isteğin kendisiyle ilgili;
/// HTTP karşılığı 4xx olmalı ve mesajı kullanıcıya gösterilebilir. Beklenmeyen
/// hatalarda ise ayrıntı log'a yazılıp istemciye genel mesaj döner. İkisini
/// ayırt edebilmek için tipin farklı olması gerekiyor.
///
/// Geometri tarafındaki WktFormatException'ın yaptığı işin aynısını, coğrafyayla
/// ilgisi olmayan kurallar için yapar.
/// </summary>
public class IsKuraliException : Exception
{
    public IsKuraliException(string message) : base(message) { }
    public IsKuraliException(string message, Exception inner) : base(message, inner) { }
}
