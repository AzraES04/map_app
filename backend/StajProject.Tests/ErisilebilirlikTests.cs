using NetTopologySuite.Geometries;
using StajProject.Business.Analiz;
using Xunit;

namespace StajProject.Tests;

// ============================================================================
//  TOPLU TAŞIMA ERİŞİLEBİLİRLİK ANALİZİ — hesap motoru
//
//  Buradaki testler saf matematiği koruyor: veritabanı/HTTP yok, doğrudan
//  TopluTasimaErisilebilirligi.Hesapla çağrılıyor (AgirlikliIsiIzgarasi'nın
//  kendi test dosyasıyla aynı desen).
// ============================================================================

public class ErisilebilirlikTests
{
    private static readonly GeometryFactory Fabrika =
        NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

    /// <summary>~1.1 km × ~1.1 km'lik bir kare alan (Ankara civarı).</summary>
    private static Polygon KareAlan() => Fabrika.CreatePolygon(new[]
    {
        new Coordinate(32.85, 39.92),
        new Coordinate(32.86, 39.92),
        new Coordinate(32.86, 39.93),
        new Coordinate(32.85, 39.93),
        new Coordinate(32.85, 39.92),
    });

    [Fact]
    public void Durak_YOKSA_yuzey_tamamen_erisilemez()
    {
        var sonuc = TopluTasimaErisilebilirligi.Hesapla(KareAlan(), Array.Empty<ErisimDuragi>());

        // Durak yoksa "her yer erişilebilir değil" demek — sıfır skorlu bir
        // yüzeyle karıştırılmamalı (o da geçerli bir sonuç olabilirdi).
        Assert.Equal(0, sonuc.EnYuksekSkor);
        Assert.Equal(0, sonuc.IyiErisimOrani);
    }

    [Fact]
    public void Alanin_TAM_ORTASINDAKI_durak_merkezi_1_SKORLUYOR()
    {
        var duraklar = new[] { new ErisimDuragi(1, "Merkez Durak", 32.855, 39.925) };

        var sonuc = TopluTasimaErisilebilirligi.Hesapla(KareAlan(), duraklar);

        // Durağın tam üstündeki hücre 0 metre uzaklıkta → skor 1.0'a en yakın.
        Assert.True(sonuc.EnYuksekSkor > 0.95);
    }

    [Fact]
    public void UZAK_hucreler_DUSUK_skor_aliyor()
    {
        // Durak alanın SOL ALT köşesinde; sağ üst köşe ondan en uzak nokta.
        var duraklar = new[] { new ErisimDuragi(1, "Köşe Durağı", 32.850, 39.920) };

        var sonuc = TopluTasimaErisilebilirligi.Hesapla(KareAlan(), duraklar);

        // Sağ üstteki hücre (dizinin ilk satır ilk sütunu — satır 0 en üst)
        var sagUst = sonuc.Degerler[0 * sonuc.Sutun + (sonuc.Sutun - 1)];
        var solAlt = sonuc.Degerler[(sonuc.Satir - 1) * sonuc.Sutun + 0];

        // Durağa YAKIN köşe, UZAK köşeden daha yüksek skorlamalı.
        Assert.True(solAlt > sagUst);
    }

    [Fact]
    public void SifirMesafeMetreNIN_otesi_TAM_SIFIR()
    {
        // Alandan çok uzakta (yaklaşık 200 km) tek bir durak — hiçbir hücre
        // SifirMesafeMetre (1500 m) sınırının içine giremez.
        var duraklar = new[] { new ErisimDuragi(1, "Uzak Durak", 34.0, 41.0) };

        var sonuc = TopluTasimaErisilebilirligi.Hesapla(KareAlan(), duraklar);

        Assert.Equal(0, sonuc.EnYuksekSkor);
        Assert.All(
            sonuc.Degerler.Where(d => d >= 0),   // -1 = alan dışı, hesaba katılmıyor
            deger => Assert.Equal(0, deger));
    }

    [Fact]
    public void Alan_DISINDAKI_hucreler_MINUS_BIR()
    {
        var duraklar = new[] { new ErisimDuragi(1, "Durak", 32.855, 39.925) };

        var sonuc = TopluTasimaErisilebilirligi.Hesapla(KareAlan(), duraklar);

        // Kare bir alan olsa da ızgara zarfı (envelope) köşelerde alanın
        // dışına taşabilir; -1 değeri "burası hiç analiz edilmedi" demek,
        // sıfırla (erişilemez) karıştırılmamalı.
        Assert.Contains(-1.0, sonuc.Degerler);
    }

    [Fact]
    public void Iki_durak_BIRBIRINE_YAKIN_alani_birlikte_kapsiyor()
    {
        var tekDurak = TopluTasimaErisilebilirligi.Hesapla(
            KareAlan(), new[] { new ErisimDuragi(1, "A", 32.850, 39.920) });

        var ikiDurak = TopluTasimaErisilebilirligi.Hesapla(
            KareAlan(),
            new[]
            {
                new ErisimDuragi(1, "A", 32.850, 39.920),
                new ErisimDuragi(2, "B", 32.860, 39.930),
            });

        // İkinci durak eklenince "iyi erişim" oranı ARTMALI, azalmamalı —
        // en yakın durağa mesafe hiçbir hücrede kötüleşemez.
        Assert.True(ikiDurak.IyiErisimOrani >= tekDurak.IyiErisimOrani);
    }

