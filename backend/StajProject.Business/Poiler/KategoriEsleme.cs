using System.Text;

namespace StajProject.Business.Poiler;

// ============================================================================
//  Ödev 13 / Madde 4: "kayıtlı bir yer seçilirken konumlandırma otomatik olsun
//  (Millî Kütüphane seçilirse direkt Eğitim › Kütüphane gelsin)".
//
//  Kullanıcı POI eklerken haritadan bir yer seçiyor ya da arama kutusundan
//  gerçek bir yeri (Nominatim/OpenStreetMap) tıklıyor. O yerin ne olduğu
//  zaten belli: OSM her nesneye bir sınıf/tür etiketi veriyor
//  (amenity=library, amenity=pharmacy…). Elimizde bu bilgi varken kullanıcıya
//  kategoriyi baştan seçtirmek gereksiz bir adım — ve seçim hatalarının
//  kaynağı.
//
//  Bu sınıf iki şeye bakıp bir KATEGORİ YOLU öneriyor:
//     1. OSM tür etiketi   → en güvenilir kaynak, önce buna bakılıyor
//     2. Yerin adındaki kelimeler → OSM etiketi yoksa/ tanınmıyorsa
//        ("Millî Kütüphane" → kütüphane)
//
//  Öneri ATA-ÇOCUK ÇİFTİ olarak dönüyor ("Eğitim" › "Kütüphane"): ödevin
//  istediği tam olarak bu — sadece alt kategori değil, hiyerarşinin kendisi.
//
//  NEDEN BACKEND'DE? Eşleme sözlüğü kategori ağacının adlarına bağlı; ağaç
//  veritabanında, yönetici oradan değiştiriyor. Arayüzde tutsaydık kategori
//  adı değiştiğinde eşleme sessizce çalışmaz hâle gelirdi ve kimse fark
//  etmezdi. Burada, öneri kategori tablosuyla EŞLEŞTİRİLEREK dönüyor:
//  karşılığı bulunamayan bir öneri hiç dönmüyor (bkz. PoiService.KategoriOner).
//
//  ÖNERİ BİR DAYATMA DEĞİL: form kategoriyi seçili getiriyor, kullanıcı
//  istediği gibi değiştirebiliyor. Bu yüzden burada "emin değilsek susalım"
//  gibi bir kaygı yok; yanlış öneri tek tıkla düzeltiliyor.
// ============================================================================

/// <summary>Önerilen kategori yolu — kök ve altındaki yaprak.</summary>
/// <param name="Kok">Kök kategori adı — "Eğitim".</param>
/// <param name="Alt">Alt kategori adı — "Kütüphane".</param>
/// <param name="Gerekce">Öneriyi neyin tetiklediği; arayüz kullanıcıya gösteriyor.</param>
public sealed record KategoriOnerisi(string Kok, string Alt, string Gerekce);

public static class KategoriEsleme
{
    /// <summary>
    /// OpenStreetMap tür etiketi → kategori yolu.
    ///
    /// Anahtarlar Nominatim'in <c>type</c> (ve gerektiğinde <c>class</c>)
    /// alanında dönen değerlerdir. Liste kasıtlı olarak kısa: projenin
    /// kategori ağacında dört kök var, OSM'in yüzlerce etiketini karşılamanın
    /// anlamı yok. Karşılığı olmayan tür için öneri üretilmiyor.
    /// </summary>
    private static readonly Dictionary<string, (string Kok, string Alt)> TureGore = new(StringComparer.OrdinalIgnoreCase)
    {
        // Yeme-İçme
        ["restaurant"] = ("Yeme-İçme", "Restoran"),
        ["fast_food"] = ("Yeme-İçme", "Restoran"),
        ["food_court"] = ("Yeme-İçme", "Restoran"),
        ["cafe"] = ("Yeme-İçme", "Kafe"),
        ["coffee"] = ("Yeme-İçme", "Kafe"),
        ["ice_cream"] = ("Yeme-İçme", "Kafe"),
        ["bakery"] = ("Yeme-İçme", "Fırın"),
        ["pastry"] = ("Yeme-İçme", "Fırın"),
        ["confectionery"] = ("Yeme-İçme", "Fırın"),

        // Konaklama
        ["hotel"] = ("Konaklama", "Otel"),
        ["motel"] = ("Konaklama", "Otel"),
        ["resort"] = ("Konaklama", "Otel"),
        ["hostel"] = ("Konaklama", "Pansiyon"),
        ["guest_house"] = ("Konaklama", "Pansiyon"),
        ["guesthouse"] = ("Konaklama", "Pansiyon"),
        ["chalet"] = ("Konaklama", "Pansiyon"),

        // Sağlık
        ["hospital"] = ("Sağlık", "Hastane"),
        ["clinic"] = ("Sağlık", "Hastane"),
        ["doctors"] = ("Sağlık", "Hastane"),
        ["pharmacy"] = ("Sağlık", "Eczane"),
        ["chemist"] = ("Sağlık", "Eczane"),

        // Eğitim
        ["school"] = ("Eğitim", "Okul"),
        ["college"] = ("Eğitim", "Okul"),
        ["university"] = ("Eğitim", "Okul"),
        ["kindergarten"] = ("Eğitim", "Okul"),
        ["library"] = ("Eğitim", "Kütüphane"),
        ["public_bookcase"] = ("Eğitim", "Kütüphane"),
    };

