using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StajProject.API.Authorization;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;

namespace StajProject.API.Controllers;

/// <summary>
/// Akıllı ulaşım modülü — güzergah ve durak uçları (Ödev 16).
///
/// TEK CONTROLLER, İKİ KAYNAK. Güzergah ve durak ayrı controller'lara
/// bölünebilirdi ama sıralama ucu (<c>PUT .../{id}/sira</c>) ikisinin
/// arasında duruyor: bir güzergahın duraklarını yeniden diziyor. Ayırsaydık
/// o ucun hangi controller'a ait olduğu tartışmalı olurdu ve iki dosya
/// sürekli birlikte değişirdi.
///
/// OKUMA UÇLARI YETKİSİZ — yalnızca <c>[Authorize]</c>.
/// Ödev notu ulaşım rollerinin POI ve çizim EKLEYEMEMESİNİ istiyor ama
/// görüntülemeyi açık bırakıyor. Aynı ilke burada da geçerli: güzergah ve
/// durak ortak referans verisidir (bkz. <see cref="IUlasimService"/>).
/// "Ulaşım Kullanıcısı" rolünün hiç yetkisi yok ve tam da bu yüzden
/// listeleri görebiliyor.
/// </summary>
[ApiController]
[Route("api/ulasim")]
[Authorize]
public class UlasimController : YonetimControllerBase
{
    private readonly IUlasimService _service;

    public UlasimController(IUlasimService service, ILogger<UlasimController> logger)
        : base(logger)
    {
        _service = service;
    }

    // ==================================================================
    //  Güzergah
    // ==================================================================

    /// <summary>
    /// Bütün güzergahlar, durakları SIRALI hâlde.
    ///
    /// Harita katmanı hattın çizgisini bu sıradan üretiyor; yönetim
    /// ekranındaki sürükle-bırak listesi de aynı cevaptan besleniyor.
    /// </summary>
    [HttpGet("guzergahlar")]
    public Task<ActionResult<List<GuzergahDto>>> GuzergahlariGetir()
        => Calistir<List<GuzergahDto>>(async () => Ok(await _service.GuzergahlariGetirAsync()));

    /// <summary>Tek güzergah; yoksa 404.</summary>
    [HttpGet("guzergahlar/{id:int}")]
    public Task<ActionResult<GuzergahDto>> GuzergahGetir(int id)
        => Calistir<GuzergahDto>(async () =>
        {
            var guzergah = await _service.GuzergahGetirAsync(id);
            return guzergah is null ? Bulunamadi(id) : Ok(guzergah);
        });

    /// <summary>Yeni güzergah (ad + renk). "Güzergah Yönetimi" yetkisi ister.</summary>
    [HttpPost("guzergahlar")]
    [YetkiGerekli(Yetkiler.GuzergahYonetimi)]
    public Task<ActionResult<GuzergahDto>> GuzergahEkle([FromBody] GuzergahSaveDto dto)
        => Calistir<GuzergahDto>(async () =>
        {
            var olusan = await _service.GuzergahEkleAsync(dto);
            return CreatedAtAction(nameof(GuzergahGetir), new { id = olusan.Id }, olusan);
        });

    [HttpPut("guzergahlar/{id:int}")]
    [YetkiGerekli(Yetkiler.GuzergahYonetimi)]
    public Task<ActionResult<GuzergahDto>> GuzergahGuncelle(int id, [FromBody] GuzergahSaveDto dto)
        => Calistir<GuzergahDto>(async () =>
        {
            var guncel = await _service.GuzergahGuncelleAsync(id, dto);
            return guncel is null ? Bulunamadi(id) : Ok(guncel);
        });

    /// <summary>
    /// Güzergahı siler (soft delete). Durağı varsa 400 ile reddedilir.
    /// </summary>
    [HttpDelete("guzergahlar/{id:int}")]
    [YetkiGerekli(Yetkiler.GuzergahYonetimi)]
    public Task<IActionResult> GuzergahSil(int id)
        => Calistir(async () =>
            await _service.GuzergahSilAsync(id) ? NoContent() : BulunamadiSonuc(id));

    /// <summary>
    /// Durak SIRASINI değiştirir — sürükle-bırakın sunucudaki karşılığı
    /// (Ödev 16 / Madde 2).
    ///
    /// Gövde bütün durak id'lerini YENİ sırasıyla taşıyor; sunucu 1..N olarak
    /// yazıyor. Tek tek "şunun sırası 3 oldu" demek yerine toplu liste
    /// gönderilmesinin gerekçesi <see cref="DurakSiralamaDto"/>'da.
    /// </summary>
    [HttpPut("guzergahlar/{id:int}/sira")]
    [YetkiGerekli(Yetkiler.GuzergahYonetimi)]
    public Task<ActionResult<GuzergahDto>> SiralamaGuncelle(int id, [FromBody] DurakSiralamaDto dto)
        => Calistir<GuzergahDto>(async () =>
        {
            var guncel = await _service.SiralamaGuncelleAsync(id, dto);
            return guncel is null ? Bulunamadi(id) : Ok(guncel);
        });

