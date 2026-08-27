namespace StajProject.Business.DTOs;

/// <summary>
/// GeoServer bağlantısının o anki durumu (Ödev 8 / Madde 2).
///
/// Harita ekranı bunu açılışta bir kez okur: veri kaynağının GeoServer
/// olduğunu panelde gösterir ve WMS katmanı düğmesini yalnızca bağlantı
/// varsa açar. Sunumda "gerçekten GeoServer'dan mı geliyor?" sorusunun
/// ekrandaki cevabı budur.
/// </summary>
public class GeoServerDurumDto
{
    /// <summary>Okumalar GeoServer üzerinden mi yapılıyor? (GeoServer:Enabled)</summary>
    public bool Etkin { get; set; }

    /// <summary>Sunucuya şu an ulaşılabiliyor mu ve kimlik bilgileri geçerli mi?</summary>
    public bool Ayakta { get; set; }

    /// <summary>GeoServer kök adresi — panelde gösteriliyor.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Katmanların yayınlandığı çalışma alanı.</summary>
    public string Workspace { get; set; } = string.Empty;

    /// <summary>Tam nitelikli katman adları — örn. "staj:vw_point".</summary>
    public List<string> Katmanlar { get; set; } = new();

    /// <summary>
    /// Nokta katmanının tam adı. Isı haritası yalnızca noktaların yoğunluğunu
    /// hesapladığı için arayüz bu katmanı ayrıca bilmek zorunda.
    /// </summary>
    public string NoktaKatmani { get; set; } = string.Empty;

    /// <summary>Isı haritası stilinin adı (Ödev 9 / Madde 2).</summary>
    public string IsiHaritasiStili { get; set; } = string.Empty;

    /// <summary>
    /// Isı haritasının gri tonlamalı ikizi (Ödev 11). Arayüz tıklanan noktanın
    /// yoğunluk değerini bu resimden okuyor.
    /// </summary>
    public string IsiDegerStili { get; set; } = string.Empty;

    /// <summary>
    /// POI katmanının tam adı — "staj:vw_poi" (Ödev 13 / Madde 1).
    /// Çizim katmanlarından ayrı duruyor çünkü ayrı bir düğmeyle açılıp
    /// kapanıyor ve kendi stilleriyle çiziliyor.
    /// </summary>
    public string PoiKatmani { get; set; } = string.Empty;

    // Stil listesi burada DEĞİL: kategori tablosundan türediği için
    // GET /api/poi/stiller ucundan geliyor (renk ve şekil bilgisiyle birlikte,
    // lejant da aynı kaynağı kullanabilsin diye).
}
