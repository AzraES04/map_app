using System.ComponentModel.DataAnnotations;

namespace StajProject.Business.DTOs;

// ============================================================================
//  Ödev 14 — Konum Analizi (analiz paneli, alan seçimi, ağırlıklı ısı haritası)
//
//  Ödev 4'teki "kesişim analizi"nden FARKLI bir iştir, bu yüzden ayrı DTO'lar:
//
//    Kesişim analizi  → "bu alanla kesişen envanter KAÇ TANE?"  (sayma)
//    Konum analizi    → "bu alanın NERESİ, verdiğim kriterlere göre daha
//                        uygun?"                                (skorlama)
//
//  İkincisinin cevabı bir liste değil bir YÜZEY: alanın her noktası için bir
//  puan. O yüzden sonuçta kayıt listesi değil, bir ızgara (grid) dönüyor.
// ============================================================================

/// <summary>
/// Tek bir analiz kriteri: hangi POI kategorisi, 100 üzerinden kaç puan.
///
/// Kategori HİYERARŞİK olduğu için seçilen düğümün ALTINDAKİLER de dahildir:
/// "Yeme-İçme" seçilirse Restoran, Kafe ve Fırın birlikte sayılır. Aksi hâlde
/// kullanıcı kök kategori seçtiğinde hiç POI bulunmazdı — POI'ler daima
/// yaprak kategorilere bağlanıyor.
/// </summary>
public class AnalizKriteriDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Kriter için bir kategori seçilmelidir.")]
    public int KategoriId { get; set; }

    /// <summary>
    /// Ağırlık puanı (1–100). Toplamları TAM OLARAK 100 olmak zorunda —
    /// kural <see cref="Services.KonumAnaliziService"/> içinde.
    ///
    /// Neden burada [Range(1,100)] var ama "toplam 100" yok? Model doğrulama
    /// tek tek alanlara bakar; alanlar ARASI kural (toplam) iş kuralıdır ve
    /// iş katmanında yaşamalı. Böylece aynı kural, servisi doğrudan çağıran
    /// testlerde de geçerli oluyor.
    /// </summary>
    [Range(1, 100, ErrorMessage = "Ağırlık puanı 1 ile 100 arasında olmalıdır.")]
    public int Agirlik { get; set; }
}

/// <summary>
/// Konum analizi isteği.
///
/// HEDEF BÖLGE İKİ YOLDAN BİRİYLE gelir (ödev metni: "İller listesinden seçim
/// yapma veya haritada Poligon çizme"):
///   • <see cref="IlPlakalari"/> dolu → seçilen illerin sınırlarının birleşimi
///   • <see cref="Wkt"/> dolu        → kullanıcının haritada çizdiği poligon
///
/// İkisi birden gönderilirse istek reddediliyor: hangisinin geçerli olduğunu
/// sessizce seçmek, kullanıcının gördüğü alanla analizin çalıştığı alanın
/// farklı olmasına yol açardı.
/// </summary>
public class KonumAnaliziRequestDto
{
    /// <summary>Haritada çizilen hedef bölge — EPSG:4326 POLYGON WKT.</summary>
    public string? Wkt { get; set; }

    /// <summary>Listeden seçilen illerin plaka kodları (1–81).</summary>
    public List<int>? IlPlakalari { get; set; }

    /// <summary>En az 2, en fazla 5 kriter; ağırlıkları toplamı tam 100.</summary>
    public List<AnalizKriteriDto> Kriterler { get; set; } = new();
}

/// <summary>Sonuçta bir kriterin özeti — panelde satır satır gösteriliyor.</summary>
public class AnalizKriterSonucuDto
{
    public int KategoriId { get; set; }

    /// <summary>Kategorinin kendi adı — "Eczane".</summary>
    public string KategoriAdi { get; set; } = string.Empty;

    /// <summary>Kökten yaprağa tam yol — "Sağlık › Eczane".</summary>
    public string KategoriYolu { get; set; } = string.Empty;

    public int Agirlik { get; set; }

    /// <summary>
    /// Bu kriterin SEÇİLEN ALAN İÇİNDE kaç POI'si var? (alt kategoriler dahil)
    ///
    /// Sıfırsa o kriter yüzeye hiçbir şey katmaz. Bunu kullanıcıya söylemek
    /// şart: aksi hâlde ağırlık verdiği bir kriterin haritada hiç karşılığı
    /// olmadığını anlayamaz ve sonucu yanlış okur.
    /// </summary>
    public int PoiSayisi { get; set; }
}

