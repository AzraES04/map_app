using StajProject.Business.Services;
using StajProject.Business.Validation;
using Xunit;

namespace StajProject.Tests;

// ============================================================================
//  YOKLAMA — "şu an kimler burada?"
//
//  Rehber sahada soruyor, bağlantıyı açan herkes tek dokunuşla cevaplıyor,
//  rehber sayıyı görüyor: "15 kişinin 13'ü buradayım".
//
//  Testlerin koruduğu üç kural, üçü de sessizce yanlış sayı üretecek cinsten:
//    1. Aynı misafir iki kez cevaplarsa sayı bir kez artar.
//    2. Yeniden başlatma eski cevapları siler.
//    3. Kapalı yoklamaya cevap kabul edilmez.
// ============================================================================

public class YoklamaTests
{
    private const int OturumId = 7;

    private static YoklamaServisi Kur() => new();

    [Fact]
    public void Baslatinca_sayilar_sifir_ve_grup_boyu_yaziliyor()
    {
        var servis = Kur();

        var durum = servis.Baslat(OturumId, "Otobüse döndünüz mü?", 15);

        Assert.Equal("Otobüse döndünüz mü?", durum.Soru);
        Assert.Equal(15, durum.GrupBoyu);
        Assert.Equal(0, durum.Buradayim);

        // Kimse cevaplamadıysa herkes cevapsız.
        Assert.Equal(15, durum.Cevapsiz);
    }

    [Fact]
    public void Cevaplar_sayiliyor()
    {
        var servis = Kur();
        servis.Baslat(OturumId, "Buradayız?", 15);

        servis.Cevapla(OturumId, "misafir-0001", "Buradayim", null, null);
        servis.Cevapla(OturumId, "misafir-0002", "Buradayim", null, null);
        servis.Cevapla(OturumId, "misafir-0003", "Degilim", null, null);
        var durum = servis.Cevapla(OturumId, "misafir-0004", "Acil", null, null);

        Assert.Equal(2, durum!.Buradayim);
        Assert.Equal(1, durum.Degilim);
        Assert.Equal(1, durum.Acil);
        Assert.Equal(11, durum.Cevapsiz);
    }

    [Fact]
    public void Ayni_misafir_iki_kez_sayilmiyor()
    {
        var servis = Kur();
        servis.Baslat(OturumId, "Buradayız?", 10);

        servis.Cevapla(OturumId, "misafir-0001", "Buradayim", null, null);
        var durum = servis.Cevapla(OturumId, "misafir-0001", "Degilim", null, null);

        // Fikrini değiştiren misafir sayıyı iki kez artırmamalı.
        Assert.Equal(0, durum!.Buradayim);
        Assert.Equal(1, durum.Degilim);
        Assert.Equal(9, durum.Cevapsiz);
    }

    [Fact]
    public void Yeniden_baslatinca_eski_cevaplar_siliniyor()
    {
        var servis = Kur();
        servis.Baslat(OturumId, "İlk soru", 10);
        servis.Cevapla(OturumId, "misafir-0001", "Buradayim", null, null);

        var durum = servis.Baslat(OturumId, "İkinci soru", 10);

        // "On dakika önce buradaydım" yeni soruya verilmiş bir cevap değil;
        // taşınsaydı rehber onu güncel sanırdı.
        Assert.Equal(0, durum.Buradayim);
        Assert.Equal("İkinci soru", durum.Soru);
    }

    [Fact]
    public void Kapali_yoklamaya_cevap_kabul_edilmiyor()
    {
        var servis = Kur();
        servis.Baslat(OturumId, "Buradayız?", 10);
        servis.Bitir(OturumId);

        // Rehberin ekranında olmayan bir sayıyı büyütmek olurdu.
        Assert.Null(servis.Cevapla(OturumId, "misafir-0001", "Buradayim", null, null));
        Assert.Null(servis.Durum(OturumId));
    }

    [Fact]
    public void Gecersiz_cevap_reddediliyor()
    {
        var servis = Kur();
        servis.Baslat(OturumId, "Buradayız?", 10);

        // Cevap kümesi KAPALI: serbest metin alsaydık uç, kimliksiz bir
        // mesaj kutusuna dönerdi ve sayılamazdı.
        Assert.Throws<IsKuraliException>(
            () => servis.Cevapla(OturumId, "misafir-0001", "belki", null, null));
    }

    [Fact]
    public void Gecersiz_misafir_anahtari_reddediliyor()
    {
        var servis = Kur();
        servis.Baslat(OturumId, "Buradayız?", 10);

        Assert.Throws<IsKuraliException>(() => servis.Cevapla(OturumId, "kisa", "Buradayim", null, null));
        Assert.Throws<IsKuraliException>(() => servis.Cevapla(OturumId, "", "Buradayim", null, null));
    }

