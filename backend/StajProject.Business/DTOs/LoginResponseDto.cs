namespace StajProject.Business.DTOs;

public class LoginResponseDto
{
    public string Token { get; set; } = string.Empty;

    /// <summary>Token'ın geçersiz olacağı an (UTC). Frontend otomatik çıkış için kullanır.</summary>
    public DateTime ExpiresAt { get; set; }

    public string Username { get; set; } = string.Empty;
}
