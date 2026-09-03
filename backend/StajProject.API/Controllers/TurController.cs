using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StajProject.API.Authorization;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Services;

namespace StajProject.API.Controllers;

/// <summary>
/// Tur modülü uçları.
///
/// Şu an tek uç var: arayüzdeki TourBuilder'ın topladığı seçimlerden rota
/// önerisi üretmek. Tur şablonunun kaydedilmesi, oturum başlatma gibi uçlar
/// aynı controller'a eklenecek — hepsi aynı kaynağın (tur) parçası.
/// </summary>
[ApiController]
[Route("api/tur")]
[Authorize]
public class TurController : YonetimControllerBase
{
    private readonly ITurPlanlamaServisi _planlama;
    private readonly ITuristikPoiAktarici _aktarici;
    private readonly ITurOturumServisi _oturumlar;
    private readonly IYoklamaServisi _yoklama;

    public TurController(
        ITurPlanlamaServisi planlama,
        ITuristikPoiAktarici aktarici,
        ITurOturumServisi oturumlar,
        IYoklamaServisi yoklama,
        ILogger<TurController> logger)
        : base(logger)
    {
        _planlama = planlama;
        _aktarici = aktarici;
        _oturumlar = oturumlar;
        _yoklama = yoklama;
    }

    /// <summary>
    /// Seçimlere göre sıralı durak listesi ve rota önerir.
    ///
    /// ---- NEDEN YETKİ İSTİYOR? ----
    /// Bu uç, isteği DIŞ ve ÜCRETLİ bir servise (Google Maps Platform)
    /// çeviriyor. Okuma uçlarının çoğu yalnızca <c>[Authorize]</c> ile
    /// korunuyor çünkü onların maliyeti bizim veritabanımızda başlıyor ve
    /// bitiyor. Burada her çağrı faturaya yazılıyor: "giriş yapan herkes
    /// çağırabilir" demek, tek bir sekmenin yenilenmesiyle kotayı tüketmek
    /// demek olurdu. Tur oluşturma yetkisi olan kullanıcı (Rehber) zaten bu
    /// işi yapan kişi.
    ///
    /// ---- NEDEN POST? ----
    /// Sorgu iç içe bir nesne (lokasyon, süre, tema, kısıtlar); adres satırına
    /// sıkıştırmak hem okunmaz bir URL hem uzunluk sınırı riski olurdu.
    /// Sunucuda KAYIT OLUŞTURMUYOR: dönen tur şablonu bir cevaptır, kullanıcı
    /// "kaydet" diyene kadar veritabanında yeri yoktur.
    /// </summary>
    /// <response code="200">Öneri hazırlandı.</response>
    /// <response code="400">Girdi geçersiz (süre, şehir, tema…).</response>
    /// <response code="503">Google servisi yapılandırılmamış ya da cevap vermiyor.</response>
    [HttpPost("rota-oner")]
    [YetkiGerekli(Yetkiler.TurYonetimi)]
    public Task<ActionResult<TurOneriDto>> RotaOner(
        [FromBody] TurRotaIstegiDto istek,
        CancellationToken iptal)
        => Calistir<TurOneriDto>(async () => Ok(await _planlama.RotaOnerAsync(istek, iptal)));

