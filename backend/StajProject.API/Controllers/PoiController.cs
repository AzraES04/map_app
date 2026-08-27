using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.API.Authorization;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Poiler;
using StajProject.Business.Services;

namespace StajProject.API.Controllers;

/// <summary>
/// POI uçları (Ödev 12).
///
/// TEK controller hem operatörün harita ekranına hem yöneticinin
/// "POI Yönetimi" ekranına hizmet ediyor. Ayrı bir <c>api/admin/poi</c>
/// açmadık: iki ekranın istediği veri ve işlemler AYNI (listele, düzenle,
/// sil, aktifliği değiştir); tek fark kimin hangi kayda dokunabildiği ve o
/// fark zaten servis katmanında, tek bir kuralda duruyor
/// (bkz. <c>PoiService.YetkiliMiAsync</c>). İki controller yazsaydık aynı
/// kural iki yerde tutulmak zorunda kalır, biri güncellenip diğeri unutulurdu.
/// </summary>
[ApiController]
[Route("api/poi")]
[Authorize]
public class PoiController : YonetimControllerBase
{
    private readonly IPoiService _service;
    private readonly IPoiCategoryService _kategoriService;
    private readonly IPoiStyleService _stilService;

    public PoiController(
        IPoiService service,
        IPoiCategoryService kategoriService,
        IPoiStyleService stilService,
        ILogger<PoiController> logger)
        : base(logger)
    {
        _service = service;
        _kategoriService = kategoriService;
        _stilService = stilService;
    }

    /// <summary>
    /// Bütün POI'ler. Sahiplik süzgeci YOK — POI ortak referans verisidir,
    /// giriş yapan herkes haritada görür (gerekçe: <see cref="IPoiService"/>).
    /// </summary>
    [HttpGet]
    public Task<ActionResult<List<PoiDto>>> GetAll()
        => Calistir<List<PoiDto>>(async () => Ok(await _service.GetAllAsync()));

    /// <summary>
    /// POI ARAMA (Ödev 13 / Madde 2) — Google Maps benzeri arama barının ucu.
    ///
    /// YETKİ ÖZNİTELİĞİ YOK, yalnızca [Authorize]. Ödev "bu arama özelliği
    /// Kullanıcı (User) rolüne de açık olmalıdır" diyor: Kullanıcı rolünün
    /// hiçbir çizim/POI yetkisi yok ama arama yapabilmeli. Zaten listeleme ucu
    /// da (GET /api/poi) süzgeçsiz ve yetkisiz — POI ortak referans verisi.
    ///
    /// Süzme SUNUCUDA yapılıyor (GeoServer'da CQL, veritabanında ILIKE);
    /// bütün listeyi indirip tarayıcıda süzmek, tablo büyüdükçe her tuş
    /// vuruşunda megabaytlar taşımak demek olurdu.
    /// </summary>
    /// <param name="q">Aranan metin. En az 2 karakter; altındaysa boş liste döner.</param>
    [HttpGet("ara")]
    public Task<ActionResult<List<PoiAramaSonucuDto>>> Ara([FromQuery] string? q)
        => Calistir<List<PoiAramaSonucuDto>>(async () => Ok(await _service.AraAsync(q)));

    /// <summary>
    /// Resmî tatil takvimi (Ödev 13 / Madde 3).
    ///
    /// "Resmî kurum" kipi seçildiğinde form, kurumun kapalı olacağı günleri
    /// listeliyor. Liste iş katmanında tanımlı (bkz. <c>ResmiTatiller</c>);
    /// arayüzde tutulsaydı her istemci kendi kopyasını taşır, biri
    /// güncellenmeyi unuturdu.
    /// </summary>
    /// <param name="yil">İstenen yıl; verilmezse içinde bulunulan yıl.</param>
    [HttpGet("resmi-tatiller")]
    public ActionResult<ResmiTatilListesiDto> ResmiTatiller([FromQuery] int? yil)
        => Ok(_service.ResmiTatilleriGetir(yil));

