using System.ComponentModel.DataAnnotations;

namespace StajProject.Business.DTOs;

// ============================================================================
//  ROTA ALTERNATİFLERİ
//
//  Kullanıcı bir durağa tıkladığında, O DURAĞA GİDEN yolların alternatifleri
//  gösteriliyor: yani bir önceki duraktan seçilen durağa uzanan BACAK için
//  OSRM'in bulduğu farklı güzergahlar.
//
//  ---- NEDEN BACAK, TÜM HAT DEĞİL? ----
//
//  Kullanıcı bir DURAĞA tıklayarak soruyor. "Bu durağa nasıl gidilir?"
//  sorusunun cevabı, hattın tamamının alternatifleri değil, o durağa varan
//  parçanın alternatifleri. Tüm hattı alternatiflendirseydik, kullanıcının
//  tıkladığı durakla ilgisi olmayan bölümler de değişirdi ve seçim
//  "neyi seçtim?" sorusunu doğururdu.
//
//  ---- SEÇİM NASIL KALICI OLUYOR? ----
//
//  Seçilen alternatifin ORTA NOKTASI, hattın tam rotası yeniden hesaplanırken
//  bir ARA NOKTA (via) olarak ekleniyor. OSRM o noktadan geçmek zorunda
//  kalıyor ve sonuç, kullanıcının seçtiği yolu izliyor.
//
//  Alternatifin geometrisini doğrudan saklamak da mümkündü ama o zaman
//  rotanın geri kalanıyla ek yerinde kopukluk oluşurdu; via noktası, tek bir
//  bütün rota üretiyor.
// ============================================================================

/// <summary>Bir durağa giden alternatif yol.</summary>
public class RotaAlternatifiDto
{
    /// <summary>0'dan başlayan sıra. 0 = OSRM'in en iyi bulduğu.</summary>
    public int Sira { get; set; }

    /// <summary>
    /// OSRM'in EN İYİ saydığı seçenek mi? (Sira == 0)
    ///
    /// Arayüz bunu otomatik seçili getiriyor. "En iyi" tanımını biz
    /// yapmıyoruz: OSRM listeyi sürüş maliyetine göre sıralıyor ve en kısa
    /// yol her zaman en hızlı yol değil.
    /// </summary>
    public bool EnIyi { get; set; }

    /// <summary>Yolun kendisi — WKT LINESTRING (EPSG:4326).</summary>
    public string Wkt { get; set; } = string.Empty;

    public double MesafeMetre { get; set; }

    /// <summary>Tahmini SÜRÜŞ süresi (saniye) — sefer süresi değil.</summary>
    public double SureSaniye { get; set; }

    /// <summary>
    /// Bu alternatifi seçmek için kullanılacak ara nokta — WKT POINT.
    ///
    /// Yolun ORTASINDAN alınıyor. Baştan ya da sondan alsaydık nokta,
    /// bütün alternatiflerin ortak olduğu bölgeye düşerdi (hepsi aynı
    /// duraktan çıkıp aynı durağa varıyor) ve OSRM'i bu alternatife
    /// yönlendirmezdi. Ortası, seçenekleri birbirinden ayıran yerdir.
    /// </summary>
    public string ViaWkt { get; set; } = string.Empty;

    /// <summary>
    /// En iyi seçeneğe göre fark (saniye). En iyide 0.
    /// Arayüz "+3 dk" diye gösteriyor: kullanıcı neyi feda ettiğini görsün.
    /// </summary>
    public double SureFarkiSaniye { get; set; }
}

/// <summary>
/// Bir durağa giden alternatiflerin tamamı.
/// </summary>
public class RotaAlternatifleriDto
{
    public int GuzergahId { get; set; }
    public int DurakId { get; set; }

    /// <summary>Bacağın başladığı durak ("Kızılay"). İlk durakta null.</summary>
    public string? OncekiDurakAdi { get; set; }

    public string DurakAdi { get; set; } = string.Empty;

    public List<RotaAlternatifiDto> Alternatifler { get; set; } = new();

    /// <summary>
    /// Alternatif YOKSA sebebini söyleyen mesaj; varsa null.
    ///
    /// Üç ayrı durum var ve kullanıcı için hepsi farklı:
    ///   • seçilen durak hattın İLKİ (öncesinde durak yok)
    ///   • OSRM kapalı
    ///   • OSRM açık ama tek makul yol var
    /// Boş liste dönüp susmak, üçünü de "bir şey olmadı"ya indirgerdi.
    /// </summary>
    public string? Mesaj { get; set; }
}

/// <summary>
/// "Rota Oluştur" isteği — isteğe bağlı ara noktalarla.
/// </summary>
public class RotaOlusturDto
{
    /// <summary>
    /// Rotanın geçmesi ZORUNLU ara noktalar.
    ///
    /// Kullanıcı bir alternatif seçtiğinde onun via noktası buraya geliyor.
    /// Boş bırakılırsa OSRM serbest: kendi en iyi bulduğu yolu çiziyor.
    /// </summary>
    public List<RotaViaDto> ViaNoktalar { get; set; } = new();
}

/// <summary>
/// Bir ara nokta ve AİT OLDUĞU BACAK.
///
/// ---- NEDEN SADECE KOORDİNAT YETMİYOR? ----
///
/// Yalnızca WKT gönderirsek sunucu, noktanın hangi iki durak arasına
/// gireceğini geometriden TAHMİN etmek zorunda kalıyor. O tahmin canlı veride
/// yanıldı: nokta alındığı bacağa değil komşusuna düştü ve rota, seçilen
/// alternatifi izlemek yerine kilometrelerce dolandı (ayrıntı:
/// <c>UlasimService.DuraklaraViaSerpistir</c>).
///
/// Oysa bacak baştan belli — alternatifler zaten TEK bir durağa gelen yol için
/// hesaplandı. <see cref="DurakId"/> o bilgiyi taşıyor ve tahmini gereksiz
/// kılıyor.
/// </summary>
public class RotaViaDto
{
    /// <summary>
    /// Ara noktanın ÖNÜNE gireceği durak. Rota bu durağa gelirken buradan
    /// geçiyor. Hattın ilk durağı verilirse ara nokta atlanıyor: öncesinde
    /// bacak yok.
    /// </summary>
    public int DurakId { get; set; }

    /// <summary>Ara noktanın konumu (WKT POINT).</summary>
    public string Wkt { get; set; } = string.Empty;
}
