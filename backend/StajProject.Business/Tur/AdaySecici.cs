using StajProject.DataAccess.Google;
using StajProject.Entities;

namespace StajProject.Business.Tur;

/// <summary>
/// Öneriye girmeye aday bir mekan: Google kaydı + turdaki karşılığı.
/// </summary>
/// <param name="Mekan">Places'ten gelen ham kayıt.</param>
/// <param name="Tip">Turdaki mekan tipi (kalış süresini belirler).</param>
/// <param name="KalisDakika">Atanan tahmini kalış süresi.</param>
public record AdayMekan(GooglePlace Mekan, VenueType Tip, int KalisDakika)
{
    /// <summary>
    /// Durağın ROLÜNÜ açıklayan not ("Yemek molası", "Önerilen konaklama
    /// bölgesi"). Sıradan gezi duraklarında null.
    ///
    /// Konumsal bir bilgi değil, bir ETİKET: aynı restoran bir turda mola
    /// durağı, başka bir turda (Gastronomi teması) sıradan bir durak
    /// olabiliyor. Bu yüzden mekanın kendisinde değil, seçimde duruyor.
    /// </summary>
    public string? Not { get; init; }

    /// <summary>
    /// Sıralama puanı — sağlayıcının verdiği ÖNEM işareti (0-1).
    ///
    /// NEDEN DOĞRUDAN PUAN DEĞİL? İki sebep:
    ///   1. 4.5 alt sınırından sonra bütün adaylar 4.5-5.0 arasında sıkışıyor;
    ///      aradaki 0.1, 40 oylu bir yerle 12.000 oylu Anıtkabir'i yan yana
    ///      koyardı. Google istemcisi bu yüzden oy sayısını da katıyor.
    ///   2. Anahtarsız kipte (OpenStreetMap) kullanıcı puanı HİÇ YOK. Skoru
    ///      puana bağlasaydık o kipte bütün adaylar sıfır alır, seçim
    ///      rastgeleye dönerdi.
    ///
    /// Hangi sinyalden üretildiği sağlayıcının işi (bkz. GooglePlace.Onem);
    /// buranın tek bildiği "büyük olan daha önemli".
    /// </summary>
    public double Skor => Mekan.Onem;
}

/// <summary>Bütçeye sığdırma sonucu.</summary>
/// <param name="Duraklar">Bütçeye sığan duraklar, SIRALI.</param>
/// <param name="YolDakika">Duraklar arası toplam seyahat süresi.</param>
/// <param name="KalisDakika">Duraklardaki toplam kalış süresi.</param>
/// <param name="Kirpildi">Bütçeye sığmadığı için sondan durak atıldı mı?</param>
public record ButceSonucu(
    IReadOnlyList<AdayMekan> Duraklar,
    int YolDakika,
    int KalisDakika,
    bool Kirpildi)
{
    public int ToplamDakika => YolDakika + KalisDakika;
}

/// <summary>
/// ADAY SEÇİMİ ve SÜRE BÜTÇESİ — saf hesap, dış servis yok.
///
/// ---- NEDEN SERVİSTEN AYRI? ----
/// Bu dosyadaki kararlar ("kaç mekan sorulacak", "hangi durak listeden
/// düşecek") turun kalitesini belirleyen kararlar ve HTTP taklidi kurmadan
/// sınanabilmeleri gerekiyor. Servis yalnızca sıralamayı yönetiyor:
/// ara → seç → Google'a sırala → bütçeye sığdır.
///
/// ---- BURASI AYNI ZAMANDA BİR KOTA KORUMASI ----
/// <see cref="TahminiKapasite"/>, Directions'a gönderilecek nokta sayısını
/// süreye göre ÖNCEDEN kısıyor. Olmasaydı 60 aday bulunan bir şehirde istek
/// ara nokta sınırını aşar, cevap MAX_WAYPOINTS_EXCEEDED ile döner ve o istek
/// de faturalanırdı — hiçbir işe yaramadan.
/// </summary>
public static class AdaySecici
{
    /// <summary>
    /// Duraklar arası ORTALAMA seyahat süresi tahmini (dakika).
    ///
    /// Yalnızca "kaç durak sığar" sorusunun ön hesabında kullanılıyor; gerçek
    /// süreler Directions'tan geliyor. Kip başına ayrı: yayanın iki durak
    /// arası 12 dakika, aracın 8, toplu taşımanın 20.
    /// </summary>
    private static int OrtalamaYolDakika(SeyahatKipi kip) => kip switch
    {
        SeyahatKipi.Yaya => 12,
        SeyahatKipi.Arac => 8,
        SeyahatKipi.ToplaTasima => 20,
        _ => 10,
    };