    /// <summary>
    /// Seçilen yer için KATEGORİ ÖNERİSİ (Ödev 13 / Madde 4).
    ///
    /// Arayüz haritadan bir yer seçtiğinde ya da arama sonucuna tıkladığında
    /// o yerin OpenStreetMap türünü ve adını buraya gönderiyor; uç, kategori
    /// ağacındaki karşılığını ata-çocuk çifti hâlinde döndürüyor
    /// ("Millî Kütüphane" → Eğitim › Kütüphane).
    ///
    /// Karşılığı yoksa 204 (No Content): "istek doğruydu ama söyleyecek bir
    /// şey yok" durumunun karşılığı bu. 404 yanlış olurdu — aranan bir kaynak
    /// yok, bir öneri yok.
    /// </summary>
    /// <param name="tur">OpenStreetMap <c>type</c> alanı — "library", "pharmacy"…</param>
    /// <param name="sinif">OpenStreetMap <c>class</c> alanı — "amenity", "tourism"…</param>
    /// <param name="isim">Yerin adı — ad içindeki anahtar kelimeye de bakılıyor.</param>
    [HttpGet("kategori-oner")]
    public Task<ActionResult<KategoriOneriDto>> KategoriOner(
        [FromQuery] string? tur, [FromQuery] string? sinif, [FromQuery] string? isim)
        => Calistir<KategoriOneriDto>(async () =>
        {
            var oneri = await _service.KategoriOnerAsync(tur, sinif, isim);
            return oneri is null ? NoContent() : Ok(oneri);
        });

    /// <summary>
    /// POI stillerinin TANIMLARI (Ödev 13 iyileştirmesi).
    ///
    /// Harita ekranı bunu iki iş için kullanıyor:
    ///   • WMS isteğinin <c>STYLES</c> parametresini kurmak
    ///   • sağ paneldeki lejantı çizmek
    ///
    /// İkisinin AYNI kaynaktan gelmesi önemli: önceki sürümde renkler arayüzde
    /// elle kopyalanmış bir listedeydi ve SLD ile eşleşmesi el emeğine
    /// bağlıydı — "lejantta mavi yazıyor, harita yeşil çiziyor" hatası
    /// mümkündü. Artık ikisi de kategori tablosundan türüyor.
    ///
    /// Yetki istemiyor: lejant ve katman görünümü, POI listesi gibi herkese
    /// açık. GeoServer'a hiç gitmiyor, yalnızca kategori tablosunu okuyor;
    /// bu yüzden GeoServer kapalıyken de doğru cevap veriyor.
    /// </summary>
    [HttpGet("stiller")]
    public Task<ActionResult<List<PoiStilDto>>> Stiller()
        => Calistir<List<PoiStilDto>>(async () => Ok(await _stilService.ListeleAsync()));

    /// <summary>Tek POI.</summary>
    [HttpGet("{id:int}")]
    public Task<ActionResult<PoiDto>> GetById(int id)
        => Calistir<PoiDto>(async () =>
        {
            var poi = await _service.GetByIdAsync(id);
            return poi is null ? Bulunamadi(id) : Ok(poi);
        });

    /// <summary>
    /// Operatörün formundaki kategori açılır listesi: yalnızca AKTİF kategoriler.
    ///
    /// Kategori YÖNETİMİ ucu ayrı (<see cref="AdminPoiCategoriesController"/>) ve
    /// "POI Yönetimi" yetkisi ister. Bu uç yalnızca okur; POI ekleyecek her
    /// operatörün kategorileri görebilmesi gerekiyor.
    /// </summary>
    [HttpGet("kategoriler")]
    public Task<ActionResult<List<PoiKategoriDto>>> GetKategoriler()
        => Calistir<List<PoiKategoriDto>>(async () => Ok(await _kategoriService.GetSelectableAsync()));

