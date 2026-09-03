using StajProject.Business.DTOs;
using StajProject.Business.Services;

namespace StajProject.API.Services;

/// <summary>
/// AÇILIŞTA ÖNBELLEĞİ ISITIR — ilk tur önerisi beklemesin.
///
/// ---- SORUN ----
/// Tur önerisi mekanları OpenStreetMap'in genel Overpass sunucusundan
/// çekiyor. Ölçüldü (Ankara, overpass-api.de): tek sorgu 6-10 saniye, yoğun
/// saatlerde daha da uzun. İkinci istek önbellekten geldiği için anında
/// dönüyor — kullanıcının gözlemi de bu: "2 ve sonra hızlanıyor".
///
/// Yani asıl sorun Overpass'in hızı DEĞİL, o beklemenin kullanıcının önüne
/// düşmesi. Overpass'i hızlandıramayız (gönüllü, ücretsiz bir altyapı ve
/// aynalar denendi: kumi 71 sn, osm.ch Türkiye verisi taşımıyor).
///
/// ---- ÇÖZÜM ----
/// Bekleme, uygulama açılırken ARKA PLANDA yapılıyor. Sunucu ayağa
/// kalktıktan birkaç saniye sonra, yapılandırılan şehirler için tema
/// aramaları bir kez çalışıp önbelleğe giriyor. Kullanıcı "Rota Oluştur"
/// dediğinde cevap hazır.
///
/// ---- NEDEN IHostedService, NEDEN GECİKMELİ? ----
/// Program.cs içinde senkron çalıştırsaydık uygulama Overpass cevabını
/// bekleyerek açılırdı — sağlık kontrolü ve giriş ekranı da gecikirdi.
/// Arka plan görevi açılışı hiç bloklamıyor; ilk isteğe kadar geçen kısa
/// gecikme de bilinçli: sunucu önce kendi işini bitirsin.
///
/// ---- HATA DURUMU ----
/// Isıtma başarısız olursa HİÇBİR ŞEY olmuyor: uygulama normal çalışmaya
/// devam eder, yalnızca ilk öneri yavaş olur. Bu yüzden bütün istisnalar
/// yutuluyor — bir önbellek ısıtması uygulamayı çökertemez.
/// </summary>
public class TurOnbellekIsiticisi : BackgroundService
{
    /// <summary>
    /// Açılıştan sonra beklenen süre.
    ///
    /// Sunucunun kendi başlangıç işleri (veritabanı göçü, seed) bitsin ve
    /// ilk sağlık kontrolü gecikmesin diye kısa bir pay.
    /// </summary>
    private static readonly TimeSpan AcilisGecikmesi = TimeSpan.FromSeconds(5);

    /// <summary>İki şehir arası bekleme — Overpass eşzamanlı isteği 429'luyor.</summary>
    private static readonly TimeSpan SehirArasi = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _kapsamlar;
    private readonly TurOnbellekAyarlari _ayarlar;
    private readonly ILogger<TurOnbellekIsiticisi> _logger;

    public TurOnbellekIsiticisi(
        IServiceScopeFactory kapsamlar,
        TurOnbellekAyarlari ayarlar,
        ILogger<TurOnbellekIsiticisi> logger)
    {
        _kapsamlar = kapsamlar;
        _ayarlar = ayarlar;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken iptal)
    {
        if (!_ayarlar.Etkin || _ayarlar.IlPlakalari.Count == 0)
        {
            return;
        }

        try
        {
            await Task.Delay(AcilisGecikmesi, iptal);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        foreach (var plaka in _ayarlar.IlPlakalari)
        {
            foreach (var tema in _ayarlar.Temalar)
            {
                if (iptal.IsCancellationRequested)
                {
                    return;
                }

                await IsitAsync(plaka, tema, iptal);

                try
                {
                    await Task.Delay(SehirArasi, iptal);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        _logger.LogInformation("Tur önbelleği ısıtıldı.");
    }

    private async Task IsitAsync(int plaka, string tema, CancellationToken iptal)
    {
        // Planlama servisi ISTEK ÖMÜRLÜ (scoped) — arka plan görevi kendi
        // kapsamını açmak zorunda. Doğrudan enjekte etseydik uygulama
        // ayaktayken kapanmış bir DbContext'i tutardık.
        using var kapsam = _kapsamlar.CreateScope();
        var planlama = kapsam.ServiceProvider.GetRequiredService<ITurPlanlamaServisi>();

        var istek = new TurRotaIstegiDto
        {
            Lokasyon = new TurLokasyonDto { IlPlaka = plaka },
            UlasimTipi = "Yaya",
            Sure = new TurSureDto { Birim = "Saat", Deger = 8, ToplamDakika = 480 },
            Tema = tema,

            // Molalar da AÇIK: onların aramaları ayrı bir sorgu ve o da
            // ısınmalı. Kapalı bıraksaydık kullanıcı yemek molası
            // işaretlediğinde yine beklerdi.
            YemekMolasi = true,
        };

        try
        {
            await planlama.RotaOnerAsync(istek, iptal);
            _logger.LogInformation("Önbellek ısıtıldı: {Plaka} / {Tema}", plaka, tema);
        }
        catch (Exception ex)
        {
            // Isıtma bir İYİLEŞTİRME; başarısızlığı uygulamayı etkilemiyor.
            _logger.LogDebug(ex, "Önbellek ısıtılamadı: {Plaka} / {Tema}", plaka, tema);
        }
    }
}

/// <summary>Isıtma ayarları (appsettings → "TurOnbellek").</summary>
public class TurOnbellekAyarlari
{
    public bool Etkin { get; set; } = true;

    /// <summary>
    /// Isıtılacak iller. Varsayılan Ankara ve İstanbul — sunumda gösterilen
    /// iki şehir. Boş liste ısıtmayı kapatıyor.
    /// </summary>
    public List<int> IlPlakalari { get; set; } = new() { 6, 34 };

    /// <summary>
    /// Isıtılacak temalar. Yalnızca "Karma": varsayılan tema o ve her tema
    /// için ayrı sorgu atmak, ısınma süresini şehir sayısıyla çarpardı.
    /// </summary>
    public List<string> Temalar { get; set; } = new() { "Karma" };
}
