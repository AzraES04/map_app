using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using StajProject.Business.Validation;

namespace StajProject.Business.Mesai;

// ============================================================================
//  Ödev 13 / Madde 3: mesai saatleri artık GÜN GÜN tanımlanıyor.
//
//  Ödev 12'de mesai tek bir metindi ("09:00 - 18:00") ve haftanın her günü
//  aynı kabul ediliyordu. Gerçek hayatta öyle değil: eczane cumartesi yarım
//  gün, kütüphane pazar kapalı, resmî kurum hafta sonu hiç açılmıyor.
//
//  NEDEN AYRI BİR KOLON (mesai_plani) AÇILDI DA mesai_saatleri METNİ KALDI?
//  İkisi farklı işler yapıyor:
//     mesai_plani    → YAPISAL veri. Makine okur: "salı 14:00'te açık mı?"
//                      sorusunun cevabı buradan çıkar. Kaynak budur.
//     mesai_saatleri → İNSAN okusun diye üretilmiş ÖZET metin
//                      ("Pzt-Cum 09:00-18:00 · Cmt 10:00-14:00 · Paz kapalı").
//                      Listelerde, popup'ta ve GeoServer katmanında bu görünür.
//
//  Özet metni kullanıcı yazmıyor, plandan ÜRETİLİYOR (bkz. OzetMetin). Yani
//  iki kolon çelişemez; biri diğerinin türevi. Metni de plandan her okumada
//  hesaplamak mümkündü, ama o zaman GeoServer'ın SQL View'ı ve admin listesi
//  JSON'u kendisi çözmek zorunda kalırdı — özeti bir kez üretip saklamak,
//  okuyan bütün tarafları basitleştiriyor.
//
//  ESKİ KAYITLAR: mesai_plani'si NULL olan Ödev 12 kayıtları hâlâ geçerli;
//  metinleri olduğu gibi duruyor ve düzenlenmek istendiğinde
//  EskiMetindenCoz() eski biçimi ("09:00 - 18:00", "7/24") plana çeviriyor.
// ============================================================================

/// <summary>Mesai planının türü — arayüzdeki üç kip.</summary>
public static class MesaiTipi
{
    /// <summary>Gün gün serbest tanım (varsayılan).</summary>
    public const string Haftalik = "haftalik";

    /// <summary>7/24 açık; gün listesi dikkate alınmaz.</summary>
    public const string Surekli = "surekli";

    /// <summary>
    /// Resmî kurum kipi: hafta içi sabit saat, hafta sonu kapalı ve resmî
    /// tatillerde kapalı. Ödev 13 / Madde 3'ün istediği kip.
    /// </summary>
    public const string ResmiKurum = "resmi";

    public static readonly string[] Tumu = { Haftalik, Surekli, ResmiKurum };
}

/// <summary>Haftanın tek bir gününün mesaisi.</summary>
public class MesaiGunu
{
    /// <summary>
    /// Gün numarası: 1 = Pazartesi … 7 = Pazar (ISO-8601 sırası).
    ///
    /// .NET'in <see cref="DayOfWeek"/> sayacı pazardan başlıyor (Sunday = 0);
    /// Türkçe takvimde hafta pazartesi başlar. Kendi numaramızı kullanıp
    /// dönüşümü tek yerde (<see cref="MesaiPlani.GunNo"/>) yapıyoruz.
    /// </summary>
    [JsonPropertyName("gun")]
    public int Gun { get; set; }

    /// <summary>Bu gün açık mı? Kapalıysa saatler yok sayılır.</summary>
    [JsonPropertyName("acik")]
    public bool Acik { get; set; }

    /// <summary>Açılış saati — "09:00". Kapalı günde null.</summary>
    [JsonPropertyName("acilis")]
    public string? Acilis { get; set; }

    /// <summary>Kapanış saati — "18:00". Kapalı günde null.</summary>
    [JsonPropertyName("kapanis")]
    public string? Kapanis { get; set; }
}

/// <summary>
/// Bir POI'nin haftalık çalışma planı. Veritabanında <c>poi.mesai_plani</c>
/// kolonunda JSON (jsonb) olarak duruyor.
/// </summary>
public class MesaiPlani
{
    /// <summary>Kısa gün adları — özet metinde ve arayüzde aynısı kullanılıyor.</summary>
    public static readonly string[] GunAdlari =
        { "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz" };

