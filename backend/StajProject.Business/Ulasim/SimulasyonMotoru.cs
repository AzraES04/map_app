using NetTopologySuite.Geometries;

namespace StajProject.Business.Ulasim;

/// <summary>
/// Simülasyonun SAF ÇEKİRDEĞİ (Ödev 19 / Madde 1): bir çizgi üzerinde
/// oranla ilerleyen aracın konumu.
///
/// NEDEN AYRI BİR SINIF?
/// Simülasyonun geri kalanı zamanlayıcı, SignalR grubu, DI ömrü gibi
/// şeylerle uğraşıyor; hepsi test edilmesi pahalı parçalar. Oysa "hattın
/// %37'sindeyken araç nerede?" sorusu tamamen matematiksel ve tek başına
/// sınanabilir. Ödev 18'de aynı ayrım işe yaramıştı (istemcideki
/// <c>hatBacagi.js</c>); burada sunucu tarafındaki karşılığı duruyor.
///
/// KOORDİNATLAR EPSG:4326 (derece). Metreye çevirmiyoruz çünkü hattın
/// geometrisi veritabanında derece cinsinden duruyor ve dönüşüm yapmak
/// simülasyonu doğrulaştırmıyor — yalnızca yeni bir hata kaynağı ekler.
/// Ama ölçerken BOYLAM DÜZELTMESİ şart (bkz. <see cref="ParcaUzunlugu"/>).
/// </summary>
public static class SimulasyonMotoru
{
    /// <summary>
    /// Ekvatorda bir enlem derecesinin metre karşılığı.
    ///
    /// Yalnızca "kaç metre ilerledi?" bilgisini KABACA gösterebilmek için var.
    /// Hattın gerçek mesafesi OSRM'den geliyor (<c>RotaMesafeMetre</c>) ve
    /// varsa o kullanılıyor; bu sabit yalnızca rotası olmayan (kuş uçuşu)
    /// hatlarda devreye giriyor.
    /// </summary>
    public const double DereceMetre = 111_320d;

