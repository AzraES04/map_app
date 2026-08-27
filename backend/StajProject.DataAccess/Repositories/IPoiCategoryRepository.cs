using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// Hiyerarşik POI kategorisi sözlüğünün veri erişim sözleşmesi (Ödev 12).
///
/// Ağacı VERİTABANINDA kurmuyoruz: <see cref="GetAllAsync"/> düz bir liste
/// döner, ağaç servis katmanında parent_id'lere bakılarak kuruluyor.
/// Sebep: kategori sayısı onlarla ölçülür; hepsini tek sorguda çekip bellekte
/// bağlamak, her düğüm için ayrı sorgu atmaktan (N+1) da recursive CTE
/// yazmaktan da hem hızlı hem okunur.
/// </summary>
public interface IPoiCategoryRepository
{
    /// <summary>Tüm kategoriler, düz liste (silinmişler hariç).</summary>
    Task<List<PoiCategory>> GetAllAsync();

    Task<PoiCategory?> GetByIdAsync(int id);

    Task<PoiCategory> AddAsync(PoiCategory kategori);

    Task<PoiCategory?> UpdateAsync(PoiCategory kategori);

    /// <summary>Soft delete. Kayıt yoksa false.</summary>
    Task<bool> SoftDeleteAsync(int id);

    /// <summary>Ada göre arar — seed'in "bu kategori zaten var mı?" kontrolü için.</summary>
    Task<PoiCategory?> GetByNameAsync(string ad, int? parentId);
}