    /// <summary>
    /// Ödev 17 / Madde 1 — "Rota Oluştur".
    ///
    /// Güzergahın duraklarından geçen sürüş rotasını OSRM'e hesaplatır,
    /// veritabanına yazar ve güncel güzergahı döner.
    ///
    /// POST, GET DEĞİL: uç veritabanını DEĞİŞTİRİYOR ve dış bir servise
    /// iş yaptırıyor. GET olsaydı tarayıcı ve vekil sunucular onu
    /// önbelleğe alabilir, hatta önceden çağırabilirdi.
    ///
    /// Gövde yok: hesaplanacak her şey (durak dizilimi) zaten sunucuda.
    /// </summary>
    [HttpPost("guzergahlar/{id:int}/rota")]
    [YetkiGerekli(Yetkiler.GuzergahYonetimi)]
    public Task<ActionResult<GuzergahDto>> RotaOlustur(int id, [FromBody] RotaOlusturDto? dto = null)
        => Calistir<GuzergahDto>(async () =>
        {
            // Gövde İSTEĞE BAĞLI: ara nokta verilmezse OSRM serbest, kendi en
            // iyi bulduğu yolu çiziyor. Gövdeyi zorunlu yapsaydık, alternatif
            // seçmeyen her çağrının boş bir nesne göndermesi gerekirdi.
            var guncel = await _service.RotaHesaplaAsync(id, dto?.ViaNoktalar);
            return guncel is null ? Bulunamadi(id) : Ok(guncel);
        });

    /// <summary>
    /// Ödev 18 — seçilen alternatifle hattın TAMAMI nasıl görünürdü?
    ///
    /// Kullanıcı haritada bir alternatife tıkladığında, Google Haritalar'daki
    /// gibi bütün güzergahın o yoldan geçen hâlini görüyor. Bu uç o çizgiyi
    /// hesaplayıp döndürüyor — VERİTABANINA YAZMIYOR.
    ///
    /// ---- NEDEN POST, MADEM DEĞİŞTİRMİYOR? ----
    ///
    /// GET olsaydı ara nokta listesini adres satırına sığdırmak gerekirdi;
    /// birden çok WKT noktası orada hem çirkin hem de uzunluk sınırına açık
    /// olurdu. POST burada "değiştiriyorum" demek değil, "gövdesi olan bir
    /// sorgu" demek.
    ///
    /// ---- NEDEN YETKİ İSTEMİYOR? ----
    ///
    /// Hiçbir şeyi değiştirmiyor. Güzergah ve durak verisi zaten yetkisiz
    /// okunabiliyor; buradan çıkan da onlardan hesaplanmış bir çizgi.
    /// KALICI adım ayrı: <see cref="RotaOlustur"/> ve "Güzergah Yönetimi".
    /// </summary>
    [HttpPost("guzergahlar/{id:int}/rota/onizleme")]
    public Task<ActionResult<RotaOnizlemeDto>> RotaOnizleme(
        int id,
        [FromBody] RotaOlusturDto? dto = null)
        => Calistir<RotaOnizlemeDto>(async () =>
        {
            var sonuc = await _service.RotaOnizleAsync(id, dto?.ViaNoktalar);
            return sonuc is null ? Bulunamadi(id) : Ok(sonuc);
        });

    /// <summary>
    /// Ödev 18 — bir durağa GİDEN yolların alternatifleri.
    ///
    /// Kullanıcı haritada bir durağa tıklayıp "bu durağa nasıl gidilir?" diye
    /// soruyor. Cevap, bir önceki duraktan bu durağa uzanan BACAĞIN farklı
    /// güzergahları.
    ///
    /// GET ve YETKİ İSTEMİYOR: hiçbir şeyi değiştirmiyor, yalnızca hesaplayıp
    /// gösteriyor. Değiştiren adım, seçilen alternatifi kaydeden
    /// <see cref="RotaOlustur"/> ve o "Güzergah Yönetimi" istiyor.
    ///
    /// Alternatif ÜRETİLEMEDİĞİNDE de 200 dönüyor: liste boş, <c>mesaj</c>
    /// sebebini söylüyor. 404 dönmek "durak yok" ile "alternatif yok"u aynı
    /// cevaba indirgerdi.
    /// </summary>
    [HttpGet("duraklar/{id:int}/alternatifler")]
    public Task<ActionResult<RotaAlternatifleriDto>> DurakAlternatifleri(int id)
        => Calistir<RotaAlternatifleriDto>(async () =>
        {
            var sonuc = await _service.DurakAlternatifleriAsync(id);
            return sonuc is null ? Bulunamadi(id) : Ok(sonuc);
        });

    // ==================================================================
    //  Durak
    // ==================================================================

    /// <summary>Bütün duraklar — sahiplik süzgeci YOK (ortak veri).</summary>
    [HttpGet("duraklar")]
    public Task<ActionResult<List<DurakDto>>> DuraklariGetir()
        => Calistir<List<DurakDto>>(async () => Ok(await _service.DuraklariGetirAsync()));

