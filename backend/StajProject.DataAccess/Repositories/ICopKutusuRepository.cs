namespace StajProject.DataAccess.Repositories;

/// <summary>
/// Çöp kutusundaki bir kaydın HAM hâli — henüz DTO'ya çevrilmemiş.
/// </summary>
/// <param name="Tur">"nokta", "poi", "durak"… (makine adı)</param>
/// <param name="Id">Kaydın kendi tablosundaki id'si</param>
/// <param name="Ad">Görünen ad</param>
/// <param name="Detay">Bağlam (kategori, güzergah, açıklama)</param>
/// <param name="SilinmeZamani">ModifiedDate — gerekçe: CopOgesiDto</param>
/// <param name="Ekleyen">Kaydı ekleyen kullanıcı adı</param>
public record SilinmisKayit(
    string Tur,
    int Id,
    string Ad,
    string? Detay,
    DateTime? SilinmeZamani,
    string? Ekleyen);

/// <summary>
/// Silinmiş kayıtları okuyan ve geri alan depo.
///
/// ---- NEDEN AYRI BİR DEPO? ----
///
/// Mevcut depoların hepsi sorgu filtresiyle çalışıyor: silinmiş kaydı
/// GÖRMÜYORLAR. Bu bilinçli — uygulamanın hiçbir yerinde silinmiş bir kayıt
/// yanlışlıkla listeye karışmasın diye.
///
/// Çöp kutusu tam tersini istiyor. Var olan depolara "silinmişleri de getir"
/// bayrağı eklemek, o filtreyi her çağrı noktasında yeniden düşünmeyi
/// gerektirirdi ve bir gün biri bayrağı yanlış geçirdiğinde silinmiş kayıtlar
/// normal listede belirirdi. Ayrı depo, <c>IgnoreQueryFilters</c>'ı TEK bir
/// dosyada tutuyor.
/// </summary>
public interface ICopKutusuRepository
{
    /// <summary>
    /// Bütün silinmiş kayıtlar, en yeniden eskiye.
    ///
    /// Tür süzgeci YOK: liste zaten türü taşıyor ve süzme arayüzde yapılıyor.
    /// Sunucuda süzmek her tür için ayrı sorgu yolu demekti; kayıt sayısı
    /// (silinmiş kayıtlar) doğası gereği küçük.
    /// </summary>
    Task<List<SilinmisKayit>> ListeleAsync();

    /// <summary>
    /// Bir kaydı geri alır: <c>is_deleted = false</c>.
    /// </summary>
    /// <returns>
    /// Kayıt bulunamazsa ya da zaten silinmemişse false.
    /// Tür tanınmıyorsa <see cref="ArgumentException"/>.
    /// </returns>
    Task<bool> GeriAlAsync(string tur, int id);

    /// <summary>
    /// Bir kaydı VERİTABANINDAN TAMAMEN SİLER — geri alma yok.
    ///
    /// Yalnızca ZATEN silinmiş (soft-deleted) kayıtlarda çalışır: çöp
    /// kutusunda görünmeyen bir kayıt kalıcı silinemez, önce normal silme
    /// yolundan geçmesi gerekiyor. Bu, yanlışlıkla aktif bir kaydın
    /// kalıcı silinmesine karşı ikinci bir kapı.
    /// </summary>
    /// <returns>
    /// Kayıt yoksa ya da henüz soft-delete edilmemişse false. Başka
    /// kayıtlar buna referans veriyorsa (yabancı anahtar) servis katmanı
    /// bunu <see cref="Validation.IsKuraliException"/>'a çevirip anlaşılır
    /// bir mesajla döner.
    /// </returns>
    Task<bool> KaliciSilAsync(string tur, int id);
}
