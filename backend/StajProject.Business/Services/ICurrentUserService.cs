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
    /// Kimlik doğrulanmış kullanıcının id'sini döner, yoksa hata fırlatır.
    /// Sahiplik gerektiren işlemlerde (kayıt oluşturma, süzme) kullanılır.
    /// </summary>
    int RequireUserId();
}
