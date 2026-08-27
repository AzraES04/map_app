using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using StajProject.DataAccess.Context;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

public class PoiRepository : IPoiRepository
{
    private readonly AppDbContext _context;

    public PoiRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Ekleyen kullanıcıyı da getiren temel sorgu.
    ///
    /// Kategori burada Include EDİLMİYOR. Sebep: DTO'da kategorinin TAM YOLU
    /// ("Yeme-İçme › Restoran") gösteriliyor ve o yol için ata zincirinin
    /// tamamı gerekiyor. ThenInclude ile zinciri kovalamak derinlik kadar
    /// JOIN demek olurdu; servis katmanı kategori listesini bir kez okuyup
    /// yolu bellekte kuruyor (bkz. PoiService.YollariHesapla).
    /// </summary>
    private IQueryable<Poi> Sorgu()
        => _context.Poiler.AsNoTracking().Include(p => p.User);

    public async Task<List<Poi>> GetAllAsync(int? userId = null)
    {
        var sorgu = Sorgu();
        if (userId is not null)
        {
            sorgu = sorgu.Where(p => p.UserId == userId);
        }

        return await sorgu.OrderByDescending(p => p.CreatedDate).ToListAsync();
    }

    public Task<Poi?> GetByIdAsync(int id)
        => Sorgu().FirstOrDefaultAsync(p => p.Id == id);

    /// <summary>
    /// Ada göre arama (Ödev 13 / Madde 2).
    ///
    /// <c>EF.Functions.ILike</c> PostgreSQL'in <c>ILIKE</c> operatörüne
    /// çevriliyor: LIKE'ın büyük/küçük harf gözetmeyen hâli. Alternatif
    /// <c>ToLower().Contains(...)</c> yazmaktı; o da SQL'e çevrilirdi ama
    /// Türkçe harflerde .NET ile PostgreSQL'in küçültme kuralları ayrışabilir
    /// (klasik "I / ı" sorunu). ILIKE karşılaştırmayı tamamen veritabanının
    /// harmanlama (collation) kurallarına bırakıyor.
    ///
    /// Aktif olmayan POI'ler de dönüyor: pasif bir kayıt silinmiş değildir,
    /// haritada durur — kullanıcının onu arayıp bulamaması kafa karıştırırdı.
    /// Arayüz sonuç satırında "Pasif" rozetini gösteriyor.
    /// </summary>
    public Task<List<Poi>> AraAsync(string sorgu, int enFazla)
    {
        // Kullanıcının yazdığı metin doğrudan LIKE kalıbına giriyor; "%" ve "_"
        // karakterleri orada joker anlamına geldiği için kaçırılmalı, yoksa
        // "%" yazan biri bütün tabloyu getirir.
        var kalip = "%" + sorgu.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

        return Sorgu()
            .Where(p => EF.Functions.ILike(p.Isim, kalip, "\\"))
            // Kısa adlar önce: "Kafe" araması "Kafe Nero"yu, uzun bir adın
            // ortasında "kafe" geçen kayıttan önce göstersin.
            .OrderBy(p => p.Isim.Length)
            .ThenBy(p => p.Isim)
            .Take(enFazla)
            .ToListAsync();
    }

    /// <summary>
    /// Alan içindeki POI'ler (Ödev 14).
    ///
    /// <c>alan.Intersects(p.Geom)</c> ifadesini Npgsql'in NetTopologySuite
    /// eklentisi <c>ST_Intersects(@alan, geom)</c>'a çeviriyor: karşılaştırma
    /// PostGIS'te yapılıyor ve geom kolonundaki gist
    /// indeksi kullanılabiliyor. Parametre olarak giden geometrinin SRID'si 4326
    /// olmak zorunda — kolonun SRID'siyle uyuşmazsa PostGIS hata verir
    /// (WktConverter zaten 4326 damgalıyor).
    ///
    /// Pasif (<c>is_active = false</c>) kayıtlar DIŞARIDA: askıya alınmış bir
    /// POI haritada duruyor olabilir ama "burada bir eczane var" demek için
    /// dayanak sayılmamalı. Silinmişler zaten global query filter ile eleniyor.
    /// </summary>
    public Task<List<Poi>> AlandakileriGetirAsync(Geometry alan)
        => _context.Poiler
            .AsNoTracking()
            .Where(p => p.IsActive && alan.Intersects(p.Geom))
            .ToListAsync();

    public async Task<Poi> AddAsync(Poi poi)
    {
        _context.Poiler.Add(poi);
        await _context.SaveChangesAsync();
        return poi;
    }

    /// <summary>
    /// Toplu ekleme — tek SaveChanges, tek işlem (transaction).
    ///
    /// EF Core <c>AddRange</c> ile eklenen satırları toplu INSERT'lere
    /// paketliyor. Analiz veri setinin binlerce satırı böylece saniyeler
    /// yerine milisaniyelerle ölçülen sürede yazılıyor.
    /// </summary>
    public async Task TopluEkleAsync(IEnumerable<Poi> poiler)
    {
        _context.Poiler.AddRange(poiler);
        await _context.SaveChangesAsync();
    }

    public async Task<Poi?> UpdateAsync(Poi poi)
    {
        // AsNoTracking YOK: değiştirip kaydedeceğiz.
        var mevcut = await _context.Poiler.FirstOrDefaultAsync(p => p.Id == poi.Id);
        if (mevcut is null)
        {
            return null;
        }

        mevcut.Isim = poi.Isim;
        mevcut.KategoriId = poi.KategoriId;
        mevcut.MesaiSaatleri = poi.MesaiSaatleri;
        // Plan ve özet metin BİRLİKTE yazılıyor: biri güncellenip diğeri
        // eski kalırsa kayıt kendi kendisiyle çelişirdi (bkz. Poi.MesaiPlani).
        mevcut.MesaiPlani = poi.MesaiPlani;
        mevcut.Geom = poi.Geom;

        // ModifiedDate'i elle yazmıyoruz — AppDbContext.ApplyAuditRules() basıyor.
        await _context.SaveChangesAsync();

        // Ekleyen kullanıcı bilgisi güncel nesnede yok; DTO'ya çevirirken
        // gerektiği için yeniden okuyoruz.
        return await GetByIdAsync(mevcut.Id);
    }

    public async Task<bool> SoftDeleteAsync(int id)
    {
        var poi = await _context.Poiler.FirstOrDefaultAsync(p => p.Id == id);
        if (poi is null)
        {
            return false;
        }

        poi.IsDeleted = true;
        poi.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(int id)
    {
        // Global query filter silinmiş kaydı GİZLİYOR; geri almak için
        // filtreyi bilinçli olarak aşıyoruz (bkz. GeometryRepository.RestoreAsync).
        var poi = await _context.Poiler
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (poi is null || !poi.IsDeleted)
        {
            return false;
        }

        poi.IsDeleted = false;
        poi.IsActive = true;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetActiveAsync(int id, bool isActive)
    {
        var poi = await _context.Poiler.FirstOrDefaultAsync(p => p.Id == id);
        if (poi is null)
        {
            return false;
        }

        poi.IsActive = isActive;
        await _context.SaveChangesAsync();
        return true;
    }

    public Task<Dictionary<int, int>> GetCountsByCategoryAsync()
        => _context.Poiler
            .GroupBy(p => p.KategoriId)
            .Select(g => new { KategoriId = g.Key, Sayi = g.Count() })
            .ToDictionaryAsync(x => x.KategoriId, x => x.Sayi);
}
