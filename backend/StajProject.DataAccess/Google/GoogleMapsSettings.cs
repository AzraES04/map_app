namespace StajProject.DataAccess.Google;

/// <summary>
/// Google Maps Platform bağlantı ve KOTA ayarları.
///
/// <c>appsettings.json</c> → "GoogleMaps" bölümünden okunur; anahtarın kendisi
/// <c>appsettings.Development.json</c>'da (git'te yok).
///
/// ---- BU SINIFIN YARISI NEDEN "SINIR" AYARI? ----
///
/// Google Maps Platform çağrı BAŞINA ücretlendiriliyor ve ücretsiz kotanın
/// üstü faturalanıyor. Bu yüzden "kaç istek atılacağı" bir uygulama detayı
/// değil, AÇIKÇA YÖNETİLEN bir bütçe: aşağıdaki sınırlar bir tur önerisinin
/// atabileceği istek sayısını girdiden BAĞIMSIZ olarak sabitliyor.
///
/// Bir tur önerisinin maliyeti (en kötü hâl):
///     Places  : <see cref="TemaBasinaArama"/> kadar Text Search  (varsayılan 4)
///     Routes  : 1 + 1 (bütçeye sığmayınca yeniden sıralama)      (varsayılan 2)
///     TOPLAM  : 6 istek — kaç aday mekan bulunursa bulunsun.
///
/// Naif bir gerçeklemede bu sayı çok daha büyük olurdu: her mekan için ayrı
/// "Place Details" çağrısı (N istek) ve mesafe matrisi için N×N çağrı. 20
/// mekanlık bir turda 400+ istek demektir — bir kullanıcının tek tıklaması.
/// </summary>
public class GoogleMapsSettings
{
    /// <summary>
    /// API anahtarı. Boşsa <see cref="Enabled"/> ne olursa olsun istemciler
    /// kapalı davranır — anahtarsız istek zaten 403 döner, boşuna ağ turu
    /// atmanın anlamı yok.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Kapatılırsa Google'a hiç gidilmez.
    ///
    /// NEDEN VAR? Projeyi klonlayan biri (jüri dahil) faturalı bir anahtar
    /// oluşturmak zorunda kalmadan uygulamayı açabilmeli. Kapalıyken tur
    /// önerisi "servis yapılandırılmamış" diyor, uygulamanın geri kalanı
    /// eksiksiz çalışıyor — OSRM'deki <c>Enabled</c> ile aynı gerekçe.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Places API (New) kökü.</summary>
    public string PlacesBaseUrl { get; set; } = "https://places.googleapis.com";

    /// <summary>Directions API kökü.</summary>
    public string DirectionsBaseUrl { get; set; } = "https://maps.googleapis.com";

    /// <summary>
    /// İstek zaman aşımı (saniye). Kullanıcı öneriyi bekliyor; Google
    /// takılırsa dakikalarca beklemektense hata vermek daha iyi.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Bir mekanın öneriye girebilmesi için gereken EN DÜŞÜK puan.
    ///
    /// Değer sunucuya (Text Search'ün <c>minRating</c> parametresine)
    /// gönderiliyor, cevabı burada süzmüyoruz: süzme sunucuda yapılınca
    /// aynı istekle daha ÇOK uygun sonuç dönüyor. Yerelde süzseydik 20
    /// sonucun 15'ini atıp elde 5 aday kalırdı ve ikinci bir istek gerekirdi.
    /// </summary>
    public double EnAzPuan { get; set; } = 4.5;

    /// <summary>
    /// Puanın anlamlı sayılması için gereken en az değerlendirme sayısı.
    ///
    /// Tek kişinin 5 verdiği bir mekan "4.5+" süzgecinden geçiyor ama bir tur
    /// durağı olarak güvenilir değil. Bu süzme YERELDE yapılıyor — Places
    /// API'de böyle bir parametre yok, cevapta gelen alandan bakıyoruz
    /// (fazladan istek maliyeti yok).
    /// </summary>
    public int EnAzDegerlendirme { get; set; } = 50;

    /// <summary>
    /// Bir tema için kaç ayrı Places araması yapılacağı.
    ///
    /// Her arama bir mekan tipini tarıyor ("müze", "park", …). Tema daha çok
    /// tip içeriyorsa listenin BAŞINDAKİLER alınıyor — tema tanımı zaten
    /// önem sırasına göre yazılmış durumda. Sayıyı büyütmek çeşitliliği
    /// artırır, maliyeti de doğrusal olarak.
    /// </summary>
    public int TemaBasinaArama { get; set; } = 4;

    /// <summary>
    /// Tek aramada istenecek en fazla sonuç. Places API (New) üst sınırı 20.
    /// </summary>
    public int AramaBasinaSonuc { get; set; } = 20;

    /// <summary>
    /// Tek bir Overpass sorgusundan istenecek EN FAZLA öge (out center N).
    ///
    /// ---- NEDEN ÜST SINIR VAR? ----
    /// Tavan, aramaların EnFazlaSonuc toplamı olarak hesaplanıyordu: dokuz
    /// arama × 150 = 1350. Overpass'in cevabı üretme maliyeti bu sayıyla
    /// doğru orantılı ve ölçümde sorgu 504 Gateway Timeout veriyordu.
    ///
    /// Asıl kısıtlama tavan değil, TEKİLLEŞTİRME: sayı artık tekil tipler
    /// üzerinden hesaplanıyor (bkz. OsmPlacesClient.SorguKur). Bu alan
    /// yalnızca emniyet supabı — katalog büyüdüğünde sorgunun sessizce
    /// devleşmemesi için.
    ///
    /// 2000: POI içe aktarımı şehrin tamamını istiyor (yedi tip × 250 = 1750)
    /// ve bu sınıra takılmamalı. Daha küçük bir değer aktarımı sessizce
    /// yarıda keserdi.
    /// </summary>
    public int ToplamSonucTavani { get; set; } = 2000;

