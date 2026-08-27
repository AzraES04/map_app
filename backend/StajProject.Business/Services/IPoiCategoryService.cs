using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// POI kategori yönetimi (Ödev 12 / Madde 2: "kategori yönetimi bu panelden").
///
/// Hiyerarşinin bütün kuralları burada: ata seçimi, döngü engeli, kardeşler
/// arası ad benzersizliği ve "dolu kategori silinemez". Veritabanı bunların
/// yalnızca bir kısmını (var olmayan ataya bağlanma) kendi başına engelleyebilir.
/// </summary>
public interface IPoiCategoryService
{
    /// <summary>
    /// Yönetim ekranı için TAM ağaç — pasif kategoriler de dahil.
    /// Kök düğümler döner; alt kategoriler <c>Cocuklar</c> içinde.
    /// </summary>
    Task<List<PoiKategoriDto>> GetTreeAsync();

    /// <summary>
    /// Operatörün POI formundaki açılır listesi: yalnızca AKTİF kategoriler,
    /// düz liste hâlinde (her düğümde <c>Seviye</c> ve <c>TamYol</c> dolu).
    /// </summary>
    Task<List<PoiKategoriDto>> GetSelectableAsync();

    Task<PoiKategoriDto> CreateAsync(PoiKategoriSaveDto dto);

    /// <summary>Kategori yoksa null. Ata değişikliği döngü üretiyorsa hata fırlatır.</summary>
    Task<PoiKategoriDto?> UpdateAsync(int id, PoiKategoriSaveDto dto);

    /// <summary>
    /// Soft delete. Alt kategorisi ya da bağlı POI'si varsa iş kuralı hatası
    /// fırlatır — kayıt yoksa false döner.
    /// </summary>
    Task<bool> DeleteAsync(int id);
}