    /// <summary>
    /// İki nokta arasındaki uzunluk — BOYLAM DÜZELTMELİ.
    ///
    /// Neden düz Öklid değil? Bir enlem derecesi her yerde ~111 km, ama bir
    /// BOYLAM derecesi kutuplara doğru daralıyor: Ankara'da (~40°K) enlem
    /// derecesinin yalnızca %77'si kadar. Düzeltmeseydik doğu-batı uzanan
    /// bir hat olduğundan uzun sayılır, araç aynı "oranla" ilerlerken
    /// doğu-batı bacaklarını yavaş, kuzey-güney bacaklarını hızlı geçerdi.
    /// Gözle görülür bir bozukluk ve hata mesajı vermez.
    /// </summary>
    public static double ParcaUzunlugu(Coordinate a, Coordinate b)
    {
        var ortalamaEnlem = (a.Y + b.Y) / 2 * Math.PI / 180;
        var dx = (b.X - a.X) * Math.Cos(ortalamaEnlem);
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Çizginin her köşesine kadar KAT EDİLEN uzunluk.
    /// İlk eleman her zaman 0, son eleman hattın toplam uzunluğu.
    ///
    /// Bir kez hesaplanıp saklanıyor: her tikte (saniyede iki kez, her aktif
    /// simülasyon için) yeniden hesaplamak binlerce köşeli bir rotada boşa
    /// giden iştir.
    /// </summary>
    public static double[] Kumulatif(IReadOnlyList<Coordinate> yol)
    {
        if (yol is null || yol.Count < 2)
        {
            throw new ArgumentException("Simülasyon için en az iki nokta gerekiyor.", nameof(yol));
        }

        var kumulatif = new double[yol.Count];
        for (var i = 1; i < yol.Count; i++)
        {
            kumulatif[i] = kumulatif[i - 1] + ParcaUzunlugu(yol[i - 1], yol[i]);
        }

        return kumulatif;
    }

    /// <summary>
    /// Hattın <paramref name="oran"/> kadarındaki nokta (0 = ilk durak,
    /// 1 = son durak).
    ///
    /// Oran KIRPILIYOR: zamanlayıcı bir tik gecikirse oran 1'i biraz aşabilir
    /// ve kırpmasaydık araç hattın ucundan taşardı.
    /// </summary>
    public static Coordinate Konum(IReadOnlyList<Coordinate> yol, double[] kumulatif, double oran)
    {
        var toplam = kumulatif[^1];
        if (toplam <= 0)
        {
            // Bütün noktalar üst üste (tek durak iki kez girilmiş olabilir).
            // Sıfıra bölmek yerine başlangıcı döndürüyoruz.
            return new Coordinate(yol[0].X, yol[0].Y);
        }

        var hedef = Math.Clamp(oran, 0, 1) * toplam;

        // Hedefin hangi parçaya düştüğünü bul. İkili arama kullanmıyoruz:
        // liste zaten sıralı ama tik başına tek geçiş yeterince ucuz ve
        // okunması çok daha kolay.
        var i = 1;
        while (i < kumulatif.Length - 1 && kumulatif[i] < hedef)
        {
            i++;
        }

        var parcaBasi = kumulatif[i - 1];
        var parcaBoyu = kumulatif[i] - parcaBasi;
        var t = parcaBoyu <= 0 ? 0 : (hedef - parcaBasi) / parcaBoyu;

        var a = yol[i - 1];
        var b = yol[i];
        return new Coordinate(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    /// <summary>
    /// Bir noktanın (durağın) hat üzerindeki ORANI.
    ///
    /// Durak, OSRM rotasının tam üstünde durmuyor: rota yola oturuyor, durak
    /// ise kullanıcının tıkladığı yerde. Bu yüzden durağı çizgiye DİK
    /// izdüşürüyoruz ve izdüşümün hat başından uzaklığını oranlıyoruz.
    ///
    /// Aynı hesap istemcide de var (<c>hatBacagi.js → cizgideIlerleme</c>):
    /// orada "tıklanan nokta hangi bacakta?", burada "durak hattın yüzde
    /// kaçında?" sorusunu cevaplıyor. İkisi de en yakın parçaya izdüşüm.
    /// </summary>
    public static double NoktaninOrani(
        IReadOnlyList<Coordinate> yol, double[] kumulatif, Coordinate nokta)
    {
        var toplam = kumulatif[^1];
        if (toplam <= 0)
        {
            return 0;
        }

        var enIyiUzaklik = double.MaxValue;
        var enIyiIlerleme = 0d;

        for (var i = 0; i < yol.Count - 1; i++)
        {
            var (t, uzaklik) = ParcayaIzdusum(yol[i], yol[i + 1], nokta);
            if (uzaklik < enIyiUzaklik)
            {
                enIyiUzaklik = uzaklik;
                enIyiIlerleme = kumulatif[i] + t * (kumulatif[i + 1] - kumulatif[i]);
            }
        }

        return Math.Clamp(enIyiIlerleme / toplam, 0, 1);
    }

    /// <summary>
    /// Noktanın doğru PARÇASINA izdüşümü: oran [0,1] aralığına kırpılır,
    /// uzaklık boylam düzeltmeli ölçülür.
    /// </summary>
    private static (double Oran, double Uzaklik) ParcayaIzdusum(
        Coordinate a, Coordinate b, Coordinate nokta)
    {
        // Ölçüm ve izdüşüm AYNI düzlemde yapılmalı; boylamı burada da
        // daraltıyoruz, yoksa izdüşüm ile uzunluk hesabı farklı iki metrik
        // kullanır ve durak oranları kayardı.
        var olcek = Math.Cos((a.Y + b.Y) / 2 * Math.PI / 180);

        var dx = (b.X - a.X) * olcek;
        var dy = b.Y - a.Y;
        var uzunlukKare = dx * dx + dy * dy;

        var px = (nokta.X - a.X) * olcek;
        var py = nokta.Y - a.Y;

        if (uzunlukKare <= 0)
        {
            return (0, Math.Sqrt(px * px + py * py));
        }

        var oran = Math.Clamp((px * dx + py * dy) / uzunlukKare, 0, 1);
        var farkX = px - oran * dx;
        var farkY = py - oran * dy;

        return (oran, Math.Sqrt(farkX * farkX + farkY * farkY));
    }
}