    /// <summary>
    /// Verilen süreye kaç durak sığar? (kaba tahmin)
    ///
    /// Ortalama bir durağın maliyeti = kalış + bir bacak yolculuğu. Tahmin
    /// KASITLI OLARAK CÖMERT (bir fazlası isteniyor): eksik istemek, bütçeye
    /// yer kalmışken turu kısa kesmek olurdu. Fazlası zaten bütçe adımında
    /// kırpılıyor.
    /// </summary>
    /// <param name="toplamDakika">Kullanıcının verdiği toplam süre.</param>
    /// <param name="ortalamaKalis">Temanın ortalama kalış süresi.</param>
    /// <param name="kip">Seyahat kipi — ortalama bacak süresi buradan.</param>
    /// <param name="ustSinir">Directions'ın kabul ettiği nokta sayısı sınırı.</param>
    public static int TahminiKapasite(int toplamDakika, int ortalamaKalis, SeyahatKipi kip, int ustSinir)
    {
        var durakMaliyeti = Math.Max(5, ortalamaKalis + OrtalamaYolDakika(kip));
        var kapasite = (int)Math.Ceiling((double)toplamDakika / durakMaliyeti) + 1;

        return Math.Clamp(kapasite, 2, ustSinir);
    }

    /// <summary>
    /// Aday seçiminin sınırları.
    /// </summary>
    /// <param name="EnAzAralikMetre">
    /// İki durak arasındaki en az mesafe.
    ///
    /// CANLIDA ÇIKAN HATA: Ankara turuna tek bir anıtın BEŞ KULESİ ayrı durak
    /// olarak girdi (aralarında 57 m, 59 m, 107 m…). Ekranda "15 durak" yazan
    /// ama aslında birkaç noktayı gezen bir program çıkıyordu. 300 m,
    /// yürüyerek "aynı yer" sayılacak mesafenin üst sınırı.
    /// </param>
    /// <param name="KompleksAralikMetre">
    /// Bu mesafe içinde ADI BENZEŞEN kayıtlar tek yer sayılıyor — aynı
    /// külliyenin parçaları (avlu, türbe, çeşme) ayrı ayrı listelenmesin.
    /// </param>
    /// <param name="TipTavani">
    /// Mekan tipi başına en fazla kaç durak. Olmasaydı liste tek tipe
    /// kayıyordu: puanlama müzeleri öne aldığı için 12 durağın 9'u müze
    /// çıkıyordu (yine canlıda görüldü).
    /// </param>
    public record SecimKurallari(
        int EnAzAralikMetre = 300,
        int KompleksAralikMetre = 800,
        IReadOnlyDictionary<VenueType, int>? TipTavani = null);

    /// <summary>
    /// Varsayılan tip tavanları — dengeli bir gezi listesi.
    ///
    /// Müze 4: yarım günün büyük kısmı müzede geçmesin. Park 2, seyir 1:
    /// bunlar turun "nefes alma" durakları, ana gövdesi değil.
    /// </summary>
    private static readonly Dictionary<VenueType, int> VarsayilanTavan = new()
    {
        [VenueType.Museum] = 4,
        [VenueType.Monument] = 6,
        [VenueType.ReligiousSite] = 3,
        [VenueType.Park] = 2,
        [VenueType.Viewpoint] = 1,
        [VenueType.Restaurant] = 2,
        [VenueType.Cafe] = 1,
        [VenueType.Shopping] = 1,
        [VenueType.Other] = 1,
    };

