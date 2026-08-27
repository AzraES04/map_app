using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using StajProject.Business.Mesai;
using StajProject.Entities;

namespace StajProject.Business.Services;

// ============================================================================
//  Ödev 14 — ANALİZ VERİ SETİ
//
//  Ödev metni: "Bu analiz için POI verisi oluşturun, ne kadar çok veri olursa
//  o kadar iyi sonuç verecektir."
//
//  Isı haritası bir YOĞUNLUK yüzeyidir; dört örnek POI ile üretilen yüzey dört
//  ayrı leke olur ve "hangi bölge daha uygun?" sorusunun görünür bir cevabı
//  olmaz. Bu sınıf, 81 ilin gerçek sınırlarını kullanarak binlerce POI üretiyor.
//
//  ÜÇ TASARIM KARARI:
//
//  1) VERİ RASTGELE DEĞİL, KÜMELİ. Her ilde birkaç "odak" (şehir merkezi,
//     ikincil merkezler) seçiliyor ve POI'ler bu odakların çevresine Gauss
//     dağılımıyla saçılıyor. İl sınırının içine düzgün (uniform) dağıtsaydık
//     yoğunluk her yerde aynı çıkar, ısı haritası tek renkli bir örtüye
//     dönüşürdü — yani analiz "çalışır" ama hiçbir şey göstermezdi.
//
//  2) KATEGORİLER FARKLI DAVRANIYOR. Eczane ve fırın çok sayıda ve merkeze
//     yakın; hastane az sayıda ve dağınık; okul her yerde. Böylece iki farklı
//     kriter seçildiğinde yüzeyler GERÇEKTEN farklı çıkıyor ve ağırlık
//     değiştirmenin etkisi ekranda görülüyor. Ödevin asıl göstermek istediği
//     şey bu.
//
//  3) TOHUM SABİT. <see cref="Tohum"/> ile aynı veri her kurulumda birebir
//     aynı üretiliyor. Jüri önünde "bende farklı çıkmıştı" durumu olmasın
//     diye: sunumda gösterilen ısı haritası, hocanın kendi makinesinde
//     kurduğunda da aynı çıkacak.
//
//  KOORDİNATLAR NEREDEN? Elle yazılmış bir şehir listesi YOK. Ödev 10'la gelen
//  <c>iller</c> tablosunun gerçek sınır geometrileri kullanılıyor: odaklar da
//  POI'ler de sınırın içinde olacak şekilde seçiliyor. Elle koordinat
//  yazsaydık hem 81 satırlık bir tablo bakımı çıkardı hem de üretilen nokta
//  sınırın dışına düştüğünde kimse fark etmezdi.
// ============================================================================

/// <summary>
/// Analiz için gerçekçi bir POI veri seti üretir. Veritabanına dokunmaz;
/// yalnızca <see cref="Poi"/> nesneleri döndürür — yazma işi seeder'ın.
/// </summary>
public static class AnalizPoiUretici
{
    /// <summary>
    /// Rastgeleliğin tohumu. Sabit olması ÜRETİLEN VERİYİ TEKRARLANABİLİR
    /// kılıyor (bkz. dosya başlığı, 3. karar).
    /// </summary>
    public const int Tohum = 20260825;

    /// <summary>
    /// Veri seti bu sayıdan az POI varken üretiliyor. Sayı 4 örnek POI'nin
    /// çok üstünde, üretilen setin ise çok altında: böylece üretim bir kez
    /// çalışıyor ve ikinci açılışta kendini tekrarlamıyor.
    /// </summary>
    public const int UretimEsigi = 400;

    /// <summary>Küçük illerde üretilen POI sayısı.</summary>
    private const int TabanPoi = 14;

