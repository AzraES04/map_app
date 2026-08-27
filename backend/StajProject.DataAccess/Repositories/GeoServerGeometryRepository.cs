using System.Globalization;
using StajProject.DataAccess.GeoServer;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// Ödev 8 / Madde 2 — OKUMA GeoServer'dan, YAZMA veritabanından.
///
/// Bu sınıf <see cref="IGeometryRepository{TEntity}"/>'yi gerçekliyor ama
/// veritabanına hiç bakmıyor: listeleme ve tekil getirme istekleri
/// GeoServer'ın WFS servisine gidiyor. Yazma işlemleri (ekle, güncelle, sil,
/// geri al, aktiflik) ise içindeki EF Core repository'sine devrediliyor.
///
/// DESEN: Decorator (sarmalayıcı). Üstteki katmanlar
/// (<c>GeometryService</c>, controller'lar) hiç değişmedi — hâlâ aynı
/// <c>IGeometryRepository</c> arayüzünü görüyorlar. Veri kaynağını
/// değiştirmek için tek bir DI kaydını değiştirmek yetti. Katmanlı mimarinin
/// asıl faydası tam olarak bu: arayüz sabit kalırken gerçekleme değişebiliyor.
///
/// NEDEN YAZMA DA WFS-T ÜZERİNDEN DEĞİL?
/// WFS'in transaction (WFS-T) desteği var, teknik olarak mümkün. Ama:
///   • Ödev "veri GETİRME isteklerini revize edin" diyor.
///   • Yazma yolunda projenin kendi kuralları çalışıyor: coğrafi yetki
///     doğrulaması, sahiplik damgası, soft delete, ModifiedDate tetikleyicisi.
///     Bunlar EF Core/AppDbContext içinde yaşıyor; WFS-T'ye geçmek hepsini
///     GeoServer tarafına taşımayı ya da atlamayı gerektirirdi.
///   • GeoServer aynı PostGIS veritabanını okuduğu için yazılan kayıt bir
///     sonraki listelemede zaten GeoServer'dan geri geliyor — veri iki yerde
///     tutulmuyor, tek kaynak hâlâ PostGIS.
/// </summary>
public class GeoServerGeometryRepository<TEntity> : IGeometryRepository<TEntity>
    where TEntity : GeometryEntityBase, new()
{
    private readonly IGeoServerClient _geoServer;

    /// <summary>
    /// Yazma işlemlerinin devredildiği depo — üretimde EF Core'lu
    /// <see cref="GeometryRepository{TEntity}"/>, testlerde bellekteki sahtesi.
    ///
    /// Tipi somut sınıf değil ARAYÜZ: bu sınıfı veritabanı olmadan test
    /// edebilmek için. DI kaydında somut tipi elle veriyoruz (bkz.
    /// DataAccessRegistration.AddGeoServer) — aksi hâlde kayıt kendi kendini
    /// çözmeye çalışıp sonsuz döngüye girerdi.
    /// </summary>
    private readonly IGeometryRepository<TEntity> _yazmaDeposu;

    /// <summary>Bu entity'nin GeoServer'daki katmanı — örn. "tbl_point".</summary>
    private static readonly string Katman = GeoServerKatmanlari.Bul(typeof(TEntity));

    public GeoServerGeometryRepository(
        IGeoServerClient geoServer,
        IGeometryRepository<TEntity> yazmaDeposu)
    {
        _geoServer = geoServer;
        _yazmaDeposu = yazmaDeposu;
    }

    // =====================================================================
    //  OKUMA — GeoServer WFS
    // =====================================================================

    /// <summary>
    /// Kayıtları GeoServer'dan çeker.
    ///
    /// Süzme GeoServer'da yapılıyor (CQL_FILTER), backend'de değil. Bu önemli:
    /// EF Core'da <c>Where(e =&gt; e.InsertedUserId == userId)</c> nasıl SQL'e
    /// çevrilip veritabanında koşuyorsa, burada da aynı koşul WFS isteğine
    /// gömülüp GeoServer tarafında işletiliyor. Başkasının kaydı ağdan hiç
    /// geçmiyor.
    /// </summary>
    public async Task<List<TEntity>> GetAllAsync(int? userId = null)
    {
        var features = await _geoServer.OzellikGetirAsync(
            Katman,
            cqlFilter: SahiplikSuzgeci(userId),
            siralama: "inserted_date D");   // D = descending, EF'teki OrderByDescending

        return features.Select(Cevir).ToList();
    }

    public async Task<TEntity?> GetByIdAsync(int id, int? userId = null)
    {
        // Id sayı olduğu için doğrudan gömmek güvenli; metin olsaydı
        // CQL enjeksiyonuna karşı tırnak kaçırmak gerekirdi.
        var suzgec = $"id = {id.ToString(CultureInfo.InvariantCulture)}";

        var sahiplik = SahiplikSuzgeci(userId);
        if (sahiplik is not null)
        {
            suzgec += $" AND {sahiplik}";
        }

        var features = await _geoServer.OzellikGetirAsync(Katman, cqlFilter: suzgec);

        return features.Count == 0 ? null : Cevir(features[0]);
    }

    /// <summary>
    /// Sahiplik süzgeci (Ödev 5). Kullanıcı verilmezse süzgeç yok — null döner.
    ///
    /// DİKKAT — burada <c>is_deleted = false</c> YOK, çünkü Ödev 9'dan sonra
    /// o kural katmanın kendi SQL View'ında duruyor. İki yerde birden yazmak
    /// zararsız olurdu ama yanıltıcı: "kural nerede?" sorusunun tek bir cevabı
    /// olmalı. Ayrıca view <c>is_deleted</c> kolonunu hiç yayınlamıyor, o yüzden
    /// süzgeçte kullanmak artık hata verirdi.
    /// </summary>
    private static string? SahiplikSuzgeci(int? userId)
        => userId is null
            ? null
            : $"inserted_user_id = {userId.Value.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>WFS kaydını entity'ye çevirir (ortak dönüştürücü).</summary>
    private static TEntity Cevir(GeoServerFeature feature)
        => FeatureDonusturucu.Cevir<TEntity>(feature, Katman);

    // =====================================================================
    //  YAZMA — EF Core / PostGIS (değişmeden devrediliyor)
    // =====================================================================

    public Task<TEntity> AddAsync(TEntity entity) => _yazmaDeposu.AddAsync(entity);

    public Task<TEntity?> UpdateAsync(TEntity entity) => _yazmaDeposu.UpdateAsync(entity);

    public Task<bool> SoftDeleteAsync(int id) => _yazmaDeposu.SoftDeleteAsync(id);

    public Task<bool> RestoreAsync(int id) => _yazmaDeposu.RestoreAsync(id);

    public Task<bool> SetActiveAsync(int id, bool isActive) => _yazmaDeposu.SetActiveAsync(id, isActive);
}
