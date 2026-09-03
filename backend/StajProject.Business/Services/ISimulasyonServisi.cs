using StajProject.Business.DTOs;
using StajProject.Business.Ulasim;

namespace StajProject.Business.Services;

/// <summary>
/// Çalışan simülasyonların BELLEKTEKİ defteri (Ödev 19 / Madde 1).
///
/// SINGLETON. Sebebi basit: simülasyon bir isteğe değil, uygulamaya ait bir
/// durum. Kullanıcı "başlat" dedikten sonra isteği biter ama araç yolda
/// kalmaya devam eder ve başka kullanıcılar onu takip eder.
///
/// VERİTABANI YOK — bilinçli. Simülasyon geçici bir gösterim; sunucu yeniden
/// başladığında yolda araç kalmaması DOĞRU davranış. Kalıcı yapsaydık,
/// kapanan bir sunucudan sonra "hâlâ yolda görünen ama ilerlemeyen" hayalet
/// kayıtları temizlemek gerekirdi.
///
/// ZAMAN DIŞARIDAN VERİLEBİLİYOR (simdi parametreleri): testler gerçekten
/// 60 saniye beklemek zorunda kalmasın diye. Verilmezse
/// <see cref="DateTime.UtcNow"/> kullanılıyor.
/// </summary>
public interface ISimulasyonServisi
{
    /// <summary>
    /// Simülasyonu başlatır ve ilk durumu döner.
    ///
    /// Aynı güzergah zaten çalışıyorsa YENİDEN başlatır (baştan alır) —
    /// "zaten çalışıyor" hatası vermek, yanlışlıkla iki kez tıklayan
    /// kullanıcıyı cezalandırmak olurdu.
    /// </summary>
    SimulasyonDurumDto Baslat(SimulasyonKaynak kaynak, DateTime? simdi = null);

    /// <summary>Durdurur. Çalışan simülasyon yoksa false.</summary>
    bool Durdur(int guzergahId);

    /// <summary>Tek bir güzergahın anlık durumu; çalışmıyorsa null.</summary>
    SimulasyonDurumDto? Durum(int guzergahId, DateTime? simdi = null);

    /// <summary>Çalışan bütün simülasyonların anlık durumu.</summary>
    IReadOnlyList<SimulasyonDurumDto> TumDurumlar(DateTime? simdi = null);

    /// <summary>
    /// Bir TİK: bütün simülasyonların yeni durumunu üretir.
    ///
    /// Süresi dolanlar Tamamlandi = true ile SON KEZ döner ve defterden
    /// düşer. Yayıncı bu listeyi olduğu gibi SignalR'a veriyor; yani
    /// "kim bitti?" kararı tek yerde (burada) veriliyor.
    /// </summary>
    IReadOnlyList<SimulasyonDurumDto> Ilerlet(DateTime? simdi = null);
}
