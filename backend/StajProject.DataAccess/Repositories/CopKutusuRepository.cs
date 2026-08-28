using Microsoft.EntityFrameworkCore;
using StajProject.DataAccess.Context;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// Silinmiş kayıtları okuyan ve geri alan depo.
///
/// ---- BURADAKİ TEK KRİTİK ÇAĞRI: IgnoreQueryFilters() ----
///
/// Projedeki bütün entity'lerde <c>!IsDeleted</c> global sorgu filtresi var
/// ve bu bilinçli: uygulamanın hiçbir yerinde silinmiş bir kayıt yanlışlıkla
/// listeye karışmasın diye. Çöp kutusu tam tersini istiyor, o yüzden filtreyi
/// AÇIKÇA aşıyor — kural varsayılan, istisna görünür.
///
/// Filtre aşımı bu dosyanın DIŞINA çıkmıyor: başka hiçbir depo silinmiş kayıt
/// göremiyor.
/// </summary>
public class CopKutusuRepository : ICopKutusuRepository
{
    private readonly AppDbContext _context;

    public CopKutusuRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>Tanınan tür adları — geri alma ucunun kabul ettiği değerler.</summary>
    public const string Nokta = "nokta";
    public const string Cizgi = "cizgi";
    public const string Poligon = "poligon";
    public const string Poi = "poi";
    public const string Kategori = "kategori";
    public const string Durak = "durak";
    public const string Guzergah = "guzergah";
    public const string Kullanici = "kullanici";
    public const string Rol = "rol";

    public async Task<List<SilinmisKayit>> ListeleAsync()
    {
        var sonuc = new List<SilinmisKayit>();

        sonuc.AddRange(await GeometriOku<PointEntity>(Nokta));
        sonuc.AddRange(await GeometriOku<LineEntity>(Cizgi));
        sonuc.AddRange(await GeometriOku<PolygonEntity>(Poligon));

        // POI — bağlam olarak KATEGORİ yolu veriliyor.
        //
        // Kategori de silinmiş olabilir; Include ona da sorgu filtresi
        // uyguladığı için ayrıca IgnoreQueryFilters gerekiyor. Olmasaydı
        // silinmiş bir kategorinin POI'lerinde kategori adı boş görünürdü.
        sonuc.AddRange(await _context.Poiler
            .IgnoreQueryFilters()
            .Where(x => x.IsDeleted)
            .Select(x => new SilinmisKayit(
                Poi,
                x.Id,
                x.Isim,
                _context.PoiKategorileri.IgnoreQueryFilters()
                    .Where(k => k.Id == x.KategoriId)
                    .Select(k => k.Ad)
                    .FirstOrDefault(),
                x.ModifiedDate,
                x.User!.Username))
            .ToListAsync());

        sonuc.AddRange(await _context.PoiKategorileri
            .IgnoreQueryFilters()
            .Where(x => x.IsDeleted)
            .Select(x => new SilinmisKayit(
                Kategori, x.Id, x.Ad, x.Aciklama, x.ModifiedDate, null))
            .ToListAsync());

        sonuc.AddRange(await _context.Duraklar
            .IgnoreQueryFilters()
            .Where(x => x.IsDeleted)
            .Select(x => new SilinmisKayit(
                Durak,
                x.Id,
                x.Ad,
                _context.Guzergahlar.IgnoreQueryFilters()
                    .Where(g => g.Id == x.GuzergahId)
                    .Select(g => g.Ad)
                    .FirstOrDefault(),
                x.ModifiedDate,
                x.User!.Username))
            .ToListAsync());

        sonuc.AddRange(await _context.Guzergahlar
            .IgnoreQueryFilters()
            .Where(x => x.IsDeleted)
            .Select(x => new SilinmisKayit(
                Guzergah, x.Id, x.Ad, x.Aciklama, x.ModifiedDate, x.User!.Username))
            .ToListAsync());

        sonuc.AddRange(await _context.Users
            .IgnoreQueryFilters()
            .Where(x => x.IsDeleted)
            .Select(x => new SilinmisKayit(
                Kullanici, x.Id, x.Username, null, x.ModifiedDate, null))
            .ToListAsync());

        sonuc.AddRange(await _context.Roles
            .IgnoreQueryFilters()
            .Where(x => x.IsDeleted)
            .Select(x => new SilinmisKayit(
                Rol, x.Id, x.Name, x.Description, x.ModifiedDate, null))
            .ToListAsync());

        // En yeni silinen en üstte: kullanıcı çoğu zaman AZ ÖNCE sildiğini
        // arıyor. Tarihi olmayan (Ödev 3 öncesinden kalan) kayıtlar sona.
        return sonuc
            .OrderByDescending(x => x.SilinmeZamani ?? DateTime.MinValue)
            .ToList();
    }