    /// <summary>
    /// ELLE DÜZENLENMİŞ durak listesi için rotayı yeniden hesaplar.
    ///
    /// ---- NEDEN AYRI UÇ (rota-oner'e eklemedik)? ----
    /// <c>rota-oner</c> mekanları kendisi arıyor; bu uç aramıyor. Kullanıcı
    /// listeye elle bir POI eklediğinde seçim zaten yapılmış, eksik olan tek
    /// şey yollara oturmuş çizgi. Aynı uçtan geçirseydik her küçük düzenleme
    /// yeni bir mekan aramasını ve dış servis maliyetini tetiklerdi.
    ///
    /// Rota çizilemezse yine 200 dönüyor, cevabın <c>uyari</c> alanı doluyor:
    /// rota olmadan da durak listesi geçerli bir tur ve dış servisin geçici
    /// aksaklığı kullanıcının düzenlemesini çöpe atmamalı.
    /// </summary>
    /// <response code="200">Rota hesaplandı (ya da uyarıyla birlikte boş döndü).</response>
    /// <response code="400">İkiden az durak ya da geçersiz koordinat.</response>
    [HttpPost("rota-hesapla")]
    [YetkiGerekli(Yetkiler.TurYonetimi)]
    public Task<ActionResult<TurRotaHesapSonucuDto>> RotaHesapla(
        [FromBody] TurRotaHesapIstegiDto istek,
        CancellationToken iptal)
        => Calistir<TurRotaHesapSonucuDto>(async () =>
            Ok(await _planlama.RotaHesaplaAsync(istek, iptal)));

    /// <summary>
    /// OpenStreetMap'ten turistik POI içe aktarır (müze, anıt, park…).
    ///
    /// ---- NEDEN "POI Yönetimi" YETKİSİ? ----
    /// Bu uç TUR üretmiyor, ORTAK POI KATALOĞUNU değiştiriyor: yeni kategoriler
    /// açıyor ve herkesin haritasında görünecek kayıtlar yazıyor. Kataloğa
    /// dokunmak baştan beri "POI Yönetimi" yetkisinin işi (bkz. Yetkiler.cs);
    /// tur yetkisine bağlasaydık, tur açabilen herkes ortak veriyi
    /// değiştirebilir olurdu.
    ///
    /// TEKRAR ÇALIŞTIRILABİLİR: aynı adla yakında duran kayıt atlanıyor.
    /// </summary>
    /// <response code="200">Aktarım tamamlandı (sayılar cevapta).</response>
    /// <response code="400">Geçersiz plaka.</response>
    /// <response code="503">OpenStreetMap kaynağına ulaşılamadı.</response>
    [HttpPost("poi-aktar")]
    [YetkiGerekli(Yetkiler.PoiYonetimi)]
    public Task<ActionResult<TuristikAktarimSonucuDto>> TuristikPoiAktar(
        [FromBody] TuristikAktarimIstegiDto istek,
        CancellationToken iptal)
        => Calistir<TuristikAktarimSonucuDto>(async () =>
            Ok(await _aktarici.AktarAsync(istek.IlPlakalari, iptal)));

    // ==================================================================
    //  Tur şablonu
    // ==================================================================

    /// <summary>Kayıtlı turlar (durakları sıralı).</summary>
    [HttpGet("turlar")]
    public Task<ActionResult<List<TourDto>>> Turlar()
        => Calistir<List<TourDto>>(async () => Ok(await _oturumlar.TurlariGetirAsync()));

    /// <summary>Tek tur; yoksa 404.</summary>
    [HttpGet("turlar/{id:int}")]
    public Task<ActionResult<TourDto>> Tur(int id)
        => Calistir<TourDto>(async () =>
        {
            var tur = await _oturumlar.TurGetirAsync(id);
            return tur is null ? Bulunamadi(id) : Ok(tur);
        });

    /// <summary>
    /// Rota önerisini kalıcı tur şablonuna çevirir.
    ///
    /// Paylaşılabilir bir bağlantının ön koşulu: bağlantı SUNUCUDA duran bir
    /// şeye işaret etmek zorunda, öneri ise yalnızca bir cevaptı.
    /// </summary>
    [HttpPost("turlar")]
    [YetkiGerekli(Yetkiler.TurYonetimi)]
    public Task<ActionResult<TourDto>> TurKaydet([FromBody] TurKaydetDto istek)
        => Calistir<TourDto>(async () => Ok(await _oturumlar.TuruKaydetAsync(istek)));

