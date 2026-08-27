using StajProject.Business.DTOs;
using StajProject.Business.Geo;
using StajProject.DataAccess.Repositories;

namespace StajProject.Business.Services;

/// <summary>
/// İl / bölge sorguları (Ödev 10).
///
/// Bölge listesi VERİTABANINDAN sayılıyor, <see cref="Bolgeler"/> sabitinden
/// değil: tabloda o bölgeye ait il yoksa listede de görünmemeli. Sabitten
/// üretseydik, veri eksik yüklendiğinde arayüz seçilebilir ama boş sonuç veren
/// bir bölge gösterirdi.
/// </summary>
public class IlService : IIlService
{
    private readonly IIlRepository _repository;

    public IlService(IIlRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<IlOzetDto>> GetIllerAsync()
        => (await _repository.OzetGetirAsync())
            .Select(i => new IlOzetDto { Plaka = i.Id, Ad = i.Ad, Bolge = i.Bolge })
            .ToList();

    public async Task<List<IlSinirDto>> GetSinirlarAsync()
        => (await _repository.SinirlariGetirAsync())
            .Select(i => new IlSinirDto
            {
                Plaka = i.Id,
                Ad = i.Ad,
                Bolge = i.Bolge,
                Wkt = WktConverter.Write(i.Geom),
            })
            .ToList();

    public async Task<List<BolgeOzetDto>> GetBolgelerAsync()
    {
        var iller = await _repository.OzetGetirAsync();

        var sayilar = iller
            .GroupBy(i => i.Bolge)
            .ToDictionary(g => g.Key, g => g.Count());

        // Sıra Bolgeler.Tumu'ndan geliyor (kuzeybatıdan güneydoğuya).
        // Alfabetik sıralasaydık "Akdeniz, Doğu Anadolu, Ege..." gibi
        // coğrafyayla ilgisiz bir dizilim çıkardı.
        return Bolgeler.Tumu
            .Where(sayilar.ContainsKey)
            .Select(ad => new BolgeOzetDto { Ad = ad, IlSayisi = sayilar[ad] })
            .ToList();
    }
}
