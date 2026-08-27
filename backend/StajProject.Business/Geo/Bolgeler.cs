namespace StajProject.Business.Geo;

/// <summary>
/// Türkiye'nin yedi coğrafi bölgesi ve hangi ilin hangi bölgede olduğu (Ödev 10).
///
/// NEDEN PLAKA NUMARASI, İL ADI DEĞİL?
/// İl adları veri kaynağına göre değişiyor: "Afyon" / "Afyonkarahisar",
/// "K.Maraş" / "Kahramanmaraş", büyük-küçük harf, Türkçe karakterler...
/// Plaka kodu ise sabit ve tek anlamlı. Eşleme ada dayansaydı veri dosyası
/// değiştiğinde bazı iller sessizce bölgesiz kalırdı.
///
/// Bölge sınırlarının kendisi burada YOK — bölge, o bölgedeki illerin
/// birleşimidir (bkz. <c>IlRepository.BolgeGeometrisiAsync</c>).
/// </summary>
public static class Bolgeler
{
    public const string Marmara = "Marmara";
    public const string Ege = "Ege";
    public const string Akdeniz = "Akdeniz";
    public const string IcAnadolu = "İç Anadolu";
    public const string Karadeniz = "Karadeniz";
    public const string DoguAnadolu = "Doğu Anadolu";
    public const string GuneydoguAnadolu = "Güneydoğu Anadolu";

    /// <summary>Bölgeler, haritada kabaca kuzeybatıdan güneydoğuya doğru sırayla.</summary>
    public static readonly string[] Tumu =
    {
        Marmara, Ege, Akdeniz, IcAnadolu, Karadeniz, DoguAnadolu, GuneydoguAnadolu,
    };

    /// <summary>Bölge → o bölgedeki illerin plaka kodları.</summary>
    private static readonly Dictionary<string, int[]> Plakalar = new()
    {
        [Marmara] = new[] { 10, 11, 16, 17, 22, 34, 39, 41, 54, 59, 77 },
        [Ege] = new[] { 3, 9, 20, 35, 43, 45, 48, 64 },
        [Akdeniz] = new[] { 1, 7, 15, 31, 32, 33, 46, 80 },
        [IcAnadolu] = new[] { 6, 18, 26, 38, 40, 42, 50, 51, 58, 66, 68, 70, 71 },
        [Karadeniz] = new[] { 5, 8, 14, 19, 28, 29, 37, 52, 53, 55, 57, 60, 61, 67, 69, 74, 78, 81 },
        [DoguAnadolu] = new[] { 4, 12, 13, 23, 24, 25, 30, 36, 44, 49, 62, 65, 75, 76 },
        [GuneydoguAnadolu] = new[] { 2, 21, 27, 47, 56, 63, 72, 73, 79 },
    };

    /// <summary>
    /// Plaka → bölge. Ters sözlük bir kez kuruluyor; her il aramasında yedi
    /// diziyi tek tek taramanın anlamı yok.
    /// </summary>
    private static readonly Dictionary<int, string> PlakadanBolge =
        Plakalar.SelectMany(p => p.Value.Select(plaka => (plaka, p.Key)))
                .ToDictionary(x => x.plaka, x => x.Key);

    /// <summary>
    /// Bir ilin bölgesini döner. Tanımsız plaka gelirse hata fırlatır —
    /// sessizce boş bölge yazmak, o ili hiçbir bölge seçiminde görünmez yapardı.
    /// </summary>
    public static string BolgeBul(int plaka)
        => PlakadanBolge.TryGetValue(plaka, out var bolge)
            ? bolge
            : throw new ArgumentOutOfRangeException(
                nameof(plaka), plaka, "Bu plaka koduna karşılık gelen bölge tanımlı değil.");

    /// <summary>Verilen ad geçerli bir bölge mi? (Büyük/küçük harf duyarsız.)</summary>
    public static bool Gecerli(string? ad)
        => ad is not null && Tumu.Contains(ad.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Kullanıcının yazdığı adı listedeki kanonik yazımına çevirir.</summary>
    public static string Normalize(string ad)
        => Tumu.First(b => string.Equals(b, ad.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>81 ilin tamamı tek bir bölgeye atanmış mı? (Test bunu doğruluyor.)</summary>
    public static IReadOnlyDictionary<int, string> TumEslesme => PlakadanBolge;
}
