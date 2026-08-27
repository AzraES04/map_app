using System.ComponentModel.DataAnnotations;
using StajProject.Business.Mesai;

namespace StajProject.Business.DTOs;

// ============================================================================
//  Ödev 12 — POI ve kategori DTO'ları
//
//  Entity'ler dışarı çıkmıyor: PoiCategory.Parent ↔ Children navigasyonları
//  doğrudan serileştirilse döngüsel JSON üretirdi (ata → çocuk → ata → ...).
//  DTO tarafında hiyerarşi TEK YÖNLÜ taşınıyor: her düğüm yalnızca
//  çocuklarını biliyor.
// ============================================================================

/// <summary>
/// Bir kategori düğümü — ağaç biçiminde (çocuklarıyla birlikte) döner.
/// </summary>
public class PoiKategoriDto
{
    public int Id { get; set; }
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public int? ParentId { get; set; }

    /// <summary>
    /// Kategoriye seçilen simgenin anahtarı (Ödev 15) — "fincan", "eczane"…
    /// Seçilmemişse null; o durumda harita atanın simgesini kullanıyor.
    /// </summary>
    public string? Ikon { get; set; }

    /// <summary>
    /// GERÇEKTEN ÇİZİLEN simgenin anahtarı: kendi ikonu, yoksa atanınki,
    /// o da yoksa varsayılan iğne.
    ///
    /// <see cref="Ikon"/>'dan ayrı duruyor çünkü ikisi farklı sorulara cevap
    /// veriyor: form "kullanıcı ne seçti?" (boş olabilir), liste ve harita
    /// "ekranda ne görünecek?" (asla boş değil). Tek alanı paylaşsaydılar
    /// düzenleme formu, kullanıcının hiç seçmediği bir simgeyi seçilmiş
    /// gibi gösterir ve kaydedince miras kırılırdı.
    /// </summary>
    public string EtkinIkon { get; set; } = string.Empty;

    /// <summary>Kökten bu düğüme kadar olan yol — "Yeme-İçme › Restoran".</summary>
    public string TamYol { get; set; } = string.Empty;

