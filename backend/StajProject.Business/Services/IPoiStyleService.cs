using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// POI stillerini kategori tablosundan üretip GeoServer'a yazar
/// (Ödev 13 iyileştirmesi).
///
/// NEDEN AYRI BİR SERVİS?
/// İki farklı işi birleştiriyor: kategori ağacını okumak (veri) ve GeoServer'a
/// yazmak (dış sistem). <c>PoiCategoryService</c>'in içine koysaydık kategori
/// CRUD'u GeoServer'a bağımlı hâle gelirdi ve GeoServer kapalıyken kategori
/// eklenemezdi. Ayrı servis, bağımlılığı tek bir yerde tutuyor.
/// </summary>
public interface IPoiStyleService
{
    /// <summary>
    /// Kategori ağacından üretilen stil TANIMLARI — GeoServer'a hiç gitmeden.
    ///
    /// Harita ekranı bunu iki iş için kullanıyor: WMS isteğinin
    /// <c>STYLES</c> parametresini kurmak ve lejantı çizmek. GeoServer'a
    /// gitmeden cevaplanabildiği için sunucu kapalıyken de lejant doğru kalıyor.
    /// </summary>
    Task<List<PoiStilDto>> ListeleAsync();

    /// <summary>
    /// Stilleri üretir, GeoServer'a yazar, katmana bağlar ve karşılığı kalmayan
    /// eski stilleri siler. Bağlantı yoksa hata fırlatır — yönetici düğmeye
    /// bastıysa sonucu bilmeli.
    /// </summary>
    Task<PoiStilYenilemeDto> YenileAsync(CancellationToken iptal = default);

    /// <summary>
    /// Kategori değiştikten sonra çağrılan SESSİZ yenileme: başarısız olursa
    /// hata fırlatmaz, yalnızca günlüğe yazar.
    ///
    /// Gerekçe: kategori ekleme işleminin kendisi başarılı olmuştur ve
    /// veritabanına yazılmıştır. GeoServer o an kapalıysa kullanıcıya
    /// "kategori eklenemedi" demek YANLIŞ olurdu — eklendi, sadece haritadaki
    /// simgesi henüz oluşmadı. Yönetici "Stilleri yenile" düğmesiyle
    /// tamamlayabilir.
    /// </summary>
    Task SessizYenileAsync();
}
