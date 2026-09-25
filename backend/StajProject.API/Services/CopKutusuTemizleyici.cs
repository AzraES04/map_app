using StajProject.Business.Services;

namespace StajProject.API.Services;

/// <summary>
/// ÇÖP KUTUSU OTOMATİK TEMİZLİĞİ — saklama süresi dolan kayıtları siler.
///
/// ---- NEDEN GEREKLİ? ----
/// Soft delete hiçbir şeyi gerçekten silmiyor: silinen her nokta, POI,
/// durak ve kullanıcı tabloda kalmaya devam ediyor. Bu, geri alınabilirlik
/// için doğru bir tercih ama SÜRESİZ olduğunda veritabanı hiç küçülmeyen
/// bir çöplüğe dönüşüyor — üstelik silinmiş kişisel veriler (kullanıcı
/// kayıtları) de sonsuza kadar duruyor.
///
/// Kural: <see cref="CopKutusuService.SaklamaSuresi"/> (30 gün). Süre
/// dolduğunda kayıt kalıcı siliniyor; kullanıcı çöp kutusunda kaç gün
/// kaldığını zaten görüyor.
///
/// ---- NEDEN ARKA PLAN GÖREVİ, NEDEN CRON DEĞİL? ----
/// Dışarıdan bir zamanlayıcı (Windows Görev Zamanlayıcı, cron) kurmak,
/// uygulamayı çalıştıran herkesin ayrıca o kurulumu da yapmasını
/// gerektirirdi — yapmayan kurulumda özellik sessizce hiç çalışmazdı.
/// Uygulamanın kendi içindeki görev, uygulama ayaktaysa temizliğin de
/// çalıştığını garanti ediyor.
///
/// ---- HATA DURUMU ----
/// Bir tur başarısız olursa görev DURMUYOR, bir sonraki turda yeniden
/// deniyor: temizlik, uygulamanın çalışmasını engelleyecek kadar kritik
/// bir iş değil.
/// </summary>
public class CopKutusuTemizleyici : BackgroundService
{
    /// <summary>
    /// Açılıştan sonraki ilk bekleme.
    ///
    /// Sunucu önce kendi işini bitirsin (göç, seed, ilk sağlık kontrolü);
    /// temizlik aceleye getirilecek bir iş değil.
    /// </summary>
    private static readonly TimeSpan AcilisGecikmesi = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Turlar arası bekleme.
    ///
    /// Günde bir: saklama süresi 30 GÜN olduğu için daha sık çalışmanın
    /// hiçbir faydası yok — en kötü ihtimalle bir kayıt 30 gün yerine
    /// 31 gün duruyor. Daha seyrek çalışmak ise uzun süre açık kalan bir
    /// sunucuda temizliği geciktirirdi.
    /// </summary>
    private static readonly TimeSpan TurAraligi = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _kapsamlar;
    private readonly ILogger<CopKutusuTemizleyici> _logger;

    public CopKutusuTemizleyici(
        IServiceScopeFactory kapsamlar,
        ILogger<CopKutusuTemizleyici> logger)
    {
        _kapsamlar = kapsamlar;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken iptal)
    {
        try
        {
            await Task.Delay(AcilisGecikmesi, iptal);
        }
        catch (OperationCanceledException)
        {
            return;   // uygulama daha açılırken kapandı
        }

        while (!iptal.IsCancellationRequested)
        {
            await BirTurCalistir(iptal);

            try
            {
                await Task.Delay(TurAraligi, iptal);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task BirTurCalistir(CancellationToken iptal)
    {
        try
        {
            // KENDİ KAPSAMI: servis (ve DbContext) scoped kayıtlı, arka plan
            // görevi ise singleton. Doğrudan enjekte etseydik uygulama
            // açılışta "singleton içinde scoped" hatasıyla patlardı.
            using var kapsam = _kapsamlar.CreateScope();
            var servis = kapsam.ServiceProvider.GetRequiredService<ICopKutusuService>();

            var (silinen, atlanan) = await servis.SuresiDolanlariTemizleAsync();

            // Hiçbir şey silinmediyse log YAZILMIYOR: günde bir kez
            // "0 kayıt silindi" satırı, günlükleri hiçbir bilgi taşımayan
            // gürültüyle doldururdu.
            if (silinen > 0 || atlanan > 0)
            {
                _logger.LogInformation(
                    "Çöp kutusu temizliği: {Silinen} kayıt kalıcı silindi, " +
                    "{Atlanan} kayıt bağlı kayıtları olduğu için atlandı " +
                    "(saklama süresi {Gun} gün).",
                    silinen, atlanan, CopKutusuService.SaklamaSuresi.TotalDays);
            }
        }
        catch (Exception ex) when (!iptal.IsCancellationRequested)
        {
            // Temizlik uygulamayı çökertemez: hata yazılıp bir sonraki
            // tura bırakılıyor.
            _logger.LogWarning(ex, "Çöp kutusu temizliği bu turda çalışamadı.");
        }
    }
}
