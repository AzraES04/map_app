namespace StajProject.DataAccess.Osrm;

/// <summary>
/// OSRM (Open Source Routing Machine) bağlantı ayarları — Ödev 17 / Madde 1.
///
/// <c>appsettings.json</c> → "Osrm" bölümünden okunur.
///
/// ---- OSRM NE YAPIYOR, NEDEN GEREKLİ? ----
///
/// Ödev 16'da hattın çizgisi durakları DÜZ ÇİZGİLERLE birleştiriyordu. O çizgi
/// binaların içinden, nehrin üstünden geçiyordu; "hat" değil "kuş uçuşu"ydu.
/// OSRM, OpenStreetMap yol ağını kullanarak iki nokta arasındaki GERÇEK
/// sürüş güzergahını hesaplıyor: yollara oturan, tek yönleri ve dönüş
/// kısıtlarını bilen bir çizgi.
///
/// ---- NEDEN KENDİ SUNUCUMUZ (Docker), HAZIR SERVİS DEĞİL? ----
///
/// Ödev metni açıkça "Docker üzerinde localde çalıştırıp HTTP istekleri
/// atmanız tercih edilmektedir" diyor ve bunun somut gerekçeleri var:
/// genel OSRM demo sunucusunun kullanım sınırı var, üretimde kullanılması
/// yasak, ve dışa bağımlılık demoyu internete bağlar. Yerelde çalıştırmak
/// ayrıca hangi OSM anlık görüntüsüyle çalıştığımızı sabitliyor.
/// </summary>
public class OsrmSettings
{
    /// <summary>
    /// OSRM kökü — sonunda "/" OLMADAN. Docker'da varsayılan 5000 portu
    /// backend tarafından kullanıldığı için 5001'e alındı.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5001";

    /// <summary>
    /// Yol profili: "driving", "walking", "cycling".
    ///
    /// Otobüs/metro hattı için en yakını "driving" — OSRM'in hazır
    /// profillerinde toplu taşıma yok ve olsaydı da GTFS gibi ayrı bir sefer
    /// verisi isterdi. Hattın YOLDAN geçmesi bizim için yeterli.
    ///
    /// Not: osrm-routed tek profille çalışır; buradaki değeri değiştirmek
    /// veriyi de o profille yeniden hazırlamayı gerektirir (bkz. osrm/README).
    /// </summary>
    public string Profil { get; set; } = "driving";

    /// <summary>
    /// İstek zaman aşımı (saniye).
    ///
    /// Kısa tutuluyor: rota hesabı sıralama kaydetme gibi kullanıcının
    /// beklediği bir işlemin İÇİNDE çalışıyor. OSRM takılırsa kullanıcıyı
    /// dakikalarca bekletmektense rotasız devam etmek daha iyi.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Kapatılırsa rota hesabı hiç denenmez ve hat Ödev 16'daki düz çizgi
    /// hâline döner.
    ///
    /// NEDEN BÖYLE BİR ANAHTAR VAR? OSRM'i ayağa kaldırmak OSM verisi
    /// indirip işlemeyi gerektiriyor (dakikalar sürer, GB'larca disk ister).
    /// Projeyi yalnızca haritayı görmek için açan biri bunu yapmak zorunda
    /// kalmamalı; kapalıyken uygulama eksiksiz çalışıyor, sadece hatlar
    /// yollara oturmuyor.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Tek istekte OSRM'e gönderilebilecek en fazla nokta sayısı.
    ///
    /// osrm-routed'un kendi sınırı (<c>max-viaroute-size</c>) varsayılan 500.
    /// Burada 100'de tutuluyor: 100 duraklı bir hat zaten gerçekçi olmayan
    /// bir üst sınır ve daha büyük istekler OSRM'i uzun süre meşgul ederdi.
    /// Aşılırsa rota hesaplanmaz ve sebebi kullanıcıya söylenir — sessizce
    /// ilk 100 durağı almak, haritada YANLIŞ bir hat çizmek olurdu.
    /// </summary>
    public int MaxNokta { get; set; } = 100;
}
