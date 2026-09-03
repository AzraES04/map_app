using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;

namespace StajProject.Business.Analiz;

// ============================================================================
//  TOPLU TAŞIMA ERİŞİLEBİLİRLİK ANALİZİ — hesap motoru
//
//  Soru: "seçilen bölgenin neresi bir toplu taşıma durağına yakın, neresi
//  değil?" AgirlikliIsiIzgarasi'nden (Ödev 14) FARKLI bir hesap:
//
//    Konum analizi     → çok kriterli, ağırlıklı ÇEKİRDEK YOĞUNLUĞU
//                         ("bu bölgede eczane+okul+hastane birlikte nerede
//                         yoğun?")
//    Bu analiz         → TEK kriterli, EN YAKIN NOKTAYA UZAKLIK
//                         ("buradan en yakın durağa kaç metre var?")
//
//  İkisi kavramsal olarak ayrı sorular olduğu için ayrı bir hesap dosyası;
//  ama ÇIKTI BİÇİMİ (IsiIzgarasiDto ile aynı şekil: Extent/Sutun/Satir/
//  Degerler/EnYuksekSkor) bilerek AYNI — arayüzdeki ısı haritası çizim
//  kodu (isiIzgarasi.js → izgaraKaynagiOlustur) hiç değiştirilmeden burada
//  da kullanılabiliyor. Aynı görsel dile iki ayrı analiz konuşuyor.
//
//  ---- ÇÖZÜNÜRLÜK NEDEN KONUM ANALİZİNDEN FARKLI? ----
//  İlk sürüm Konum Analizi'yle aynı sabit ızgarayı (uzun kenarda 96 hücre)
//  kullanıyordu ve bu, canlıda analizi TAMAMEN anlamsız kılan bir hataya
//  dönüştü: Ankara + İstanbul seçildiğinde zarf ~500 km, hücre ~5.200 m.
//  Skorun sıfıra indiği mesafe 1.500 m — yani bir hücrenin MERKEZİ bir
//  durağa 1.500 m'den yakın olmadıkça hücre sıfır alıyor, ve 5 km'lik
//  hücrelerde bu neredeyse hiç olmuyor. Sonuç: "alanın %0'ı erişilebilir",
//  haritada tek renk mor. Kullanıcı bunu "hepsi uzak olamaz, analiz yanlış"
//  diye bildirdi ve haklıydı.
//
//  Konum analizinde bu sorun yok çünkü o bir YOĞUNLUK ölçüyor — çekirdek
//  yarıçapı hücreyle birlikte büyüyor. Burada ise eşikler METRE cinsinden
//  ve sabit (400 m yürüme mesafesi bir fizik gerçeği, alanın büyüklüğüne
//  göre esnemez). O yüzden burada hücre boyutu da metreye bağlanıyor:
//  hücre sayısı değil, hücre BOYUTU hedefleniyor ve toplam hücre sayısına
//  bir tavan konuyor.
// ============================================================================

/// <summary>Bir durağın konumu — yalnızca hesaba giren kısmı.</summary>
public sealed record ErisimDuragi(int Id, string Ad, double Boylam, double Enlem);

/// <summary>Hesabın çıktısı — IsiIzgarasiDto'ya BİREBİR eşlenecek şekilde.</summary>
public sealed record ErisilebilirlikSonucu(
    double[] Extent,
    int Sutun,
    int Satir,
    double HucreMetre,
    double[] Degerler,
    double EnYuksekSkor,
    /// <summary>Alan içindeki hücrelerin YÜZDE KAÇI "iyi erişim" sınırının (≤ İyiEsikMetre) içinde.</summary>
    double IyiErisimOrani);

public static class TopluTasimaErisilebilirligi
{
    /// <summary>
    /// Hedeflenen hücre kenarı (metre) — alan küçükse hücre bundan daha
    /// ince yapılmıyor. 200 m: "iyi erişim" eşiği (400 m) iki hücreye
    /// denk geliyor, yani eşik ızgarada gerçekten görünüyor. Daha ince
    /// yapmak (50 m) mahalle ölçeğinde bile 100 binlerce hücre üretir ve
    /// ekranda hiçbir fark yaratmaz — hücre zaten yürüme adımından küçük.
    /// </summary>
    public const double HedefHucreMetre = 200;