    [JsonPropertyName("tip")]
    public string Tip { get; set; } = MesaiTipi.Haftalik;

    /// <summary>
    /// Resmî tatillerde kapalı mı? (Ödev 13 / Madde 3)
    ///
    /// Resmî kurum kipinde ZORUNLU olarak true — bir kurumun 29 Ekim'de açık
    /// olması diye bir şey yok. Diğer kiplerde kullanıcının seçimi: market
    /// bayramda açık olabilir, vergi dairesi olamaz.
    /// </summary>
    [JsonPropertyName("resmiTatilKapali")]
    public bool ResmiTatilKapali { get; set; }

    /// <summary>
    /// Yedi gün. <see cref="MesaiTipi.Surekli"/> kipinde de dolu tutuluyor ki
    /// kullanıcı 7/24 kutusunu açıp kapattığında saatlerini kaybetmesin.
    /// </summary>
    [JsonPropertyName("gunler")]
    public List<MesaiGunu> Gunler { get; set; } = new();

    // ----------------------------------------------------------------------
    //  Hazır şablonlar
    // ----------------------------------------------------------------------

    /// <summary>Hafta içi 09:00–18:00, hafta sonu kapalı — formun açılış hâli.</summary>
    public static MesaiPlani Varsayilan()
        => HaftaIci("09:00", "18:00", MesaiTipi.Haftalik, tatilKapali: false);

    /// <summary>
    /// Resmî kurum planı: hafta içi 08:00–17:00, hafta sonu ve resmî tatillerde
    /// kapalı. Arayüz "Resmî kurum" seçildiğinde bu şablonu yüklüyor; kullanıcı
    /// saatleri sonradan değiştirebiliyor (kurumdan kuruma 30 dakika oynuyor).
    /// </summary>
    public static MesaiPlani ResmiKurum()
        => HaftaIci("08:00", "17:00", MesaiTipi.ResmiKurum, tatilKapali: true);

    private static MesaiPlani HaftaIci(string acilis, string kapanis, string tip, bool tatilKapali)
        => new()
        {
            Tip = tip,
            ResmiTatilKapali = tatilKapali,
            Gunler = Enumerable.Range(1, 7).Select(g => new MesaiGunu
            {
                Gun = g,
                Acik = g <= 5,
                Acilis = g <= 5 ? acilis : null,
                Kapanis = g <= 5 ? kapanis : null,
            }).ToList(),
        };

    // ----------------------------------------------------------------------
    //  Doğrulama
    // ----------------------------------------------------------------------

    /// <summary>
    /// Planı normalleştirir: tipi denetler, eksik günleri kapalı olarak
    /// tamamlar, saatleri "HH:mm" biçimine sabitler.
    ///
    /// NEDEN NORMALLEŞTİRME? İstemciden gelen JSON'a güvenmiyoruz: gün listesi
    /// eksik olabilir, sırasız gelebilir, aynı günü iki kez içerebilir. Sonradan
    /// okuyan hiçbir kod "acaba yedi gün var mı?" diye kontrol etmek zorunda
    /// kalmasın diye tek bir çıkış biçimi dayatıyoruz.
    /// </summary>
    public MesaiPlani Dogrula()
    {
        var tip = (Tip ?? string.Empty).Trim().ToLowerInvariant();
        if (!MesaiTipi.Tumu.Contains(tip))
        {
            throw new IsKuraliException(
                $"Bilinmeyen mesai tipi: \"{Tip}\". Beklenen: {string.Join(", ", MesaiTipi.Tumu)}.");
        }

        // Aynı gün iki kez gelirse İLKİ geçerli; sessizce ikincisini almak
        // kullanıcının gördüğü ilk satırla çelişirdi.
        var gelen = new Dictionary<int, MesaiGunu>();
        foreach (var gun in Gunler ?? new List<MesaiGunu>())
        {
            gelen.TryAdd(gun.Gun, gun);
        }

        var gunler = new List<MesaiGunu>();
        for (var gun = 1; gun <= 7; gun++)
        {
            if (!gelen.TryGetValue(gun, out var kayit) || !kayit.Acik)
            {
                gunler.Add(new MesaiGunu { Gun = gun, Acik = false });
                continue;
            }

            var acilis = SaatiDogrula(kayit.Acilis, gun, "açılış");
            var kapanis = SaatiDogrula(kayit.Kapanis, gun, "kapanış");

            // Eşit saat "sıfır dakika açık" demek olurdu — kullanıcı büyük
            // ihtimalle günü kapatmak istiyordu. Ters sıra (18:00–09:00) gece
            // devam eden mesai gibi görünür ama bu proje onu modellemiyor;
            // sessizce kabul etmek yanlış veriyi kaydetmek olurdu.
            if (string.CompareOrdinal(acilis, kapanis) >= 0)
            {
                throw new IsKuraliException(
                    $"{GunAdlari[gun - 1]} günü için kapanış saati açılıştan sonra olmalı " +
                    $"({acilis} – {kapanis}). Gün boyu kapalıysa günü kapalı işaretleyin.");
            }

            gunler.Add(new MesaiGunu { Gun = gun, Acik = true, Acilis = acilis, Kapanis = kapanis });
        }

        return new MesaiPlani
        {
            Tip = tip,
            // Resmî kurumda seçenek yok: tatil kapalılığı kipin tanımının parçası.
            ResmiTatilKapali = tip == MesaiTipi.ResmiKurum || ResmiTatilKapali,
            Gunler = gunler,
        };
    }

