using System.ComponentModel.DataAnnotations;

namespace StajProject.Business.DTOs;

// ============================================================================
//  İKİ ADIMLI DOĞRULAMA (TOTP) DTO'ları
//
//  Akış:
//
//    1. POST /api/auth/login          kullanıcı adı + şifre
//         ├─ 2FA kapalı  → normal oturum (token + yenileme anahtarı)
//         └─ 2FA açık    → ikinciAdimGerekli = true + kısa ömürlü ara token
//
//    2. POST /api/auth/login/2fa      ara token + 6 haneli kod
//         └─ normal oturum
//
//  Ara token'ın normal erişim token'ı yerine GEÇEMEMESİ, farklı bir
//  audience ile imzalanmasıyla sağlanıyor (bkz. JwtSettings.IkinciAdimAudience).
// ============================================================================

/// <summary>
/// <c>POST /api/auth/login/2fa</c> gövdesi.
/// </summary>
public class IkinciAdimGirisDto
{
    /// <summary>
    /// Birinci adımdan gelen kısa ömürlü ara token.
    ///
    /// Gövdede taşınıyor, <c>Authorization</c> başlığında DEĞİL: başlığa
    /// koymak onu "geçerli bir erişim token'ı" gibi göstermek olurdu ve
    /// istemci tarafında yanlışlıkla başka isteklere de eklenmesi kolaylaşırdı.
    /// </summary>
    [Required(ErrorMessage = "Doğrulama oturumu gereklidir.")]
    public string AraToken { get; set; } = string.Empty;

    /// <summary>Authenticator uygulamasındaki 6 haneli kod.</summary>
    [Required(ErrorMessage = "Doğrulama kodu gereklidir.")]
    public string Kod { get; set; } = string.Empty;
}

/// <summary>
/// İki adımlı doğrulama kurulumunun BAŞLANGICI —
/// <c>POST /api/auth/2fa/baslat</c> cevabı.
///
/// Bu cevap gizli anahtarı DÜZ METİN taşıyor ve bu kaçınılmaz: kullanıcının
/// onu telefonuna kaydetmesi gerekiyor. Tam da bu yüzden uç yalnızca giriş
/// yapmış kullanıcıya açık ve anahtar bir daha ASLA gösterilmiyor —
/// kaybedilirse kurulum baştan başlıyor.
/// </summary>
public class TotpKurulumDto
{
    /// <summary>Gizli anahtar (Base32) — elle yazmak için dörderli gruplanmış hâli.</summary>
    public string Anahtar { get; set; } = string.Empty;

    /// <summary>
    /// <c>otpauth://</c> adresi. Authenticator uygulaması bunu QR olarak
    /// okuyor; arayüz QR'ı bu metinden üretiyor.
    /// </summary>
    public string KurulumAdresi { get; set; } = string.Empty;
}

/// <summary>
/// Kurulumu TAMAMLAMA — <c>POST /api/auth/2fa/dogrula</c> gövdesi.
///
/// Kod isteniyor çünkü kullanıcının gerçekten kod ÜRETEBİLDİĞİNİ kanıtlaması
/// gerekiyor. Kanıt istemeseydik, QR'ı okutmayı yarıda bırakan kullanıcı bir
/// daha hiç giriş yapamazdı.
/// </summary>
public class TotpDogrulaDto
{
    [Required(ErrorMessage = "Doğrulama kodu gereklidir.")]
    public string Kod { get; set; } = string.Empty;
}

/// <summary>
/// İki adımlı doğrulamayı KAPATMA — <c>POST /api/auth/2fa/kapat</c> gövdesi.
///
/// ŞİFRE İSTENİYOR, kod değil. Sebep: kapatma işlemi güvenliği AZALTAN bir
/// işlem ve tam da saldırganın yapmak isteyeceği şey. Açık kalmış bir
/// oturumu ele geçiren biri, şifreyi bilmeden korumayı kaldıramamalı.
/// </summary>
public class TotpKapatDto
{
    [Required(ErrorMessage = "Şifrenizi girmelisiniz.")]
    public string Sifre { get; set; } = string.Empty;
}

/// <summary>
/// Kullanıcının iki adımlı doğrulama durumu — ayarlar ekranı için.
/// </summary>
public class TotpDurumDto
{
    public bool Etkin { get; set; }
}