    /// <summary>
    /// Toplam hücre tavanı (400 × 400).
    ///
    /// Alan büyüdükçe hücre bu tavana göre KABALAŞIYOR: Ankara ili için
    /// ~600 m, Ankara + İstanbul birlikte ~1.000 m. Bu hâlâ kaba ama
    /// 5 km'nin aksine kullanılabilir — bir durağın 1.500 m'lik etki alanı
    /// birkaç hücre ediyor ve haritada görünüyor.
    ///
    /// Tavanın sebebi ağ ve tarayıcı: değerler JSON olarak istemciye gidiyor
    /// ve orada tuvale boyanıyor. 160 bin hücre ~500 KB ve 400×400 piksel;
    /// bir milyon hücre 3 MB ve 1000×1000 piksel olurdu — her "Analiz Et"
    /// tıklamasında.
    /// </summary>
    public const int AzamiHucreSayisi = 160_000;

    /// <summary>
    /// "İyi erişim" sınırı (metre). 400 m ~ yürüyerek 5 dakika — toplu
    /// taşımacılıkta yaygın kullanılan "yürünebilir mesafe" eşiği
    /// (Uluslararası Toplu Taşıma Birliği'nin (UITP) da referans aldığı
    /// ölçü). Bu mesafenin altı "iyi", üstü kademeli olarak kötüleşiyor.
    /// </summary>
    public const double IyiEsikMetre = 400;

    /// <summary>
    /// Skorun sıfıra indiği mesafe. Bunun ötesi "erişilemez" sayılıyor —
    /// sıfır olmayan ama anlamsızca küçük bir skorla yüzeyi bulandırmak
    /// yerine düz sıfır, haritada net bir "buraya toplu taşıma ulaşmıyor"
    /// mesajı veriyor. 1.500 m ~ yürüyerek 18-20 dakika, bu mesafenin
    /// ötesini kimse "durağa erişilebilir" saymaz.
    /// </summary>
    public const double SifirMesafeMetre = 1_500;

    private const double MetreDereceY = 110_540;
    private const double MetreDereceX = 111_320;

    /// <summary>
    /// Alanın boyutundan hücre kenarını (metre) türetir.
    ///
    /// İki kural, ikisi de üstte gerekçelendirildi: hücre
    /// <see cref="HedefHucreMetre"/>'den ince olmasın; toplam hücre sayısı
    /// <see cref="AzamiHucreSayisi"/>'nı aşmasın. İkincisi bağlayıcı olduğunda
    /// hücre, alanın ALANINDAN (genişlik × yükseklik) türüyor — uzun kenardan
    /// değil, ki dar-uzun bir alanda hücre boşuna kabalaşmasın.
    /// </summary>
    public static double HucreBoyutuSec(double genislikM, double yukseklikM)
    {
        var tavanaGoreHucre = Math.Sqrt(genislikM * yukseklikM / AzamiHucreSayisi);
        return Math.Max(HedefHucreMetre, tavanaGoreHucre);
    }

