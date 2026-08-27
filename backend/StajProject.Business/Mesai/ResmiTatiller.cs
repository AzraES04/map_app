namespace StajProject.Business.Mesai;

// ============================================================================
//  Ödev 13 / Madde 3: "resmî kurum modu seçilince ortak tatillerde kapalı".
//
//  "Ortak tatil" listesi bir yerde tanımlı olmak zorunda. Arayüzde tutmak
//  kolay olurdu ama yanlış olurdu: mesainin "şu an açık mı?" cevabını üreten
//  kural iş kuralıdır, tarayıcıda durursa her istemci kendi listesini taşır
//  ve biri güncellenmeyi unutur. Burada tek kaynak var; arayüz listeyi
//  /api/poi/resmi-tatiller ucundan okuyor.
//
//  ── DİKKAT: DİNÎ BAYRAMLARIN TARİHLERİ ELLE YAZILIDIR ──
//  2429 sayılı kanundaki tatillerin çoğu SABİT tarihlidir (1 Ocak, 23 Nisan…)
//  ve her yıl için hesaplanabilir. Ramazan ve Kurban bayramları ise hicrî
//  takvime bağlı olduğu için kayar; bu projede Diyanet takvimindeki tarihler
//  yıl yıl <see cref="DiniBayramlar"/> tablosuna yazılmıştır.
//
//  Hicrî tarihi kodda hesaplamak (ör. Umm al-Qura dönüşümü) mümkündü ama
//  Türkiye'deki resmî tatil, hesaplanan tarih değil DİYANET'İN İLAN ETTİĞİ
//  tarihtir; ikisi bazı yıllarda bir gün ayrışır. Yanlış olabilecek bir hesap
//  yerine doğruluğu denetlenebilir bir tablo tercih edildi.
//
//  Tabloda olmayan bir yıl sorulursa yalnızca sabit tatiller döner ve
//  <see cref="DiniBayramTablosuVarMi"/> false verir — arayüz "bu yılın dinî
//  bayram tarihleri tanımlı değil" uyarısını buradan basıyor. Sessizce eksik
//  liste dönmek, "bayramda açığız" gibi yanlış bir bilgi üretirdi.
// ============================================================================

/// <summary>Tek bir resmî tatil günü.</summary>
/// <param name="Tarih">Günün kendisi.</param>
/// <param name="Ad">Görünen ad — "Cumhuriyet Bayramı".</param>
/// <param name="YarimGun">
/// Yalnızca öğleden sonra tatil olan günler (arifeler ve 28 Ekim). Tam gün
/// kapalı sayılmazlar; arayüz bunları ayrı işaretliyor.
/// </param>
public sealed record ResmiTatil(DateOnly Tarih, string Ad, bool YarimGun);

public static class ResmiTatiller
{
    /// <summary>
    /// Her yıl aynı tarihe düşen tatiller (2429 sayılı kanun).
    /// (ay, gün, ad, yarım gün mü)
    /// </summary>
    private static readonly (int Ay, int Gun, string Ad, bool YarimGun)[] SabitTatiller =
    {
        (1,  1,  "Yılbaşı", false),
        (4,  23, "Ulusal Egemenlik ve Çocuk Bayramı", false),
        (5,  1,  "Emek ve Dayanışma Günü", false),
        (5,  19, "Atatürk'ü Anma, Gençlik ve Spor Bayramı", false),
        (7,  15, "Demokrasi ve Millî Birlik Günü", false),
        (8,  30, "Zafer Bayramı", false),
        // 28 Ekim tam gün değil: kanunda "13.00'ten itibaren" yazıyor.
        (10, 28, "Cumhuriyet Bayramı arifesi (öğleden sonra)", true),
        (10, 29, "Cumhuriyet Bayramı", false),
    };

    /// <summary>
    /// Dinî bayramlar — Diyanet İşleri Başkanlığı takvimine göre.
    /// Her bayram ARİFE (yarım gün) + bayram günleriyle birlikte yazılıdır.
    ///
    /// Yeni yıl eklerken: takvimden bakıp buraya bir satır ekleyin, başka
    /// hiçbir yeri değiştirmeniz gerekmiyor.
    /// </summary>
    private static readonly Dictionary<int, (string Ad, DateOnly Arife, int GunSayisi)[]> DiniBayramlar = new()
    {
        [2026] = new[]
        {
            ("Ramazan Bayramı", new DateOnly(2026, 3, 19), 3),
            ("Kurban Bayramı",  new DateOnly(2026, 5, 26), 4),
        },
        [2027] = new[]
        {
            ("Ramazan Bayramı", new DateOnly(2027, 3, 8),  3),
            ("Kurban Bayramı",  new DateOnly(2027, 5, 15), 4),
        },
        [2028] = new[]
        {
            ("Ramazan Bayramı", new DateOnly(2028, 2, 25), 3),
            ("Kurban Bayramı",  new DateOnly(2028, 5, 4),  4),
        },
    };

    /// <summary>Bu yıl için dinî bayram tarihleri tabloda var mı?</summary>
    public static bool DiniBayramTablosuVarMi(int yil) => DiniBayramlar.ContainsKey(yil);

    /// <summary>
    /// Bir yılın bütün resmî tatilleri, tarihe göre sıralı.
    ///
    /// Aynı güne iki tatil düşebilir (ör. arife bir sabit tatile denk gelirse);
    /// o durumda TAM GÜN olan kazanıyor — yarım gün etiketi kapalı bir günü
    /// "yarım açık" göstererek yanlış bilgi verirdi.
    /// </summary>
    public static IReadOnlyList<ResmiTatil> Yil(int yil)
    {
        var gunler = new Dictionary<DateOnly, ResmiTatil>();

        void Ekle(ResmiTatil tatil)
        {
            if (gunler.TryGetValue(tatil.Tarih, out var mevcut) && !mevcut.YarimGun)
            {
                return;   // tam gün tatil zaten var, yarım günle değiştirme
            }

            gunler[tatil.Tarih] = tatil;
        }

        foreach (var (ay, gun, ad, yarim) in SabitTatiller)
        {
            Ekle(new ResmiTatil(new DateOnly(yil, ay, gun), ad, yarim));
        }

        if (DiniBayramlar.TryGetValue(yil, out var bayramlar))
        {
            foreach (var (ad, arife, gunSayisi) in bayramlar)
            {
                Ekle(new ResmiTatil(arife, $"{ad} arifesi (öğleden sonra)", YarimGun: true));

                for (var i = 1; i <= gunSayisi; i++)
                {
                    Ekle(new ResmiTatil(arife.AddDays(i), $"{ad} {i}. gün", YarimGun: false));
                }
            }
        }

        return gunler.Values.OrderBy(t => t.Tarih).ToList();
    }

    /// <summary>Verilen gün resmî tatil mi? Değilse null.</summary>
    public static ResmiTatil? Bul(DateOnly tarih)
        => Yil(tarih.Year).FirstOrDefault(t => t.Tarih == tarih);
}