    /// <summary>
    /// Büyükşehirlerde eklenen fazladan POI sayısı.
    ///
    /// TABANIN ON KATINA YAKIN olması bilinçli. Ödev "ne kadar çok veri olursa
    /// o kadar iyi" diyor ama önemli olan TOPLAM sayı değil, ANALİZ EDİLEN
    /// ALANDAKİ yoğunluk: 81 ile eşit dağıtılmış on bin POI, tek bir ilin
    /// analizinde yine seyrek kalırdı. Üstelik toplamı büyütmenin bir bedeli
    /// var — harita ekranı açılışta bütün POI'leri indiriyor (tıklama hedefi
    /// olarak gerekiyor), yani her kayıt açılış süresine yazılıyor.
    ///
    /// Bu dağılım gerçeğe de daha yakın: Ankara'da Bayburt'takinden on kat
    /// çok eczane var.
    /// </summary>
    private const int BuyuksehirEki = 150;

    /// <summary>
    /// Nüfusça büyük iller (plaka). Elle tutulan tek liste ve yalnızca
    /// MİKTARI etkiliyor — konumlar yine sınır geometrisinden geliyor,
    /// yani bu liste yanlış olsa bile veri hatalı yere düşmez.
    /// </summary>
    private static readonly HashSet<int> BuyukIller = new()
    {
        34, 6, 35, 16, 7, 1, 42, 27, 41, 21, 31, 33, 44, 55, 61, 26, 20, 38, 45, 10,
    };

    /// <summary>
    /// Kategori davranış profili.
    /// </summary>
    /// <param name="Kategori">Kategori adı — seed'deki ağaçla birebir eşleşmeli.</param>
    /// <param name="Siklik">Göreli görülme sıklığı (rulet ağırlığı).</param>
    /// <param name="MerkezEgilimi">0–1: 1'e yakınsa şehir merkezine yapışır, 0'a yakınsa dağılır.</param>
    /// <param name="Yayilma">Odak çevresindeki saçılma; ilin küçük kenarına oran olarak.</param>
    /// <param name="Kaliplar">Ad şablonları — {semt} ve {il} yer tutucularıyla.</param>
    /// <param name="Mesai">Mesai planını üreten fonksiyon.</param>
    private sealed record Profil(
        string Kategori,
        int Siklik,
        double MerkezEgilimi,
        double Yayilma,
        string[] Kaliplar,
        Func<MesaiPlani> Mesai);

    private static readonly Profil[] Profiller =
    {
        new("Restoran", 16, 0.60, 0.10,
            new[] { "{semt} Kebap Salonu", "{semt} Ocakbaşı", "{semt} Lokantası",
                    "{semt} Balık Evi", "{semt} Mantı Evi", "{semt} Pide Salonu" },
            () => Plan("11:00", "23:00", haftaSonu: ("12:00", "23:30"))),

        new("Kafe", 14, 0.65, 0.09,
            new[] { "{semt} Kahvecisi", "{semt} Kafe", "Kahve Durağı {semt}",
                    "{semt} Çay Bahçesi", "{semt} Pastanesi" },
            // 24:00 değil 23:59: MesaiPlani.Dogrula saati 00–23 aralığında
            // istiyor ve gece yarısını aşan mesaiyi bu proje modellemiyor.
            () => Plan("08:00", "23:00", haftaSonu: ("09:00", "23:59"))),

        new("Fırın", 11, 0.45, 0.16,
            new[] { "{semt} Fırını", "{semt} Unlu Mamulleri", "{semt} Ekmek Evi" },
            () => Plan("06:00", "20:00", haftaSonu: ("06:30", "19:00"))),

        new("Eczane", 15, 0.50, 0.15,
            new[] { "{semt} Eczanesi", "Yeni {semt} Eczanesi", "{semt} Sağlık Eczanesi" },
            () => Plan("09:00", "19:00", haftaSonu: ("10:00", "14:00"), pazarAcik: false)),

        new("Hastane", 4, 0.35, 0.22,
            new[] { "{il} {semt} Devlet Hastanesi", "{semt} Tıp Merkezi",
                    "{il} Eğitim ve Araştırma Hastanesi" },
            Surekli),

        new("Okul", 14, 0.30, 0.24,
            new[] { "{semt} İlkokulu", "{semt} Ortaokulu", "{semt} Anadolu Lisesi",
                    "{semt} Mesleki ve Teknik Anadolu Lisesi" },
            MesaiPlani.ResmiKurum),

        new("Kütüphane", 4, 0.40, 0.20,
            new[] { "{semt} Halk Kütüphanesi", "{il} İl Halk Kütüphanesi",
                    "{semt} Çocuk Kütüphanesi" },
            MesaiPlani.ResmiKurum),

        new("Otel", 6, 0.70, 0.12,
            new[] { "{il} {semt} Otel", "Otel {semt}", "{semt} Konak Otel" },
            Surekli),

        new("Pansiyon", 4, 0.35, 0.20,
            new[] { "{semt} Pansiyon", "{semt} Konukevi", "{semt} Apart" },
            Surekli),
    };