    /// <summary>
    /// Yer adındaki anahtar kelime → kategori yolu.
    ///
    /// Sıra ÖNEMLİ: liste yukarıdan aşağı taranıyor ve ilk eşleşen kazanıyor.
    /// Daha uzun/özel kelimeler üstte duruyor ki "aile sağlığı merkezi",
    /// yalnızca "sağlık" görüp yanlış dala düşmesin.
    ///
    /// Kelimeler ASCII'ye indirgenmiş hâlde yazılı (bkz. <see cref="Sadelestir"/>):
    /// kullanıcı "KÜTÜPHANE", "Kütüphane" veya "kutuphane" yazsa da eşleşsin.
    /// </summary>
    private static readonly (string Kelime, string Kok, string Alt)[] AdaGore =
    {
        ("kutuphane",     "Eğitim",    "Kütüphane"),
        ("universite",    "Eğitim",    "Okul"),
        ("anaokulu",      "Eğitim",    "Okul"),
        ("ilkokul",       "Eğitim",    "Okul"),
        ("ortaokul",      "Eğitim",    "Okul"),
        ("kolej",         "Eğitim",    "Okul"),
        ("lisesi",        "Eğitim",    "Okul"),
        ("okulu",         "Eğitim",    "Okul"),

        ("eczane",        "Sağlık",    "Eczane"),
        ("hastane",       "Sağlık",    "Hastane"),
        ("poliklinik",    "Sağlık",    "Hastane"),
        ("saglik ocagi",  "Sağlık",    "Hastane"),
        ("tip merkezi",   "Sağlık",    "Hastane"),

        ("pansiyon",      "Konaklama", "Pansiyon"),
        ("konukevi",      "Konaklama", "Pansiyon"),
        ("otel",          "Konaklama", "Otel"),
        ("hotel",         "Konaklama", "Otel"),

        ("pastane",       "Yeme-İçme", "Fırın"),
        ("firin",         "Yeme-İçme", "Fırın"),
        ("borekci",       "Yeme-İçme", "Fırın"),
        ("kahvalti",      "Yeme-İçme", "Kafe"),
        ("kahve",         "Yeme-İçme", "Kafe"),
        ("kafe",          "Yeme-İçme", "Kafe"),
        ("cafe",          "Yeme-İçme", "Kafe"),
        ("lokanta",       "Yeme-İçme", "Restoran"),
        ("restoran",      "Yeme-İçme", "Restoran"),
        ("restaurant",    "Yeme-İçme", "Restoran"),
        ("kebap",         "Yeme-İçme", "Restoran"),
        ("ocakbasi",      "Yeme-İçme", "Restoran"),
        ("pide",          "Yeme-İçme", "Restoran"),
    };

    /// <summary>
    /// Yer türü ve adından kategori önerir. Karşılığı yoksa null.
    /// </summary>
    /// <param name="tur">Nominatim'in <c>type</c> alanı — "library", "pharmacy"…</param>
    /// <param name="sinif">Nominatim'in <c>class</c> alanı — "amenity", "tourism"…</param>
    /// <param name="isim">Yerin adı — "Millî Kütüphane".</param>
    public static KategoriOnerisi? Oner(string? tur, string? sinif, string? isim)
    {
        // 1) Tür etiketi. OSM'in kendi sınıflandırması, ada göre tahminden
        //    çok daha güvenilir: "Yeşil Vadi" adlı bir yer restoran da olabilir
        //    park da, ama amenity=restaurant etiketi tartışmasızdır.
        foreach (var aday in new[] { tur, sinif })
        {
            if (!string.IsNullOrWhiteSpace(aday) && TureGore.TryGetValue(aday.Trim(), out var eslesme))
            {
                return new KategoriOnerisi(eslesme.Kok, eslesme.Alt, $"OpenStreetMap türü: {aday.Trim()}");
            }
        }

        // 2) Ad içindeki anahtar kelime.
        var sade = Sadelestir(isim);
        if (sade.Length > 0)
        {
            foreach (var (kelime, kok, alt) in AdaGore)
            {
                if (sade.Contains(kelime, StringComparison.Ordinal))
                {
                    return new KategoriOnerisi(kok, alt, $"ad içinde \"{kelime}\" geçiyor");
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Metni karşılaştırmaya hazırlar: küçük harfe indirir ve Türkçe harfleri
    /// ASCII karşılıklarına çevirir.
    ///
    /// NEDEN ToLowerInvariant YETMİYOR? Türkçede "I" harfinin küçüğü "ı",
    /// "İ" harfininki "i"dir; invariant kültür ikisini de "i" sanır ve
    /// "TIP MERKEZİ" metni "tip merkezi" olur — sözlükteki "tıp" ile
    /// eşleşmez. Harfleri elle katlayarak bu tuzağı tamamen ortadan
    /// kaldırıyoruz: hem "tıp" hem "tip" aynı anahtara iniyor.
    /// </summary>
    private static string Sadelestir(string? metin)
    {
        if (string.IsNullOrWhiteSpace(metin)) return string.Empty;

        var sonuc = new StringBuilder(metin.Length);

        foreach (var harf in metin.Trim())
        {
            sonuc.Append(harf switch
            {
                'ı' or 'I' or 'i' or 'İ' => 'i',
                'ş' or 'Ş' => 's',
                'ğ' or 'Ğ' => 'g',
                'ü' or 'Ü' => 'u',
                'ö' or 'Ö' => 'o',
                'ç' or 'Ç' => 'c',
                _ => char.ToLowerInvariant(harf),
            });
        }

        return sonuc.ToString();
    }

    /// <summary>
    /// Kategori adlarını karşılaştırmak için dışarı açılan hâli — servis
    /// katmanı, önerideki adı veritabanındaki kategoriyle bunun üzerinden
    /// eşleştiriyor ("Yeme-İçme" ile "yeme-icme" aynı sayılsın).
    /// </summary>
    public static string Anahtar(string? ad) => Sadelestir(ad);
}
