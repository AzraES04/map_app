using StajProject.Business.DTOs;

namespace StajProject.Business.Services;

/// <summary>
/// OpenStreetMap'ten TURİSTİK POI içe aktarımı.
///
/// ---- NEDEN GEREKLİ? ----
/// Sistemdeki POI verisi Ödev 14 için üretilmiş sentetik bir veri seti:
/// 4140 kayıt, on kategori (eczane, okul, fırın, otel…) ve isimler şablondan
/// ("Alparslan Kafe"). Analiz için yeterli — yoğunluk yüzeyi gerçek isim
/// istemiyor — ama TUR için değil: müze, anıt, tarihi yer, park kategorileri
/// hiç yok ve olsa bile isimler gerçek değil.
///
/// Bu servis bir kereye mahsus çalışıp gerçek turistik mekanları
/// (Anıtkabir, Ayasofya, Ankara Kalesi…) kendi <c>poi</c> tablomuza yazıyor.
///
/// ---- NEDEN AÇILIŞTA DEĞİL, İSTEK ÜZERİNE? ----
/// Aktarım dış bir servise (Overpass) gidiyor. Uygulamanın AÇILIŞINI dış bir
/// servise bağlamak, o servis yavaşladığında projenin hiç açılmaması demek —
/// bu tuzağa GeoServer'la bir kez düşüldü (bkz. DataAccessRegistration →
/// VeritabaniDeposu). Bu yüzden aktarım yönetim panelinden tetikleniyor.
///
/// ---- TEKRAR ÇALIŞTIRILABİLİR ----
/// Aynı şehir iki kez aktarılırsa kayıtlar İKİYE KATLANMIYOR: aynı adla
/// yakında (100 m) duran bir POI varsa o kayıt atlanıyor. Tekrar çalıştırmak
/// yalnızca eksikleri tamamlıyor.
/// </summary>
public interface ITuristikPoiAktarici
{
    /// <summary>
    /// Verilen illerin turistik mekanlarını içe aktarır.
    ///
    /// Kategoriler yoksa oluşturulur (Gezilecek Yer → Müze / Tarihi Yer /
    /// Park / Seyir Noktası / İbadet Yeri).
    ///
    /// Dış servise ulaşılamazsa <see cref="Validation.DisServisException"/>
    /// fırlatır; geçersiz plakada <see cref="Validation.IsKuraliException"/>.
    /// </summary>
    Task<TuristikAktarimSonucuDto> AktarAsync(
        IReadOnlyList<int> ilPlakalari,
        CancellationToken iptal = default);
}
