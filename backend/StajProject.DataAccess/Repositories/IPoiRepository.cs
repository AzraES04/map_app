using NetTopologySuite.Geometries;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// POI tablosunun veri erişim sözleşmesi (Ödev 12).
///
/// Geometri tablolarının aksine BURADA GENERIC BİR YAPI YOK: POI tek bir tip
/// (nokta) ve kendine özgü alanları var (kategori, mesai). Üç tabloyu ortak
/// gövdeyle yönetmenin gerekçesi burada geçerli değil.
/// </summary>
public interface IPoiRepository
{
    /// <summary>
    /// POI'leri listeler — ekleyen kullanıcı bilgisiyle birlikte.
    /// <paramref name="userId"/> verilirse yalnızca o kullanıcının kayıtları.
    /// </summary>
    Task<List<Poi>> GetAllAsync(int? userId = null);

    /// <summary>Tek POI (ekleyen kullanıcı dahil); yoksa null.</summary>
    Task<Poi?> GetByIdAsync(int id);

    /// <summary>
    /// Arama barının kaynağı (Ödev 13 / Madde 2): adı verilen metni İÇEREN
    /// POI'ler, büyük/küçük harf gözetmeden.
    ///
    /// NEDEN AYRI BİR METOT? "Hepsini çek, C#'ta filtrele" en kolayıydı ama
    /// yanlış olurdu: süzme veriyi TUTAN tarafta yapılmalı. EF gerçeklemesinde
    /// bu bir SQL <c>ILIKE</c>'a, GeoServer gerçeklemesinde bir CQL süzgecine
    /// dönüşüyor — her iki durumda da eşleşmeyen kayıt ağdan hiç geçmiyor.
    /// Kullanıcı her tuşa bastığında çalışan bir uç için bu fark önemli.
    /// </summary>
    /// <param name="sorgu">Aranan metin; boş/çok kısaysa çağıran taraf hiç çağırmıyor.</param>
    /// <param name="enFazla">Dönecek azami kayıt — açılır liste zaten sınırlı yer kaplıyor.</param>
    Task<List<Poi>> AraAsync(string sorgu, int enFazla);

    /// <summary>
    /// Verilen alanın İÇİNDE kalan POI'ler (Ödev 14 / Madde 2).
    ///
    /// Ödev metni: "Analiz yalnızca seçilen alan içerisindeki POI'ler üzerinde
    /// çalışmalıdır." Bu cümlenin veri katmanındaki karşılığı burası —
    /// süzgeci C#'a taşımak, alan küçüldükçe küçülmesi gereken bir aktarımı
    /// sabit tutardı (81 ilin POI'sini indirip bir ilçeyi analiz etmek).
    ///
    /// Sınırın ÜSTÜNDEKİ nokta İÇERİDE sayılıyor (ST_Intersects): il
    /// sınırındaki bir eczanenin analiz dışı kalması, kullanıcının haritada
    /// gördüğüyle çelişirdi. Coğrafi yetkideki "Covers ile sınır dahil"
    /// kararının aynısı.
    /// </summary>
    Task<List<Poi>> AlandakileriGetirAsync(Geometry alan);

    Task<Poi> AddAsync(Poi poi);

    /// <summary>
    /// Çok sayıda POI'yi TEK işlemde ekler (Ödev 14 — analiz veri seti).
    ///
    /// <see cref="AddAsync"/> her kayıtta ayrı bir SaveChanges çalıştırıyor;
    /// binlerce satırlık başlangıç verisi için bu, binlerce ağ gidiş-gelişi
    /// demek ve uygulamanın ilk açılışını dakikalara çıkarıyordu.
    /// </summary>
    Task TopluEkleAsync(IEnumerable<Poi> poiler);

    /// <summary>Ad / kategori / mesai / konum günceller; kayıt yoksa null.</summary>
    Task<Poi?> UpdateAsync(Poi poi);

    /// <summary>Fiziksel silme değil; is_deleted işaretlenir.</summary>
    Task<bool> SoftDeleteAsync(int id);

    /// <summary>Soft delete edilmiş POI'yi geri getirir.</summary>
    Task<bool> RestoreAsync(int id);

    /// <summary>Kaydı askıya alır / yeniden aktif eder (is_active).</summary>
    Task<bool> SetActiveAsync(int id, bool isActive);

    /// <summary>
    /// Kategori id → o kategoriye bağlı POI sayısı.
    /// Kategori listesinde "3 POI" yazabilmek ve dolu kategorinin silinmesini
    /// engellemek için; her kategori için ayrı COUNT atmak yerine tek gruplama.
    /// </summary>
    Task<Dictionary<int, int>> GetCountsByCategoryAsync();
}
