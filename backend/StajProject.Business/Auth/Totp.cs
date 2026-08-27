using System.Security.Cryptography;
using System.Text;

namespace StajProject.Business.Auth;

/// <summary>
/// TOTP — zaman tabanlı tek kullanımlık şifre (RFC 6238).
/// İki adımlı doğrulamanın matematiği.
///
/// ---- NEDEN TOTP, NEDEN SMS/E-POSTA DEĞİL? ----
///
/// SMS ve e-posta bir DIŞ SERVİSE bağlanmayı gerektiriyor: SMS sağlayıcısı
/// para ve sözleşme, e-posta ise bir SMTP sunucusu ister. İkisi de internet
/// olmadan çalışmaz — jüri önünde ağ sorunu, girişin tamamen kilitlenmesi
/// demek olurdu.
///
/// TOTP'de sunucu ile telefon arasında HİÇBİR İLETİŞİM YOK. İkisi de aynı
/// gizli anahtarı ve aynı saati biliyor; kodu bağımsız hesaplıyorlar. Bu
/// yüzden çevrimdışı çalışıyor ve Google Authenticator, Microsoft
/// Authenticator, Authy gibi hazır uygulamalarla uyumlu — standart olması
/// kullanıcıya tanıdık bir akış sunuyor.
///
/// ---- NEDEN KÜTÜPHANE KULLANILMADI? ----
///
/// Algoritma toplam 30 satır ve .NET'in kendi HMACSHA1 sınıfına dayanıyor.
/// Kimlik doğrulamanın kalbindeki bir kod için üçüncü parti bir bağımlılık
/// eklemek, denetlenmesi gereken yüzeyi büyütürdü. Burada her satır okunuyor
/// ve RFC'nin resmî test vektörleriyle sınanıyor (<c>TotpTests</c>).
/// </summary>
public static class Totp
{
    /// <summary>Kod uzunluğu — authenticator uygulamalarının evrensel varsayılanı.</summary>
    public const int Basamak = 6;

    /// <summary>
    /// Bir kodun geçerli kaldığı süre (saniye). RFC 6238'in önerdiği ve
    /// bütün authenticator uygulamalarının varsaydığı değer.
    /// </summary>
    public const int AdimSaniye = 30;

    /// <summary>
    /// Kabul edilen zaman penceresi (adım sayısı, ileri ve geri).
    ///
    /// 1 → şu anki kodun yanı sıra bir öncekini ve bir sonrakini de kabul
    /// ediyoruz, yani toplam ±30 saniye tolerans.
    ///
    /// NEDEN 0 DEĞİL? Telefonun saati sunucununkinden birkaç saniye kayabilir
    /// ve kullanıcı kodu okuyup yazarken zaten saniyeler geçiyor. Tolerans
    /// olmasaydı geçerli kodlar sırf zamanlama yüzünden reddedilirdi.
    ///
    /// NEDEN DAHA BÜYÜK DEĞİL? Her ek adım, çalınmış bir kodun kullanılabilir
    /// kaldığı süreyi 30 saniye uzatıyor. 1 adım, kullanılabilirlik ile
    /// güvenlik arasındaki yaygın dengedir.
    /// </summary>
    public const int PencereAdimi = 1;

    /// <summary>Base32 alfabesi (RFC 4648) — authenticator uygulamalarının beklediği biçim.</summary>
    private const string Base32Alfabesi = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// Yeni bir gizli anahtar üretir (Base32 metin).
    ///
    /// 20 bayt = 160 bit: HMAC-SHA1'in blok boyutuyla uyumlu ve RFC 4226'nın
    /// önerdiği alt sınır. <c>RandomNumberGenerator</c> kullanılıyor,
    /// <c>Random</c> DEĞİL — bu değeri tahmin edebilen biri, kullanıcının
    /// bütün gelecek kodlarını üretebilir.
    /// </summary>
    public static string AnahtarUret(int bayt = 20)
        => Base32Kodla(RandomNumberGenerator.GetBytes(bayt));

    /// <summary>
    /// Verilen an için beklenen kodu hesaplar.
    /// </summary>
    public static string KodUret(string base32Anahtar, DateTimeOffset an)
        => KodUret(Base32Coz(base32Anahtar), AdimNo(an));

