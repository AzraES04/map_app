namespace StajProject.DataAccess.Google;

/// <summary>
/// Google Places'ten dönen tek bir mekan.
///
/// Google'ın cevabındaki ONLARCA alanın yalnızca bunlar isteniyor (field
/// mask): Places API (New) faturayı İSTENEN ALANLARA göre kesiyor, "hepsini
/// al sonra bakarız" en pahalı çağrı biçimi. Buradaki alan listesi doğrudan
/// <see cref="PlacesClient"/>'taki maskeyle eşleşiyor.
/// </summary>
/// <param name="PlaceId">Google'ın kalıcı mekan kimliği ("ChIJ…").</param>
/// <param name="Ad">Görünen ad (istenen dilde).</param>
/// <param name="Lat">Enlem (EPSG:4326).</param>
/// <param name="Lon">Boylam (EPSG:4326).</param>
/// <param name="Puan">0-5 arası kullanıcı puanı; puanı olmayan mekanlarda null.</param>
/// <param name="DegerlendirmeSayisi">Puanın kaç oya dayandığı.</param>
/// <param name="Tipler">Google'ın tip etiketleri ("museum", "cafe"…).</param>
/// <param name="BirincilTip">Google'ın seçtiği baskın tip; yoksa null.</param>
/// <param name="Onem">
/// 0-1 arası ÖNEM işareti — "bu mekan ne kadar dikkate değer?".
///
/// NEDEN PUANDAN AYRI BİR ALAN? Sıralama, puanı OLMAYAN kaynaklarla da
/// çalışmak zorunda: OpenStreetMap'te kullanıcı puanı yok. Sıralamayı
/// doğrudan <paramref name="Puan"/>'a bağlasaydık anahtarsız kipte bütün
/// adaylar eşit skor alır ve seçim rastgeleye dönerdi.
///
/// Her sağlayıcı bu değeri KENDİ en iyi sinyalinden üretiyor: Google puan ve
/// oy sayısından, OSM ise wikidata/wikipedia etiketi gibi "bu yer kayda
/// değer" işaretlerinden. Sıralama tek bir alana bakıyor, kaynağın hangisi
/// olduğunu bilmek zorunda kalmıyor.
/// </param>
/// <param name="Ekstra">
/// Sağlayıcıya özgü ham etiketler — OSM'de <c>cuisine</c>, <c>brand</c>,
/// <c>wikidata</c> gibi alanlar.
///
/// NEDEN VAR? Bazı kararlar sağlayıcının HAM verisine bakmadan verilemiyor:
/// "bu bir zincir şubesi mi?" sorusunun cevabı OSM'de <c>brand:wikidata</c>
/// etiketinde. Bunları GooglePlace'in kendi alanlarına çevirmek, her yeni
/// soruda kayıt tipini büyütmek olurdu; sözlük olarak taşımak sağlayıcıya
/// özgü olanı sağlayıcıya özgü bırakıyor.
///
/// Google istemcisi BOŞ dolduruyor: Places API'de karşılığı olan alanlar
/// zaten tipli olarak yukarıda.
/// </param>
/// <param name="VejetaryenSecenegiVar">
/// Yalnızca yeme-içme mekanlarında dolu gelir; bilinmiyorsa null.
/// "Bilinmiyor" ile "hayır" AYRI: bilinmiyor diye elemek, etiketi girilmemiş
/// mekanların hepsini kaybetmek olurdu.
/// </param>
public record GooglePlace(
    string PlaceId,
    string Ad,
    double Lat,
    double Lon,
    double? Puan,
    int DegerlendirmeSayisi,
    IReadOnlyList<string> Tipler,
    string? BirincilTip,
    double Onem,
    bool? VejetaryenSecenegiVar,
    IReadOnlyDictionary<string, string>? Ekstra = null);

