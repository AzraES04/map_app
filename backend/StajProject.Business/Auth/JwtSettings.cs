namespace StajProject.Business.Auth;

/// <summary>
/// appsettings.json'daki "Jwt" bölümünün karşılığı.
/// </summary>
public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    /// <summary>Token geçerlilik süresi (dakika). Ödev gereği kısa: 10 dk.</summary>
    public int ExpiryMinutes { get; set; } = 10;
}
