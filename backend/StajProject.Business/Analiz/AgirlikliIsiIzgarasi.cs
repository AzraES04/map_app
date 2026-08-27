using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;

namespace StajProject.Business.Analiz;

// ============================================================================
//  Ödev 14 / Madde 2 — AĞIRLIKLI ISI HARİTASININ HESAP MOTORU
//
//  Bu dosyada veritabanı da HTTP de yok: içeri POI koordinatları ve ağırlıklar
//  girer, dışarı bir skor ızgarası çıkar. Saf fonksiyon olması bilinçli —
//  "50 puanlık kriteri 80'e çıkarınca yüzey gerçekten değişiyor mu?" sorusu
//  ancak böyle bir birim testiyle cevaplanabiliyor.
//
//  YÖNTEM: ağırlıklı örtüştürme (weighted overlay) — çok ölçütlü karar
//  analizinin (MCDA) klasik kalıbı, üç adım:
//
//    1) Her kriter için YOĞUNLUK YÜZEYİ üret.
//       Her POI, çevresine Gauss çanı biçiminde puan dağıtır: tam üstünde
//       en yüksek, uzaklaştıkça azalan. Bu bir "çekirdek yoğunluk kestirimi"
//       (kernel density estimation) ve GeoServer'ın gs:Heatmap'inin yaptığı
//       işin aynısı — farkı, burada ağırlığı BİZ veriyoruz.
//
//    2) Her kriteri KENDİ İÇİNDE 0–1'e normalleştir.
//       Bu adım olmasaydı ağırlıkların hiçbir hükmü kalmazdı: alanda 400
//       eczane, 12 hastane varsa ham yoğunlukların toplamında eczane
//       kriteri, ağırlığı ne olursa olsun hastaneyi ezerdi. Normalleştirme
//       her kriteri "bu kriterin en yoğun yeri = 1" ölçeğine çekiyor,
//       böylece kullanıcının verdiği puan gerçekten belirleyici oluyor.
//
//    3) AĞIRLIKLI TOPLA.  skor = Σ (agirlik / 100) × normalize_yogunluk
//       Ağırlıklar toplamı 100 olduğu için sonuç kendiliğinden 0–1 aralığında.
//
//  NEDEN GeoServer'ın gs:Heatmap'i KULLANILMADI?
//  gs:Heatmap tek bir katmanın yoğunluğunu çiziyor ve ağırlığı bir SÜTUNDAN
//  okuyor. Bizim ağırlıklarımız kullanıcı panelde kaydırıcıyı oynattıkça
//  değişiyor; her denemede veritabanına sütun yazmak ya da GeoServer'a yeni
//  bir SLD göndermek gerekirdi. Üstelik gs:Heatmap her istek için kendi
//  içinde yeniden normalleştiriyor, yani iki kriterin yüzeyleri birbiriyle
//  KIYASLANABİLİR olmuyor. Ödev 9'daki ısı haritası olduğu gibi duruyor;
//  bu, onun yanına gelen ikinci ve farklı bir araç.
// ============================================================================

/// <summary>Hesaba giren tek bir POI — yalnızca konumu, kategorisi ve adı.</summary>
/// <param name="KategoriId">KRİTERİN kategorisi (alt kategoriler ataya eşlenmiş hâlde).</param>
/// <param name="Isim">POI adı; aday konumun "en yakın X" satırında gösteriliyor.</param>
/// <param name="Boylam">EPSG:4326 boylam (derece).</param>
/// <param name="Enlem">EPSG:4326 enlem (derece).</param>
public sealed record AnalizPoisi(int KategoriId, string Isim, double Boylam, double Enlem);

/// <summary>Kriter: hangi kategori, 100 üzerinden kaç puan.</summary>
public sealed record AnalizAgirligi(int KategoriId, int Agirlik);

/// <summary>Bir aday konumun, bir kriterin en yakın POI'sine uzaklığı.</summary>
public sealed record AdayMesafesi(int KategoriId, string? PoiAdi, double? MesafeMetre);