    /// <summary>
    /// Ad şablonlarındaki {semt} yer tutucusunu dolduran mahalle/semt adları.
    /// Türkiye'de gerçekten yaygın adlar seçildi; üretilen veri listede
    /// "Test POI 417" gibi değil, gerçek bir envanter gibi okunuyor.
    /// </summary>
    private static readonly string[] Semtler =
    {
        "Cumhuriyet", "Atatürk", "İstiklal", "Bahçelievler", "Yenimahalle", "Kültür",
        "Fatih", "Barbaros", "Gazi", "Hürriyet", "Zafer", "Çamlık", "Yıldız",
        "Alparslan", "Karanfil", "Menekşe", "Şehitler", "Yeşiltepe", "Güzelyalı",
        "Pınarbaşı", "Karşıyaka", "Bağlar", "Esentepe", "Konak", "Sanayi",
        "Üniversite", "Çarşı", "Yeşilova", "Akpınar", "Gülbahçe",
    };

    /// <summary>
    /// İl sınırlarından POI listesi üretir.
    /// </summary>
    /// <param name="iller">Sınır geometrileriyle iller (81 kayıt).</param>
    /// <param name="kategoriIdleri">Kategori adı → id. Ad karşılığı olmayan profiller atlanır.</param>
    public static List<Poi> Uret(
        IReadOnlyList<Il> iller,
        IReadOnlyDictionary<string, int> kategoriIdleri)
    {
        // Tohum sabit → aynı sonuç. Random'ın kendisi de tek örnek: her il
        // için yenisini kurmak, farklı illerde aynı sayı dizisini üretirdi.
        var rastgele = new Random(Tohum);

        // Karşılığı olan profiller. Yönetici kategori ağacını değiştirdiyse
        // (örn. "Pansiyon"u sildiyse) o profil sessizce düşüyor; üretim
        // durmuyor, çünkü eksik bir kategori bu verinin varlık sebebini
        // ortadan kaldırmıyor.
        var gecerliProfiller = Profiller
            .Where(p => kategoriIdleri.ContainsKey(p.Kategori))
            .ToList();

        if (gecerliProfiller.Count == 0) return new List<Poi>();

        var sonuc = new List<Poi>();

        // Plaka sırası: üretim sırası da tekrarlanabilir olsun.
        foreach (var il in iller.OrderBy(i => i.Id))
        {
            if (il.Geom is null || il.Geom.IsEmpty) continue;

            sonuc.AddRange(IlIcinUret(il, gecerliProfiller, kategoriIdleri, rastgele));
        }

        return sonuc;
    }

