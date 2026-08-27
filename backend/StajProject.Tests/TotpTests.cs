using System.Text;
using StajProject.Business.Auth;
using Xunit;

namespace StajProject.Tests;

/// <summary>
/// TOTP — iki adımlı doğrulamanın matematiği.
///
/// ---- BU TESTLERİN ÖNEMİ ----
///
/// TOTP'de "çalışıyor gibi görünmek" kolay: kod her 30 saniyede değişen altı
/// haneli bir sayı üretir ve kendi ürettiğimizi kendimiz doğrularız. Ama
/// kullanıcının telefonundaki Google Authenticator BAŞKA bir kod gösteriyorsa
/// hiçbir hata mesajı almayız — yalnızca "kod yanlış" deriz ve kimse
/// giremez.
///
/// Bu yüzden asıl ölçüt kendi tutarlılığımız değil, RFC 6238'in RESMÎ TEST
/// VEKTÖRLERİ. Aşağıdaki beklenen değerler standardın Appendix B tablosundan
/// alındı; tutuyorlarsa gerçek authenticator uygulamalarıyla da tutar.
/// </summary>
public class TotpTests
{
    /// <summary>
    /// RFC 6238 Appendix B'nin SHA-1 anahtarı: ASCII "12345678901234567890".
    /// Tablo bunu ham bayt olarak veriyor; biz Base32 saklıyoruz.
    /// </summary>
    private static readonly string RfcAnahtari =
        Totp.Base32Kodla(Encoding.ASCII.GetBytes("12345678901234567890"));

    // ==================================================================
    //  1) RFC 6238 resmî test vektörleri (Appendix B, SHA-1 satırları)
    // ==================================================================

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void RfcTestVektorleri(long unixSaniye, string beklenenKod)
    {
        var an = DateTimeOffset.FromUnixTimeSeconds(unixSaniye);

        Assert.Equal(beklenenKod, Totp.KodUret(RfcAnahtari, an));
    }

    [Fact]
    public void RfcVektorleri_DogrulamadanDaGeciyor()
    {
        // KodUret ile Dogrula aynı yoldan gitmeli. Ayrışsalardı üretim
        // doğru, doğrulama yanlış olabilirdi — ve bunu ancak kullanıcı
        // giremediğinde fark ederdik.
        var an = DateTimeOffset.FromUnixTimeSeconds(1111111111L);

        Assert.True(Totp.Dogrula(RfcAnahtari, "050471", an));
    }

    // ==================================================================
    //  2) Base32
    // ==================================================================

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32_RfcOrnekleri(string girdi, string beklenen)
    {
        // RFC 4648 Section 10. Dolgu ('=') YAZILMIYOR: authenticator
        // uygulamaları dolgusuz bekliyor ve dolgu koysaydık bazıları
        // anahtarı hiç kabul etmezdi.
        Assert.Equal(beklenen, Totp.Base32Kodla(Encoding.ASCII.GetBytes(girdi)));
    }

    [Fact]
    public void Base32_GidipGelmeAyniBaytlariVeriyor()
    {
        var baytlar = new byte[] { 0, 1, 127, 128, 255, 42, 200, 7, 99, 250 };

        Assert.Equal(baytlar, Totp.Base32Coz(Totp.Base32Kodla(baytlar)));
    }

    [Fact]
    public void Base32_BOSLUKLUVeKUCUKHarfKabulEdiliyor()
    {
        // Kullanıcı anahtarı ekrandaki gruplu hâliyle ("JBSW Y3DP …")
        // kopyalayıp yapıştırabiliyor; küçük harfle de yazabiliyor.
        var duz = Totp.Base32Coz("MZXW6YTBOI");

        Assert.Equal(duz, Totp.Base32Coz("MZXW 6YTB OI"));
        Assert.Equal(duz, Totp.Base32Coz("mzxw6ytboi"));
        Assert.Equal(duz, Totp.Base32Coz("MZXW-6YTB-OI"));
    }

    [Fact]
    public void Base32_GecersizKarakterHataVeriyor()
        => Assert.Throws<FormatException>(() => Totp.Base32Coz("MZXW1!"));

    // ==================================================================
    //  3) Zaman penceresi
    // ==================================================================

    [Fact]
    public void OncekiVeSonrakiAdimDaKabulEdiliyor()
    {
        var an = DateTimeOffset.FromUnixTimeSeconds(1111111111L);
        var kod = Totp.KodUret(RfcAnahtari, an);

        // Telefonun saati birkaç saniye kayabilir ve kullanıcı kodu okuyup
        // yazarken zaten saniyeler geçiyor. Tolerans olmasaydı geçerli
        // kodlar sırf zamanlama yüzünden reddedilirdi.
        Assert.True(Totp.Dogrula(RfcAnahtari, kod, an.AddSeconds(-Totp.AdimSaniye)));
        Assert.True(Totp.Dogrula(RfcAnahtari, kod, an.AddSeconds(Totp.AdimSaniye)));
    }

    [Fact]
    public void PencereDISINDAKIKodReddediliyor()
    {
        var an = DateTimeOffset.FromUnixTimeSeconds(1111111111L);
        var kod = Totp.KodUret(RfcAnahtari, an);

        // Tolerans SINIRSIZ olsaydı çalınmış bir kod süresiz kullanılabilirdi.
        Assert.False(Totp.Dogrula(RfcAnahtari, kod, an.AddSeconds(Totp.AdimSaniye * 3)));
        Assert.False(Totp.Dogrula(RfcAnahtari, kod, an.AddSeconds(-Totp.AdimSaniye * 3)));
    }

