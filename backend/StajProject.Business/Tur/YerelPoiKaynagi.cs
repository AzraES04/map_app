using System.Globalization;
using StajProject.DataAccess.Google;
using StajProject.Entities;

namespace StajProject.Business.Tur;

// ============================================================================
//  YEREL POI YEDEĞİ — Overpass cevap vermediğinde tur yine oluşsun
//
//  ---- NEDEN VAR? ----
//  Tur önerisi mekanları OpenStreetMap'ten (Overpass) çekiyor. Overpass
//  ÜCRETSİZ ve GÖNÜLLÜ bir altyapı; ölçüldü ve aynı sorgu arka arkaya
//  şu cevapları verdi:
//
//      deneme 1 → 504 Gateway Timeout (11,8 sn)
//      deneme 2 → 200 (6,1 sn)
//      deneme 3 → 200 (16,9 sn)
//
//  İstemci zaman aşımı 25 sn ve iki deneme yapıyor; ikisi de aşınca
//  kullanıcı 50 saniye bekleyip "Seçilen bölgede uygun mekan bulunamadı"
//  hatası alıyordu — yani DIŞ BİR SERVİSİN o anki yoğunluğu, uygulamanın
//  en görünür özelliğini tamamen çalışmaz hâle getiriyordu.
//
//  ---- NEDEN VERİTABANI? ----
//  Çünkü aradığımız veri ZATEN BİZDE. Turistik POI aktarımı (Ödev 12) aynı
//  OpenStreetMap kayıtlarını veritabanına yazmıştı: Ankara'da 618,
//  İstanbul'da 398 gerçek mekan — müzeler, tarihi yerler, parklar, camiler.
//  Overpass'e sorduğumuz sorunun cevabı, çevrimdışı elimizde duruyor.
//
//  ---- BU BİR "TAKLİT VERİ" DEĞİL ----
//  Sahte/örnek mekan üretmiyoruz. Yedeğin döndürdüğü her kayıt, daha önce
//  OpenStreetMap'ten alınmış GERÇEK bir mekan. Fark yalnızca tazelikte:
//  aktarım anındaki hâli. Kullanıcıya da bu söyleniyor (öneri uyarısı).
//
//  ---- NEDEN AYRI DOSYA? ----
//  Buradaki eşlemeler (mekan tipi → kategori adı, POI → aday) saf hesap ve
//  veritabanı olmadan sınanabiliyor. Servise gömseydik, "müze kategorisi
//  doğru eşleşiyor mu?" sorusunu ancak canlı veriyle sorabilirdik.
// ============================================================================

public static class YerelPoiKaynagi
{
    /// <summary>
    /// Tur mekan tipi → turistik aktarımın kategori ADLARI.
    ///
    /// Kategori ID'si DEĞİL ad kullanılıyor: id'ler seed sırasına göre
    /// oluşuyor ve başka bir veritabanında farklı çıkabilir; ad ise
    /// aktarıcının yazdığı sabit (bkz. TuristikPoiAktarici.Esleme).
    ///
    /// Viewpoint'in kendi kategorisi yok — turistik aktarım manzara
    /// noktalarını "Tarihi Yer" altında toplamıyor, o yüzden Park'a
    /// düşürülüyor: ikisi de açık hava, gezi amaçlı duraklar.
    /// </summary>
    public static IReadOnlyList<string> KategoriAdlari(VenueType tip) => tip switch
    {
        VenueType.Museum => new[] { "Müze" },
        VenueType.Monument => new[] { "Tarihi Yer" },
        VenueType.ReligiousSite => new[] { "İbadet Yeri" },
        VenueType.Park => new[] { "Park" },
        VenueType.Viewpoint => new[] { "Park" },
        VenueType.Restaurant => new[] { "Yöresel Lezzet" },
        VenueType.Cafe => new[] { "Kahve & Tatlı" },
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// Yerel kayda verilen SIRALAMA PUANI (0-1).
    ///
    /// Overpass adaylarında bu puan etiket zenginliğinden (wikidata, isim,
    /// tip) türüyordu; veritabanındaki POI'de öyle bir sinyal yok — puan
    /// da yok (OSM'de kullanıcı puanı hiç yok).
    ///
    /// Bu yüzden puan MEKAN TİPİNDEN türetiliyor: "bir şehir turunda müze,
    /// mahalle parkından daha muhtemel bir duraktır" tercihi. Hepsine aynı
    /// puanı verseydik seçim veritabanı sırasına düşerdi — yani rastgeleye.
    ///
    /// Değerler 0,4-0,75 aralığında: Overpass'ten gelen güçlü adaylarla
    /// (wikidata'lı kayıtlar ~0,8+) yarıştıklarında geride kalsınlar.
    /// Yedek veri, canlı veriyi bastırmamalı.
    /// </summary>
    public static double Onem(VenueType tip) => tip switch
    {
        VenueType.Museum => 0.75,
        VenueType.Monument => 0.72,
        VenueType.ReligiousSite => 0.62,
        VenueType.Park => 0.55,
        VenueType.Viewpoint => 0.50,
        VenueType.Restaurant => 0.50,
        VenueType.Cafe => 0.42,
        _ => 0.40,
    };

    /// <summary>
    /// Veritabanı kaydını tur adayına çevirir.
    ///
    /// PlaceId "yerel:{id}" ön ekli: <see cref="AdaySecici"/> aynı mekanı
    /// iki kez almamak için PlaceId'ye bakıyor ve Overpass'ten gelen bir
    /// kayıtla numaraların çakışması, iki farklı mekanı aynı sanmasına yol
    /// açardı.
    /// </summary>
    public static AdayMekan Cevir(Poi poi, VenueType tip)
    {
        var mekan = new GooglePlace(
            PlaceId: string.Create(CultureInfo.InvariantCulture, $"yerel:{poi.Id}"),
            Ad: poi.Isim,
            Lat: poi.Geom.Y,
            Lon: poi.Geom.X,

            // Puan YOK ve uydurulmuyor: OSM'de kullanıcı puanı hiç yok.
            // Sahte bir 4.7 yazmak, öneri panelindeki "Google puanı" notunu
            // da yalan söyler hâle getirirdi.
            Puan: null,
            DegerlendirmeSayisi: 0,

            Tipler: Array.Empty<string>(),
            BirincilTip: null,
            Onem: Onem(tip),
            VejetaryenSecenegiVar: null);

        return new AdayMekan(mekan, tip, TurKatalogu.KalisSuresiDakika(tip, 0));
    }
}