    /// <summary>Kök 0, çocuğu 1... Arayüz girintiyi buna göre veriyor.</summary>
    public int Seviye { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    /// <summary>Doğrudan bu kategoriye bağlı POI sayısı (alt kategoriler hariç).</summary>
    public int PoiSayisi { get; set; }

    /// <summary>Alt kategoriler. Yaprak düğümlerde boş liste.</summary>
    public List<PoiKategoriDto> Cocuklar { get; set; } = new();
}

/// <summary>Kategori ekleme/güncelleme isteği.</summary>
public class PoiKategoriSaveDto
{
    [Required(ErrorMessage = "Kategori adı zorunludur.")]
    [MaxLength(150, ErrorMessage = "Kategori adı en fazla 150 karakter olabilir.")]
    public string Ad { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Açıklama en fazla 500 karakter olabilir.")]
    public string? Aciklama { get; set; }

    /// <summary>
    /// Simge anahtarı (Ödev 15). Boş bırakılabilir — "simge seçilmedi" demek,
    /// kategori atasının simgesini miras alır. Tanınmayan bir anahtar 400 ile
    /// reddediliyor: sessizce yok saysaydık yönetici seçtiğini sandığı simgeyi
    /// haritada hiç göremezdi.
    /// </summary>
    [MaxLength(40, ErrorMessage = "Simge anahtarı en fazla 40 karakter olabilir.")]
    public string? Ikon { get; set; }

    /// <summary>Üst kategori id'si; boş bırakılırsa kök kategori olur.</summary>
    public int? ParentId { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>İstemciye giden POI kaydı.</summary>
public class PoiDto
{
    public int Id { get; set; }
    public string Isim { get; set; } = string.Empty;

    public int KategoriId { get; set; }

    /// <summary>Kategorinin kendi adı — "Restoran".</summary>
    public string KategoriAdi { get; set; } = string.Empty;

    /// <summary>Kök dahil tam yol — "Yeme-İçme › Restoran". Listelerde bağlamı verir.</summary>
    public string KategoriYolu { get; set; } = string.Empty;

    /// <summary>Mesainin okunur özeti — "Pzt-Cum 09:00-18:00 · Cmt kapalı".</summary>
    public string? MesaiSaatleri { get; set; }

    /// <summary>
    /// Gün gün mesai planı (Ödev 13 / Madde 3). Ödev 12'den kalan, planı
    /// olmayan kayıtlarda null — o kayıtlarda yalnızca özet metin var.
    ///
    /// Arayüz düzenleme formunu bu nesneden dolduruyor; metni ayrıştırmaya
    /// çalışmıyor.
    /// </summary>
    public MesaiPlani? MesaiPlani { get; set; }

    /// <summary>Konum, WKT (EPSG:4326): "POINT (32.85 39.93)".</summary>
    public string Wkt { get; set; } = string.Empty;

    /// <summary>Ekleyen kullanıcının id'si; kullanıcı silinmişse null.</summary>
    public int? UserId { get; set; }

    /// <summary>Ekleyen kullanıcının adı — admin panelindeki "ekleyen" sütunu.</summary>
    public string? KullaniciAdi { get; set; }

    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Yeni POI isteği (operatörün harita formu).</summary>
public class PoiCreateDto
{
    [Required(ErrorMessage = "POI adı zorunludur.")]
    [MaxLength(200, ErrorMessage = "POI adı en fazla 200 karakter olabilir.")]
    public string Isim { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Kategori seçilmelidir.")]
    public int KategoriId { get; set; }

    [MaxLength(200, ErrorMessage = "Mesai saatleri en fazla 200 karakter olabilir.")]
    public string? MesaiSaatleri { get; set; }

    /// <summary>
    /// Gün gün mesai planı (Ödev 13 / Madde 3). Doluysa <see cref="MesaiSaatleri"/>
    /// YOK SAYILIR: özet metin plandan üretiliyor, iki kaynağın çelişmesi
    /// mümkün olmasın diye.
    ///
    /// Boş bırakılabilir — mesaisi bilinmeyen bir POI de eklenebilmeli.
    /// </summary>
    public MesaiPlani? MesaiPlani { get; set; }

    /// <summary>Konum — "POINT (32.85 39.93)". Tipi POINT olmak zorunda.</summary>
    [Required(ErrorMessage = "Konum (WKT) zorunludur.")]
    public string Wkt { get; set; } = string.Empty;
}

/// <summary>POI güncelleme isteği.</summary>
public class PoiUpdateDto
{
    [Required(ErrorMessage = "POI adı zorunludur.")]
    [MaxLength(200, ErrorMessage = "POI adı en fazla 200 karakter olabilir.")]
    public string Isim { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Kategori seçilmelidir.")]
    public int KategoriId { get; set; }

    [MaxLength(200, ErrorMessage = "Mesai saatleri en fazla 200 karakter olabilir.")]
    public string? MesaiSaatleri { get; set; }

    /// <summary>Gün gün mesai planı; doluysa özet metin bundan üretilir.</summary>
    public MesaiPlani? MesaiPlani { get; set; }

    /// <summary>
    /// Boş bırakılırsa KONUM DEĞİŞMEZ; yalnızca ad/kategori/mesai güncellenir.
    /// (Geometri güncellemesindeki desenin aynısı — bkz. GeometryUpdateDto.)
    /// </summary>
    public string? Wkt { get; set; }
}

// ============================================================================
//  Ödev 13 — arama, resmî tatiller ve kategori önerisi
// ============================================================================

/// <summary>
/// Arama barının POI sonucu (Ödev 13 / Madde 2).
///
/// Neden <see cref="PoiDto"/> değil? Arama sonucu listesinde mesai, ekleyen
/// kullanıcı ve tarihler gösterilmiyor; her tuş vuruşunda dönen bir cevapta
/// bunları taşımak boşuna bant genişliği. Sonuca tıklanınca zaten haritadaki
/// tam kayda gidiliyor.
/// </summary>
public class PoiAramaSonucuDto
{
    public int Id { get; set; }
    public string Isim { get; set; } = string.Empty;

    /// <summary>Kök dahil tam yol — "Eğitim › Kütüphane".</summary>
    public string KategoriYolu { get; set; } = string.Empty;

    /// <summary>Kök kategori adı — sonuç satırında bağlam veriyor.</summary>
    public string KokKategori { get; set; } = string.Empty;

    /// <summary>
    /// Kategorinin id'si. Arayüz sonuç satırının rengini bununla buluyor:
    /// stil listesi de (GET /api/poi/stiller) kategori id'siyle anahtarlı,
    /// yani listedeki renk haritadaki renkle BİREBİR aynı oluyor.
    /// Ada göre eşleştirseydik kategori yeniden adlandırıldığında renk kaybolurdu.
    /// </summary>
    public int KategoriId { get; set; }

    /// <summary>Konum, WKT (EPSG:4326) — haritanın zoom yapacağı nokta.</summary>
    public string Wkt { get; set; } = string.Empty;

    /// <summary>Mesainin okunur özeti; sonuç satırında ikinci satır olarak görünüyor.</summary>
    public string? MesaiSaatleri { get; set; }

    /// <summary>
    /// Şu an açık mı? Mesai planı olmayan (Ödev 12'den kalan) kayıtlarda null —
    /// "bilmiyoruz" ile "kapalı" farklı şeyler, ikisini aynı değere indirmek
    /// arayüzde yanlış rozet gösterirdi.
    /// </summary>
    public bool? SuAnAcik { get; set; }

    /// <summary>"Açık · 18:00 kapanıyor" gibi kısa durum metni; bilinmiyorsa null.</summary>
    public string? MesaiDurumu { get; set; }

    public bool IsActive { get; set; }
}

/// <summary>
/// Bir POI kategorisinin haritadaki görünümü (Ödev 13 iyileştirmesi).
///
/// Hem WMS isteğinin <c>STYLES</c> parametresini hem de paneldeki lejantı
/// besliyor. İkisi AYNI kaynaktan geldiği için "lejantta mavi yazıyor ama
/// harita yeşil çiziyor" durumu imkânsız — önceki sürümde renkler arayüzde
/// elle kopyalanmış bir listedeydi ve SLD ile eşleşmesi el emeğine bağlıydı.
/// </summary>
public class PoiStilDto
{
    /// <summary>GeoServer'daki stil adı — "poi_kat_13".</summary>
    public string Stil { get; set; } = string.Empty;

    /// <summary>Süzgecin baktığı kategori; yedek stilde 0.</summary>
    public int KategoriId { get; set; }

    /// <summary>Kategorinin kendi adı — "Kütüphane".</summary>
    public string Ad { get; set; } = string.Empty;

    /// <summary>Kökten yaprağa — "Eğitim › Kütüphane".</summary>
    public string TamYol { get; set; } = string.Empty;

    /// <summary>Simgenin rengi (#rrggbb) — lejant bunu kullanıyor.</summary>
    public string Renk { get; set; } = string.Empty;

    /// <summary>SLD şekli: circle · square · triangle · star · cross · x.</summary>
    public string Sekil { get; set; } = string.Empty;

    /// <summary>Çizilen simgenin anahtarı (Ödev 15) — "fincan", "eczane"…</summary>
    public string Ikon { get; set; } = string.Empty;

    /// <summary>
    /// Simgenin çizim parçaları. Arayüz bunlardan hem haritadaki
    /// OpenLayers simgesini hem lejantı kuruyor — yani çizim verisinin
    /// frontend'de KOPYASI YOK, GeoServer'ın çizdiği SVG ile aynı kaynaktan
    /// geliyor (bkz. Business/Poiler/PoiIkonlari.cs).
    /// </summary>
    public List<IkonParcasiDto> IkonParcalari { get; set; } = new();
}

/// <summary>Stil yenileme sonucunun özeti — yönetim ekranı bunu gösteriyor.</summary>
public class PoiStilYenilemeDto
{
    public List<PoiStilDto> Stiller { get; set; } = new();

    /// <summary>GeoServer'a yazılan stil sayısı.</summary>
    public int Yazilan { get; set; }

    /// <summary>Karşılığı kalmadığı için silinen eski stil sayısı.</summary>
    public int Silinen { get; set; }
}

/// <summary>Tek bir resmî tatil günü (Ödev 13 / Madde 3).</summary>
public class ResmiTatilDto
{
    /// <summary>ISO tarih — "2026-10-29".</summary>
    public string Tarih { get; set; } = string.Empty;

    public string Ad { get; set; } = string.Empty;

    /// <summary>Yalnızca öğleden sonrası tatil olan günler (arifeler, 28 Ekim).</summary>
    public bool YarimGun { get; set; }
}

/// <summary>Bir yılın resmî tatil listesi ve listenin güvenilirlik bilgisi.</summary>
public class ResmiTatilListesiDto
{
    public int Yil { get; set; }

    public List<ResmiTatilDto> Tatiller { get; set; } = new();

    /// <summary>
    /// Bu yılın dinî bayram tarihleri tanımlı mı?
    ///
    /// Ramazan/Kurban tarihleri hicrî takvime bağlı olduğu için elle
    /// tutuluyor (bkz. ResmiTatiller). Tanımlı olmayan bir yılda liste
    /// yalnızca sabit tatilleri içerir; arayüz bunu kullanıcıya söylüyor —
    /// eksik listeyi tam listeymiş gibi göstermek yanlış bilgi olurdu.
    /// </summary>
    public bool DiniBayramlarTanimli { get; set; }
}

/// <summary>
/// Seçilen yer için önerilen kategori (Ödev 13 / Madde 4).
/// Karşılığı bulunamazsa uç 204 döner, bu nesne hiç üretilmez.
/// </summary>
public class KategoriOneriDto
{
    /// <summary>Önerilen (yaprak) kategorinin id'si — form bunu seçiyor.</summary>
    public int KategoriId { get; set; }

    /// <summary>Yaprak kategorinin adı — "Kütüphane".</summary>
    public string KategoriAdi { get; set; } = string.Empty;

    /// <summary>Kök kategorinin id'si; öneri zaten kökse null.</summary>
    public int? ParentId { get; set; }

    /// <summary>Kök kategorinin adı — "Eğitim".</summary>
    public string? ParentAdi { get; set; }

    /// <summary>Tam yol — "Eğitim › Kütüphane".</summary>
    public string TamYol { get; set; } = string.Empty;

    /// <summary>Öneriyi neyin tetiklediği; arayüz kullanıcıya gösteriyor.</summary>
    public string Gerekce { get; set; } = string.Empty;
}

// ============================================================================
//  Ödev 15 — kategori simgeleri
// ============================================================================

/// <summary>
/// Bir simgenin tek çizim parçası — SVG <c>path</c> verisi.
///
/// Entity/Business tarafındaki <c>IkonParcasi</c> record'unun DTO ikizi.
/// Neden doğrudan o tip gönderilmiyor? Business tipleri dışarı çıkmıyor
/// (projedeki genel kural); ayrıca DTO alan adları JSON sözleşmesidir,
/// iç tipin adı değişince istemcinin kırılmaması gerekiyor.
/// </summary>
public class IkonParcasiDto
{
    /// <summary>SVG <c>path</c> elemanının <c>d</c> özniteliği.</summary>
    public string D { get; set; } = string.Empty;

    /// <summary>true ise kategori rengiyle değil BEYAZLA boyanır (simge içindeki oyuk).</summary>
    public bool Beyaz { get; set; }
}

/// <summary>Seçilebilir bir simge — yönetim panelindeki ikon seçicinin satırı.</summary>
public class PoiIkonDto
{
    /// <summary>Veritabanında saklanan anahtar — "fincan".</summary>
    public string Anahtar { get; set; } = string.Empty;

    /// <summary>Seçicide görünen ad — "Fincan".</summary>
    public string Ad { get; set; } = string.Empty;

    /// <summary>Çizim parçaları; seçici önizlemeyi bunlardan basıyor.</summary>
    public List<IkonParcasiDto> Parcalar { get; set; } = new();
}

/// <summary>Simge kataloğu: seçilebilecek bütün ikonlar + ortak viewBox.</summary>
public class PoiIkonKatalogDto
{
    /// <summary>Bütün çizimlerin ortak kutusu — "0 0 24 24".</summary>
    public string ViewBox { get; set; } = string.Empty;

    /// <summary>Kategoriye simge seçilmediğinde kullanılan anahtar.</summary>
    public string Varsayilan { get; set; } = string.Empty;

    public List<PoiIkonDto> Ikonlar { get; set; } = new();
}
