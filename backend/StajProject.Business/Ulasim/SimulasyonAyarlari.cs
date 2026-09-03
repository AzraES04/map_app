namespace StajProject.Business.Ulasim;

/// <summary>
/// Simülasyonun ayarları (appsettings.json → "Simulasyon").
///
/// Bölüm hiç yazılmazsa sınıfın kendi varsayılanları geçerli: 60 saniyede
/// bir tur, saniyede iki kare.
/// </summary>
public class SimulasyonAyarlari
{
    /// <summary>
    /// Bir aracın hattın tamamını kaç saniyede bitireceği.
    ///
    /// GERÇEK sürüş süresi DEĞİL — kasıtlı olarak. Hatlarımızın biri 1,8 bin
    /// kilometre; gerçek zamanlı oynatılsa jüri önünde hiçbir şey görünmezdi.
    /// Sabit gösterim süresi, uzun ve kısa hatların ikisini de aynı sürede
    /// bitiriyor ve yüzde göstergesi öngörülebilir ilerliyor.
    /// </summary>
    public double SureSaniye { get; set; } = 60;

    /// <summary>
    /// Yayın sıklığı (ms). 500 ms = saniyede iki güncelleme.
    ///
    /// Daha sık göndermek akıcılığı belirgin biçimde artırmıyor (araç
    /// istemcide iki nokta arasında zaten yumuşak hareket ediyor) ama her
    /// takipçiye giden mesaj sayısını ikiye katlıyor.
    /// </summary>
    public int TikMs { get; set; } = 500;

    /// <summary>
    /// Bir hattın simülasyonu için EN AZ kaç durak gerekli.
    /// Tek duraklı bir hatta "hareket" tanımsızdır.
    /// </summary>
    public const int EnAzDurak = 2;
}
