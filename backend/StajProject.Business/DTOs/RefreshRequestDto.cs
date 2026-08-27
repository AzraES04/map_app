using System.ComponentModel.DataAnnotations;

namespace StajProject.Business.DTOs;

/// <summary>
/// <c>POST /api/auth/refresh</c> ve <c>POST /api/auth/logout</c> gövdesi.
///
/// Anahtar neden URL'de değil GÖVDEDE? URL'ler sunucu erişim kayıtlarına,
/// vekil sunucu günlüklerine ve tarayıcı geçmişine düz metin yazılır.
/// Oturumu devralmaya yeten bir değerin oralarda birikmesi, onu saklamak
/// için harcanan bütün emeği boşa çıkarırdı.
/// </summary>
public class RefreshRequestDto
{
    [Required(ErrorMessage = "Yenileme anahtarı zorunludur.")]
    public string RefreshToken { get; set; } = string.Empty;
}
