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
    ///
    /// Ödev 17: sıra değiştiği için rota da OTOMATİK yenileniyor.
    /// </summary>
    Task<GuzergahDto?> SiralamaGuncelleAsync(int guzergahId, DurakSiralamaDto dto);

    /// <summary>
    /// Ödev 17 / Madde 1 — "Rota Oluştur": güzergahın rotasını OSRM'den
    /// hesaplatıp veritabanına yazar.
    ///
    /// Güzergah yoksa null. OSRM kapalıysa, durak sayısı 2'nin altındaysa ya
    /// da rota hesaplanamadıysa <see cref="Validation.IsKuraliException"/>
    /// fırlatır — kullanıcı düğmeye bastı, sebebini görmeli.
    ///
    /// (Otomatik yenileme aynı işi SESSİZCE yapıyor: orada kullanıcı başka
    /// bir iş yapıyordu ve OSRM'in arızası o işi engellememeli.)
    /// </summary>
    Task<GuzergahDto?> RotaHesaplaAsync(
        int guzergahId,
        IReadOnlyList<RotaViaDto>? viaNoktalar = null);

    /// <summary>
    /// Ödev 18 — seçilen alternatifle hattın TAMAMININ nasıl olacağını
    /// gösterir; hiçbir şey kaydetmez.
    ///
    /// Kullanıcı haritada alternatifler arasında gezinirken çağrılıyor.
    /// Yetki istemiyor: veriyi değiştirmiyor. Kalıcı hâle getirmek
    /// <see cref="RotaHesaplaAsync"/> ve "Güzergah Yönetimi" istiyor.
    /// </summary>
    Task<RotaOnizlemeDto?> RotaOnizleAsync(
        int guzergahId,
        IReadOnlyList<RotaViaDto>? viaNoktalar = null);

    /// <summary>
    /// Ödev 18 — seçilen durağa GİDEN yolların alternatifleri.
    ///
    /// Bacak = bir önceki durak → seçilen durak. Durak yoksa null; alternatif
    /// üretilemediyse liste boş ve <c>Mesaj</c> sebebini söylüyor (hattın ilk
    /// durağı / OSRM kapalı / tek makul yol var — üçü de farklı durumlar).
    /// </summary>
    Task<RotaAlternatifleriDto?> DurakAlternatifleriAsync(int durakId);

    // ---------- Ödev 19: araç simülasyonu ----------

    /// <summary>
    /// Bir güzergahta araç simülasyonunu başlatır (Ödev 19 / Madde 1).
    ///
    /// "Simülasyon Başlatma" yetkisi ister (uçta kontrol ediliyor).
    /// Güzergah yoksa null; iki duraktan azsa iş kuralı hatası.
    ///
    /// Araç, hattın KAYITLI ROTASINI izliyor; rota yoksa duraklardan geçen
    /// düz çizgiyi. Simülasyonu OSRM'e bağlamadık: rota motoru kapalıyken de
    /// modülün gösterilebilir olması gerekiyor.
    /// </summary>
    Task<SimulasyonDurumDto?> SimulasyonBaslatAsync(int guzergahId);

    /// <summary>
    /// Simülasyonu durdurur. Çalışan simülasyon yoksa false.
    /// Başlatmakla aynı yetkiyi ister — durdurmak da bir MÜDAHALEDİR ve
    /// takipçilerin ekranındaki aracı kaldırır.
    /// </summary>
    bool SimulasyonDurdur(int guzergahId);

    /// <summary>
    /// Şu an çalışan bütün simülasyonlar.
    ///
    /// Haritayı YENİ AÇAN istemci için: SignalR yalnızca bundan SONRAKİ
    /// güncellemeleri gönderir, o an yolda olan araçları bilmez. Bu uç
    /// olmasaydı, kullanıcı sayfayı yenilediğinde çalışan simülasyon
    /// bir sonraki tike kadar görünmez olurdu.
    /// </summary>
    IReadOnlyList<SimulasyonDurumDto> AktifSimulasyonlar();
}