    /// <summary>
    /// Adayları ÖNEM SIRASINA göre seçer; yakın/tekrarlı olanları eler.
    ///
    /// ---- NEDEN ÖNCE ÖNEM, SONRA ÇEŞİTLİLİK? ----
    /// Önceki sürüm tipler arasında sırayla ilerliyordu (bir müze, bir park,
    /// bir anıt…). Amaç çeşitlilikti ama sonuç şuydu: her tipin EN İYİSİ
    /// değil, sırası gelen alınıyordu ve Anadolu Medeniyetleri Müzesi listeye
    /// hiç girmiyordu. Şimdi sıralama tamamen öneme göre; çeşitlilik
    /// TAVANLARLA sağlanıyor — yani "en iyiyi al, ama aynı tipten dördü
    /// yeter" diyerek.
    ///
    /// ---- ÜÇ ELEME ----
    ///   1. Aynı PlaceId (aynı mekan iki aramadan gelebiliyor)
    ///   2. Çok yakın kayıtlar — tek bir anıtın parçaları
    ///   3. Tip tavanı — liste tek tipe kaymasın
    /// </summary>
    public static IReadOnlyList<AdayMekan> Sec(
        IEnumerable<AdayMekan> adaylar,
        int enFazla,
        SecimKurallari? kurallar = null)
    {
        if (enFazla <= 0)
        {
            return Array.Empty<AdayMekan>();
        }

        var kural = kurallar ?? new SecimKurallari();
        var tavan = kural.TipTavani ?? VarsayilanTavan;

        var sirali = adaylar
            .GroupBy(a => a.Mekan.PlaceId, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(a => a.Skor).First())
            .OrderByDescending(a => a.Skor)
            .ToList();

        var secilen = new List<AdayMekan>();
        var tipSayaci = new Dictionary<VenueType, int>();

        foreach (var aday in sirali)
        {
            if (secilen.Count >= enFazla) break;

            if (tipSayaci.GetValueOrDefault(aday.Tip) >= tavan.GetValueOrDefault(aday.Tip, int.MaxValue))
            {
                continue;
            }

            if (secilen.Any(s => AyniYerMi(aday, s, kural)))
            {
                continue;
            }

            secilen.Add(aday);
            tipSayaci[aday.Tip] = tipSayaci.GetValueOrDefault(aday.Tip) + 1;
        }

        return secilen;
    }

    /// <summary>
    /// İki aday pratikte AYNI YER mi?
    ///
    /// İki ölçüt: çok yakınlar, ya da yakın sayılabilecek mesafede olup
    /// adlarında ortak bir ÖZEL sözcük paylaşıyorlar ("Hisar Camii" ile
    /// "Hisar Çeşmesi"). "Camii", "Parkı", "Müzesi" gibi genel sözcükler
    /// sayılmıyor — yoksa şehirdeki bütün camiler aynı yer olurdu.
    /// </summary>
    private static bool AyniYerMi(AdayMekan a, AdayMekan b, SecimKurallari kural)
    {
        var mesafe = MesafeMetre(a, b);

        if (mesafe < kural.EnAzAralikMetre) return true;

        if (mesafe < kural.KompleksAralikMetre)
        {
            var ortak = Kelimeler(a.Mekan.Ad).Intersect(Kelimeler(b.Mekan.Ad)).Any();
            if (ortak) return true;
        }

        return false;
    }

    /// <summary>
    /// Ad benzerliğinde SAYILMAYAN sözcükler.
    ///
    /// Tur duraklarının adları bu sözcüklerle dolu; hesaba katsaydık
    /// "Gençlik Parkı" ile "Kuğulu Park" aynı yer sayılırdı.
    /// </summary>
    private static readonly HashSet<string> GenelSozcukler = new(StringComparer.OrdinalIgnoreCase)
    {
        "camii", "cami", "müzesi", "muzesi", "müze", "anıtı", "aniti", "anit",
        "parkı", "parki", "park", "kulesi", "kule", "meydanı", "meydani",
        "kültür", "kultur", "merkezi", "sarayı", "sarayi", "köşkü", "kosku",
        "türbesi", "turbesi", "kalesi", "hamamı", "hamami", "bahçesi", "bahcesi",
        "ve", "ile",
    };

