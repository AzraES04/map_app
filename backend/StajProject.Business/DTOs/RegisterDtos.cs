using System.ComponentModel.DataAnnotations;

namespace StajProject.Business.DTOs;

/// <summary>
/// Giriş ekranından kendi hesabını açan kullanıcının isteği (Ödev 10).
///
/// <see cref="UserCreateDto"/>'dan farkı: burada rol ve aktiflik YOK.
/// Kendi kaydını açan kullanıcı kendine rol veremez — verebilseydi
/// "Yönetici" rolünü seçip yetkilendirme sistemini baştan delerdi.
/// </summary>
public class RegisterRequestDto
{
    [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
    [MinLength(3, ErrorMessage = "Kullanıcı adı en az 3 karakter olmalıdır.")]
    [MaxLength(100, ErrorMessage = "Kullanıcı adı en fazla 100 karakter olabilir.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [MinLength(6, ErrorMessage = "Şifre en az 6 karakter olmalıdır.")]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// İSTEĞE BAĞLI davet kodu.
    ///
    /// Doğru bir kod girilirse hesap o kodun sahibine (bir admine) bağlanır
    /// VE manuel onay beklemeden aktif olur — admin kodu paylaşarak zaten
    /// vouch etmiş oluyor. Kod girilmezse eski akış aynen sürüyor: hesap
    /// oluşur ama bir yöneticinin onayını bekler.
    /// </summary>
    [StringLength(24)]
    public string? InviteCode { get; set; }
}

/// <summary>Kayıt sonucu — token DEĞİL, çünkü hesap henüz kullanılamaz.</summary>
public class RegisterResponseDto
{
    public string Username { get; set; } = string.Empty;

    /// <summary>Kullanıcıya gösterilecek bilgilendirme.</summary>
    public string Message { get; set; } = string.Empty;
}