    /// <summary>
    /// Üç geometri tablosu için ortak okuma.
    ///
    /// Üçü de <see cref="GeometryEntityBase"/>'den türüyor ve alanları aynı;
    /// üç kez kopyalasaydık biri güncellenip diğerleri unutulabilirdi.
    /// </summary>
    private Task<List<SilinmisKayit>> GeometriOku<T>(string tur)
        where T : GeometryEntityBase
        => _context.Set<T>()
            .IgnoreQueryFilters()
            .Where(x => x.IsDeleted)
            .Select(x => new SilinmisKayit(
                tur,
                x.Id,
                x.Name,
                x.Description,
                x.ModifiedDate,
                _context.Users.IgnoreQueryFilters()
                    .Where(u => u.Id == x.InsertedUserId)
                    .Select(u => u.Username)
                    .FirstOrDefault()))
            .ToListAsync();

    public async Task<bool> GeriAlAsync(string tur, int id) => tur switch
    {
        Nokta => await GeometriGeriAl<PointEntity>(id),
        Cizgi => await GeometriGeriAl<LineEntity>(id),
        Poligon => await GeometriGeriAl<PolygonEntity>(id),
        Poi => await GeriAl(_context.Poiler, id),
        Kategori => await GeriAl(_context.PoiKategorileri, id),
        Durak => await GeriAl(_context.Duraklar, id),
        Guzergah => await GeriAl(_context.Guzergahlar, id),
        Kullanici => await GeriAl(_context.Users, id),
        Rol => await GeriAl(_context.Roles, id),
        _ => throw new ArgumentException($"Bilinmeyen kayıt türü: \"{tur}\".", nameof(tur)),
    };

    private Task<bool> GeometriGeriAl<T>(int id) where T : GeometryEntityBase
        => GeriAl(_context.Set<T>(), id);

    /// <summary>
    /// Ortak geri alma: <c>is_deleted = false</c> ve <c>is_active = true</c>.
    ///
    /// ---- NEDEN is_active DA AÇILIYOR? ----
    ///
    /// Mevcut <c>GeometryRepository.RestoreAsync</c> da böyle yapıyor ve
    /// gerekçesi şu: silme işlemi ikisini birden kapatıyor
    /// (is_deleted = true, is_active = false). Yalnızca is_deleted'ı geri
    /// alsaydık kayıt "geri geldi ama hâlâ pasif" gibi yarım bir durumda
    /// kalır, kullanıcı geri almanın işe yaramadığını sanırdı.
    /// </summary>
    private async Task<bool> GeriAl<T>(DbSet<T> tablo, int id) where T : class, IAuditableEntity
    {
        var kayit = await tablo.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => EF.Property<int>(x, "Id") == id);

        if (kayit is null || !kayit.IsDeleted)
        {
            return false;   // yok ya da zaten silinmemiş
        }

        kayit.IsDeleted = false;
        kayit.IsActive = true;
        await _context.SaveChangesAsync();
        return true;
    }
}