    /// <summary>
    /// Seçilebilecek kategori SİMGELERİ ve çizimleri (Ödev 15).
    ///
    /// Yetki istemez, veritabanına da gitmez: katalog koddaki sabit listedir
    /// (<c>PoiIkonlari</c>). Yönetim panelindeki simge seçici bunu bir kez
    /// indirip önizlemeleri buradan çiziyor — yani SVG yolları arayüzde
    /// TEKRAR TANIMLANMIYOR. GeoServer'ın haritaya bastığı simge ile paneldeki
    /// önizleme aynı veriden geliyor, ikisi ayrışamaz.
    /// </summary>
    [HttpGet("ikonlar")]
    public ActionResult<PoiIkonKatalogDto> GetIkonlar() => Ok(new PoiIkonKatalogDto
    {
        ViewBox = PoiIkonlari.ViewBox,
        Varsayilan = PoiIkonlari.Varsayilan,
        Ikonlar = PoiIkonlari.Tumu.Select(i => new PoiIkonDto
        {
            Anahtar = i.Anahtar,
            Ad = i.Ad,
            Parcalar = i.Parcalar.Select(p => new IkonParcasiDto { D = p.D, Beyaz = p.Beyaz }).ToList(),
        }).ToList(),
    });

    /// <summary>
    /// Yeni POI. "POI Ekleme" yetkisi ister; konum, kullanıcının coğrafi
    /// yetki alanının dışındaysa 400 ile reddedilir (Ödev 7 kuralı).
    /// </summary>
    [HttpPost]
    [YetkiGerekli(Yetkiler.PoiEkleme)]
    public Task<ActionResult<PoiDto>> Create([FromBody] PoiCreateDto dto)
        => Calistir<PoiDto>(async () =>
        {
            var olusan = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = olusan.Id }, olusan);
        });

    /// <summary>
    /// POI günceller. Öznitelik YOK: gereken yetki "POI Yönetimi" VEYA
    /// ("POI Ekleme" + kaydın sahibi olmak) — bu VEYA'yı tek yetki adı alan
    /// [YetkiGerekli] ifade edemiyor, kural servis katmanında.
    /// </summary>
    [HttpPut("{id:int}")]
    public Task<ActionResult<PoiDto>> Update(int id, [FromBody] PoiUpdateDto dto)
        => Calistir<PoiDto>(async () =>
        {
            var guncel = await _service.UpdateAsync(id, dto);
            return guncel is null ? Bulunamadi(id) : Ok(guncel);
        });

    /// <summary>Soft delete — kayıt tabloda kalır, is_deleted işaretlenir.</summary>
    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id)
        => Calistir(async () =>
        {
            var silindi = await _service.DeleteAsync(id);
            return silindi ? NoContent() : BulunamadiSonuc(id);
        });

    /// <summary>
    /// Kaydı askıya alır / yeniden aktif eder. Pasif POI silinmiş değildir:
    /// listelerde ve haritada görünmeye devam eder, sadece "Pasif" işaretlenir.
    /// </summary>
    [HttpPost("{id:int}/active")]
    public Task<IActionResult> SetActive(int id, [FromBody] SetActiveDto dto)
        => Calistir(async () =>
        {
            var degisti = await _service.SetActiveAsync(id, dto.IsActive);
            return degisti ? NoContent() : BulunamadiSonuc(id);
        });

    /// <summary>
    /// Silmeyi geri alır. Bu uç sahiplik SORAMAZ (silinmiş kayıt sorgu
    /// filtresinin arkasında), o yüzden doğrudan "POI Yönetimi" yetkisine
    /// bağlandı: geri getirme yönetimsel bir iştir.
    /// </summary>
    [HttpPost("{id:int}/restore")]
    [YetkiGerekli(Yetkiler.PoiYonetimi)]
    public Task<IActionResult> Restore(int id)
        => Calistir(async () =>
        {
            var geriAlindi = await _service.RestoreAsync(id);
            return geriAlindi
                ? NoContent()
                : NotFound(new { message = $"Id={id} için geri alınacak silinmiş POI bulunamadı." });
        });
}
