using StajProject.DataAccess.Google;

namespace StajProject.DataAccess.Yerel;

/// <summary>
/// "Bana Google'ı değil, DOĞRUDAN OpenStreetMap'i ver" demenin yolu.
///
/// <see cref="IPlacesClient"/>'a hiçbir üye eklemiyor; tek işi KAYNAĞI
/// isimlendirmek. Buna ihtiyaç duyan tek yer turistik POI içe aktarımı:
/// o iş Google anahtarı tanımlı olsa bile OSM'den okumak zorunda (Google'ın
/// şartları sonuçların kalıcı saklanmasına izin vermiyor, ODbL veriyor).
///
/// Alternatif, aktarıcının somut sınıfa bağlanmasıydı; o zaman da test için
/// gerçek HTTP gerekirdi. Aynı desen DataAccessRegistration'daki
/// "veritabani" anahtarlı depo kaydında da var.
/// </summary>
public interface IOsmPlacesClient : IPlacesClient
{
}

/// <summary>
/// ANAHTARSIZ KİP ayarları — Google anahtarı yokken tur önerisinin kullandığı
/// kaynaklar.
///
/// <c>appsettings.json</c> → "YerelTurKaynagi" bölümü.
///
/// ---- BU KİP NEDEN VAR? ----
/// Google Maps Platform anahtarı faturalandırma hesabı (kredi kartı) istiyor.
/// Projeyi klonlayan biri — jüri dahil — bunu yapmak zorunda kalmadan tur
/// önerisini görebilmeli. Anahtar girildiği anda sistem kendiliğinden Google'a
/// dönüyor; bu kip bir "demo modu" değil, gerçek çalışan bir yedek kaynak.
///
/// ---- FARKI NE? ----
/// Mekanlar OpenStreetMap'ten (Overpass API) geliyor: müze, park, anıt gibi
/// tur duraklarının tamamı orada ve ücretsiz. Tek eksik KULLANICI PUANI —
/// OSM'de böyle bir veri yok. Bu yüzden anahtarsız kipte 4.5+ süzgeci
/// uygulanmıyor ve cevapta bu açıkça yazılıyor (bkz. IPlacesClient.PuanVerisiVar).
/// </summary>
public class YerelKaynakSettings
{
    /// <summary>
    /// Overpass API ucu.
    ///
    /// Varsayılan genel sunucu: ücretsiz ve anahtarsız ama GÖNÜLLÜ altyapı.
    /// Kullanım şartları "ağır yükü kendi sunucunuza taşıyın" diyor; bu yüzden
    /// istekler önbelleklenip günlük bütçeye tabi tutuluyor (Google
    /// istemcileriyle aynı mekanizma).
    /// </summary>
    public string OverpassUrl { get; set; } = "https://overpass-api.de/api/interpreter";

    /// <summary>
    /// İstek zaman aşımı (saniye). Overpass genel sunucusu yoğun saatlerde
    /// yavaşlayabiliyor; kullanıcıyı dakikalarca bekletmektense hata vermek
    /// daha iyi.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 25;

    /// <summary>
    /// Kapatılırsa anahtarsız kip de devre dışı kalır ve Google anahtarı
    /// yokken tur önerisi "servis yapılandırılmamış" der.
    ///
    /// İnternet erişimi olmayan bir kurulumda anlamlı: Overpass'a boşuna
    /// gidip her seferinde zaman aşımı beklemek yerine baştan kapatmak.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
