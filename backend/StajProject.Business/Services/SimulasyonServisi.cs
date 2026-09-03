using System.Collections.Concurrent;
using StajProject.Business.DTOs;
using StajProject.Business.Ulasim;

namespace StajProject.Business.Services;

/// <inheritdoc cref="ISimulasyonServisi"/>
public class SimulasyonServisi : ISimulasyonServisi
{
    /// <summary>
    /// Güzergah id → çalışan simülasyon.
    ///
    /// <see cref="ConcurrentDictionary{TKey, TValue}"/> çünkü iki farklı iş
    /// parçacığı aynı anda dokunuyor: HTTP isteği (başlat/durdur) ve arka
    /// plandaki zamanlayıcı (ilerlet). Sıradan bir Dictionary burada nadiren
    /// ama gerçekten çöker — ve çöktüğünde nedeni bulunması en zor
    /// hatalardan biri olur.
    ///
    /// Anahtarın GÜZERGAH ID olması bir iş kuralı: bir hatta aynı anda tek
    /// araç. İki araç, "Takip Et" düğmesinin hangisini takip ettiğini
    /// belirsiz kılardı.
    /// </summary>
    private readonly ConcurrentDictionary<int, AktifSimulasyon> _aktifler = new();

    public SimulasyonDurumDto Baslat(SimulasyonKaynak kaynak, DateTime? simdi = null)
    {
        var baslangic = simdi ?? DateTime.UtcNow;

        var aktif = new AktifSimulasyon
        {
            Kaynak = kaynak,
            Kumulatif = SimulasyonMotoru.Kumulatif(kaynak.Yol),
            BaslangicUtc = baslangic,
        };

        // Aynı hat için ikinci kez "başlat" denirse eskisinin üstüne
        // yazılıyor, yani simülasyon baştan alınıyor.
        _aktifler[kaynak.GuzergahId] = aktif;

        return DurumUret(aktif, baslangic);
    }

    public bool Durdur(int guzergahId) => _aktifler.TryRemove(guzergahId, out _);

    public SimulasyonDurumDto? Durum(int guzergahId, DateTime? simdi = null)
        => _aktifler.TryGetValue(guzergahId, out var aktif)
            ? DurumUret(aktif, simdi ?? DateTime.UtcNow)
            : null;

    public IReadOnlyList<SimulasyonDurumDto> TumDurumlar(DateTime? simdi = null)
    {
        var an = simdi ?? DateTime.UtcNow;
        return _aktifler.Values.Select(a => DurumUret(a, an)).ToList();
    }

    public IReadOnlyList<SimulasyonDurumDto> Ilerlet(DateTime? simdi = null)
    {
        var an = simdi ?? DateTime.UtcNow;
        var sonuc = new List<SimulasyonDurumDto>();

        foreach (var (id, aktif) in _aktifler)
        {
            var durum = DurumUret(aktif, an);
            sonuc.Add(durum);

            // Biten simülasyon önce YAYINLANIYOR, sonra siliniyor: son mesajı
            // görmeyen istemci aracı ekranda asılı bırakırdı.
            if (durum.Tamamlandi)
            {
                _aktifler.TryRemove(id, out _);
            }
        }

        return sonuc;
    }

    /// <summary>
    /// Anlık durumu hesaplar. Tek doğruluk kaynağı: GEÇEN ZAMAN.
    ///
    /// Konumu "her tikte biraz ilerlet" diye biriktirmiyoruz. Biriktirseydik
    /// gecikmiş ya da atlanmış bir tik aracı yavaşlatır, sunucu yük altında
    /// kaldığında simülasyon sürüklenirdi. Zamandan türetince tik sıklığı
    /// yalnızca AKICILIĞI etkiliyor, ilerlemeyi değil.
    /// </summary>
    private static SimulasyonDurumDto DurumUret(AktifSimulasyon aktif, DateTime simdi)
    {
        var kaynak = aktif.Kaynak;

        var gecen = (simdi - aktif.BaslangicUtc).TotalSeconds;
        if (gecen < 0)
        {
            gecen = 0;
        }

        var oran = kaynak.ToplamSaniye <= 0 ? 1 : Math.Clamp(gecen / kaynak.ToplamSaniye, 0, 1);
        var konum = SimulasyonMotoru.Konum(kaynak.Yol, aktif.Kumulatif, oran);
        var (onceki, sonraki) = DuraklariBul(kaynak.Duraklar, oran);

        return new SimulasyonDurumDto
        {
            GuzergahId = kaynak.GuzergahId,
            GuzergahAdi = kaynak.GuzergahAdi,
            Renk = kaynak.Renk,
            Lon = konum.X,
            Lat = konum.Y,
            Yuzde = Math.Round(oran * 100, 1),
            GecenSaniye = Math.Round(gecen, 1),
            ToplamSaniye = kaynak.ToplamSaniye,
            AlinanMetre = Math.Round(kaynak.ToplamMetre * oran),
            ToplamMetre = Math.Round(kaynak.ToplamMetre),
            OncekiDurakSira = onceki?.Sira,
            OncekiDurakAdi = onceki?.Ad,
            SonrakiDurakSira = sonraki?.Sira,
            SonrakiDurakAdi = sonraki?.Ad,
            DurakSayisi = kaynak.Duraklar.Count,
            BaslatanKullanici = kaynak.BaslatanKullanici,
            BaslatanKullaniciId = kaynak.BaslatanKullaniciId,
            BaslangicUtc = aktif.BaslangicUtc,
            Tamamlandi = oran >= 1,
        };
    }

    /// <summary>
    /// Araç hangi iki durağın arasında?
    ///
    /// "Önceki" = oranı aracın oranını GEÇMEYEN son durak.
    /// "Sonraki" = oranı aracın oranından büyük ilk durak.
    /// Son durağa varıldığında sonraki null olur ve arayüz "vardı" yazar.
    /// </summary>
    private static (SimulasyonDurak? Onceki, SimulasyonDurak? Sonraki) DuraklariBul(
        IReadOnlyList<SimulasyonDurak> duraklar, double oran)
    {
        SimulasyonDurak? onceki = null;
        SimulasyonDurak? sonraki = null;

        foreach (var durak in duraklar)
        {
            if (durak.Oran <= oran)
            {
                onceki = durak;
            }
            else
            {
                sonraki = durak;
                break;
            }
        }

        return (onceki, sonraki);
    }

    /// <summary>Defterdeki bir satır: kaynak + önceden hesaplanmış uzunluklar.</summary>
    private sealed class AktifSimulasyon
    {
        public required SimulasyonKaynak Kaynak { get; init; }

        /// <summary>Köşe köşe kat edilen uzunluk — her tikte yeniden hesaplanmasın.</summary>
        public required double[] Kumulatif { get; init; }

        public required DateTime BaslangicUtc { get; init; }
    }
}