    [Fact]
    public void Grup_boyundan_fazla_cevapta_cevapsiz_negatife_dusmuyor()
    {
        var servis = Kur();
        servis.Baslat(OturumId, "Buradayız?", 2);

        servis.Cevapla(OturumId, "misafir-0001", "Buradayim", null, null);
        servis.Cevapla(OturumId, "misafir-0002", "Buradayim", null, null);
        var durum = servis.Cevapla(OturumId, "misafir-0003", "Buradayim", null, null);

        // Rehber sayıyı olduğundan küçük girmiş olabilir; ekranda
        // "-1 kişi cevapsız" gibi bir sayı çıkmamalı.
        Assert.Equal(0, durum!.Cevapsiz);
        Assert.Equal(3, durum.Buradayim);
    }

    [Fact]
    public void Oturumlar_birbirinden_bagimsiz()
    {
        var servis = Kur();
        servis.Baslat(1, "A grubu", 5);
        servis.Baslat(2, "B grubu", 5);

        servis.Cevapla(1, "misafir-0001", "Buradayim", null, null);

        // İki rehber aynı anda yoklama yapabilmeli; sayılar karışmamalı.
        Assert.Equal(1, servis.Durum(1)!.Buradayim);
        Assert.Equal(0, servis.Durum(2)!.Buradayim);
    }

    [Fact]
    public void Isim_ve_telefon_ISTEGE_BAGLI_yaziliyor()
    {
        var servis = Kur();
        servis.Baslat(OturumId, "Buradayız?", 10);

        var durum = servis.Cevapla(OturumId, "misafir-0001", "Acil", "Zeynep", "0532 000 00 00");

        var kayit = Assert.Single(durum!.Kisiler);
        Assert.Equal("Zeynep", kayit.Ad);
        Assert.Equal("Acil", kayit.Cevap);
        Assert.Equal("0532 000 00 00", kayit.Telefon);
    }

    [Fact]
    public void Isim_ve_telefon_BOS_BIRAKILABILIR()
    {
        var servis = Kur();
        servis.Baslat(OturumId, "Buradayız?", 10);

        var durum = servis.Cevapla(OturumId, "misafir-0001", "Buradayim", null, null);

        var kayit = Assert.Single(durum!.Kisiler);
        Assert.Null(kayit.Ad);
        Assert.Null(kayit.Telefon);
    }

    [Fact]
    public void ACIL_cevaplar_LISTEDE_EN_USTTE()
    {
        var servis = Kur();
        servis.Baslat(OturumId, "Buradayız?", 10);

        servis.Cevapla(OturumId, "misafir-0001", "Buradayim", "Ali", null);
        servis.Cevapla(OturumId, "misafir-0002", "Degilim", "Veli", null);
        var durum = servis.Cevapla(OturumId, "misafir-0003", "Acil", "Zeynep", "0532 000 00 00");

        // Rehberin ilk bakması gereken satır — sırayla taramak zorunda
        // kalmamalı.
        Assert.Equal("Zeynep", durum!.Kisiler[0].Ad);
        Assert.Equal("Acil", durum.Kisiler[0].Cevap);
    }

    [Fact]
    public void Fikir_degistirince_ESKI_telefon_KALMIYOR()
    {
        var servis = Kur();
        servis.Baslat(OturumId, "Buradayız?", 10);

        servis.Cevapla(OturumId, "misafir-0001", "Acil", "Zeynep", "0532 000 00 00");
        var durum = servis.Cevapla(OturumId, "misafir-0001", "Buradayim", "Zeynep", null);

        // "Acil"den "Buradayım"a geçen kişinin eski telefon kaydı rehberin
        // listesinde asılı kalmamalı.
        var kayit = Assert.Single(durum!.Kisiler);
        Assert.Equal("Buradayim", kayit.Cevap);
        Assert.Null(kayit.Telefon);
    }

    [Fact]
    public void Cok_uzun_isim_reddediliyor()
    {
        var servis = Kur();
        servis.Baslat(OturumId, "Buradayız?", 10);

        Assert.Throws<IsKuraliException>(
            () => servis.Cevapla(OturumId, "misafir-0001", "Buradayim", new string('a', 100), null));
    }

    [Fact]
    public void Cok_uzun_soru_reddediliyor()
    {
        var servis = Kur();

        // Soru misafirin telefonunda başlık olarak çıkıyor; uzun metin
        // cevap düğmelerini ekranın dışına iter.
        Assert.Throws<IsKuraliException>(
            () => servis.Baslat(OturumId, new string('x', 200), 10));
    }

    [Fact]
    public void Bos_soru_varsayilana_dusuyor()
    {
        var servis = Kur();

        var durum = servis.Baslat(OturumId, "   ", 10);

        Assert.False(string.IsNullOrWhiteSpace(durum.Soru));
    }
}
