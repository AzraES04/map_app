namespace StajProject.DataAccess.GeoServer;

/// <summary>Ham bir GeoServer cevabı (WMS resmi gibi ikili içerik için).</summary>
/// <param name="Icerik">Gövdenin baytları.</param>
/// <param name="IcerikTipi">MIME tipi — örn. "image/png".</param>
public sealed record GeoServerCevabi(byte[] Icerik, string IcerikTipi);

/// <summary>
/// GeoServer ile konuşan tek kapı (Ödev 8 / Madde 2).
///
/// Arayüz olarak tanımlı çünkü:
///   • Testlerde sahte bir gerçekleme koyabilelim (ağ olmadan test).
///   • Repository sınıfları HTTP ayrıntısını değil, "katmandan kayıt getir"
///     sözleşmesini görsün.
/// </summary>
public interface IGeoServerClient
{
    /// <summary>Bağlantı ayarları — katman adı üretmek isteyen çağıranlar için.</summary>
    GeoServerSettings Ayarlar { get; }

    /// <summary>
    /// WFS GetFeature: bir katmandaki kayıtları getirir.
    /// </summary>
    /// <param name="tablo">Katmanın tablo adı — örn. "tbl_point".</param>
    /// <param name="cqlFilter">
    /// GeoServer'ın CQL süzgeci. Örn: <c>is_deleted = false AND inserted_user_id = 3</c>.
    /// SQL'in WHERE'inin OGC standardındaki karşılığı; süzme SUNUCUDA yapılır,
    /// tüm tabloyu çekip backend'de elemeye gerek kalmaz.
    /// </param>
    /// <param name="siralama">WFS sortBy değeri — örn. <c>inserted_date D</c> (D = azalan).</param>
    Task<List<GeoServerFeature>> OzellikGetirAsync(
        string tablo,
        string? cqlFilter = null,
        string? siralama = null,
        CancellationToken iptal = default);

    /// <summary>
    /// WMS GetMap: hazır boyanmış harita karosunu (PNG) getirir.
    /// Parametreler olduğu gibi GeoServer'a iletilir; çağıran taraf hangi
    /// katmanların ve hangi süzgecin isteneceğini kendisi belirler.
    /// </summary>
    Task<GeoServerCevabi> HaritaResmiAsync(
        IEnumerable<KeyValuePair<string, string>> parametreler,
        CancellationToken iptal = default);

    /// <summary>
    /// GeoServer ayakta ve kimlik bilgileri geçerli mi?
    /// Hata fırlatmaz — durum ekranı için sadece true/false döner.
    /// </summary>
    Task<bool> AyaktaMiAsync(CancellationToken iptal = default);

    // ------------------------------------------------------------------
    //  Stil yönetimi (REST) — Ödev 13 iyileştirmesi
    //
    //  POI stilleri artık elle yazılmıyor, KATEGORİ TABLOSUNDAN üretiliyor
    //  (bkz. PoiStyleService). Üretilen SLD'leri GeoServer'a yazabilmek için
    //  istemcinin REST tarafına da açılması gerekti.
    //
    //  Neden PowerShell betiği yetmedi? Betik kurulumda BİR KEZ çalışıyor.
    //  Yönetici panelden yeni bir kategori açtığında stilin de oluşması
    //  gerekiyor; bunu betiğe bırakmak "yeni kategori haritada görünmüyor,
    //  betiği tekrar çalıştır" demek olurdu.
    // ------------------------------------------------------------------

    /// <summary>
    /// SLD'yi çalışma alanına yazar. Stil varsa günceller, yoksa oluşturur.
    /// </summary>
    Task StilYazAsync(string stilAdi, string sld, CancellationToken iptal = default);

    /// <summary>Çalışma alanındaki stil adları. Bağlantı yoksa hata fırlatır.</summary>
    Task<List<string>> StilleriListeleAsync(CancellationToken iptal = default);

    /// <summary>
    /// Çalışma alanının STİL DİZİNİNE bir dosya yazar (Ödev 15).
    ///
    /// Neden gerekli? SLD'nin <c>ExternalGraphic</c>'i bir dosya adı gösteriyor
    /// ("poi_kat_13.svg") ve GeoServer o dosyayı stilin kendi dizininde arıyor.
    /// Stil REST API'si yalnızca SLD gövdesini kabul ediyor; yanındaki simge
    /// dosyaları GeoServer'ın <c>/rest/resource</c> ucundan yükleniyor.
    ///
    /// Alternatif, SVG'leri depoya koyup kurulum betiğiyle elle kopyalamaktı;
    /// o zaman kategori eklendiğinde simgesi kendiliğinden oluşmazdı — yani
    /// Ödev 13'te stilleri koddan üretmenin bütün gerekçesi burada da geçerli.
    /// </summary>
    /// <param name="dosyaAdi">Stil dizinine göre göreli ad — "poi_kat_13.svg".</param>
    /// <param name="icerik">Dosya gövdesi (UTF-8 metin).</param>
    /// <param name="icerikTipi">MIME tipi — "image/svg+xml".</param>
    Task StilKaynagiYazAsync(
        string dosyaAdi, string icerik, string icerikTipi, CancellationToken iptal = default);

    /// <summary>
    /// Stili siler. <paramref name="purge"/> ile SLD dosyası da diskten kalkar;
    /// aksi hâlde katalogdan düşer ama dosya artık olarak kalır.
    /// </summary>
    Task StilSilAsync(string stilAdi, CancellationToken iptal = default);

    /// <summary>
    /// Katmanın EK stil listesini verilen listeyle değiştirir (varsayılan stile
    /// dokunmaz). Sıra korunur — WMS'te sonraki stil öncekinin üstüne çizilir.
    /// </summary>
    Task KatmanaStilBaglaAsync(string katman, IEnumerable<string> stiller, CancellationToken iptal = default);
}