/// <summary>Izgaranın en yüksek puanlı hücrelerinden biri.</summary>
public sealed record IzgaraAdayi(
    double Boylam, double Enlem, double Skor, IReadOnlyList<AdayMesafesi> Mesafeler);

/// <summary>Hesabın tam çıktısı.</summary>
public sealed record IzgaraSonucu(
    double[] Extent,
    int Sutun,
    int Satir,
    double HucreMetre,
    double EtkiYaricapiMetre,
    double[] Degerler,
    double EnYuksekSkor,
    IReadOnlyList<IzgaraAdayi> Adaylar);

public static class AgirlikliIsiIzgarasi
{
    /// <summary>
    /// Izgaranın UZUN KENARINDAKİ hücre sayısı.
    ///
    /// 96 bilinçli bir orta yol: 9216 hücreye kadar çıkıyor, JSON olarak
    /// ~90 KB ediyor ve ekranda pürüzsüz bir yüzey veriyor. 200'e çıkarmak
    /// dört katı veri ve gözle görülmeyen bir iyileşme demekti; 40'a
    /// düşürmek ise ısı haritasını kareli bir battaniyeye çeviriyordu.
    /// </summary>
    public const int Cozunurluk = 96;

    /// <summary>
    /// Gauss çanının standart sapması, HÜCRE cinsinden.
    ///
    /// Metre yerine hücre cinsinden tanımlı olması ölçekten bağımsızlık
    /// sağlıyor: ızgara zaten seçilen alana göre boyutlandığı için, bir ilçe
    /// analizinde de bütün Türkiye analizinde de yüzey aynı "pürüzlülükte"
    /// çıkıyor. Sabit bir metre değeri verseydik ülke ölçeğinde tek tek
    /// noktacıklar, mahalle ölçeğinde ise tek renkli bir örtü görürdük.
    /// </summary>
    private const double SigmaHucre = 2.0;

    /// <summary>
    /// Çekirdeğin kesme yarıçapı (σ katı). 3σ'da Gauss'un değeri %1'in
    /// altına düşüyor; ötesini hesaplamak sonucu değiştirmeden maliyeti
    /// büyütürdü. Bu kesme sayesinde her POI yalnızca 13×13'lük bir pencereye
    /// dağıtılıyor, ızgaranın tamamına değil.
    /// </summary>
    private const double KesmeSigma = 3.0;

    /// <summary>Ekvatorda 1 boylam derecesinin metre karşılığı.</summary>
    private const double MetreDereceX = 111_320.0;

    /// <summary>1 enlem derecesinin metre karşılığı (enleme göre çok az değişir).</summary>
    private const double MetreDereceY = 110_540.0;

