using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// Yetki tablosunun veri erişim sözleşmesi.
///
/// Yetkiler arayüzden EKLENMEZ: bir yetki ancak kod tarafında karşılığı olduğunda
/// (örn. "Point Ekleme" kontrolü) anlamlıdır. Bu yüzden liste okuma ağırlıklı;
/// ekleme yalnızca seed tarafından kullanılır.
/// </summary>
public interface IPermissionRepository
{
    Task<List<Permission>> GetAllAsync();

    /// <summary>Verilen id'lerden veritabanında GERÇEKTEN var olanlar.</summary>
    Task<List<int>> GetExistingIdsAsync(IReadOnlyCollection<int> ids);

    Task<Permission?> GetByNameAsync(string name);

    Task<Permission> AddAsync(Permission permission);
}