    [Fact]
    public void AyniAdimIcindeKodDEGISMIYOR()
    {
        // 30 saniyelik adımın başı ve sonu aynı kodu vermeli; vermeseydi
        // kullanıcı kodu yazarken kod değişir ve giriş rastgele başarısız
        // olurdu.
        var adimBasi = DateTimeOffset.FromUnixTimeSeconds(1111111110L);   // 30'a tam bölünür

        Assert.Equal(
            Totp.KodUret(RfcAnahtari, adimBasi),
            Totp.KodUret(RfcAnahtari, adimBasi.AddSeconds(29)));
    }

    // ==================================================================
    //  4) Geçersiz girdiler — sessizce false, istisna DEĞİL
    // ==================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]      // eksik basamak
    [InlineData("1234567")]    // fazla basamak
    [InlineData("abcdef")]     // rakam değil
    public void GecersizKod_Reddediliyor(string? kod)
        => Assert.False(Totp.Dogrula(RfcAnahtari, kod));

    [Fact]
    public void AnahtarYoksa_Reddediliyor()
    {
        // TOTP açık OLMAYAN bir kullanıcının anahtarı null. Burada istisna
        // fırlatsaydık, giriş akışı 500 ile patlardı.
        Assert.False(Totp.Dogrula(null, "123456"));
        Assert.False(Totp.Dogrula("", "123456"));
    }

    [Fact]
    public void BOZUKAnahtar_IstisnaFirlatmiyor()
    {
        // Veritabanındaki anahtar bir şekilde bozulduysa kullanıcı giriş
        // yapamamalı — ama sunucu da çökmemeli.
        Assert.False(Totp.Dogrula("BU!GECERSIZ!", "123456"));
    }

    [Fact]
    public void BOSLUKLUKodKabulEdiliyor()
    {
        // Bazı uygulamalar kodu "050 471" diye gösteriyor ve kullanıcı
        // boşlukla birlikte yapıştırıyor.
        var an = DateTimeOffset.FromUnixTimeSeconds(1111111111L);

        Assert.True(Totp.Dogrula(RfcAnahtari, "050 471", an));
    }

    // ==================================================================
    //  5) Anahtar üretimi
    // ==================================================================

    [Fact]
    public void AnahtarUret_HerSeferindeFARKLI()
    {
        var anahtarlar = Enumerable.Range(0, 50).Select(_ => Totp.AnahtarUret()).ToList();

        // Tahmin edilemezlik iddiasının en kaba kontrolü. Sabit ya da sayaca
        // bağlı bir üretim burada tekrar üretirdi.
        Assert.Equal(anahtarlar.Count, anahtarlar.Distinct().Count());
    }

    [Fact]
    public void AnahtarUret_Base32VeYeterinceUzun()
    {
        var anahtar = Totp.AnahtarUret();

        // 20 bayt → 32 Base32 karakteri. RFC 4226'nın önerdiği alt sınır.
        Assert.Equal(32, anahtar.Length);
        Assert.Matches("^[A-Z2-7]+$", anahtar);
        Assert.Equal(20, Totp.Base32Coz(anahtar).Length);
    }

    [Fact]
    public void UretilenAnahtarlaUretilenKod_Dogrulaniyor()
    {
        var anahtar = Totp.AnahtarUret();
        var an = DateTimeOffset.UtcNow;

        Assert.True(Totp.Dogrula(anahtar, Totp.KodUret(anahtar, an), an));
    }

    [Fact]
    public void FARKLIAnahtarinKodu_Reddediliyor()
    {
        var an = DateTimeOffset.UtcNow;
        var kod = Totp.KodUret(Totp.AnahtarUret(), an);

        Assert.False(Totp.Dogrula(Totp.AnahtarUret(), kod, an));
    }

    // ==================================================================
    //  6) Kurulum adresi ve okunur biçim
    // ==================================================================

    [Fact]
    public void KurulumAdresi_StandartBicimde()
    {
        var adres = Totp.KurulumAdresi("azra", "JBSWY3DPEHPK3PXP", "StajProject");

        Assert.StartsWith("otpauth://totp/StajProject:azra?", adres);
        Assert.Contains("secret=JBSWY3DPEHPK3PXP", adres);
        Assert.Contains("issuer=StajProject", adres);
        Assert.Contains($"digits={Totp.Basamak}", adres);
        Assert.Contains($"period={Totp.AdimSaniye}", adres);
    }

    [Fact]
    public void KurulumAdresi_OZELKarakterleriKacisliyor()
    {
        // Kullanıcı adı boşluk ya da '#' içerirse adres bozulur ve
        // authenticator uygulaması anahtarı yanlış okur.
        var adres = Totp.KurulumAdresi("a z#ra", "JBSWY3DP", "Staj Project");

        Assert.DoesNotContain(" ", adres);
        Assert.Contains("a%20z%23ra", adres);
    }

    [Fact]
    public void OkunurAnahtar_DorderliGruplaniyor()
    {
        // Kullanıcı bunu telefona ELLE yazacak; 32 karakterlik kesintisiz
        // bir dizide gözün yerini kaybetmesi kaçınılmaz.
        Assert.Equal("JBSW Y3DP EHPK 3PXP", Totp.OkunurAnahtar("JBSWY3DPEHPK3PXP"));
    }

    [Fact]
    public void OkunurAnahtar_TamBolunmeyenUzunluktaDaCalisiyor()
        => Assert.Equal("ABCD EF", Totp.OkunurAnahtar("ABCDEF"));

    [Fact]
    public void OkunurAnahtar_BOSLUKLARIAyiklanipCozulebiliyor()
    {
        // Ekranda gruplu gösteriyoruz; kullanıcı öyle kopyalarsa çözülmeli.
        var anahtar = Totp.AnahtarUret();

        Assert.Equal(
            Totp.Base32Coz(anahtar),
            Totp.Base32Coz(Totp.OkunurAnahtar(anahtar)));
    }
}
