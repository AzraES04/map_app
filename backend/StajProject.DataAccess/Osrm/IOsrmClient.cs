using NetTopologySuite.Geometries;

namespace StajProject.DataAccess.Osrm;

/// <summary>
/// OSRM'den hesaplanan rota (Ödev 17 / Madde 1).
/// </summary>
/// <param name="Cizgi">
/// Yola oturmuş güzergah çizgisi (EPSG:4326). OSRM'in GeoJSON çıktısından
/// üretiliyor.
/// </param>
/// <param name="MesafeMetre">Toplam sürüş mesafesi.</param>
/// <param name="SureSaniye">
/// Tahmini sürüş süresi. Bir OTOBÜS süresi değil: duraklarda bekleme,
/// yolcu iniş-binişi ve trafik yoğunluğu hesaba katılmıyor. Arayüzde
/// "sürüş süresi" diye yazılıyor, "sefer süresi" değil.
/// </param>
public record OsrmRotaSonucu(LineString Cizgi, double MesafeMetre, double SureSaniye);

/// <summary>
/// OSRM ile konuşan istemci.
///
/// Arayüz DataAccess'te çünkü OSRM bir DIŞ VERİ KAYNAĞI — GeoServer istemcisi
/// de aynı sebeple burada. Business katmanı "rotayı kim hesaplıyor"
/// bilmiyor, yalnızca <c>IOsrmClient</c>'ı çağırıyor; yarın başka bir
/// yönlendirme motoruna (Valhalla, GraphHopper) geçilse Business'ta tek satır
/// değişmez.
/// </summary>
public interface IOsrmClient
{
    /// <summary>
    /// OSRM kullanılabilir durumda mı? (<see cref="OsrmSettings.Enabled"/>)
    ///
    /// Ayrı bir özellik olarak duruyor ki çağıran taraf "kapalı olduğu için
    /// hesaplanmadı" ile "denendi ve başarısız oldu" durumlarını ayırt
    /// edebilsin — kullanıcıya gösterilecek mesaj ikisinde farklı.
    /// </summary>
    bool Etkin { get; }

    /// <summary>
    /// Verilen noktalardan geçen sürüş rotasını hesaplar.
    ///
    /// Noktalar SIRALI verilmeli: OSRM bunları "önce buraya, sonra şuraya"
    /// diye anlıyor ve sırayı optimize ETMİYOR (o ayrı bir servis: /trip).
    /// Burada optimizasyon İSTENMİYOR zaten — durak sırasını kullanıcı
    /// sürükle-bırakla kendisi belirliyor, sunucu onu değiştirirse haritadaki
    /// hat ile yönetim ekranındaki liste ayrışırdı.
    /// </summary>
    /// <param name="noktalar">En az 2 nokta (boylam/enlem, EPSG:4326).</param>
    /// <returns>
    /// Rota; OSRM kapalıysa, yol bulunamadıysa ya da servise ulaşılamadıysa
    /// <c>null</c>.
    ///
    /// NEDEN İSTİSNA FIRLATMIYOR? Rota hesabı, kullanıcının asıl yaptığı işin
    /// (durak ekleme, sıralama) YAN ÜRÜNÜ. OSRM kapalı diye sıralama
    /// kaydetmeyi reddetmek, dış bir servisin arızasını kullanıcının işini
    /// engellemeye çevirmek olurdu. Hata kaydı düşülüyor, iş devam ediyor.
    /// </returns>
    Task<OsrmRotaSonucu?> RotaHesaplaAsync(
        IReadOnlyList<Coordinate> noktalar,
        CancellationToken iptal = default);

    /// <summary>
    /// OSRM ayakta mı? Durum ekranı ve "Rota Oluştur" düğmesinin ipucu için.
    /// </summary>
    Task<bool> AyaktaMiAsync(CancellationToken iptal = default);
}
