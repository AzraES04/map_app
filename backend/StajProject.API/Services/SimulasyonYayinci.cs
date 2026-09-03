using Microsoft.AspNetCore.SignalR;
using StajProject.API.Hubs;
using StajProject.Business.DTOs;
using StajProject.Business.Services;
using StajProject.Business.Ulasim;

namespace StajProject.API.Services;

/// <summary>
/// Simülasyonların KALBİ (Ödev 19 / Madde 2): sabit aralıklarla tik atar,
/// araçları ilerletir ve yeni konumları SignalR gruplarına yayınlar.
///
/// NEDEN API KATMANINDA?
/// Yayın yapmak bir SUNUM işi: SignalR bir taşıma teknolojisi. İş katmanı
/// (Business) yalnızca "araç şu an nerede?" sorusunu cevaplıyor
/// (<see cref="ISimulasyonServisi"/>), o cevabın kime nasıl gideceğini
/// bilmiyor. Aynı ayrım Ödev 5'te ICurrentUserService için yapılmıştı:
/// arayüz Business'ta, HttpContext'e bağlı gerçekleme API'de.
///
/// NEDEN <see cref="BackgroundService"/>?
/// Uygulama ayakta olduğu sürece yaşayan tek bir döngü gerekiyor. Zamanlayıcıyı
/// isteğin içinde kursaydık istek bitince ölürdü; ayrı bir Thread açsaydık
/// uygulama kapanırken düzgün durdurmak bize kalırdı. BackgroundService
/// ikisini de ASP.NET'in yaşam döngüsüne bağlıyor.
/// </summary>
public class SimulasyonYayinci : BackgroundService
{
    private readonly ISimulasyonServisi _simulasyon;
    private readonly IHubContext<SimulasyonHub> _hub;
    private readonly SimulasyonAyarlari _ayarlar;
    private readonly ILogger<SimulasyonYayinci> _logger;

    /// <summary>
    /// Bir önceki tikte yolda olan hatlar.
    ///
    /// NEDEN? Ödev, "diğer kullanıcılar aynı güzergaha tıkladığında Takip Et
    /// butonu çıksın" diyor. Ama bir kullanıcı sefer başlattığında ötekilerin
    /// ekranında hiçbir şey değişmiyordu: konum mesajları yalnızca o hattın
    /// GRUBUNA gidiyor ve gruba henüz kimse katılmamış oluyor.
    ///
    /// Çözüm, her tiki herkese yayınlamak DEĞİL (on hat çalışırken herkes on
    /// kat gereksiz mesaj alırdı); aktif hat KÜMESİNİ karşılaştırıp yalnızca
    /// başlangıç ve bitiş anlarında bir duyuru göndermek. Sefer başına iki
    /// mesaj, saniyede iki değil.
    ///
    /// Bu alanın tek bir iş parçacığından okunup yazıldığını biliyoruz:
    /// yalnızca ExecuteAsync döngüsü dokunuyor.
    /// </summary>
    private HashSet<int> _oncekiAktifler = new();

    public SimulasyonYayinci(
        ISimulasyonServisi simulasyon,
        IHubContext<SimulasyonHub> hub,
        SimulasyonAyarlari ayarlar,
        ILogger<SimulasyonYayinci> logger)
    {
        _simulasyon = simulasyon;
        _hub = hub;
        _ayarlar = ayarlar;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken iptal)
    {
        // PeriodicTimer, Task.Delay döngüsünden farklı olarak SÜRÜKLENMİYOR:
        // aralığı bir önceki tikin bittiği ana göre değil, sabit takvime göre
        // ölçüyor. Yine de simülasyonun ilerlemesi zamandan türetildiği için
        // (bkz. SimulasyonServisi.DurumUret) gecikmeler yalnızca akıcılığı
        // etkiler, konumu değil.
        using var zamanlayici = new PeriodicTimer(TimeSpan.FromMilliseconds(_ayarlar.TikMs));

        while (await ZamanlayiciBekleAsync(zamanlayici, iptal))
        {
            try
            {
                var durumlar = _simulasyon.Ilerlet();

                await DegisimleriDuyurAsync(durumlar, iptal);

                foreach (var durum in durumlar)
                {
                    // Mesaj YALNIZCA o hattın grubuna gidiyor. Bütün istemcilere
                    // yayınlasaydık, on hat çalışırken herkes on kat gereksiz
                    // mesaj alırdı ve "takip" kavramı anlamsızlaşırdı.
                    await _hub.Clients
                        .Group(SimulasyonHub.GrupAdi(durum.GuzergahId))
                        .SendAsync("KonumGuncellendi", durum, iptal);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Bir tikte olan hata döngüyü ÖLDÜRMEMELİ: yayıncı ölürse
                // bütün simülasyonlar sessizce donar ve uygulama "çalışıyor"
                // görünmeye devam eder. Hatayı yazıp bir sonraki tike geçiyoruz.
                _logger.LogError(ex, "Simülasyon yayını sırasında hata oluştu.");
            }
        }
    }

    /// <summary>
    /// Yeni başlayan ve biten seferleri HERKESE duyurur.
    ///
    /// İki olay, iki farklı iş yapıyor:
    ///   SimulasyonBasladi → başka kullanıcıların ekranında "Takip Et"
    ///                       düğmesi ve "canlı" rozeti belirir.
    ///   SimulasyonBitti   → aynı düğme ve rozet kalkar.
    ///
    /// Konum mesajlarından AYRI tutulmalarının sebebi maliyet: bunlar sefer
    /// başına birer kez, ötekiler saniyede iki kez gidiyor.
    /// </summary>
    private async Task DegisimleriDuyurAsync(
        IReadOnlyList<SimulasyonDurumDto> durumlar, CancellationToken iptal)
    {
        // Biten simülasyon son mesajını `Tamamlandi = true` ile gönderiyor ve
        // defterden düşüyor; "şu an yolda olanlar" onu içermiyor.
        var suanki = durumlar.Where(d => !d.Tamamlandi).Select(d => d.GuzergahId).ToHashSet();

        foreach (var durum in durumlar.Where(d => !d.Tamamlandi
                                                  && !_oncekiAktifler.Contains(d.GuzergahId)))
        {
            await _hub.Clients.All.SendAsync("SimulasyonBasladi", durum, iptal);
        }

        foreach (var biten in _oncekiAktifler.Where(id => !suanki.Contains(id)))
        {
            await _hub.Clients.All.SendAsync("SimulasyonBitti", biten, iptal);
        }

        _oncekiAktifler = suanki;
    }

    /// <summary>
    /// Bir sonraki tiki bekler. Uygulama kapanırken <see cref="PeriodicTimer"/>
    /// <see cref="OperationCanceledException"/> fırlatıyor; bu beklenen bir
    /// son, hata değil — yakalayıp döngüyü sessizce bitiriyoruz.
    /// </summary>
    private static async Task<bool> ZamanlayiciBekleAsync(
        PeriodicTimer zamanlayici, CancellationToken iptal)
    {
        try
        {
            return await zamanlayici.WaitForNextTickAsync(iptal);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
