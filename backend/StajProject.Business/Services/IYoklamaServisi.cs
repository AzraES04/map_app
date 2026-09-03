using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// TUR YOKLAMASI — "şu an kimler burada?" (bellekte).
///
/// ---- NE İŞE YARIYOR? ----
/// Rehber sahada bir soru soruyor ("Otobüse döndünüz mü?"), bağlantıyı açan
/// herkes tek dokunuşla cevaplıyor ve rehber sayıyı görüyor:
/// "15 kişinin 13'ü buradayım, 1 değil, 1 acil".
///
/// ---- NEDEN VERİTABANI YOK? ----
/// Yoklama ANLIK bir durum: cevabın anlamı yalnızca sorulduğu dakikada var.
/// Kalıcı yapsaydık, kapanan bir turdan sonra "hâlâ burada" diyen ölü
/// kayıtları temizlemek gerekirdi ve kimliksiz misafirlerin cevaplarını
/// süresiz saklamak, toplamamız gerekmeyen bir veriyi saklamak olurdu.
/// Simülasyon defteriyle aynı karar (bkz. ISimulasyonServisi).
///
/// SINGLETON: yoklama bir isteğe değil, uygulamaya ait bir durum — rehber
/// soruyor, cevaplar başka isteklerden geliyor.
///
/// ---- MİSAFİR NASIL TEKİLLEŞTİRİLİYOR? ----
/// Misafirin hesabı yok. Tarayıcısında üretilen rastgele bir anahtar
/// (<c>misafirAnahtari</c>) kullanılıyor: aynı kişi cevabını değiştirince
/// yeni satır açılmıyor, kendi cevabı güncelleniyor. Anahtar kimlik DEĞİL —
/// kim olduğunu söylemiyor, yalnızca "aynı tarayıcı" diyor.
/// </summary>
public interface IYoklamaServisi
{
    /// <summary>
    /// Yoklamayı başlatır (varsa sıfırlayıp yeniden başlatır).
    ///
    /// Yeniden başlatmak ESKİ CEVAPLARI SİLİYOR: "on dakika önce buradaydım"
    /// yeni soruya verilmiş bir cevap değil ve rehber onu güncel sanırdı.
    /// </summary>
    /// <param name="grupBoyu">
    /// Rehberin bildirdiği kişi sayısı. Misafirler kayıtlı olmadığı için
    /// toplamı sunucu bilemiyor; "15 kişinin 13'ü" ifadesindeki 15 bu.
    /// </param>
    YoklamaDurumuDto Baslat(int oturumId, string soru, int grupBoyu);

    /// <summary>Yoklamayı kapatır. Açık yoklama yoksa false.</summary>
    bool Bitir(int oturumId);

    /// <summary>Açık yoklamanın durumu; yoksa null.</summary>
    YoklamaDurumuDto? Durum(int oturumId);

    /// <summary>
    /// Misafirin cevabını yazar (aynı anahtar yeniden yazarsa günceller).
    ///
    /// Açık yoklama yoksa null döner: kapanmış bir yoklamaya cevap kabul
    /// etmek, rehberin ekranında olmayan bir sayıyı büyütmek olurdu.
    /// </summary>
    /// <param name="ad">İsteğe bağlı — rehberin listesinde görünür.</param>
    /// <param name="telefon">
    /// İsteğe bağlı — özellikle "Acil" cevabında anlamlı: rehberin geri
    /// arayabilmesi için.
    /// </param>
    YoklamaDurumuDto? Cevapla(
        int oturumId, string misafirAnahtari, string cevap, string? ad, string? telefon);
}