    // ------------------------------------------------------------------
    //  REGRESYON: BÜYÜK ALANDA SABİT ÇÖZÜNÜRLÜK HATASI
    //
    //  Canlıda görülen ve kullanıcının bildirdiği hata: Ankara + İstanbul
    //  birlikte seçildiğinde ("hepsi uzak olamaz, analiz yanlış") zarf
    //  ~500 km × ~300 km'ye çıkıyor. Hücre boyutu ALANA GÖRE
    //  ÖLÇEKLENMEZSE (sabit ~96 sütun gibi eski davranış) hücre kenarı
    //  ~5 km'ye çıkar; SifirMesafeMetre (1.500 m) bu hücrede neredeyse
    //  hiç aşılamaz ve yüzey tamamen mor (skor 0) görünür — durakların
    //  TAM ÜSTÜNDEKİ hücreler dahil.
    //
    //  Bu testler HucreBoyutuSec'in büyük alanda makul kaldığını ve bir
    //  durağın kendi hücresinin YÜKSEK skorladığını doğruluyor.
    // ------------------------------------------------------------------

    /// <summary>
    /// Ankara–İstanbul zarfını taklit eden geniş dikdörtgen (~5,4° × ~2,7°,
    /// kabaca 500 km × 300 km).
    /// </summary>
    private static Polygon GenisAlan() => Fabrika.CreatePolygon(new[]
    {
        new Coordinate(28.0, 39.0),
        new Coordinate(33.4, 39.0),
        new Coordinate(33.4, 41.7),
        new Coordinate(28.0, 41.7),
        new Coordinate(28.0, 39.0),
    });

    [Fact]
    public void HucreBoyutuSec_BUYUK_ALANDA_tavana_carpsa_bile_makul_kaliyor()
    {
        // 500 km × 300 km'de eski (sabit ızgaralı) davranış hücreyi ~5 km'ye
        // çıkarıyordu. Alana göre ölçekleyen hesap birkaç yüz metrede kalmalı
        // — SifirMesafeMetre'nin (1.500 m) çok altında, yoksa eşik hiç
        // aşılamaz.
        var hucre = TopluTasimaErisilebilirligi.HucreBoyutuSec(500_000, 300_000);

        Assert.True(
            hucre < TopluTasimaErisilebilirligi.SifirMesafeMetre,
            $"Hücre {hucre:0} m — SifirMesafeMetre'nin ({TopluTasimaErisilebilirligi.SifirMesafeMetre} m) altında olmalı.");
    }

    [Fact]
    public void GENIS_alanda_bir_DURAK_kendi_hucresinde_hala_YUKSEK_skorluyor()
    {
        // Durak, geniş alanın ortasına yakın bir yerde. Sabit-çözünürlük
        // hatasında bu hücre bile 0 skorluyordu; kullanıcının "hepsi uzak
        // olamaz" şikâyeti tam olarak buydu.
        var duraklar = new[] { new ErisimDuragi(1, "Ankara Merkez", 32.85, 39.93) };

        var sonuc = TopluTasimaErisilebilirligi.Hesapla(GenisAlan(), duraklar);

        Assert.True(
            sonuc.EnYuksekSkor > 0.5,
            $"En yüksek skor {sonuc.EnYuksekSkor:0.00} — durağın kendi hücresi düşük erişim göstermemeli.");
        Assert.True(sonuc.IyiErisimOrani > 0);
    }

    [Fact]
    public void GENIS_alanda_hucre_sayisi_TAVANA_YAKIN_kaliyor()
    {
        var duraklar = new[] { new ErisimDuragi(1, "Ankara Merkez", 32.85, 39.93) };

        var sonuc = TopluTasimaErisilebilirligi.Hesapla(GenisAlan(), duraklar);

        // Tavan JSON boyutunu (ve tarayıcı tuvalini) sınırlıyor. Kesin bir
        // ÜST SINIR DEĞİL: sutun/satir ayrı ayrı yukarı yuvarlandığı için
        // çarpım tavanı birkaç yüzde aşabilir (500 km × 300 km'de ~%0,2).
        // Sınanan şey tavanın YAKININDA kalması — eski sabit-ızgara hatasında
        // olduğu gibi milyonlarca hücreye SIÇRAMAMASI.
        var hucreSayisi = sonuc.Sutun * sonuc.Satir;
        Assert.True(
            hucreSayisi <= TopluTasimaErisilebilirligi.AzamiHucreSayisi * 1.05,
            $"Hücre sayısı {hucreSayisi} — tavanın ({TopluTasimaErisilebilirligi.AzamiHucreSayisi}) %5 üzerini aşmamalı.");
    }
}