    /// <summary>"9:5" gibi girdileri de kabul edip "09:05"e sabitler.</summary>
    private static string SaatiDogrula(string? ham, int gun, string alan)
    {
        if (string.IsNullOrWhiteSpace(ham))
        {
            throw new IsKuraliException(
                $"{GunAdlari[gun - 1]} günü açık işaretlendi ama {alan} saati boş.");
        }

        var parcalar = ham.Trim().Split(':');
        if (parcalar.Length != 2
            || !int.TryParse(parcalar[0], out var saat)
            || !int.TryParse(parcalar[1], out var dakika)
            || saat is < 0 or > 23
            || dakika is < 0 or > 59)
        {
            throw new IsKuraliException(
                $"{GunAdlari[gun - 1]} günü {alan} saati geçersiz: \"{ham}\". " +
                "Beklenen biçim: 09:00.");
        }

        return $"{saat:00}:{dakika:00}";
    }

    // ----------------------------------------------------------------------
    //  Özet metin
    // ----------------------------------------------------------------------

    /// <summary>
    /// İnsanın okuyacağı özet:
    /// <c>"Pzt-Cum 09:00-18:00 · Cmt 10:00-14:00 · Paz kapalı"</c>
    ///
    /// Aynı saatlere sahip ARDIŞIK günler tek grupta toplanıyor. Yedi günü tek
    /// tek yazmak 200 karakterlik kolona sığardı ama liste okunmaz olurdu;
    /// insanlar mesaiyi zaten "hafta içi şu, cumartesi bu" diye düşünüyor.
    /// </summary>
    public string OzetMetin()
    {
        if (Tip == MesaiTipi.Surekli)
        {
            // 7/24 açık bir yerin "resmî tatilde kapalı" olması çelişki değil
            // (nöbetçi eczane değil de kamu wifi noktası gibi düşünün), ama
            // ödevin ilgilendiği kip bu değil; özeti sade tutuyoruz.
            return ResmiTatilKapali ? "7/24 · resmî tatillerde kapalı" : "7/24";
        }

        var gunler = (Gunler ?? new List<MesaiGunu>()).OrderBy(g => g.Gun).ToList();

        if (gunler.Count == 0 || gunler.All(g => !g.Acik))
        {
            return "Kapalı";
        }

        var parcalar = new List<string>();
        var i = 0;

        while (i < gunler.Count)
        {
            var bas = i;
            var anahtar = Anahtar(gunler[i]);

            // Aynı saatleri taşıyan ardışık günleri tek grup say.
            while (i + 1 < gunler.Count && Anahtar(gunler[i + 1]) == anahtar)
            {
                i++;
            }

            var adAraligi = bas == i
                ? GunAdlari[gunler[bas].Gun - 1]
                : $"{GunAdlari[gunler[bas].Gun - 1]}-{GunAdlari[gunler[i].Gun - 1]}";

            parcalar.Add(gunler[bas].Acik
                ? $"{adAraligi} {gunler[bas].Acilis}-{gunler[bas].Kapanis}"
                : $"{adAraligi} kapalı");

            i++;
        }

        if (ResmiTatilKapali)
        {
            parcalar.Add("resmî tatillerde kapalı");
        }

        return string.Join(" · ", parcalar);
    }