/// <summary>
/// Bir Places araması: "şu noktanın çevresinde, şu tipte, şu puanın üstünde".
/// </summary>
/// <param name="Sorgu">
/// Serbest metin ("müze", "vegan restoran Çankaya"). Places API (New) Text
/// Search kullanılıyor — Nearby Search'ün aksine <c>minRating</c> parametresini
/// destekliyor ve puan süzgeci SUNUCUDA uygulanabiliyor.
/// </param>
/// <param name="Tip">Google'ın tip anahtarı ("museum"); süzmeyi daraltır.</param>
/// <param name="Lat">Arama merkezi.</param>
/// <param name="Lon">Arama merkezi.</param>
/// <param name="YaricapMetre">Arama yarıçapı.</param>
/// <param name="EnAzPuan">Alt puan sınırı (ödevin istediği 4.5).</param>
/// <param name="EnFazlaSonuc">
/// Bu arama için istenen azami sonuç; null ise ayarlardaki varsayılan.
///
/// İÇE AKTARIM için var: tur önerisi 20 sonuçla yetiniyor (zaten süreye
/// birkaç durak sığıyor), POI aktarımı ise şehrin tamamını istiyor. Sabit bir
/// üst sınır ikisinden birine yanlış gelirdi.
/// </param>
public record PlacesAramasi(
    string Sorgu,
    string? Tip,
    double Lat,
    double Lon,
    int YaricapMetre,
    double EnAzPuan,
    int? EnFazlaSonuc = null);

/// <summary>
/// Google Places ile konuşan istemci.
///
/// Arayüz DataAccess'te: Places bir DIŞ VERİ KAYNAĞI, tıpkı GeoServer ve OSRM
/// gibi. Business katmanı "mekanları kim buluyor" bilmiyor; yarın başka bir
/// sağlayıcıya (Foursquare, OSM Overpass) geçilirse iş katmanında tek satır
/// değişmez.
/// </summary>
public interface IPlacesClient
{
    /// <summary>
    /// Anahtar girilmiş ve modül açık mı? Kapalıyken çağıran taraf "servis
    /// yapılandırılmamış" diyebilsin diye ayrı duruyor — "kapalı" ile
    /// "aradı, bulamadı" farklı mesajlar.
    /// </summary>
    bool Etkin { get; }

    /// <summary>
    /// Bu kaynak KULLANICI PUANI taşıyor mu?
    ///
    /// Google taşıyor (4.5+ süzgeci uygulanabiliyor), OpenStreetMap taşımıyor.
    /// Bayrak, iş katmanının süzgeci sessizce atlamak yerine AÇIKÇA devre dışı
    /// bırakmasını ve kullanıcıya bunu söylemesini sağlıyor — puansız veriyi
    /// "4.5+ seçildi" diye sunmak, olmayan bir doğruluk iddia etmek olurdu.
    /// </summary>
    bool PuanVerisiVar { get; }

    /// <summary>
    /// Aramaları çalıştırır ve sonuçları TEK listede birleştirir.
    ///
    /// ---- NEDEN TEK ARAMA DEĞİL DE LİSTE? ----
    /// Bir tema birkaç aramadan oluşuyor ("müze", "park", "kafe"). Sözleşme
    /// tek arama alsaydı, kaç isteğe bölüneceğine ÇAĞIRAN taraf karar vermiş
    /// olurdu — oysa bu tamamen kaynağa özgü bir karar:
    ///
    ///   Google   → her arama ayrı bir Text Search isteği (paralel atılıyor).
    ///   Overpass → hepsi TEK sorguda birleşiyor; ayrı ayrı sorsaydık genel
    ///              sunucunun eşzamanlılık sınırına takılıp 429 alırdık.
    ///
    /// Listeyi sözleşmeye koymak, bu kararı her kaynağın kendi içinde
    /// vermesini sağlıyor.
    ///
    /// Sonuçlar PlaceId'ye göre tekilleştirilmiş gelir: aynı mekan birden çok
    /// aramadan dönebiliyor.
    ///
    /// İSTİSNA FIRLATMAZ: ağ hatası, kota, bozuk cevap — hepsinde BOŞ liste
    /// döner ve kayda geçer. Dış bir servisin arızası, kullanıcının isteğini
    /// çökerten bir istisnaya dönüşmemeli.
    /// </summary>
    Task<IReadOnlyList<GooglePlace>> AraAsync(
        IReadOnlyList<PlacesAramasi> aramalar,
        CancellationToken iptal = default);
}
