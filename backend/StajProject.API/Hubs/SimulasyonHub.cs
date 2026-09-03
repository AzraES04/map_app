using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StajProject.Business.Services;

namespace StajProject.API.Hubs;

/// <summary>
/// Canlı araç konumlarının yayın kanalı (Ödev 19 / Madde 2).
///
/// NEDEN SignalR, DÜZ WEBSOCKET DEĞİL?
/// Ödev açıkça SignalR istiyor; ama isteği bir kenara bıraksak bile üç şeyi
/// hazır getiriyor: bağlantı kopunca yeniden bağlanma, WebSocket
/// desteklenmeyen ortamlarda otomatik olarak SSE/long-polling'e düşme ve
/// GRUP kavramı. Grup olmasaydı her konum mesajı bağlı olan HERKESE
/// giderdi; oysa bir kullanıcı yalnızca takip ettiği hattı görmek istiyor.
///
/// GRUP ADI = "guzergah-{id}". Yayıncı da aynı adı kuruyor
/// (<see cref="Services.SimulasyonYayinci"/>); ad üretimi tek bir yerde
/// (<see cref="GrupAdi"/>) durduğu için ikisi ayrı düşemiyor.
///
/// KİMLİK DOĞRULAMA ZORUNLU. Tarayıcı WebSocket el sıkışmasında
/// Authorization başlığı gönderemediği için token adres satırında
/// (access_token) geliyor; Program.cs'teki OnMessageReceived onu okuyor.
/// Bu, SignalR'ın belgelenmiş standart yolu — ve yalnızca /hubs yolları
/// için açılıyor, REST uçları eskisi gibi başlıkla çalışmaya devam ediyor.
/// </summary>
[Authorize]
public class SimulasyonHub : Hub
{
    private readonly ISimulasyonServisi _simulasyon;

    public SimulasyonHub(ISimulasyonServisi simulasyon)
    {
        _simulasyon = simulasyon;
    }

    /// <summary>Bir güzergahın yayın grubunun adı.</summary>
    public static string GrupAdi(int guzergahId) => $"guzergah-{guzergahId}";

    /// <summary>
    /// "Takip Et" — istemci bu güzergahın grubuna katılır.
    ///
    /// Katılır katılmaz o anki durum TEK BİR istemciye gönderiliyor
    /// (<c>Clients.Caller</c>): yayın 500 ms'de bir olduğu için, bu olmasaydı
    /// düğmeye basan kullanıcı yarım saniye boş harita görürdü. Küçük ama
    /// "çalışmıyor mu?" dedirten bir boşluk.
    /// </summary>
    public async Task Katil(int guzergahId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GrupAdi(guzergahId));

        var durum = _simulasyon.Durum(guzergahId);
        if (durum is not null)
        {
            await Clients.Caller.SendAsync("KonumGuncellendi", durum);
        }
    }

    /// <summary>
    /// "Takibi Bırak" — gruptan çıkar.
    ///
    /// Bağlantı kapanırsa gruptan çıkmaya gerek yok: SignalR, kopan
    /// bağlantıyı bütün gruplardan kendisi düşürüyor.
    /// </summary>
    public Task Ayril(int guzergahId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GrupAdi(guzergahId));
}
