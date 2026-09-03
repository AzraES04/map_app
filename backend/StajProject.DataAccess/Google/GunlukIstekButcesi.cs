using Microsoft.Extensions.Logging;

namespace StajProject.DataAccess.Google;

/// <summary>
/// <see cref="IIstekButcesi"/>'nin bellek içi gerçeklemesi.
///
/// Gün dönümünde kendiliğinden sıfırlanıyor (UTC). Sayaç bellekte duruyor:
/// uygulama yeniden başladığında sıfırlanır ve bu KABUL EDİLEBİLİR — burada
/// amaç muhasebe değil, kaçak bir döngünün faturayı büyütmesini engellemek.
/// Kalıcı olması gerekseydi veritabanına yazmak gerekirdi ve her dış istek
/// bir de veritabanı yazması demek olurdu.
/// </summary>
public class GunlukIstekButcesi : IIstekButcesi
{
    private readonly GoogleMapsSettings _ayarlar;
    private readonly ILogger<GunlukIstekButcesi> _logger;

    /// <summary>Sayacın ve gün damgasının kilidi — birlikte değişmeleri gerekiyor.</summary>
    private readonly object _kilit = new();

    private DateOnly _gun = DateOnly.FromDateTime(DateTime.UtcNow);
    private int _kullanilan;

    /// <summary>
    /// Tavan aşıldığında günde bir kez uyarı basılsın diye: her reddedilen
    /// istekte log yazmak, kaçak bir döngüde log dosyasını da şişirirdi.
    /// </summary>
    private bool _uyariYazildi;

    public GunlukIstekButcesi(GoogleMapsSettings ayarlar, ILogger<GunlukIstekButcesi> logger)
    {
        _ayarlar = ayarlar;
        _logger = logger;
    }

    public int GunlukTavan => _ayarlar.GunlukIstekButcesi;

    public int BugunKullanilan
    {
        get { lock (_kilit) { GunuTazele(); return _kullanilan; } }
    }

    public bool IzinIste()
    {
        lock (_kilit)
        {
            GunuTazele();

            if (_kullanilan >= _ayarlar.GunlukIstekButcesi)
            {
                if (!_uyariYazildi)
                {
                    _logger.LogError(
                        "Google günlük istek bütçesi doldu ({Tavan}). Yeni istek atılmıyor.",
                        _ayarlar.GunlukIstekButcesi);
                    _uyariYazildi = true;
                }

                return false;
            }

            _kullanilan++;
            return true;
        }
    }

    /// <summary>Gün değiştiyse sayacı sıfırlar. Kilit ALTINDA çağrılmalı.</summary>
    private void GunuTazele()
    {
        var bugun = DateOnly.FromDateTime(DateTime.UtcNow);
        if (bugun == _gun) return;

        _gun = bugun;
        _kullanilan = 0;
        _uyariYazildi = false;
    }
}