    private static string Anahtar(MesaiGunu gun)
        => gun.Acik ? $"{gun.Acilis}-{gun.Kapanis}" : "kapali";

    // ----------------------------------------------------------------------
    //  "Şu an açık mı?"
    // ----------------------------------------------------------------------

    /// <summary>
    /// Verilen ana göre POI açık mı, değilse ne zaman açılıyor?
    ///
    /// YAPISAL KOLONUN ASIL GEREKÇESİ BU METOT. Özet metin ("Pzt-Cum
    /// 09:00-18:00") insana bilgi verir ama makine ondan "salı 14:00'te açık
    /// mı?" sorusunu cevaplayamaz. <c>mesai_plani</c> kolonunu açmamızın
    /// sebebi tam olarak bu soruydu; burada karşılığını üretiyoruz.
    ///
    /// SAAT DİLİMİ ÇAĞIRANIN SORUMLULUĞU: metot kendisi <c>DateTime.Now</c>
    /// okumuyor, zamanı parametre alıyor. Böylece test edilebiliyor (sabit bir
    /// an verilebiliyor) ve sunucunun UTC'sini yanlışlıkla yerel saat sanma
    /// hatası imkânsız hâle geliyor — çağıran <see cref="TurkiyeZamani"/>'ni
    /// kullanmak zorunda.
    /// </summary>
    /// <param name="yerelZaman">Türkiye saatiyle "şu an".</param>
    /// <param name="bugunkuTatil">Bugün resmî tatilse o kayıt; değilse null.</param>
    public MesaiDurumu Durum(DateTime yerelZaman, ResmiTatil? bugunkuTatil)
    {
        var saat = TimeOnly.FromDateTime(yerelZaman);

        // ---- 1) Resmî tatil, her şeyin önünde ----
        if (bugunkuTatil is not null && ResmiTatilKapali)
        {
            // Yarım günde öğleden ÖNCE açık: kanundaki tatil 13.00'te başlıyor.
            // Bunu atlayıp bütün günü kapalı saymak, 28 Ekim sabahı açık olan
            // bir kurumu kapalı göstermek olurdu.
            if (bugunkuTatil.YarimGun && GununPlani(yerelZaman) is { Acik: true } yarimGun
                && saat < YarimGunSiniri
                && TimeOnly.Parse(yarimGun.Acilis!) <= saat)
            {
                return new MesaiDurumu(true, $"Açık · {YarimGunSiniri:HH\\:mm}'de kapanıyor ({bugunkuTatil.Ad})");
            }

            return new MesaiDurumu(false, $"Kapalı · {bugunkuTatil.Ad}");
        }

        // ---- 2) 7/24 ----
        if (Tip == MesaiTipi.Surekli)
        {
            return new MesaiDurumu(true, "Açık · 7/24");
        }

        // ---- 3) Bugünün planı ----
        var bugun = GununPlani(yerelZaman);

        if (bugun is { Acik: true })
        {
            var acilis = TimeOnly.Parse(bugun.Acilis!);
            var kapanis = TimeOnly.Parse(bugun.Kapanis!);

            if (saat < acilis)
            {
                return new MesaiDurumu(false, $"Kapalı · bugün {acilis:HH\\:mm} açılıyor");
            }

            if (saat < kapanis)
            {
                return new MesaiDurumu(true, $"Açık · {kapanis:HH\\:mm} kapanıyor");
            }
        }

        // ---- 4) Kapalı: sıradaki açılış ne zaman? ----
        return new MesaiDurumu(false, SonrakiAcilis(yerelZaman));
    }

    /// <summary>Kanundaki yarım gün tatillerinin başlangıcı (13.00).</summary>
    private static readonly TimeOnly YarimGunSiniri = new(13, 0);

    private MesaiGunu? GununPlani(DateTime zaman)
        => Gunler?.FirstOrDefault(g => g.Gun == GunNo(zaman.DayOfWeek));

