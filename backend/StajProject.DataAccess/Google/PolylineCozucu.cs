using NetTopologySuite.Geometries;

namespace StajProject.DataAccess.Google;

/// <summary>
/// Google'ın "encoded polyline" biçimini koordinatlara çevirir.
///
/// ---- BU KOD NEDEN VAR? ----
/// Directions API rota geometrisini SIKIŞTIRILMIŞ bir metin olarak döndürüyor
/// ("_p~iF~ps|U_ulLnnqC…"): her koordinat bir öncekine göre FARK olarak
/// yazılıyor, fark 1e5 ile çarpılıp tam sayıya çevriliyor, sonra 5'er bitlik
/// parçalara bölünüp ASCII'ye kaydırılıyor. GeoJSON isteme seçeneği yok
/// (OSRM'de vardı), o yüzden çözücüyü yazmak zorundayız.
///
/// Hazır bir NuGet paketi de kullanılabilirdi; 40 satırlık bir kod için
/// bağımlılık eklemek, sürüm yönetimi ve güvenlik yüzeyi getirirdi.
/// </summary>
public static class PolylineCozucu
{
    /// <summary>Google'ın kullandığı sabit: koordinatlar 5 ondalık basamakla saklanıyor.</summary>
    private const double Olcek = 1e5;

    /// <summary>
    /// Kodlanmış metni çözer.
    /// </summary>
    /// <returns>
    /// Koordinatlar (boylam, enlem sırasıyla — NetTopologySuite'in beklediği
    /// düzen). Metin boş ya da bozuksa BOŞ liste: geometri turun yan ürünü,
    /// çözülemeyen bir çizgi yüzünden öneriyi düşürmek doğru olmaz.
    /// </returns>
    public static IReadOnlyList<Coordinate> Coz(string? kodlanmis)
    {
        if (string.IsNullOrEmpty(kodlanmis))
        {
            return Array.Empty<Coordinate>();
        }

        var sonuc = new List<Coordinate>();
        var indis = 0;
        var enlem = 0;
        var boylam = 0;

        try
        {
            while (indis < kodlanmis.Length)
            {
                enlem += SonrakiFark(kodlanmis, ref indis);
                boylam += SonrakiFark(kodlanmis, ref indis);

                sonuc.Add(new Coordinate(boylam / Olcek, enlem / Olcek));
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Metin yarıda kesilmiş: o ana kadar çözülenler geçerli, gerisi yok.
            // Fırlatmak yerine elde olanı dönüyoruz — yarım bir çizgi,
            // hiç çizgi olmamasından iyidir.
        }

        return sonuc;
    }

    /// <summary>
    /// Bir sonraki farkı okur ve <paramref name="indis"/>'i ilerletir.
    ///
    /// Kodlama: her karakterden 63 çıkarılır, alttaki 5 bit değere eklenir;
    /// 6. bit "devam ediyor" demektir. Sonuçta en düşük bit işareti taşır
    /// (zigzag kodlama), o yüzden sonda bir kaydırma ve tümleme var.
    /// </summary>
    private static int SonrakiFark(string metin, ref int indis)
    {
        var kaydirma = 0;
        var deger = 0;
        int karakter;

        do
        {
            karakter = metin[indis++] - 63;
            deger |= (karakter & 0x1f) << kaydirma;
            kaydirma += 5;
        }
        while (karakter >= 0x20);

        return (deger & 1) != 0 ? ~(deger >> 1) : deger >> 1;
    }

    /// <summary>
    /// Çözülen koordinatlardan LineString üretir; iki noktadan az varsa null
    /// (tek noktalı bir çizgi geçersiz bir geometridir).
    /// </summary>
    public static LineString? CizgiyeCevir(string? kodlanmis)
    {
        var koordinatlar = Coz(kodlanmis);
        if (koordinatlar.Count < 2)
        {
            return null;
        }

        return new LineString(koordinatlar.ToArray()) { SRID = 4326 };
    }
}
