using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// TUR ROTA ÖNERİSİ — Google Places + Directions üzerinden.
///
/// Tek bir soruyu cevaplıyor: "Şu şehirde, şu ulaşım tipiyle, şu sürede, şu
/// temada bir tur yapsam nereleri hangi sırayla gezerim?"
///
/// ---- SORUMLULUK SINIRI ----
///   IPlacesClient      → mekanları BULMAK           (dış servis)
///   IDirectionsClient  → sırayı ve rotayı ÇÖZMEK    (dış servis)
///   TurKatalogu        → tema/tip/süre eşlemeleri   (saf veri)
///   AdaySecici         → seçim ve bütçe hesabı      (saf mantık)
///   bu servis          → sıralamayı yönetmek        (orkestrasyon)
///
/// Bu ayrım tek bir amaca hizmet ediyor: kaç dış istek atıldığı BURADA, tek
/// bir dosyada okunabilsin. Aramalar servisin içine dağılsaydı "bir öneri kaça
/// mal oluyor?" sorusunun cevabı kodun tamamını okumaktan geçerdi.
/// </summary>
public interface ITurPlanlamaServisi
{
    /// <summary>
    /// İstekteki seçimlere göre sıralı durak listesi ve rota önerir.
    ///
    /// Öneri KAYDEDİLMEZ: dönen tur şablonunun ve durakların Id'si 0'dır.
    ///
    /// Geçersiz girdide <see cref="Validation.IsKuraliException"/> (→ 400),
    /// servis yapılandırılmamış ya da ulaşılamıyorsa
    /// <see cref="Validation.DisServisException"/> (→ 503) fırlatır.
    /// </summary>
    Task<TurOneriDto> RotaOnerAsync(TurRotaIstegiDto istek, CancellationToken iptal = default);

    /// <summary>
    /// ELLE DÜZENLENMİŞ durak listesi için rotayı yeniden hesaplar.
    ///
    /// Mekan ARAMASI YAPMAZ: duraklar zaten belli, eksik olan yalnızca
    /// yollara oturmuş çizgi. Bu yüzden maliyeti bir Directions isteği.
    ///
    /// Rota çizilemezse İSTİSNA FIRLATMAZ; sonucun <c>Uyari</c> alanı dolar
    /// ve çizgi null döner — dış servisin geçici aksaklığı, kullanıcının
    /// elle yaptığı düzenlemeyi çöpe atmasın.
    /// </summary>
    Task<TurRotaHesapSonucuDto> RotaHesaplaAsync(
        TurRotaHesapIstegiDto istek,
        CancellationToken iptal = default);
}