    /// <summary>Turu siler (soft delete) — yalnızca oluşturan rehber.</summary>
    [HttpDelete("turlar/{id:int}")]
    [YetkiGerekli(Yetkiler.TurYonetimi)]
    public Task<IActionResult> TurSil(int id)
        => Calistir(async () =>
            await _oturumlar.TurSilAsync(id) ? NoContent() : BulunamadiSonuc(id));

    /// <summary>
    /// Rehberin canlı konumunu bildirir (cihazın GPS'i).
    ///
    /// Yetki kontrolü SERVİSTE: "Tur Yönetimi" yetkisi burada yeterli değil,
    /// çağıranın O OTURUMUN rehberi olması gerekiyor. Öznitelik bunu ifade
    /// edemediği için kontrol iş kuralında (bkz. TurOturumServisi).
    /// </summary>
    [HttpPut("oturumlar/{id:int}/konum")]
    [YetkiGerekli(Yetkiler.TurYonetimi)]
    public Task<ActionResult<TourSessionDto>> KonumBildir(
        int id,
        [FromBody] TurKonumDto konum)
        => Calistir<TourSessionDto>(async () =>
            Ok(await _oturumlar.KonumBildirAsync(id, konum)));

    /// <summary>Konum yayınını durdurur ve kayıtlı konumu siler.</summary>
    [HttpDelete("oturumlar/{id:int}/konum")]
    [YetkiGerekli(Yetkiler.TurYonetimi)]
    public Task<ActionResult<TourSessionDto>> KonumYayiniDurdur(int id)
        => Calistir<TourSessionDto>(async () =>
            Ok(await _oturumlar.KonumYayininiDurdurAsync(id)));

    // ==================================================================
    //  Yoklama — "şu an kimler burada?"
    // ==================================================================

    /// <summary>
    /// Yoklamayı başlatır (varsa sıfırlar). Yalnızca oturumun rehberi.
    /// </summary>
    [HttpPost("oturumlar/{id:int}/yoklama")]
    [YetkiGerekli(Yetkiler.TurYonetimi)]
    public Task<ActionResult<YoklamaDurumuDto>> YoklamaBaslat(
        int id,
        [FromBody] YoklamaBaslatDto istek)
        => Calistir<YoklamaDurumuDto>(async () =>
        {
            // Rehber kontrolü konum ucundaki gibi SERVİSTE: "Tur Yönetimi"
            // yetkisi yeterli değil, çağıranın O oturumun rehberi olması
            // gerekiyor. Doğrulamayı oturum servisine yaptırıp sonucu
            // kullanıyoruz.
            await _oturumlar.RehberDogrulaAsync(id);

            return Ok(_yoklama.Baslat(id, istek.Soru, istek.GrupBoyu));
        });

    /// <summary>Açık yoklamanın anlık sayıları (rehber).</summary>
    [HttpGet("oturumlar/{id:int}/yoklama")]
    [YetkiGerekli(Yetkiler.TurYonetimi)]
    public Task<ActionResult<YoklamaDurumuDto>> YoklamaDurumu(int id)
        => Calistir<YoklamaDurumuDto>(async () =>
        {
            await _oturumlar.RehberDogrulaAsync(id);

            var durum = _yoklama.Durum(id);
            return durum is null ? NoContent() : Ok(durum);
        });

    /// <summary>Yoklamayı kapatır (rehber).</summary>
    [HttpDelete("oturumlar/{id:int}/yoklama")]
    [YetkiGerekli(Yetkiler.TurYonetimi)]
    public Task<IActionResult> YoklamaBitir(int id)
        => Calistir(async () =>
        {
            await _oturumlar.RehberDogrulaAsync(id);
            _yoklama.Bitir(id);
            return NoContent();
        });