    private static List<Poi> IlIcinUret(
        Il il,
        List<Profil> profiller,
        IReadOnlyDictionary<string, int> kategoriIdleri,
        Random rastgele)
    {
        var zarf = il.Geom.EnvelopeInternal;
        var olcek = Math.Min(zarf.Width, zarf.Height);
        var fabrika = il.Geom.Factory;

        // Aynı geometriye yüzlerce kez "bu nokta içeride mi?" sorulacak;
        // PreparedGeometry kenarları bir kez indeksliyor (bkz. AgirlikliIsiIzgarasi).
        var hazir = PreparedGeometryFactory.Prepare(il.Geom);

        var buyuk = BuyukIller.Contains(il.Id);
        var poiSayisi = TabanPoi + (buyuk ? BuyuksehirEki : 0);
        var odakSayisi = buyuk ? 5 : 3;

        // ---- Odaklar: ilin içinde birkaç "merkez" ----
        //
        // İlk odak ilin İÇ NOKTASI (InteriorPoint) — geometrinin garanti
        // olarak içinde kalan bir nokta. Centroid'i KULLANMIYORUZ: hilal
        // biçimli ya da çok parçalı illerde (Çanakkale, Muğla) centroid
        // denize düşebiliyor ve "şehir merkezi" oradan başlıyor sanılırdı.
        var odaklar = new List<Coordinate> { il.Geom.InteriorPoint.Coordinate };

        // Deneme sayısı SINIRLI: zarfının küçük bir kısmını dolduran ince bir
        // ilde (örn. Hatay) rastgele nokta çoğu zaman dışarı düşer ve
        // koşulsuz bir döngü tıkanırdı. Hedeflenen sayıya ulaşılamazsa daha
        // az odakla devam ediyoruz — bu, üretimi durduracak bir eksik değil.
        for (var deneme = 0; odaklar.Count < odakSayisi && deneme < 200; deneme++)
        {
            var aday = ZarftaNokta(zarf, rastgele);
            if (hazir.Intersects(fabrika.CreatePoint(aday))) odaklar.Add(aday);
        }

        var toplamSiklik = profiller.Sum(p => p.Siklik);
        var poiler = new List<Poi>(poiSayisi);
        var simdi = DateTime.UtcNow;

        for (var i = 0; i < poiSayisi; i++)
        {
            var profil = ProfilSec(profiller, toplamSiklik, rastgele);

            // MERKEZ EĞİLİMİ: zar tutarsa birinci odak (şehir merkezi),
            // tutmazsa rastgele bir odak. Restoran/otel merkeze yapışıyor,
            // okul/hastane ilin geneline dağılıyor.
            var odak = rastgele.NextDouble() < profil.MerkezEgilimi
                ? odaklar[0]
                : odaklar[rastgele.Next(odaklar.Count)];

            var sapma = profil.Yayilma * olcek;
            var nokta = OdakCevresindeNokta(odak, sapma, hazir, fabrika, rastgele);

            var plan = profil.Mesai().Dogrula();

            poiler.Add(new Poi
            {
                Isim = AdUret(profil, il.Ad, rastgele),
                KategoriId = kategoriIdleri[profil.Kategori],
                MesaiSaatleri = plan.OzetMetin(),
                MesaiPlani = plan.Serilestir(),
                Geom = fabrika.CreatePoint(nokta),

                // SAHİPSİZ (user_id = null): bu veri bir kullanıcının girdiği
                // kayıt değil, analiz için üretilmiş referans envanteri.
                // Birine yazsaydık admin panelindeki "ekleyen kullanıcı"
                // sütunu binlerce satırda yanlış bilgi verirdi. Yabancı anahtar
                // zaten SetNull olduğu için null geçerli bir değer
                // ("ekleyen bilinmiyor" — bkz. Poi.UserId).
                UserId = null,
                CreatedDate = simdi,
            });
        }

        return poiler;
    }

    /// <summary>Zarfın içinde düzgün dağılımlı rastgele nokta.</summary>
    private static Coordinate ZarftaNokta(Envelope zarf, Random rastgele)
        => new(
            zarf.MinX + rastgele.NextDouble() * zarf.Width,
            zarf.MinY + rastgele.NextDouble() * zarf.Height);

