using Microsoft.EntityFrameworkCore;
using StajProject.DataAccess.Context;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

public class PoiCategoryRepository : IPoiCategoryRepository
{
    private readonly AppDbContext _context;

    public PoiCategoryRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<List<PoiCategory>> GetAllAsync()
        => _context.PoiKategorileri
            .AsNoTracking()
            .OrderBy(k => k.Ad)
            .ToListAsync();

    public Task<PoiCategory?> GetByIdAsync(int id)
        => _context.PoiKategorileri.AsNoTracking().FirstOrDefaultAsync(k => k.Id == id);

    public Task<PoiCategory?> GetByNameAsync(string ad, int? parentId)
        => _context.PoiKategorileri
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.Ad == ad && k.ParentId == parentId);

    public async Task<PoiCategory> AddAsync(PoiCategory kategori)
    {
        _context.PoiKategorileri.Add(kategori);
        await _context.SaveChangesAsync();
        return kategori;
    }

    public async Task<PoiCategory?> UpdateAsync(PoiCategory kategori)
    {
        var mevcut = await _context.PoiKategorileri.FirstOrDefaultAsync(k => k.Id == kategori.Id);
        if (mevcut is null)
        {
            return null;
        }

        mevcut.Ad = kategori.Ad;
        mevcut.Aciklama = kategori.Aciklama;
        mevcut.Ikon = kategori.Ikon;
        mevcut.ParentId = kategori.ParentId;
        mevcut.IsActive = kategori.IsActive;

        await _context.SaveChangesAsync();   // ModifiedDate otomatik damgalanır
        return mevcut;
    }

    public async Task<bool> SoftDeleteAsync(int id)
    {
        var kategori = await _context.PoiKategorileri.FirstOrDefaultAsync(k => k.Id == id);
        if (kategori is null)
        {
            return false;
        }

        kategori.IsDeleted = true;
        kategori.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }
}
