namespace StajProject.DataAccess.GeoServer;

/// <summary>
/// GeoServer bağlantı ayarları (Ödev 8 / Madde 2).
///
/// appsettings.json → "GeoServer" bölümünden okunur. Şifre gibi değerler
/// appsettings.Development.json'da (git'te yok) durur; buradaki varsayılanlar
/// GeoServer'ın kutudan çıkan ayarlarıdır.
/// </summary>
public class GeoServerSettings
{
    /// <summary>
    /// GeoServer ile konuştuğumuz koordinat sistemi (EPSG:4326 / WGS84).
    /// Business katmanındaki <c>WktConverter.Srid</c> ile aynı değer olmalı —
    /// bağımlılık yönü yüzünden (DataAccess, Business'ı göremez) burada
    /// ikinci bir kopyası duruyor.
    /// </summary>
    public const int Srid = 4326;

    /// <summary>
    /// GeoServer kökü — sonunda "/" OLMADAN. Örn: http://localhost:8080/geoserver
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8080/geoserver";

    /// <summary>Katmanların yayınlandığı çalışma alanı (workspace).</summary>
    public string Workspace { get; set; } = "staj";

    /// <summary>
    /// Isı haritası stilinin adı (Ödev 9 / Madde 2).
    ///
    /// SLD deposu <c>geoserver/isi-haritasi.sld</c>; GeoServer'a
    /// <c>gs-yapilandir.ps1</c> yüklüyor. Adı ayarlarda tutmamızın sebebi,
    /// stil adının kodda üç yere (WMS vekili, lejant ucu, durum ucu)
    /// dağılmasını önlemek.
    /// </summary>
    public string IsiHaritasiStili { get; set; } = "isi_haritasi";

    /// <summary>
    /// Isı haritasının GRİ TONLAMALI ikizi (Ödev 11 — "termometre").
    ///
    /// Aynı yoğunluğu renk yerine gri seviyeyle kodluyor; arayüz pikselden
    /// <c>deger = gri / 255</c> okuyup tıklanan noktanın değerini gösteriyor.
    /// SLD: <c>geoserver/isi-deger.sld</c>.
    /// </summary>
    public string IsiDegerStili { get; set; } = "isi_deger";

    // NOT — POI stilleri BURADA DEĞİL.
    //
    // İlk uygulamada kategori başına stil adları burada sabit bir dizi olarak
    // duruyordu. Artık stiller kategori TABLOSUNDAN üretiliyor
    // (Business/Poiler/PoiStilUretici) ve adları kategori id'sinden geliyor:
    // "poi_kat_13". Ayarlarda sabit bir liste tutmak, yönetici kategori
    // eklediğinde güncellenmesi gereken ikinci bir kaynak yaratırdı.
    //
    // Güncel listeyi GET /api/poi/stiller veriyor.

    public string Username { get; set; } = "admin";

    public string Password { get; set; } = "geoserver";

    /// <summary>Tek bir GeoServer isteğinin azami süresi.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// GeoServer üzerinden okuma AÇIK mı?
    ///
    /// Ödevin istediği mimari bu bayrak <c>true</c> iken çalışır: listeleme ve
    /// analiz istekleri veritabanına değil GeoServer'a gider. <c>false</c>
    /// yapıldığında proje Ödev 7'deki hâline döner (doğrudan EF Core + PostGIS).
    ///
    /// Neden böyle bir kapı bırakıldı? GeoServer ayrı bir süreçtir; kurulu
    /// olmayan bir makinede projenin geri kalanı üzerinde çalışabilmek,
    /// birim testlerin GeoServer beklemeden koşabilmesi gerekiyor.
    /// Varsayılan AÇIK — yani ödevin istediği davranış varsayılan davranıştır.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>"staj:tbl_point" gibi tam nitelikli katman adı üretir.</summary>
    public string KatmanAdi(string tablo) => $"{Workspace}:{tablo}";

    /// <summary>Bu workspace'in WFS/WMS uç adresi. Örn: .../geoserver/staj/wfs</summary>
    public string ServisAdresi(string servis) => $"{BaseUrl.TrimEnd('/')}/{Workspace}/{servis}";
}
