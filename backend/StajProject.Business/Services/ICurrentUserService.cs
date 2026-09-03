namespace StajProject.Business.Services;

/// <summary>
/// O anki isteği yapan kullanıcıyı iş katmanına taşır (Ödev 5).
///
/// Neden arayüz? İş katmanının HTTP'den, JWT'den, HttpContext'ten haberi
/// olmamalı — bunlar sunum katmanının ayrıntıları. Business yalnızca
/// "şu an kim var?" diye soruyor; cevabı nereden bulduğu onu ilgilendirmiyor.
/// Gerçeklemesi API katmanında (CurrentUserService), testlerde ise sahte
/// bir sınıfla değiştirilebiliyor.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>Giriş yapmış kullanıcının id'si; kimlik yoksa null.</summary>
    int? UserId { get; }

    /// <summary>
    /// Giriş yapmış kullanıcının ADI; kimlik yoksa null.
    ///
    /// Ödev 19 ile eklendi: simülasyonu kimin başlattığı, canlı yayını
    /// dinleyen HERKESE gidiyor ("kemal başlattı"). Id yeterli olmazdı —
    /// yayını alan istemcinin kullanıcı listesine erişimi yok ve olmamalı.
    /// Ad zaten JWT'nin içinde (unique_name), fazladan sorgu gerektirmiyor.
    /// </summary>
    string? UserName { get; }

    /// <summary>
    /// Kimlik doğrulanmış kullanıcının id'sini döner, yoksa hata fırlatır.
    /// Sahiplik gerektiren işlemlerde (kayıt oluşturma, süzme) kullanılır.
    /// </summary>
    int RequireUserId();
}