    [HttpGet("duraklar/{id:int}")]
    public Task<ActionResult<DurakDto>> DurakGetir(int id)
        => Calistir<DurakDto>(async () =>
        {
            var durak = await _service.DurakGetirAsync(id);
            return durak is null ? Bulunamadi(id) : Ok(durak);
        });

    /// <summary>
    /// Yeni durak. "Durak Ekleme" yetkisi ister; konum, kullanıcının coğrafi
    /// yetki alanının dışındaysa 400 ile reddedilir (Ödev 7 kuralı).
    /// </summary>
    [HttpPost("duraklar")]
    [YetkiGerekli(Yetkiler.DurakEkleme)]
    public Task<ActionResult<DurakDto>> DurakEkle([FromBody] DurakCreateDto dto)
        => Calistir<DurakDto>(async () =>
        {
            var olusan = await _service.DurakEkleAsync(dto);
            return CreatedAtAction(nameof(DurakGetir), new { id = olusan.Id }, olusan);
        });

    /// <summary>
    /// Durak günceller. Öznitelik YOK: gereken yetki "Güzergah Yönetimi"
    /// VEYA ("Durak Ekleme" + kaydın sahibi olmak) — bu VEYA'yı tek yetki adı
    /// alan <c>[YetkiGerekli]</c> ifade edemiyor, kural servis katmanında.
    /// (POI güncellemesinde de aynı durum var.)
    /// </summary>
    [HttpPut("duraklar/{id:int}")]
    public Task<ActionResult<DurakDto>> DurakGuncelle(int id, [FromBody] DurakUpdateDto dto)
        => Calistir<DurakDto>(async () =>
        {
            var guncel = await _service.DurakGuncelleAsync(id, dto);
            return guncel is null ? Bulunamadi(id) : Ok(guncel);
        });

    /// <summary>Soft delete; yetki kuralı güncellemedekiyle aynı.</summary>
    [HttpDelete("duraklar/{id:int}")]
    public Task<IActionResult> DurakSil(int id)
        => Calistir(async () =>
            await _service.DurakSilAsync(id) ? NoContent() : BulunamadiSonuc(id));

    // ==================================================================
    //  Ödev 19: araç simülasyonu
    // ==================================================================

    /// <summary>
    /// Güzergahta araç simülasyonunu başlatır (Ödev 19 / Madde 1).
    ///
    /// "Simülasyon Başlatma" yetkisi ister — ödev metni gereği yalnızca
    /// Admin ve Operatör rollerinde. Yetkisiz kullanıcı bu ucu çağırırsa
    /// 403 alır; arayüz düğmeyi zaten göstermiyor ama asıl kontrol burada.
    ///
    /// Cevap, aracın BAŞLANGIÇ durumu. Sonraki konumlar SignalR ile geliyor
    /// (<c>/hubs/simulasyon</c> → "KonumGuncellendi").
    /// </summary>
    [HttpPost("guzergahlar/{id:int}/simulasyon")]
    [YetkiGerekli(Yetkiler.SimulasyonBaslatma)]
    public Task<ActionResult<SimulasyonDurumDto>> SimulasyonBaslat(int id)
        => Calistir<SimulasyonDurumDto>(async () =>
        {
            var durum = await _service.SimulasyonBaslatAsync(id);
            return durum is null ? Bulunamadi(id) : Ok(durum);
        });

    /// <summary>
    /// Çalışan simülasyonu durdurur. Çalışmıyorsa 404.
    ///
    /// Başlatmakla AYNI yetkiyi istiyor: durdurmak da bir müdahale, üstelik
    /// bütün takipçilerin ekranından aracı kaldırıyor.
    /// </summary>
    [HttpDelete("guzergahlar/{id:int}/simulasyon")]
    [YetkiGerekli(Yetkiler.SimulasyonBaslatma)]
    public Task<IActionResult> SimulasyonDurdur(int id)
        => Calistir(() => Task.FromResult<IActionResult>(
            _service.SimulasyonDurdur(id)
                ? NoContent()
                : NotFound(new { message = $"{id} numaralı güzergahta çalışan bir simülasyon yok." })));

    /// <summary>
    /// Şu an çalışan bütün simülasyonlar.
    ///
    /// YETKİ İSTEMEZ (yalnızca <c>[Authorize]</c>): ödev "diğer kullanıcılar
    /// takip edebilsin" diyor, takip etmek okuma işidir. Haritayı yeni açan
    /// istemci bu uçtan "hangi hatlarda araç var?" öğreniyor — SignalR
    /// yalnızca bundan SONRAKİ güncellemeleri gönderir.
    /// </summary>
    [HttpGet("simulasyonlar")]
    public ActionResult<IReadOnlyList<SimulasyonDurumDto>> Simulasyonlar()
        => Ok(_service.AktifSimulasyonlar());
}
