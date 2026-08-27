using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// POI iş mantığı (Ödev 12).
///
/// SAHİPLİK KURALI, geometri tablolarındakinden BİLEREK farklı:
/// çizimler kişiseldir (Ödev 5: herkes yalnızca kendi çizimini görür), POI ise
/// ORTAK REFERANS VERİSİDİR. Bir restoranın konumu onu giren operatöre ait
/// değildir; giriş yapan herkes görmelidir. Bu yüzden LİSTELEME süzülmüyor.
///
/// Sahiplik yalnızca DEĞİŞTİRME tarafında iş görüyor: bir POI'yi ekleyen
/// operatör kendi kaydını düzenleyip silebilir, başkasınınkine dokunamaz —
/// "POI Yönetimi" yetkisi olan (yönetici) ise hepsine dokunabilir.
/// </summary>
public interface IPoiService
{
    /// <summary>Tüm POI'ler — kategori yolu ve ekleyen kullanıcı adıyla.</summary>
    Task<List<PoiDto>> GetAllAsync();

    /// <summary>Tek POI; yoksa null.</summary>
    Task<PoiDto?> GetByIdAsync(int id);

    /// <summary>
    /// Arama barının sonuçları (Ödev 13 / Madde 2). Sorgu çok kısaysa boş liste.
    /// Yetki istemez — listeleme gibi bu da herkese açık.
    /// </summary>
    Task<List<PoiAramaSonucuDto>> AraAsync(string? sorgu, int enFazla = 8);

    /// <summary>
    /// Haritadan seçilen / aranan yer için kategori önerir (Ödev 13 / Madde 4).
    /// Karşılığı olan bir kategori bulunamazsa null.
    /// </summary>
    Task<KategoriOneriDto?> KategoriOnerAsync(string? tur, string? sinif, string? isim);

    /// <summary>
    /// Bir yılın resmî tatilleri (Ödev 13 / Madde 3). Yıl verilmezse içinde
    /// bulunulan yıl. Veritabanına bakmadığı için async değil.
    /// </summary>
    ResmiTatilListesiDto ResmiTatilleriGetir(int? yil);

    /// <summary>
    /// Yeni POI. Kategori doğrulanır, konum coğrafi yetki alanına göre
    /// denetlenir (Ödev 7) ve kayıt giriş yapan kullanıcıya bağlanır.
    /// </summary>
    Task<PoiDto> CreateAsync(PoiCreateDto dto);

    /// <summary>Kayıt yoksa null. Yetki yoksa iş kuralı hatası fırlatır.</summary>
    Task<PoiDto?> UpdateAsync(int id, PoiUpdateDto dto);

    /// <summary>Soft delete. Kayıt yoksa false, yetki yoksa iş kuralı hatası.</summary>
    Task<bool> DeleteAsync(int id);

    /// <summary>Silinen POI'yi geri getirir.</summary>
    Task<bool> RestoreAsync(int id);

    /// <summary>Kaydı askıya alır / yeniden aktif eder (is_active).</summary>
    Task<bool> SetActiveAsync(int id, bool isActive);
}
