using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>İl ve bölge sorguları (Ödev 10).</summary>
public interface IIlService
{
    /// <summary>81 il — geometrisiz, plakaya göre sıralı.</summary>
    Task<List<IlOzetDto>> GetIllerAsync();

    /// <summary>81 ilin sınırları (WKT) — haritadaki seçim katmanı için.</summary>
    Task<List<IlSinirDto>> GetSinirlarAsync();

    /// <summary>Yedi coğrafi bölge ve içerdikleri il sayısı.</summary>
    Task<List<BolgeOzetDto>> GetBolgelerAsync();
}
