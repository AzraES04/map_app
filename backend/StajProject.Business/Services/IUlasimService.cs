using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// Akıllı ulaşım modülünün iş mantığı (Ödev 16).
///
/// SAHİPLİK KURALI POI ile aynı: güzergah ve durak ORTAK REFERANS VERİSİDİR —
/// bir hattın nereden geçtiği onu giren operatöre ait değildir, giriş yapan
/// herkes görmelidir. Bu yüzden LİSTELEME süzülmüyor ve yetki istemiyor.
///
/// Sahiplik yalnızca DEĞİŞTİRME tarafında iş görüyor: bir durağı ekleyen
/// operatör kendi kaydını düzenleyip silebilir, başkasınınkine dokunamaz —
/// "Güzergah Yönetimi" yetkisi olan (hat sorumlusu / admin) ise hepsine
/// dokunabilir.
/// </summary>
public interface IUlasimService
{
    // ---------- Güzergah ----------

    /// <summary>Bütün güzergahlar, durakları sıralı hâlde.</summary>
    Task<List<GuzergahDto>> GuzergahlariGetirAsync();

    /// <summary>Tek güzergah; yoksa null.</summary>
    Task<GuzergahDto?> GuzergahGetirAsync(int id);

    /// <summary>Yeni güzergah. "Güzergah Yönetimi" yetkisi ister.</summary>
    Task<GuzergahDto> GuzergahEkleAsync(GuzergahSaveDto dto);

    /// <summary>Kayıt yoksa null. Ad/renk/açıklama/aktiflik günceller.</summary>
    Task<GuzergahDto?> GuzergahGuncelleAsync(int id, GuzergahSaveDto dto);

    /// <summary>
    /// Soft delete. DURAĞI OLAN güzergah silinemez — iş kuralı hatası fırlatır;
    /// alt ağacı sessizce götürmek yerine kararı kullanıcıya bırakıyoruz
    /// (PoiCategoryService.DeleteAsync ile aynı kural).
    /// </summary>
    Task<bool> GuzergahSilAsync(int id);

    // ---------- Durak ----------

    /// <summary>Bütün duraklar (güzergah ve sıra bilgisiyle).</summary>
    Task<List<DurakDto>> DuraklariGetirAsync();

    Task<DurakDto?> DurakGetirAsync(int id);

    /// <summary>
    /// Yeni durak. Güzergah doğrulanır, konum coğrafi yetki alanına göre
    /// denetlenir (Ödev 7) ve kayıt giriş yapan kullanıcıya bağlanır.
    /// Sıra verilmezse durak SONA eklenir.
    /// </summary>
    Task<DurakDto> DurakEkleAsync(DurakCreateDto dto);

    Task<DurakDto?> DurakGuncelleAsync(int id, DurakUpdateDto dto);

    /// <summary>Soft delete; kalan durakların sırası 1..N olacak şekilde sıkıştırılır.</summary>
    Task<bool> DurakSilAsync(int id);

    /// <summary>
    /// Sürükle-bırak sıralamasını uygular (Ödev 16 / Madde 2).
    /// "Güzergah Yönetimi" yetkisi ister; güncel güzergahı döndürür.
    /// </summary>
    Task<GuzergahDto?> SiralamaGuncelleAsync(int guzergahId, DurakSiralamaDto dto);
}
