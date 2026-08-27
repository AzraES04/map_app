using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// Konum analizi (Ödev 14).
///
/// Ödev 4'teki <see cref="IAnalysisService"/> ile aynı controller'ı paylaşıyor
/// ama farklı bir soru soruyor: o "bu alanda kaç kayıt var?" der, bu "bu alanın
/// neresi daha uygun?" der. Ayrı bir arayüz olmasının sebebi de bu — ortak
/// hiçbir kuralları yok ve tek arayüzde toplamak, iki farklı işi tek isimle
/// anmak olurdu.
/// </summary>
public interface IKonumAnaliziService
{
    /// <summary>
    /// Seçilen alan ve ağırlıklı kriterlere göre uygunluk yüzeyi üretir.
    ///
    /// Kural ihlallerinde (kriter sayısı, ağırlık toplamı, alan seçimi)
    /// <see cref="Validation.IsKuraliException"/> fırlatır; bozuk WKT'de
    /// <see cref="Geo.WktFormatException"/>. İkisi de controller tarafında
    /// 400'e çevriliyor.
    /// </summary>
    Task<KonumAnaliziSonucuDto> CalistirAsync(KonumAnaliziRequestDto istek);
}