    /// <summary>
    /// Odağın çevresine Gauss dağılımıyla nokta koyar ve İL SINIRININ İÇİNDE
    /// kalmasını garanti eder.
    ///
    /// Kabul-ret (rejection sampling): dışarı düşen nokta atılır, yenisi
    /// denenir. Hiçbiri tutmazsa odağın kendisi kullanılıyor — odak zaten
    /// sınırın içinde olduğu için üretim asla sınır dışına kaçmıyor.
    /// "Sınıra doğru it" gibi bir düzeltme yapmıyoruz: o, noktaları sınır
    /// çizgisine dizerdi ve ısı haritasında ilin kenarında sahte bir halka
    /// oluştururdu.
    /// </summary>
    private static Coordinate OdakCevresindeNokta(
        Coordinate odak, double sapma, IPreparedGeometry hazir,
        GeometryFactory fabrika, Random rastgele)
    {
        for (var deneme = 0; deneme < 12; deneme++)
        {
            var aday = new Coordinate(
                odak.X + NormalDagilim(rastgele) * sapma,
                odak.Y + NormalDagilim(rastgele) * sapma);

            if (hazir.Intersects(fabrika.CreatePoint(aday))) return aday;
        }

        return odak.Copy();
    }

    /// <summary>
    /// Standart normal dağılımdan (ortalama 0, sapma 1) bir sayı —
    /// Box-Muller dönüşümü.
    ///
    /// .NET'in <c>Random</c>'ı yalnızca düzgün dağılım veriyor. Düzgün
    /// dağılımla saçsaydık her POI odaktan eşit olasılıkla uzaklaşırdı ve
    /// küme, kare bir lekeye dönüşürdü; normal dağılım merkeze doğru
    /// yoğunlaşan gerçekçi bir bulut üretiyor.
    /// </summary>
    private static double NormalDagilim(Random rastgele)
    {
        // log(0) tanımsız; sıfırı dışlamak için 1'den çıkarıyoruz.
        var u1 = 1.0 - rastgele.NextDouble();
        var u2 = rastgele.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>Sıklığa göre ağırlıklı kategori seçimi (rulet çevirme).</summary>
    private static Profil ProfilSec(List<Profil> profiller, int toplamSiklik, Random rastgele)
    {
        var hedef = rastgele.Next(toplamSiklik);
        var birikim = 0;

        foreach (var profil in profiller)
        {
            birikim += profil.Siklik;
            if (hedef < birikim) return profil;
        }

        return profiller[^1];
    }

    private static string AdUret(Profil profil, string ilAdi, Random rastgele)
        => profil.Kaliplar[rastgele.Next(profil.Kaliplar.Length)]
            .Replace("{semt}", Semtler[rastgele.Next(Semtler.Length)])
            .Replace("{il}", ilAdi);

    // ---- Mesai şablonları --------------------------------------------------

    /// <summary>Hafta içi tek saat, hafta sonu (isteğe bağlı) başka saat.</summary>
    private static MesaiPlani Plan(
        string acilis, string kapanis,
        (string Acilis, string Kapanis)? haftaSonu = null,
        bool pazarAcik = true)
    {
        var plan = MesaiPlani.Varsayilan();

        foreach (var gun in plan.Gunler)
        {
            if (gun.Gun <= 5)
            {
                gun.Acik = true;
                gun.Acilis = acilis;
                gun.Kapanis = kapanis;
                continue;
            }

            var acik = haftaSonu is not null && (gun.Gun == 6 || pazarAcik);
            gun.Acik = acik;
            gun.Acilis = acik ? haftaSonu!.Value.Acilis : null;
            gun.Kapanis = acik ? haftaSonu!.Value.Kapanis : null;
        }

        return plan;
    }

    private static MesaiPlani Surekli()
    {
        var plan = MesaiPlani.Varsayilan();
        plan.Tip = MesaiTipi.Surekli;
        return plan;
    }
}