    /// <summary>
    /// MİSAFİRİN CEVABI — kimliksiz, katılım koduyla.
    ///
    /// Uç anonim çünkü cevap verecek kişinin hesabı yok; kod, o gruba ait
    /// olduğunun tek kanıtı. Cevap kümesi kapalı ("Buradayim" | "Degilim" |
    /// "Acil") — serbest metin alsaydık uç, kimliksiz bir mesaj kutusuna
    /// dönerdi ve sayılamazdı.
    /// </summary>
    [HttpPost("misafir/{kod}/yoklama")]
    [AllowAnonymous]
    [EnableRateLimiting("misafir")]
    public Task<ActionResult<YoklamaDurumuDto>> MisafirYoklamaCevabi(
        string kod,
        [FromBody] YoklamaCevapDto istek)
        => Calistir<YoklamaDurumuDto>(async () =>
        {
            // Kod, oturumu bulmanın TEK yolu: misafir oturum id'sini bilmiyor
            // ve bilmemeli (bkz. MisafirTurDto).
            var oturumId = await _oturumlar.MisafirOturumIdAsync(kod);

            if (oturumId is null)
            {
                return NotFound(new { message = "Bu katılım kodu geçerli değil ya da tur sona ermiş." });
            }

            var durum = _yoklama.Cevapla(
                oturumId.Value, istek.MisafirAnahtari, istek.Cevap, istek.Ad, istek.Telefon);

            // Açık yoklama yoksa 204: misafirin ekranı "yoklama kapandı"
            // diyebilsin. Hata döndürseydik bu normal durum kırmızı bir
            // uyarıya dönerdi.
            if (durum is null)
            {
                return NoContent();
            }

            // KİŞİ LİSTESİ MİSAFİRE GİTMİYOR: kimliksiz bir uçtan gruptaki
            // herkesin adı/telefonu dışarı sızmasın. Misafir kendi cevabını
            // BenimCevabim'den zaten görüyor.
            durum.Kisiler = new List<YoklamaKisiDto>();
            return Ok(durum);
        });

    // ==================================================================
    //  Misafir görünümü — KİMLİKSİZ
    // ==================================================================

    /// <summary>
    /// Paylaşılan bağlantıyı açan misafirin gördüğü tur.
    ///
    /// ---- BU UÇ NEDEN [AllowAnonymous]? ----
    /// Kullanıcı geri bildirimi: "kayıt yapmadan giriş yapmadan sadece
    /// verilen kod kullanılarak misafir olarak görünsün". Önceki akışta
    /// bağlantı giriş ekranına düşüyordu; turu görmek için hesap açmak
    /// gerekiyordu ve bu, paylaşımın anlamını ortadan kaldırıyordu.
    ///
    /// ---- NEYE İZİN VERİYOR, NEYE VERMİYOR? ----
    /// YALNIZCA OKUMA. Katılım satırı yazmıyor, oturumu ilerletmiyor,
    /// haritanın geri kalanına kapı açmıyor. Cevap da eleyerek kuruldu:
    /// katılımcı adları, oturum/kullanıcı id'leri ve katılım kodu dışarı
    /// çıkmıyor (gerekçesi MisafirTurDto başlığında).
    ///
    /// Kaba kuvvete karşı IP başına dakikada 60 istek sınırı var
    /// (Program.cs → MisafirPolitikasi).
    /// </summary>
    /// <response code="200">Tur bulundu.</response>
    /// <response code="404">Kod geçersiz ya da tur sona ermiş.</response>
    [HttpGet("misafir/{kod}")]
    [AllowAnonymous]
    [EnableRateLimiting("misafir")]
    public Task<ActionResult<MisafirTurDto>> Misafir(string kod)
        => Calistir<MisafirTurDto>(async () =>
        {
            var tur = await _oturumlar.MisafirGorunumuAsync(kod);

            // Açık yoklamanın SORUSU burada ekleniyor: yoklama bellekteki
            // ayrı bir serviste (IYoklamaServisi) ve oturum servisi ona
            // erişmiyor — iki kaynağı birleştirmek controller'ın işi.
            if (tur is not null)
            {
                tur.AcikYoklamaSorusu = _yoklama.Durum(tur.OturumId)?.Soru;
            }

            // GEÇERSİZ KOD ile SONA ERMİŞ TUR aynı cevabı veriyor: ayırsaydık
            // uç, "bu kod var mı?" sorusuna cevap veren bir tarayıcıya
            // dönüşürdü.
            return tur is null
                ? NotFound(new { message = "Bu katılım kodu geçerli değil ya da tur sona ermiş." })
                : Ok(tur);
        });

