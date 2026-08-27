namespace StajProject.Business.Mesai;

/// <summary>Bir POI'nin o andaki açık/kapalı durumu (Ödev 13 iyileştirmesi).</summary>
/// <param name="Acik">Şu an hizmet veriyor mu?</param>
/// <param name="Aciklama">Ekranda gösterilen kısa metin — "Açık · 18:00 kapanıyor".</param>
public sealed record MesaiDurumu(bool Acik, string Aciklama);

/// <summary>
/// "Şu an" sorusunun Türkiye saatiyle cevabı.
///
/// NEDEN AYRI BİR SINIF?
/// Projenin tamamı zamanı UTC tutuyor (bkz. README → "Neden UTC?"): kolon tipi
/// <c>timestamptz</c>, damgalar <c>DateTime.UtcNow</c>. Bu doğru bir tercih —
/// ama "bu POI şu an açık mı?" sorusunun cevabı UTC ile verilemez. Türkiye
/// UTC+3; sunucu UTC ile çalışırken saat 20:00'de kapanan bir yer, UTC 17:00
/// olduğu için hâlâ açık görünürdü.
///
/// Dönüşümü tek bir yerde yapıyoruz ki "acaba burada UTC mi yerel mi?"
/// sorusu kodun her yerine dağılmasın.
///
/// KİMLİK SORUNU: saat dilimi kimliği işletim sistemine göre değişiyor —
/// Windows'ta "Turkey Standard Time", Linux/macOS'ta "Europe/Istanbul".
/// .NET 8 çoğu durumda ikisini de çözebiliyor ama garanti değil; ikisini de
/// deniyoruz. Hiçbiri bulunamazsa sabit +03:00 kullanıyoruz — Türkiye 2016'dan
/// beri yaz saati uygulamıyor, yani sabit fark doğru sonucu veriyor.
/// </summary>
public static class TurkiyeZamani
{
    private static readonly TimeSpan SabitFark = TimeSpan.FromHours(3);

    private static readonly TimeZoneInfo? Dilim = DilimiBul();

    private static TimeZoneInfo? DilimiBul()
    {
        foreach (var kimlik in new[] { "Turkey Standard Time", "Europe/Istanbul" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(kimlik);
            }
            catch (TimeZoneNotFoundException) { /* diğerini dene */ }
            catch (InvalidTimeZoneException) { /* diğerini dene */ }
        }

        return null;
    }

    /// <summary>Türkiye saatiyle şu an.</summary>
    public static DateTime Simdi() => Cevir(DateTime.UtcNow);

    /// <summary>Bir UTC anını Türkiye saatine çevirir.</summary>
    public static DateTime Cevir(DateTime utc)
        => Dilim is null
            ? DateTime.SpecifyKind(utc + SabitFark, DateTimeKind.Unspecified)
            : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Dilim);

    /// <summary>Türkiye saatiyle bugünün tarihi.</summary>
    public static DateOnly Bugun() => DateOnly.FromDateTime(Simdi());
}