    /// <summary>
    /// Ağırlıklı uygunluk yüzeyini hesaplar.
    /// </summary>
    /// <param name="alan">Analizin sınırı (EPSG:4326). Dışında kalan hücreler boş bırakılır.</param>
    /// <param name="agirliklar">Kriterler ve puanları; toplamları 100 varsayılır (doğrulama servisin işi).</param>
    /// <param name="poiler">Alan içindeki POI'ler, kriter kategorisine eşlenmiş hâlde.</param>
    /// <param name="adaySayisi">Kaç aday konum önerilecek.</param>
    public static IzgaraSonucu Hesapla(
        Geometry alan,
        IReadOnlyList<AnalizAgirligi> agirliklar,
        IReadOnlyList<AnalizPoisi> poiler,
        int adaySayisi = 5)
    {
        var zarf = alan.EnvelopeInternal;

        // Enlemde metre sabit, boylamda kutuplara gidildikçe kısalıyor.
        // Ortalama enlemin kosinüsüyle düzeltmezsek Türkiye enlemlerinde
        // hücreler yaklaşık %30 yamuk olurdu: doğu-batı yönünde daha geniş
        // bir komşuluk, kuzey-güney yönünde daha darı sayılırdı.
        var ortaEnlem = (zarf.MinY + zarf.MaxY) / 2.0;
        var metreDereceX = MetreDereceX * Math.Cos(ortaEnlem * Math.PI / 180.0);

        // Dejenere alan (tek nokta gibi) hesabı sıfıra bölerdi; en az 100 m.
        var genislikM = Math.Max(zarf.Width * metreDereceX, 100.0);
        var yukseklikM = Math.Max(zarf.Height * MetreDereceY, 100.0);

        var hucreMetre = Math.Max(genislikM, yukseklikM) / Cozunurluk;
        var sutun = Math.Max(1, (int)Math.Ceiling(genislikM / hucreMetre));
        var satir = Math.Max(1, (int)Math.Ceiling(yukseklikM / hucreMetre));

        // Hücrenin derece cinsinden kenarları. Metrede kare olan hücre,
        // derece düzleminde dikdörtgen görünür — istemci ızgarayı zaten
        // Extent'e yayarak çizdiği için bu doğru olan.
        var hucreDerX = hucreMetre / metreDereceX;
        var hucreDerY = hucreMetre / MetreDereceY;

        var minBoylam = zarf.MinX;
        var maxEnlem = zarf.MinY + satir * hucreDerY;   // ızgara sol-alttan büyüyor
        var extent = new[]
        {
            minBoylam,
            zarf.MinY,
            minBoylam + sutun * hucreDerX,
            maxEnlem,
        };

        var hucreSayisi = sutun * satir;

        // ---- 1) Hangi hücre alanın İÇİNDE? -------------------------------
        //
        // PreparedGeometry, aynı geometriye binlerce kez soru sorulacağı
        // durumlar için: kenarlarını bir kez indeksliyor, sonraki her
        // "içinde mi?" sorusu logaritmik zamanda cevaplanıyor. Hazırlamadan
        // 9216 kez Contains çağırmak, 81 ilin birleşimi gibi on binlerce
        // köşesi olan bir geometride gözle görülür şekilde yavaştı.
        var hazir = PreparedGeometryFactory.Prepare(alan);
        var fabrika = alan.Factory;

        var icerde = new bool[hucreSayisi];
        var icerdekiSayi = 0;

        for (var r = 0; r < satir; r++)
        {
            // Satır 0 EN ÜST: dizinin sırası ekranın sırasıyla aynı olsun.
            var enlem = maxEnlem - (r + 0.5) * hucreDerY;

            for (var c = 0; c < sutun; c++)
            {
                var boylam = minBoylam + (c + 0.5) * hucreDerX;
                if (hazir.Intersects(fabrika.CreatePoint(new Coordinate(boylam, enlem))))
                {
                    icerde[r * sutun + c] = true;
                    icerdekiSayi++;
                }
            }
        }

        // Zarfın merkezi alanın dışında kalabilir (örn. hilal biçimli bir
        // il birleşimi). Hiç hücre kalmadıysa boş bir yüzey döndürüyoruz;
        // çağıran taraf bunu "sonuç yok" diye gösteriyor.
        if (icerdekiSayi == 0)
        {
            return new IzgaraSonucu(
                extent, sutun, satir, hucreMetre, hucreMetre * SigmaHucre * KesmeSigma,
                Enumerable.Repeat(-1.0, hucreSayisi).ToArray(), 0, Array.Empty<IzgaraAdayi>());
        }

        // ---- 2) Kriter başına yoğunluk yüzeyi ----------------------------
        var kategoriPoileri = poiler
            .GroupBy(p => p.KategoriId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var skor = new double[hucreSayisi];

        foreach (var agirlik in agirliklar)
        {
            if (!kategoriPoileri.TryGetValue(agirlik.KategoriId, out var kategoriListesi))
            {
                // Alan içinde bu kriterden hiç POI yok → yüzeye katkısı sıfır.
                // Sessizce atlıyoruz; kullanıcı sayıyı panelde görüyor.
                continue;
            }

            var yogunluk = YogunlukYuzeyi(
                kategoriListesi, sutun, satir, minBoylam, maxEnlem, hucreDerX, hucreDerY);

            // Normalleştirme yalnızca ALAN İÇİNDEKİ hücrelere bakıyor.
            // Zarfın köşesinde kalan (ama alanın dışındaki) bir yığılma
            // ölçeği belirleseydi, alan içindeki gerçek yoğunluklar
            // olduğundan sönük görünürdü.
            var enBuyuk = 0.0;
            for (var i = 0; i < hucreSayisi; i++)
            {
                if (icerde[i] && yogunluk[i] > enBuyuk) enBuyuk = yogunluk[i];
            }

            if (enBuyuk <= 0) continue;

            var pay = agirlik.Agirlik / 100.0;
            for (var i = 0; i < hucreSayisi; i++)
            {
                if (icerde[i]) skor[i] += pay * (yogunluk[i] / enBuyuk);
            }
        }

        // ---- 3) Çıktıyı topla --------------------------------------------
        var degerler = new double[hucreSayisi];
        var enYuksek = 0.0;

        for (var i = 0; i < hucreSayisi; i++)
        {
            if (!icerde[i])
            {
                degerler[i] = -1;   // "analiz edilmedi" — sıfırdan farklı
                continue;
            }

            degerler[i] = skor[i];
            if (skor[i] > enYuksek) enYuksek = skor[i];
        }

        var adaylar = AdaylariSec(
            degerler, icerde, sutun, satir, minBoylam, maxEnlem, hucreDerX, hucreDerY,
            agirliklar, kategoriPoileri, metreDereceX, adaySayisi);

        return new IzgaraSonucu(
            extent, sutun, satir, hucreMetre, hucreMetre * SigmaHucre * KesmeSigma,
            degerler, enYuksek, adaylar);
    }

    /// <summary>
    /// Tek bir kategorinin çekirdek yoğunluk yüzeyi.
    ///
    /// Her POI için ızgaranın TAMAMINI dolaşmıyoruz; POI'nin düştüğü hücrenin
    /// etrafındaki 3σ'lık pencereyi dolaşıp oraya "serpiyoruz" (splatting).
    /// Maliyet POI sayısı × sabit bir pencere; ızgara büyüdükçe artmıyor.
    /// </summary>
    private static double[] YogunlukYuzeyi(
        List<AnalizPoisi> poiler,
        int sutun, int satir,
        double minBoylam, double maxEnlem,
        double hucreDerX, double hucreDerY)
    {
        var yuzey = new double[sutun * satir];
        var kesme = SigmaHucre * KesmeSigma;
        var ikiSigmaKare = 2.0 * SigmaHucre * SigmaHucre;

        foreach (var poi in poiler)
        {
            // POI'nin ızgaradaki KESİRLİ konumu (hücre merkezleri ölçeğinde).
            // Tam sayıya yuvarlasaydık aynı hücreye düşen POI'ler tıpatıp
            // aynı yere serpilir, yüzey basamaklı görünürdü.
            var cx = (poi.Boylam - minBoylam) / hucreDerX - 0.5;
            var cy = (maxEnlem - poi.Enlem) / hucreDerY - 0.5;

            var ilkC = Math.Max(0, (int)Math.Floor(cx - kesme));
            var sonC = Math.Min(sutun - 1, (int)Math.Ceiling(cx + kesme));
            var ilkR = Math.Max(0, (int)Math.Floor(cy - kesme));
            var sonR = Math.Min(satir - 1, (int)Math.Ceiling(cy + kesme));

            for (var r = ilkR; r <= sonR; r++)
            {
                var dy = r - cy;

                for (var c = ilkC; c <= sonC; c++)
                {
                    var dx = c - cx;
                    var uzaklikKare = dx * dx + dy * dy;
                    if (uzaklikKare > kesme * kesme) continue;

                    yuzey[r * sutun + c] += Math.Exp(-uzaklikKare / ikiSigmaKare);
                }
            }
        }

        return yuzey;
    }

    /// <summary>
    /// En yüksek puanlı hücrelerden aday konumlar seçer.
    ///
    /// EN ÖNEMLİ AYRINTI: adaylar arasında en az <c>ayrikMesafe</c> hücrelik
    /// mesafe şartı var. Bu olmasaydı ilk beş aday hep aynı tepenin beş
    /// komşu hücresi çıkardı — kullanıcıya beş öneri gibi görünen tek bir
    /// öneri. Şart sayesinde adaylar farklı yığılmaları temsil ediyor.
    /// </summary>
    private static List<IzgaraAdayi> AdaylariSec(
        double[] degerler, bool[] icerde,
        int sutun, int satir,
        double minBoylam, double maxEnlem,
        double hucreDerX, double hucreDerY,
        IReadOnlyList<AnalizAgirligi> agirliklar,
        Dictionary<int, List<AnalizPoisi>> kategoriPoileri,
        double metreDereceX,
        int adaySayisi)
    {
        // Izgaranın yaklaşık onda biri: küçük alanda da büyük alanda da
        // adaylar birbirine yapışmıyor.
        var ayrikMesafe = Math.Max(3, Math.Max(sutun, satir) / 10);

        var siralanmis = Enumerable.Range(0, degerler.Length)
            .Where(i => icerde[i] && degerler[i] > 0)
            .OrderByDescending(i => degerler[i])
            .ToList();

        var secilen = new List<(int Satir, int Sutun, double Deger)>();

        foreach (var i in siralanmis)
        {
            if (secilen.Count >= adaySayisi) break;

            var r = i / sutun;
            var c = i % sutun;

            var cokYakin = secilen.Any(s =>
                Math.Abs(s.Satir - r) < ayrikMesafe && Math.Abs(s.Sutun - c) < ayrikMesafe);

            if (!cokYakin) secilen.Add((r, c, degerler[i]));
        }

        return secilen
            .Select((s, sira) =>
            {
                var boylam = minBoylam + (s.Sutun + 0.5) * hucreDerX;
                var enlem = maxEnlem - (s.Satir + 0.5) * hucreDerY;

                var mesafeler = agirliklar
                    .Select(a => EnYakinPoi(a.KategoriId, kategoriPoileri, boylam, enlem, metreDereceX))
                    .ToList();

                return new IzgaraAdayi(boylam, enlem, s.Deger, mesafeler);
            })
            .ToList();
    }

    /// <summary>
    /// Aday konuma bir kategorinin en yakın POI'si.
    ///
    /// Uzaklık düzlemsel (equirectangular) yaklaşımla ölçülüyor: boylam farkı
    /// enlemin kosinüsüyle düzeltilip Pisagor uygulanıyor. Birkaç yüz
    /// kilometreye kadar hatası binde birin altında; büyük çember hesabı
    /// (haversine) bu ölçekte fark yaratmadan pahalıya gelirdi.
    /// </summary>
    private static AdayMesafesi EnYakinPoi(
        int kategoriId,
        Dictionary<int, List<AnalizPoisi>> kategoriPoileri,
        double boylam, double enlem,
        double metreDereceX)
    {
        if (!kategoriPoileri.TryGetValue(kategoriId, out var liste) || liste.Count == 0)
        {
            return new AdayMesafesi(kategoriId, null, null);
        }

        AnalizPoisi? enYakin = null;
        var enKisa = double.MaxValue;

        foreach (var poi in liste)
        {
            var dx = (poi.Boylam - boylam) * metreDereceX;
            var dy = (poi.Enlem - enlem) * MetreDereceY;
            var uzaklikKare = dx * dx + dy * dy;

            if (uzaklikKare < enKisa)
            {
                enKisa = uzaklikKare;
                enYakin = poi;
            }
        }

        return new AdayMesafesi(kategoriId, enYakin!.Isim, Math.Sqrt(enKisa));
    }
}
