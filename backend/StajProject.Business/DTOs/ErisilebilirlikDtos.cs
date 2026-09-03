namespace StajProject.Business.DTOs;

// ============================================================================
//  TOPLU TAŞIMA ERİŞİLEBİLİRLİK ANALİZİ
//
//  Konum Analizi (Ödev 14) ile AYNI biçimde hedef bölge seçiliyor (il listesi
//  ya da çizilen poligon) — kullanıcı zaten o deseni biliyor, ikinci bir
//  öğrenme eğrisi çıkarmamak için bilerek aynı sözleşme kopyalandı.
// ============================================================================

/// <summary>İstek — hedef bölge, Konum Analizi'yle AYNI iki yoldan biriyle.</summary>
public class ErisilebilirlikRequestDto
{
    public string? Wkt { get; set; }

    public List<int>? IlPlakalari { get; set; }

    /// <summary>
    /// Yalnızca AKTİF güzergahların durakları mı sayılsın?
    ///
    /// Varsayılan true: pasife alınmış (artık çalışmayan) bir hat, "buraya
    /// toplu taşıma ulaşıyor" yanılgısı yaratırdı.
    /// </summary>
    public bool YalnizcaAktif { get; set; } = true;
}

/// <summary>Sonuç — ısı haritasıyla AYNI ızgara biçimi (bkz. IsiIzgarasiDto).</summary>
public class ErisilebilirlikSonucuDto
{
    public string AlanWkt { get; set; } = string.Empty;

    public string AlanAdi { get; set; } = string.Empty;

    /// <summary>Hesaba giren durak sayısı — sıfırsa yüzey anlamsız, arayüz bunu söylüyor.</summary>
    public int DurakSayisi { get; set; }

    /// <summary>
    /// Alan içindeki hücrelerin yüzde kaçı "iyi erişim" (≤400 m) sınırında.
    ///
    /// İKİ ondalık: bir ilin tamamında bu oran %0,03 gibi çıkabiliyor
    /// (15 durak, 25.000 km²). Tek ondalığa yuvarlanınca "0,0" oluyordu ve
    /// "hiç erişim yok" ile "çok az erişim var" ayırt edilemiyordu — oysa
    /// ikincisinde haritada sarı adacıklar var ve sayı onlarla çelişiyordu.
    /// </summary>
    public double IyiErisimYuzdesi { get; set; }

    /// <summary>
    /// Sonucu OKUMAYA yardım eden notlar — "hücreler kaba", "oran ilin
    /// tamamı üzerinden" gibi. Hata değil: sonuç geçerli, ama bağlamsız
    /// bakan biri "%0,1" sayısını "analiz bozuk" diye okuyor (canlıda tam
    /// olarak bu oldu).
    /// </summary>
    public List<string> Uyarilar { get; set; } = new();

    public IsiIzgarasiDto Izgara { get; set; } = new();
}
