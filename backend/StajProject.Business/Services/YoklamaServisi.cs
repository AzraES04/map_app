using System.Collections.Concurrent;
using StajProject.Business.DTOs;
using StajProject.Business.Validation;

namespace StajProject.Business.Services;

/// <summary>
/// Yoklama defteri — bellekte, oturum başına bir kayıt.
/// Gerekçelerin tamamı <see cref="IYoklamaServisi"/> başlığında.
/// </summary>
public class YoklamaServisi : IYoklamaServisi
{
    /// <summary>Kabul edilen cevaplar. Serbest metin YOK — sayılabilir olmalı.</summary>
    private static readonly string[] Cevaplar = { "Buradayim", "Degilim", "Acil" };

    /// <summary>Bir kişinin cevabı: durum, isteğe bağlı ad ve telefon.</summary>
    /// <param name="Cevap">"Buradayim" | "Degilim" | "Acil".</param>
    /// <param name="Ad">
    /// İsteğe bağlı — misafir yazmak istemeyebilir. Rehberin listesinde
    /// "kimin cevapladığı" bunu gösteriyor.
    /// </param>
    /// <param name="Telefon">
    /// Yalnızca ACİL durumda anlamlı: rehberin geri arayabilmesi için.
    /// Diğer cevaplarda da alınabilir ama arayüz yalnızca acilde soruyor.
    /// </param>
    private sealed record Cevap(string Durum, string? Ad, string? Telefon, DateTime ZamanUtc);

    /// <summary>
    /// Bir yoklamanın bellekteki hâli.
    ///
    /// ConcurrentDictionary: cevaplar aynı anda birden çok istekten geliyor
    /// (bir grup aynı anda düğmeye basıyor) ve düz Dictionary o sırada
    /// bozulabiliyordu.
    /// </summary>
    private sealed record Yoklama(
        string Soru,
        int GrupBoyu,
        DateTime BaslangicUtc,
        ConcurrentDictionary<string, Cevap> Cevaplar);

    private readonly ConcurrentDictionary<int, Yoklama> _acik = new();

    public YoklamaDurumuDto Baslat(int oturumId, string soru, int grupBoyu)
    {
        var temizSoru = (soru ?? string.Empty).Trim();

        if (temizSoru.Length == 0)
        {
            temizSoru = "Şu an grupta mısınız?";
        }

        if (temizSoru.Length > 120)
        {
            // Soru misafirin telefonunda bir başlık olarak çıkıyor; uzun metin
            // ekranı kaydırır ve cevap düğmelerini aşağı iter.
            throw new IsKuraliException("Soru en fazla 120 karakter olabilir.");
        }

        if (grupBoyu is < 1 or > 500)
        {
            throw new IsKuraliException("Grup sayısı 1 ile 500 arasında olmalıdır.");
        }

        // Yeniden başlatmak ESKİ CEVAPLARI SİLİYOR (gerekçe arayüzde).
        var yoklama = new Yoklama(
            temizSoru, grupBoyu, DateTime.UtcNow,
            new ConcurrentDictionary<string, Cevap>(StringComparer.Ordinal));

        _acik[oturumId] = yoklama;

        return Cevir(oturumId, yoklama, null);
    }

    public bool Bitir(int oturumId) => _acik.TryRemove(oturumId, out _);

    public YoklamaDurumuDto? Durum(int oturumId)
        => _acik.TryGetValue(oturumId, out var yoklama) ? Cevir(oturumId, yoklama, null) : null;

    public YoklamaDurumuDto? Cevapla(
        int oturumId, string misafirAnahtari, string cevap, string? ad, string? telefon)
    {
        if (!_acik.TryGetValue(oturumId, out var yoklama))
        {
            return null;
        }

        var anahtar = (misafirAnahtari ?? string.Empty).Trim();

        if (anahtar.Length is < 8 or > 64)
        {
            throw new IsKuraliException("Geçersiz misafir anahtarı.");
        }

        var secilen = Cevaplar.FirstOrDefault(
            c => string.Equals(c, cevap, StringComparison.OrdinalIgnoreCase))
            ?? throw new IsKuraliException("Geçersiz cevap.");

        // Ad/telefon KIRPILIYOR ve BOŞSA null'a düşüyor: misafir alanı boş
        // bırakıp gönderebiliyor (ikisi de isteğe bağlı), boş dizeyi "girildi
        // ama boş" diye taşımanın bir anlamı yok.
        var temizAd = string.IsNullOrWhiteSpace(ad) ? null : ad.Trim();
        var temizTelefon = string.IsNullOrWhiteSpace(telefon) ? null : telefon.Trim();

        if (temizAd is { Length: > 60 })
        {
            throw new IsKuraliException("Ad en fazla 60 karakter olabilir.");
        }

        if (temizTelefon is { Length: > 30 })
        {
            throw new IsKuraliException("Telefon numarası en fazla 30 karakter olabilir.");
        }

        // Aynı anahtar yeniden yazarsa GÜNCELLENİYOR: fikrini değiştiren
        // misafir sayıyı iki kez artırmamalı. AD/TELEFON de KORUNMAZ,
        // YENİDEN YAZILIR: kişi "Acil"den "Buradayım"a geçtiğinde eski
        // acil kaydının telefonu rehberin listesinde asılı kalmamalı.
        yoklama.Cevaplar[anahtar] = new Cevap(secilen, temizAd, temizTelefon, DateTime.UtcNow);

        return Cevir(oturumId, yoklama, secilen);
    }

    private static YoklamaDurumuDto Cevir(int oturumId, Yoklama yoklama, string? benimCevabim)
    {
        var cevaplar = yoklama.Cevaplar.Values.ToList();

        return new YoklamaDurumuDto
        {
            OturumId = oturumId,
            Soru = yoklama.Soru,
            GrupBoyu = yoklama.GrupBoyu,
            BaslangicUtc = yoklama.BaslangicUtc,

            Buradayim = cevaplar.Count(c => c.Durum == "Buradayim"),
            Degilim = cevaplar.Count(c => c.Durum == "Degilim"),
            Acil = cevaplar.Count(c => c.Durum == "Acil"),

            // CEVAPSIZ = grup boyu - cevap veren. Negatife düşebiliyor
            // (rehber sayıyı olduğundan küçük girdiyse); sıfırda kesiyoruz
            // ki ekranda "-2 kişi cevapsız" gibi bir sayı çıkmasın.
            Cevapsiz = Math.Max(0, yoklama.GrupBoyu - cevaplar.Count),

            BenimCevabim = benimCevabim,

            // KİŞİ LİSTESİ — yalnızca REHBERİN görmesi gereken ayrıntı.
            // Misafir kendi cevabını BenimCevabim'den zaten biliyor; ona
            // başkalarının adını/telefonunu göstermek gizlilik ihlali
            // olurdu. Controller bu alanı yalnızca rehbere döndürüyor
            // (bkz. TurController.YoklamaDurumu / YoklamaBaslat).
            Kisiler = cevaplar
                .OrderByDescending(c => c.Durum == "Acil")   // acil en üstte
                .ThenByDescending(c => c.ZamanUtc)
                .Select(c => new YoklamaKisiDto
                {
                    Ad = c.Ad,
                    Cevap = c.Durum,
                    Telefon = c.Telefon,
                })
                .ToList(),
        };
    }
}