    /// <summary>
    /// Arama yarıçapı (metre). Şehir merkezli bir tur için 10 km, ilçe
    /// verildiğinde daralıyor (bkz. TurPlanlamaServisi).
    ///
    /// ---- NEDEN 15 KM DEĞİL 10 KM? ----
    /// 15 km'de "Karma" temasının dört arama tipi (gezilecek yer + müze +
    /// cami + park, tekilleştirilince 11 ayrı OSM süzgeci) canlıda ÖLÇÜLDÜ:
    /// Ankara merkezinde 504 Gateway Timeout — kullanıcının bildirdiği
    /// "rota oluşmuyor" hatasının doğrudan sebebi buydu (çoklu şehir
    /// özelliğiyle ilgisi yoktu, TEK şehirli istek bile aynı şekilde
    /// başarısız oluyordu).
    ///
    /// 10 km'de AYNI sorgu 10 saniyede dönüyor ve Ankara'nın bütün önemli
    /// simgeleri (Anıtkabir, Ankara Kalesi, Roma Hamamı, Atakule…) hâlâ
    /// kapsamda — kentin turistik dokusu zaten merkeze yakın toplanıyor.
    /// Daha küçük şehirlerde bu daha da rahat: sorun büyük şehirlerde
    /// alanın GENİŞLİĞİ, yoğunluğu değil.
    /// </summary>
    public int AramaYaricapiMetre { get; set; } = 10_000;

    /// <summary>
    /// ANAHTARSIZ kaynakta (OpenStreetMap) arama başına istenen sonuç.
    ///
    /// Google'ınkinden (20) çok yüksek çünkü iki kaynak farklı çalışıyor:
    /// Places sonuçları ALAKA SIRASINDA döndürüyor, ilk 20 zaten en
    /// uygunları. Overpass ise SIRASIZ döndürüyor — tavan, hangi kayıtların
    /// geleceğini de belirliyor. 20'yle çalışırken Ankara'da Anadolu
    /// Medeniyetleri Müzesi cevaba hiç girmiyor, tur önerisi küçük anıtlarla
    /// doluyordu. Sıralama iş katmanında yapıldığı için önce geniş almak
    /// doğru sıra.
    /// </summary>
    public int YerelAramaSonucu { get; set; } = 250;

    /// <summary>
    /// Rotaya konulabilecek EN FAZLA ara durak.
    ///
    /// Directions API tek istekte en çok 25 ara nokta kabul ediyor. 23'te
    /// duruyoruz: başlangıç ve bitiş de sayıldığı için üst sınıra yapışmak,
    /// tek bir aday fazladan eklendiğinde isteğin tamamının
    /// MAX_WAYPOINTS_EXCEEDED ile düşmesi demek olurdu.
    /// </summary>
    public int MaxAraNokta { get; set; } = 23;

    /// <summary>
    /// Places sonuçlarının önbellekte kalma süresi (dakika).
    ///
    /// NEDEN ÖNBELLEK? Aynı şehir + aynı tema için gelen ikinci istek,
    /// birincisiyle neredeyse aynı sonucu döndürür — mekanlar saat başı
    /// değişmiyor. Bu, kotayı en çok koruyan tek karar: bir sunumda aynı
    /// turun on kez denenmesi tek arama maliyetiyle karşılanıyor.
    ///
    /// NEDEN GÜNLERCE DEĞİL? Google'ın kullanım şartları, mekan içeriğinin
    /// (ad, puan) süresiz saklanmasına izin vermiyor; ayrıca puanlar zamanla
    /// değişiyor. 6 saat, sunum boyunca yeterince uzun ve veriyi bayatlatacak
    /// kadar uzun değil.
    /// </summary>
    public int OnbellekDakika { get; set; } = 360;

    /// <summary>
    /// GÜNLÜK istek tavanı — kotanın son güvenlik ağı.
    ///
    /// Önbellek ve sabit istek sayısı normal kullanımda yeter; bu sayı
    /// ANORMAL durumu (bir betiğin döngüye girmesi, kötü niyetli tekrar)
    /// karşılıyor. Aşıldığında istemciler Google'a hiç gitmiyor ve öneri
    /// "günlük istek bütçesi doldu" diyerek başarısız oluyor — sessizce
    /// fatura büyütmek yerine görünür biçimde durmak.
    /// </summary>
    public int GunlukIstekButcesi { get; set; } = 1_000;

    /// <summary>Cevapların dili ve bölgesi — mekan adları Türkçe gelsin.</summary>
    public string DilKodu { get; set; } = "tr";

    public string BolgeKodu { get; set; } = "TR";

    /// <summary>Anahtar girilmiş ve modül açık mı?</summary>
    public bool KullanimaHazir => Enabled && !string.IsNullOrWhiteSpace(ApiKey);
}
