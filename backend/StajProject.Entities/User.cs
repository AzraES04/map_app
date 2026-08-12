namespace StajProject.Entities;

/// <summary>
/// Giriş yapabilen kullanıcı. Şifre asla düz metin saklanmaz, hash'i saklanır.
/// </summary>
public class User
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
