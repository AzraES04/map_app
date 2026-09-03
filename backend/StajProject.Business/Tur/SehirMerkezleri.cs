namespace StajProject.Business.Tur;

/// <summary>
/// 81 ilin ŞEHİR MERKEZİ koordinatları (plaka → enlem/boylam, EPSG:4326).
///
/// ---- BU DOSYA NEDEN VAR? ----
/// Tur önerisi mekanları bir MERKEZ noktanın çevresinde arıyor. İlk
/// gerçeklemede o merkez, il sınırının geometrik iç noktasıydı
/// (<c>ST_PointOnSurface</c>) ve bu SESSİZ bir hataya yol açtı:
///
///     Ankara   → (32.60, 39.69)  ≈ Kızılay'ın 35 km güneybatısı
///     İstanbul → (28.62, 41.25)  ≈ Çatalca civarı, tarihi yarımada değil
///
/// Sonuç, ekranda "çalışıyor" gibi görünen ama Polatlı'daki bir müzeyi
/// Ankara turuna koyan bir öneriydi. Hata mesajı yok, çökme yok — yalnızca
/// yanlış şehir merkezi.
///
/// GEOMETRİ NEDEN YETMİYOR? İl sınırı idari bir alan; şehrin nerede
/// kurulduğunu bilmiyor. Türkiye'de illerin çoğunda merkez ilçe sınırın
/// ortasında değil (Ankara'da kuzeyde, İstanbul'da güneyde, Muğla'da içeride).
/// Bu bilgi coğrafyadan TÜRETİLEMEZ, veri olarak durmalı.
///
/// NEDEN GEOCODING SERVİSİ DEĞİL? Nominatim/Google'a sormak her tur önerisine
/// bir dış istek daha eklerdi ve bu liste hiç değişmiyor — 81 satır, bir kez
/// yazılıyor.
///
/// DOĞRULAMA: her koordinatın kendi ilinin sınırları İÇİNDE kaldığı
/// PostGIS ile denetlendi (<c>ST_Contains</c>); listeye satır eklendiğinde
/// aynı denetim tekrarlanmalı.
/// </summary>
public static class SehirMerkezleri
{
    /// <summary>Plaka → (enlem, boylam).</summary>
    private static readonly Dictionary<int, (double Lat, double Lon)> Merkezler = new()
    {
        [1] = (37.0000, 35.3213),   // Adana
        [2] = (37.7648, 38.2786),   // Adıyaman
        [3] = (38.7507, 30.5567),   // Afyonkarahisar
        [4] = (39.7191, 43.0503),   // Ağrı
        [5] = (40.6499, 35.8353),   // Amasya
        [6] = (39.9334, 32.8597),   // Ankara
        [7] = (36.8969, 30.7133),   // Antalya
        [8] = (41.1828, 41.8183),   // Artvin
        [9] = (37.8560, 27.8416),   // Aydın
        [10] = (39.6484, 27.8826),  // Balıkesir
        [11] = (40.1451, 29.9799),  // Bilecik
        [12] = (38.8853, 40.4980),  // Bingöl
        [13] = (38.3938, 42.1232),  // Bitlis
        [14] = (40.5760, 31.5788),  // Bolu
        [15] = (37.7203, 30.2908),  // Burdur
        [16] = (40.1826, 29.0665),  // Bursa
        [17] = (40.1553, 26.4142),  // Çanakkale
        [18] = (40.6013, 33.6134),  // Çankırı
        [19] = (40.5506, 34.9556),  // Çorum
        [20] = (37.7765, 29.0864),  // Denizli
        [21] = (37.9144, 40.2306),  // Diyarbakır
        [22] = (41.6771, 26.5557),  // Edirne
        [23] = (38.6810, 39.2264),  // Elazığ
        [24] = (39.7500, 39.4900),  // Erzincan
        [25] = (39.9000, 41.2700),  // Erzurum
        [26] = (39.7767, 30.5206),  // Eskişehir
        [27] = (37.0662, 37.3833),  // Gaziantep
        [28] = (40.9128, 38.3895),  // Giresun
        [29] = (40.4386, 39.5086),  // Gümüşhane
        [30] = (37.5744, 43.7408),  // Hakkari
        [31] = (36.2023, 36.1613),  // Hatay (Antakya)
        [32] = (37.7648, 30.5566),  // Isparta
        [33] = (36.8000, 34.6333),  // Mersin
        [34] = (41.0082, 28.9784),  // İstanbul (tarihi yarımada / Fatih)
        [35] = (38.4237, 27.1428),  // İzmir
        [36] = (40.6013, 43.0975),  // Kars
        [37] = (41.3887, 33.7827),  // Kastamonu
        [38] = (38.7312, 35.4787),  // Kayseri
        [39] = (41.7333, 27.2167),  // Kırklareli
        [40] = (39.1425, 34.1709),  // Kırşehir
        [41] = (40.7654, 29.9408),  // Kocaeli (İzmit)
        [42] = (37.8746, 32.4932),  // Konya
        [43] = (39.4242, 29.9833),  // Kütahya
        [44] = (38.3552, 38.3095),  // Malatya
        [45] = (38.6191, 27.4289),  // Manisa
        [46] = (37.5858, 36.9371),  // Kahramanmaraş
        [47] = (37.3212, 40.7245),  // Mardin
        [48] = (37.2153, 28.3636),  // Muğla
        [49] = (38.9462, 41.7539),  // Muş
        [50] = (38.6939, 34.6857),  // Nevşehir
        [51] = (37.9667, 34.6833),  // Niğde
        [52] = (40.9839, 37.8764),  // Ordu
        [53] = (41.0201, 40.5234),  // Rize
        [54] = (40.7569, 30.3783),  // Sakarya (Adapazarı)
        [55] = (41.2867, 36.3300),  // Samsun
        [56] = (37.9274, 41.9403),  // Siirt
        [57] = (42.0231, 35.1531),  // Sinop
        [58] = (39.7477, 37.0179),  // Sivas
        [59] = (40.9781, 27.5117),  // Tekirdağ
        [60] = (40.3167, 36.5500),  // Tokat
        [61] = (41.0027, 39.7168),  // Trabzon
        [62] = (39.1079, 39.5401),  // Tunceli
        [63] = (37.1591, 38.7969),  // Şanlıurfa
        [64] = (38.6823, 29.4082),  // Uşak
        [65] = (38.4891, 43.4089),  // Van
        [66] = (39.8181, 34.8147),  // Yozgat
        [67] = (41.4564, 31.7987),  // Zonguldak
        [68] = (38.3687, 34.0370),  // Aksaray
        [69] = (40.2552, 40.2249),  // Bayburt
        [70] = (37.1811, 33.2150),  // Karaman
        [71] = (39.8468, 33.5153),  // Kırıkkale
        [72] = (37.8812, 41.1351),  // Batman
        [73] = (37.4187, 42.4918),  // Şırnak
        [74] = (41.6344, 32.3375),  // Bartın
        [75] = (41.1105, 42.7022),  // Ardahan
        [76] = (39.8880, 44.0048),  // Iğdır
        [77] = (40.6500, 29.2667),  // Yalova
        [78] = (41.2061, 32.6204),  // Karabük
        [79] = (36.7184, 37.1212),  // Kilis
        [80] = (37.0742, 36.2478),  // Osmaniye
        [81] = (40.8438, 31.1565),  // Düzce
    };

    /// <summary>
    /// Plakanın şehir merkezi; liste dışıysa null.
    ///
    /// Null dönmesi bir hata değil: çağıran taraf o zaman il sınırının iç
    /// noktasına düşüyor. Yani liste eksik kalsa bile öneri üretilmeye devam
    /// ediyor, yalnızca merkez daha kaba oluyor.
    /// </summary>
    public static (double Lat, double Lon)? Bul(int plaka)
        => Merkezler.TryGetValue(plaka, out var merkez) ? merkez : null;

    /// <summary>Tanımlı plakalar — doğrulama testi bunun üzerinden dönüyor.</summary>
    public static IReadOnlyCollection<int> Plakalar => Merkezler.Keys;
}
