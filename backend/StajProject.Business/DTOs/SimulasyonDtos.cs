namespace StajProject.Business.DTOs;

/// <summary>
/// Bir simülasyonun ANLIK DURUMU (Ödev 19).
///
/// TEK BİÇİM, İKİ YOL: hem SignalR yayınında ("KonumGuncellendi") hem de
/// REST cevaplarında bu DTO gidiyor. İkiye bölseydik istemcide iki ayrı
/// çizim kodu olurdu; oysa haritaya konan araç, verinin hangi kanaldan
/// geldiğini bilmek zorunda değil.
///
/// Sayılar ARAYÜZ İÇİN hazırlanmış hâlde: yüzde 0-100 arası, mesafe metre,
/// süre saniye. İstemcinin ayrıca hesap yapmasına gerek kalmıyor — aynı
/// hesabın iki yerde yapılması, iki yerde farklı yuvarlanması demektir.
/// </summary>
public class SimulasyonDurumDto
{
    public int GuzergahId { get; set; }

    public string GuzergahAdi { get; set; } = string.Empty;

    /// <summary>Hattın rengi — araç ikonu hattın rengiyle çiziliyor.</summary>
    public string Renk { get; set; } = "#2d7dd2";

    /// <summary>Aracın anlık konumu (EPSG:4326).</summary>
    public double Lon { get; set; }

    public double Lat { get; set; }

    /// <summary>
    /// Güzergahın yüzde kaçı tamamlandı (0-100).
    /// Ödev metni: "güzergahın yüzde kaçının tamamlandığı bilgisi gösterilsin".
    /// </summary>
    public double Yuzde { get; set; }

    public double GecenSaniye { get; set; }

    public double ToplamSaniye { get; set; }

    /// <summary>Kat edilen mesafe (metre) — yüzdenin toplam mesafeyle çarpımı.</summary>
    public double AlinanMetre { get; set; }

    public double ToplamMetre { get; set; }

    /// <summary>Aracın SON GEÇTİĞİ durak (henüz ilk duraktan çıkmadıysa null).</summary>
    public int? OncekiDurakSira { get; set; }

    public string? OncekiDurakAdi { get; set; }

    /// <summary>Aracın YAKLAŞTIĞI durak (son duraktaysa null).</summary>
    public int? SonrakiDurakSira { get; set; }

    public string? SonrakiDurakAdi { get; set; }

    public int DurakSayisi { get; set; }

    /// <summary>Simülasyonu başlatan kullanıcı — bilgi kutucuğunda yazıyor.</summary>
    public string BaslatanKullanici { get; set; } = string.Empty;

    public int BaslatanKullaniciId { get; set; }

    public DateTime BaslangicUtc { get; set; }

    /// <summary>
    /// Araç son durağa vardı mı?
    ///
    /// Yayının SON mesajında true geliyor ve simülasyon o anda listeden
    /// düşüyor. İstemci bunu görünce aracı haritadan kaldırıyor. Ayrı bir
    /// "bitti" olayı tanımlamadık: aynı bilgiyi taşıyan iki mesaj türü,
    /// biri kaybolduğunda sessizce yanlış davranan bir arayüz demektir.
    /// </summary>
    public bool Tamamlandi { get; set; }
}