/// <summary>
/// Skor yüzeyinin ızgara (grid) hâli — ısı haritasının ham verisi.
///
/// NEDEN POLİGON LİSTESİ DEĞİL DE DÜZ SAYI DİZİSİ?
/// 96×96'lık bir ızgara 9216 hücre demek. Her hücreyi GeoJSON poligonu olarak
/// göndermek megabaytlarca çıktı üretirdi; düz dizide aynı bilgi onlarca
/// kilobayt. Hücrenin nerede olduğu zaten <see cref="Extent"/> + satır/sütun
/// sayısından hesaplanabiliyor, yani koordinatları taşımak fazlalık.
/// </summary>
public class IsiIzgarasiDto
{
    /// <summary>Izgaranın kapladığı alan: [minBoylam, minEnlem, maxBoylam, maxEnlem].</summary>
    public double[] Extent { get; set; } = new double[4];

    public int Sutun { get; set; }
    public int Satir { get; set; }

    /// <summary>Bir hücrenin kenar uzunluğu (metre) — panelde "çözünürlük" olarak yazılıyor.</summary>
    public double HucreMetre { get; set; }

    /// <summary>
    /// Yoğunluk çekirdeğinin etki yarıçapı (metre). Bir POI'nin puanını kaç
    /// metre öteye kadar taşıdığını söylüyor; ölçek bilgisi olmadan
    /// "0.82" sayısının anlamı eksik kalırdı.
    /// </summary>
    public double EtkiYaricapiMetre { get; set; }

    /// <summary>
    /// Hücre değerleri, <c>satir * Sutun + sutun</c> sırasıyla.
    /// SATIR 0 = EN ÜST (en büyük enlem) — tuvale (canvas) doğrudan
    /// çizilebilsin diye ekran sırasıyla, harita sırasıyla değil.
    ///
    /// Seçilen alanın DIŞINDA kalan hücreler <c>-1</c>: "sıfır" ile
    /// "burası analiz edilmedi" farklı şeyler; aynı değere indirmek haritada
    /// alan sınırını yok ederdi.
    /// </summary>
    public double[] Degerler { get; set; } = Array.Empty<double>();

    /// <summary>
    /// Izgaradaki en yüksek skor (0–1).
    ///
    /// Arayüz renk rampasını 0–<c>EnYuksekSkor</c> aralığına yayıyor. Ham
    /// 0–1'e yaysaydık harita neredeyse hep soluk kalırdı: 1.00 ancak bir
    /// hücre BÜTÜN kriterlerin aynı anda en yoğun olduğu yer olsaydı çıkardı,
    /// bu da pratikte hiç olmuyor.
    /// </summary>
    public double EnYuksekSkor { get; set; }
}

/// <summary>Analizin önerdiği aday konum — ızgaranın en yüksek puanlı hücreleri.</summary>
public class AdayKonumDto
{
    /// <summary>1'den başlayan sıra — panelde "1." diye gösteriliyor.</summary>
    public int Sira { get; set; }

    public double Boylam { get; set; }
    public double Enlem { get; set; }

    /// <summary>Ağırlıklı skor, 0–100 arasına ölçeklenmiş hâli.</summary>
    public double Skor { get; set; }

    /// <summary>Her kriter için en yakın POI ve uzaklığı — öneriyi somutlaştırır.</summary>
    public List<AdayMesafesiDto> Mesafeler { get; set; } = new();
}

/// <summary>Aday konumdan bir kriterin en yakın POI'sine olan uzaklık.</summary>
public class AdayMesafesiDto
{
    public int KategoriId { get; set; }
    public string KategoriAdi { get; set; } = string.Empty;

    /// <summary>En yakın POI'nin adı; o kriterde alan içinde POI yoksa null.</summary>
    public string? PoiAdi { get; set; }

    /// <summary>Uzaklık (metre); POI yoksa null.</summary>
    public double? MesafeMetre { get; set; }
}

/// <summary>Konum analizinin tam sonucu.</summary>
public class KonumAnaliziSonucuDto
{
    /// <summary>
    /// Analizin çalıştığı alan (WKT, EPSG:4326).
    ///
    /// İl seçildiğinde bu, seçilen illerin BİRLEŞİMİDİR — istemci onu
    /// haritaya çizip "analiz tam olarak burada çalıştı" diyebiliyor.
    /// Çizilen poligonda ise gönderilen alanın aynısı.
    /// </summary>
    public string AlanWkt { get; set; } = string.Empty;

    /// <summary>İnsan okuyacağı alan adı — "Ankara, Konya" ya da "Çizilen alan".</summary>
    public string AlanAdi { get; set; } = string.Empty;

    /// <summary>Alanın yüzölçümü (km²) — sonucun ölçeğini anlatıyor.</summary>
    public double AlanKm2 { get; set; }

    /// <summary>Alan içinde kalan ve kriterlerden birine giren toplam POI sayısı.</summary>
    public int ToplamPoi { get; set; }

    public List<AnalizKriterSonucuDto> Kriterler { get; set; } = new();

    public IsiIzgarasiDto Izgara { get; set; } = new();

    public List<AdayKonumDto> Adaylar { get; set; } = new();
}