    // ==================================================================
    //  Canlı oturum
    // ==================================================================

    /// <summary>
    /// Turdan canlı oturum açar; cevapta KATILIM KODU var (yalnızca rehbere).
    /// </summary>
    [HttpPost("oturumlar")]
    [YetkiGerekli(Yetkiler.TurYonetimi)]
    public Task<ActionResult<TourSessionDto>> OturumAc([FromBody] TourSessionCreateDto istek)
        => Calistir<TourSessionDto>(async () => Ok(await _oturumlar.OturumAcAsync(istek)));

    /// <summary>
    /// Katılım koduyla oturuma katılır.
    ///
    /// ---- NEDEN YETKİ İSTEMİYOR? ----
    /// Katılmak İZLEME işidir; ödevdeki ayrımın aynısı (simülasyonu başlatmak
    /// yetki ister, takip etmek istemez). Kodu bilen herkes katılabilir ama
    /// yine de GİRİŞ YAPMIŞ olmalı: katılım satırı bir kullanıcıya bağlanıyor,
    /// yoksa "kimler katıldı" listesi anlamsız kalırdı.
    /// </summary>
    [HttpPost("oturumlar/katil")]
    public Task<ActionResult<TourSessionDto>> OturumaKatil([FromBody] TourSessionJoinDto istek)
        => Calistir<TourSessionDto>(async () => Ok(await _oturumlar.OturumaKatilAsync(istek)));

    /// <summary>İsteği yapan kullanıcının katıldığı açık oturumlar.</summary>
    [HttpGet("oturumlar")]
    public Task<ActionResult<List<TourSessionDto>>> Oturumlarim()
        => Calistir<List<TourSessionDto>>(async () => Ok(await _oturumlar.OturumlarimAsync()));

    /// <summary>Oturumun anlık durumu; yoksa 404.</summary>
    [HttpGet("oturumlar/{id:int}")]
    public Task<ActionResult<TourSessionDto>> Oturum(int id)
        => Calistir<TourSessionDto>(async () =>
        {
            var oturum = await _oturumlar.OturumGetirAsync(id);
            return oturum is null ? Bulunamadi(id) : Ok(oturum);
        });

    /// <summary>
    /// Oturumu ilerletir: durum ve/veya bulunulan durak.
    ///
    /// ---- NEDEN [YetkiGerekli] YOK? ----
    /// Gereken izin bir YETKİ değil, BU OTURUMUN REHBERİ OLMAK. "Tur Yönetimi"
    /// yetkisine bağlasaydık, tur açabilen herkes başkasının grubunu
    /// yönlendirebilirdi. Kural serviste (TurOturumServisi.OturumGuncelleAsync)
    /// ve testi var — çöp kutusundaki "yetki kayıt türüne göre değişiyor"
    /// istisnasının aynı gerekçesi.
    /// </summary>
    [HttpPut("oturumlar/{id:int}")]
    public Task<ActionResult<TourSessionDto>> OturumGuncelle(
        int id,
        [FromBody] TourSessionUpdateDto istek)
        => Calistir<TourSessionDto>(async () => Ok(await _oturumlar.OturumGuncelleAsync(id, istek)));

    /// <summary>Oturumdan ayrılır (katılım kaydı silinmez, damgalanır).</summary>
    [HttpDelete("oturumlar/{id:int}/katilim")]
    public Task<IActionResult> OturumdanAyril(int id)
        => Calistir(async () =>
            await _oturumlar.OturumdanAyrilAsync(id) ? NoContent() : BulunamadiSonuc(id));
}