    /// <summary>Addaki AYIRT EDİCİ sözcükler (3 harften uzun, genel olmayan).</summary>
    private static IEnumerable<string> Kelimeler(string ad)
        => ad.Split(new[] { ' ', '-', ',', '.', '(', ')', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(k => k.Length > 3 && !GenelSozcukler.Contains(k))
            .Select(k => k.ToLowerInvariant());

    /// <summary>İki aday arasındaki kuş uçuşu mesafe (metre) — haversine.</summary>
    private static double MesafeMetre(AdayMekan a, AdayMekan b)
    {
        const double yaricap = 6_371_000;
        static double Rad(double derece) => derece * Math.PI / 180;

        var dLat = Rad(b.Mekan.Lat - a.Mekan.Lat);
        var dLon = Rad(b.Mekan.Lon - a.Mekan.Lon);

        var h = Math.Pow(Math.Sin(dLat / 2), 2)
              + Math.Cos(Rad(a.Mekan.Lat)) * Math.Cos(Rad(b.Mekan.Lat)) * Math.Pow(Math.Sin(dLon / 2), 2);

        return 2 * yaricap * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>
    /// Sıralanmış durakları TOPLAM SÜREYE sığdırır.
    ///
    /// Sıra Google'ın optimize ettiği sıradır; buradaki iş yalnızca "nereye
    /// kadar yetiyor" sorusunu cevaplamak. Baştan ilerleniyor ve bütçeyi aşan
    /// ilk duraktan itibaren KUYRUK ATILIYOR.
    ///
    /// NEDEN "en pahalı durağı at" gibi bir seçim değil? Rota sırası
    /// coğrafi: ortadan bir durak atmak, ondan sonraki bacakların
    /// sürelerini geçersiz kılardı (yeni bacak A→C'nin süresi elimizde yok) ve
    /// bunu öğrenmek yeni bir Directions isteği demekti. Kuyruğu atmak,
    /// elimizdeki bacak sürelerinin tamamını geçerli bırakan tek kırpma
    /// biçimi.
    /// </summary>
    /// <param name="sirali">Optimize edilmiş sırayla duraklar.</param>
    /// <param name="bacaklar">Duraklar arası süreler (sirali.Count - 1 adet).</param>
    /// <param name="toplamDakika">Kullanıcının verdiği bütçe.</param>
    /// <param name="sureCarpani">Toplu taşıma düzeltmesi gibi çarpanlar.</param>
    public static ButceSonucu ButceyeSigdir(
        IReadOnlyList<AdayMekan> sirali,
        IReadOnlyList<DirectionsBacagi> bacaklar,
        int toplamDakika,
        double sureCarpani = 1.0)
    {
        var tutulan = new List<AdayMekan>();
        var yol = 0.0;
        var kalis = 0.0;

        for (var i = 0; i < sirali.Count; i++)
        {
            // İlk durak dışında, önceki duraktan buraya gelmenin süresi.
            var bacak = i == 0
                ? 0
                : (i - 1 < bacaklar.Count ? bacaklar[i - 1].SureSaniye / 60.0 * sureCarpani : 0);

            var yeniYol = yol + bacak;
            var yeniKalis = kalis + sirali[i].KalisDakika;

            if (yeniYol + yeniKalis > toplamDakika)
            {
                // Bu durak sığmıyor → burada ve sonrasında duruyoruz.
                return new ButceSonucu(tutulan, (int)Math.Round(yol), (int)Math.Round(kalis), true);
            }

            tutulan.Add(sirali[i]);
            yol = yeniYol;
            kalis = yeniKalis;
        }

        return new ButceSonucu(tutulan, (int)Math.Round(yol), (int)Math.Round(kalis), false);
    }

    /// <summary>
    /// Google'ın <c>waypoint_order</c> çıktısını gerçek sıraya uygular.
    ///
    /// Dizideki değerler ARA NOKTALARIN indisleri; başlangıç ve varış
    /// yerlerinde kalıyor. Yani [B,C,D] ara noktaları için gelen [2,0,1],
    /// "A → D → B → C → E" demek.
    ///
    /// Bozuk/eksik bir sıra gelirse (indis taşması, tekrar) GİRDİ SIRASI
    /// korunuyor: yanlış eşlenmiş bir sıra, durakları birbirinin koordinatına
    /// oturtan sessiz bir hataya dönüşürdü.
    /// </summary>
    public static IReadOnlyList<AdayMekan> SirayiUygula(
        IReadOnlyList<AdayMekan> baslangicSirasi,
        IReadOnlyList<int> waypointOrder)
    {
        var araSayisi = baslangicSirasi.Count - 2;

        if (araSayisi <= 0 || waypointOrder.Count != araSayisi)
        {
            return baslangicSirasi;
        }

        if (waypointOrder.Distinct().Count() != araSayisi
            || waypointOrder.Any(i => i < 0 || i >= araSayisi))
        {
            return baslangicSirasi;
        }

        var sonuc = new List<AdayMekan>(baslangicSirasi.Count) { baslangicSirasi[0] };
        sonuc.AddRange(waypointOrder.Select(i => baslangicSirasi[i + 1]));
        sonuc.Add(baslangicSirasi[^1]);

        return sonuc;
    }
}