    /// <summary>
    /// Kod doğru mu? Zaman penceresi toleransıyla birlikte kontrol eder.
    ///
    /// Boş/biçimsiz kod sessizce false döner: çağıran tarafın ayrıca
    /// biçim kontrolü yapmasına gerek kalmasın.
    /// </summary>
    public static bool Dogrula(string? base32Anahtar, string? kod, DateTimeOffset? an = null)
    {
        if (string.IsNullOrWhiteSpace(base32Anahtar) || string.IsNullOrWhiteSpace(kod))
        {
            return false;
        }

        // Kullanıcı kodu "123 456" gibi boşluklu yapıştırabiliyor.
        var temiz = new string(kod.Where(char.IsDigit).ToArray());
        if (temiz.Length != Basamak)
        {
            return false;
        }

        byte[] anahtar;
        try
        {
            anahtar = Base32Coz(base32Anahtar);
        }
        catch (FormatException)
        {
            return false;
        }

        var merkez = AdimNo(an ?? DateTimeOffset.UtcNow);

        for (var kaydir = -PencereAdimi; kaydir <= PencereAdimi; kaydir++)
        {
            // SABİT ZAMANLI karşılaştırma.
            //
            // Sıradan == karşılaştırması ilk farklı karakterde durur ve
            // geçen SÜRE, kaç karakterin tuttuğunu ele verir. Saldırgan bunu
            // ölçerek kodu basamak basamak bulabilirdi — teoride uzak ama
            // kapatması bir satırlık iş.
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(KodUret(anahtar, merkez + kaydir)),
                    Encoding.ASCII.GetBytes(temiz)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Authenticator uygulamasının okuduğu kurulum adresi.
    ///
    /// Biçim: <c>otpauth://totp/{yayinci}:{hesap}?secret=…&amp;issuer={yayinci}</c>
    ///
    /// Yayıncı adı İKİ KEZ geçiyor (hem etiketin içinde hem parametre olarak)
    /// — bu, standardın kendi önerisi: eski uygulamalar etiketi okuyor,
    /// yenileri parametreyi.
    /// </summary>
    public static string KurulumAdresi(string kullaniciAdi, string base32Anahtar, string yayinci)
    {
        var y = Uri.EscapeDataString(yayinci);
        var k = Uri.EscapeDataString(kullaniciAdi);

        return $"otpauth://totp/{y}:{k}"
             + $"?secret={base32Anahtar}"
             + $"&issuer={y}"
             + $"&algorithm=SHA1&digits={Basamak}&period={AdimSaniye}";
    }

    /// <summary>
    /// Anahtarı dörderli gruplara ayırır: "JBSW Y3DP EHPK 3PXP".
    ///
    /// Kullanıcı bunu telefona ELLE yazacak; 32 karakterlik kesintisiz bir
    /// dizide gözün yerini kaybetmesi kaçınılmaz.
    /// </summary>
    public static string OkunurAnahtar(string base32Anahtar)
        => string.Join(' ', Enumerable
            .Range(0, (base32Anahtar.Length + 3) / 4)
            .Select(i => base32Anahtar.Substring(i * 4, Math.Min(4, base32Anahtar.Length - i * 4))));

    // ==================================================================
    //  İç işler — RFC 6238 / RFC 4226
    // ==================================================================

    /// <summary>Unix zamanının kaçıncı 30 saniyelik adımda olduğu.</summary>
    private static long AdimNo(DateTimeOffset an) => an.ToUnixTimeSeconds() / AdimSaniye;

    /// <summary>
    /// HOTP (RFC 4226) — sayaç değerinden kod üretir.
    ///
    /// Adımlar:
    ///   1. Sayaç 8 baytlık BÜYÜK UÇLU (big-endian) diziye yazılır.
    ///      BitConverter küçük uçlu makinelerde ters yazar; bu yüzden
    ///      açıkça ters çevriliyor. Atlanırsa kodlar üretilir ama HİÇBİR
    ///      authenticator uygulamasıyla uyuşmaz — ve hata mesajı alınmaz.
    ///   2. HMAC-SHA1 ile özet alınır.
    ///   3. "Dinamik kırpma": son baytın alt 4 biti bir konum veriyor,
    ///      oradaki 4 bayt okunuyor.
    ///   4. En anlamlı bit maskeleniyor (işaret bitini atmak için) ve
    ///      10^6'ya göre modu alınıyor.
    /// </summary>
    private static string KodUret(byte[] anahtar, long sayac)
    {
        var sayacBaytlari = BitConverter.GetBytes(sayac);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(sayacBaytlari);
        }

        using var hmac = new HMACSHA1(anahtar);
        var ozet = hmac.ComputeHash(sayacBaytlari);

        var konum = ozet[^1] & 0x0F;
        var ikili = ((ozet[konum] & 0x7F) << 24)
                  | ((ozet[konum + 1] & 0xFF) << 16)
                  | ((ozet[konum + 2] & 0xFF) << 8)
                  | (ozet[konum + 3] & 0xFF);

        return (ikili % (int)Math.Pow(10, Basamak)).ToString().PadLeft(Basamak, '0');
    }

    /// <summary>Baytları Base32'ye çevirir (RFC 4648, dolgu yok).</summary>
    internal static string Base32Kodla(byte[] veri)
    {
        var sonuc = new StringBuilder((veri.Length * 8 + 4) / 5);
        int tampon = 0, bit = 0;

        foreach (var b in veri)
        {
            tampon = (tampon << 8) | b;
            bit += 8;

            while (bit >= 5)
            {
                sonuc.Append(Base32Alfabesi[(tampon >> (bit - 5)) & 31]);
                bit -= 5;
            }
        }

        // Artan bitler sağdan sıfırla tamamlanıyor.
        if (bit > 0)
        {
            sonuc.Append(Base32Alfabesi[(tampon << (5 - bit)) & 31]);
        }

        return sonuc.ToString();
    }

    /// <summary>
    /// Base32 metni baytlara çevirir.
    ///
    /// Boşluk ve '=' dolgusu yok sayılıyor: kullanıcı anahtarı gruplu hâlde
    /// ("JBSW Y3DP …") kopyalayabiliyor ve bazı uygulamalar dolgulu üretiyor.
    /// Küçük harf de kabul: elle yazan kullanıcı büyük harf kullanmayabilir.
    /// </summary>
    internal static byte[] Base32Coz(string metin)
    {
        var baytlar = new List<byte>(metin.Length * 5 / 8 + 1);
        int tampon = 0, bit = 0;

        foreach (var ch in metin)
        {
            if (ch is ' ' or '-' or '=') continue;

            var deger = Base32Alfabesi.IndexOf(char.ToUpperInvariant(ch));
            if (deger < 0)
            {
                throw new FormatException($"Base32 dışı karakter: '{ch}'");
            }

            tampon = (tampon << 5) | deger;
            bit += 5;

            if (bit >= 8)
            {
                baytlar.Add((byte)((tampon >> (bit - 8)) & 0xFF));
                bit -= 8;
            }
        }

        return baytlar.ToArray();
    }
}