    public static ErisilebilirlikSonucu Hesapla(Geometry alan, IReadOnlyList<ErisimDuragi> duraklar)
    {
        var zarf = alan.EnvelopeInternal;
        var ortaEnlem = (zarf.MinY + zarf.MaxY) / 2.0;
        var metreDereceX = MetreDereceX * Math.Cos(ortaEnlem * Math.PI / 180.0);

        var genislikM = Math.Max(zarf.Width * metreDereceX, 100.0);
        var yukseklikM = Math.Max(zarf.Height * MetreDereceY, 100.0);

        var hucreMetre = HucreBoyutuSec(genislikM, yukseklikM);
        var sutun = Math.Max(1, (int)Math.Ceiling(genislikM / hucreMetre));
        var satir = Math.Max(1, (int)Math.Ceiling(yukseklikM / hucreMetre));

        var hucreDerX = hucreMetre / metreDereceX;
        var hucreDerY = hucreMetre / MetreDereceY;

        var minBoylam = zarf.MinX;
        var maxEnlem = zarf.MinY + satir * hucreDerY;
        var extent = new[] { minBoylam, zarf.MinY, minBoylam + sutun * hucreDerX, maxEnlem };

        var hucreSayisi = sutun * satir;
        var degerler = new double[hucreSayisi];

        if (duraklar.Count == 0)
        {
            // Durak hiç yoksa her yer "erişilemez" — bunu sessizce sıfır
            // yüzey olarak değil, çağıran tarafın ayrıca söylemesi gereken
            // bir durum olarak ele alıyoruz (servis katmanı bunu uyarıya çeviriyor).
            Array.Fill(degerler, -1.0);
            return new ErisilebilirlikSonucu(extent, sutun, satir, hucreMetre, degerler, 0, 0);
        }

        var hazir = PreparedGeometryFactory.Prepare(alan);
        var fabrika = alan.Factory;

        // ---- HÜCRE BOYUTU DÜZELTMESİ ----
        // Mesafe hücrenin MERKEZİNDEN ölçülüyor; ama durak hücrenin içinde
        // bir yerdeyse o hücreden durağa "uzaklık" gerçekte sıfırdır —
        // merkezden ölçüm, kabalaşan hücrede sistematik bir ceza üretir
        // (1 km'lik hücrede merkez, içindeki durağa 700 m uzak çıkabilir ve
        // hücre "iyi erişim" sayılmaz). Yarım köşegen düşülerek mesafe,
        // "durağın hücreye en kötü ihtimalle uzaklığı"na çevriliyor.
        // İnce hücrede (200 m) düzeltme 141 m — 400 m eşiğine göre küçük;
        // kaba hücrede ise ölçümü anlamlı kılan şey tam olarak bu.
        var yariKosegen = hucreMetre * Math.Sqrt(2) / 2.0;

        var icerdekiSayi = 0;
        var iyiSayisi = 0;
        var enYuksek = 0.0;

        // Metre cinsinden kaba (ekvirektangüler) mesafe: analiz alanı bir
        // il/ilçe ölçeğinde, bu yaklaşıklık ~%1'in altında hata veriyor —
        // ekranda hücre renklerini seçmek için fazlasıyla yeterli. Tam
        // Haversine, yüz binlerce hücre × durak sayısı kadar trigonometri
        // demek olur ve fark gözle görülmez.
        double MesafeMetre(double boylam, double enlem, ErisimDuragi durak)
        {
            var dx = (boylam - durak.Boylam) * metreDereceX;
            var dy = (enlem - durak.Enlem) * MetreDereceY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        for (var r = 0; r < satir; r++)
        {
            var enlem = maxEnlem - (r + 0.5) * hucreDerY;

            for (var c = 0; c < sutun; c++)
            {
                var boylam = minBoylam + (c + 0.5) * hucreDerX;
                var idx = r * sutun + c;

                if (!hazir.Intersects(fabrika.CreatePoint(new Coordinate(boylam, enlem))))
                {
                    degerler[idx] = -1;
                    continue;
                }

                icerdekiSayi++;

                var enYakin = double.MaxValue;
                foreach (var durak in duraklar)
                {
                    var mesafe = MesafeMetre(boylam, enlem, durak);
                    if (mesafe < enYakin) enYakin = mesafe;
                }

                var hucreyeUzaklik = Math.Max(0.0, enYakin - yariKosegen);

                // Skor DOĞRUSAL AZALAN: 0 m'de 1.0, SifirMesafeMetre'de 0.0.
                // Üstel/Gauss bir eğri de düşünülebilirdi ama burada bilerek
                // basit tutuldu — "erişilebilirlik" gibi doğrudan yorumlanan
                // bir ölçüde doğrusal ilişki kullanıcının sezgisine daha
                // yakın: "iki kat uzaksan yarı yarıya daha az erişilebilirsin."
                var skor = Math.Max(0.0, 1.0 - hucreyeUzaklik / SifirMesafeMetre);
                degerler[idx] = skor;

                if (skor > enYuksek) enYuksek = skor;
                if (hucreyeUzaklik <= IyiEsikMetre) iyiSayisi++;
            }
        }

        var iyiOran = icerdekiSayi > 0 ? (double)iyiSayisi / icerdekiSayi : 0;

        return new ErisilebilirlikSonucu(extent, sutun, satir, hucreMetre, degerler, enYuksek, iyiOran);
    }
}