    /// <summary>
    /// Bugünden sonraki ilk açık günü bulur ve "Kapalı · yarın 09:00 açılıyor"
    /// gibi bir metin üretir.
    ///
    /// Yedi günü de kapalıysa "Kapalı" deyip bırakıyoruz: olmayan bir açılışı
    /// uydurmaktansa bilgiyi eksik bırakmak doğru.
    ///
    /// NOT: resmî tatiller BURADA hesaba katılmıyor. Katmak için sıradaki yedi
    /// günün tatil listesini de taşımak gerekirdi; kazanç ("aslında yarın da
    /// bayram") küçük, maliyet (metodun tatil takvimine bağımlı hâle gelmesi)
    /// büyük. Bugünün tatili zaten yukarıda ele alınıyor.
    /// </summary>
    private string SonrakiAcilis(DateTime yerelZaman)
    {
        var bugunNo = GunNo(yerelZaman.DayOfWeek);

        for (var ileri = 1; ileri <= 7; ileri++)
        {
            var gunNo = ((bugunNo - 1 + ileri) % 7) + 1;
            var gun = Gunler?.FirstOrDefault(g => g.Gun == gunNo);

            if (gun is not { Acik: true }) continue;

            var ad = ileri == 1 ? "yarın" : GunAdlari[gunNo - 1];
            return $"Kapalı · {ad} {gun.Acilis} açılıyor";
        }

        return "Kapalı";
    }

    // ----------------------------------------------------------------------
    //  JSON ve eski biçim
    // ----------------------------------------------------------------------

    private static readonly JsonSerializerOptions Secenekler = new()
    {
        WriteIndented = false,
    };

    public string Serilestir() => JsonSerializer.Serialize(this, Secenekler);

    /// <summary>
    /// Kolondaki JSON'u plana çevirir. Kolon boşsa null döner.
    ///
    /// Bozuk JSON'da ÇÖKMÜYORUZ: kolonun içeriği elle değiştirilmiş olabilir ve
    /// tek bir kaydın bozuk mesaisi yüzünden bütün POI listesinin 500 vermesi
    /// orantısız olurdu. O durumda plan "yok" sayılıyor, özet metin yerinde kalıyor.
    /// </summary>
    public static MesaiPlani? Coz(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<MesaiPlani>(json, Secenekler);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ödev 12'den kalan metni plana çevirir: "7/24" ve "09:00 - 18:00"
    /// biçimlerini tanır, tanımadığını null bırakır.
    ///
    /// Bu köprü olmasaydı eski kayıtlar yeni formda "hiç mesai girilmemiş" gibi
    /// açılır, kullanıcı kaydettiğinde mevcut bilgi sessizce silinirdi.
    /// </summary>
    public static MesaiPlani? EskiMetindenCoz(string? metin)
    {
        if (string.IsNullOrWhiteSpace(metin)) return null;

        var temiz = metin.Trim();
        if (temiz == "7/24")
        {
            var surekli = Varsayilan();
            surekli.Tip = MesaiTipi.Surekli;
            return surekli;
        }

        var eslesme = Regex.Match(temiz, @"^(\d{1,2}:\d{2})\s*-\s*(\d{1,2}:\d{2})$");
        if (!eslesme.Success) return null;

        // Eski biçim gün ayrımı taşımıyordu: her gün aynı saat kabul ediliyordu.
        // Aynen öyle çeviriyoruz — "hafta sonu herhâlde kapalıdır" diye bir
        // varsayım eklemek, kullanıcının girmediği bir bilgiyi uydurmak olurdu.
        return new MesaiPlani
        {
            Tip = MesaiTipi.Haftalik,
            ResmiTatilKapali = false,
            Gunler = Enumerable.Range(1, 7).Select(g => new MesaiGunu
            {
                Gun = g,
                Acik = true,
                Acilis = eslesme.Groups[1].Value,
                Kapanis = eslesme.Groups[2].Value,
            }).ToList(),
        };
    }

    /// <summary>ISO gün numarası (1 = Pazartesi) — .NET'in DayOfWeek'inden çevirir.</summary>
    public static int GunNo(DayOfWeek gun) => gun == DayOfWeek.Sunday ? 7 : (int)gun;
}
