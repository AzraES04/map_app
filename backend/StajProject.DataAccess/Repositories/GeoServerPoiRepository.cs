using System.Globalization;
using NetTopologySuite.Geometries;
using StajProject.DataAccess.GeoServer;
using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// Ödev 13 / Madde 1 — POI OKUMASI GeoServer'dan, YAZMA veritabanından.
///
/// Ödev 8'de çizim tabloları için kurulan desenin POI'ye uygulanmış hâli
/// (bkz. <see cref="GeoServerGeometryRepository{TEntity}"/>): bu sınıf
/// <see cref="IPoiRepository"/>'yi gerçekliyor, listeleme/arama isteklerini
/// WFS'e gönderiyor, yazma işlemlerini içindeki EF Core deposuna devrediyor.
///
/// Üstteki katmanların hiçbiri değişmedi. <c>PoiService</c> hâlâ
/// <c>IPoiRepository</c> görüyor; veri kaynağının değişmesi tek bir DI
/// kaydına bakıyor (bkz. DataAccessRegistration.AddGeoServer).
///
/// NEDEN AYRI BİR SINIF, GENERIC OLAN KULLANILMADI?
/// <c>GeoServerGeometryRepository&lt;T&gt;</c> <c>GeometryEntityBase</c>
/// türevlerine bağlı: name/description/color kolonlarını okuyor, sahiplik
/// süzgecini <c>inserted_user_id</c> üzerinden kuruyor. POI'de o kolonların
/// hiçbiri yok; kategori, mesai ve <c>user_id</c> var. Ortak bir taban
/// uydurmak iki tablonun benzemediği bir yerde benzerlik iddia etmek olurdu.
///
/// SAHİPLİK SÜZGECİ BURADA DA YOK: POI ortak referans verisidir, giriş yapan
/// herkes hepsini görür (gerekçe: <c>IPoiService</c>). Süzgeç yalnızca
/// <c>GetAllAsync(userId)</c> açıkça istendiğinde kuruluyor.
/// </summary>
public class GeoServerPoiRepository : IPoiRepository
{
    private readonly IGeoServerClient _geoServer;

    /// <summary>
    /// Yazma işlemlerinin devredildiği depo — üretimde EF Core'lu
    /// <see cref="PoiRepository"/>, testlerde bellekteki sahtesi.
    /// </summary>
    private readonly IPoiRepository _yazmaDeposu;

    public GeoServerPoiRepository(IGeoServerClient geoServer, IPoiRepository yazmaDeposu)
    {
        _geoServer = geoServer;
        _yazmaDeposu = yazmaDeposu;
    }

    // =====================================================================
    //  OKUMA — GeoServer WFS
    // =====================================================================

    public async Task<List<Poi>> GetAllAsync(int? userId = null)
    {
        var features = await _geoServer.OzellikGetirAsync(
            GeoServerKatmanlari.Poi,
            cqlFilter: userId is null
                ? null
                : $"user_id = {userId.Value.ToString(CultureInfo.InvariantCulture)}",
            siralama: "created_date D");   // D = descending, EF'teki OrderByDescending

        return features.Select(Cevir).ToList();
    }

    public async Task<Poi?> GetByIdAsync(int id)
    {
        // Id sayı olduğu için doğrudan gömmek güvenli; metin olsaydı
        // CQL enjeksiyonuna karşı tırnak kaçırmak gerekirdi.
        var features = await _geoServer.OzellikGetirAsync(
            GeoServerKatmanlari.Poi,
            cqlFilter: $"id = {id.ToString(CultureInfo.InvariantCulture)}");

        return features.Count == 0 ? null : Cevir(features[0]);
    }

    /// <summary>
    /// Arama (Ödev 13 / Madde 2) — süzgeç GeoServer'da çalışıyor.
    ///
    /// <c>ILIKE</c>, ECQL'in büyük/küçük harf gözetmeyen LIKE'ıdır ve
    /// GeoServer bunu doğrudan PostGIS sorgusuna çeviriyor: eşleşmeyen kayıt
    /// ne GeoServer'dan çıkıyor ne de ağdan geçiyor. EF gerçeklemesindeki
    /// <c>EF.Functions.ILike</c> ile aynı işi yapıyor — iki yol farklı
    /// teknolojiyle aynı sözleşmeyi karşılıyor.
    ///
    /// maxFeatures yerine <c>Take</c>: WFS'in kendi sınırı da var ama
    /// <see cref="IGeoServerClient"/> sözleşmesinde böyle bir parametre yok;
    /// sıralamayı da (kısa ad önce) burada, EF gerçeklemesiyle AYNI kuralla
    /// uyguluyoruz ki iki kaynak aynı sırayı üretsin.
    /// </summary>
    public async Task<List<Poi>> AraAsync(string sorgu, int enFazla)
    {
        var features = await _geoServer.OzellikGetirAsync(
            GeoServerKatmanlari.Poi,
            cqlFilter: $"isim ILIKE '%{CqlKacir(sorgu)}%'",
            siralama: "isim A");

        return features
            .Select(Cevir)
            .OrderBy(p => p.Isim.Length)
            .ThenBy(p => p.Isim, StringComparer.CurrentCulture)
            .Take(enFazla)
            .ToList();
    }

    /// <summary>
    /// Kullanıcının yazdığı metni CQL metin sabitine güvenle gömer.
    ///
    /// İki şey yapılıyor:
    ///   • Tek tırnak ikileniyor — CQL'de metin kaçış kuralı budur; olmasaydı
    ///     "Ali'nin Yeri" araması süzgeci ortadan bölerdi.
    ///   • LIKE jokerleri (% ve _) siliniyor. ECQL'de bunları kaçırmak için
    ///     standart bir ESCAPE ifadesi yok; kullanıcı "%" yazdığında bütün
    ///     tabloyu getirmemesi için jokerleri metinden çıkarmak en sağlam yol.
    ///     Yer adlarında bu iki karakter pratikte geçmiyor.
    /// </summary>
    private static string CqlKacir(string metin)
        => metin.Replace("'", "''").Replace("%", string.Empty).Replace("_", string.Empty);

    /// <summary>
    /// WFS kaydını POI entity'sine çevirir.
    ///
    /// Kolon adları <c>vw_poi</c> görünümündekilerle aynı (bkz.
    /// geoserver/gs-yapilandir.ps1). Görünüm, tabloda olmayan iki kolonu da
    /// taşıyor: <c>kategori_adi</c> (SLD stilleri için) ve
    /// <c>kullanici_adi</c> (admin listesindeki "ekleyen" sütunu için).
    /// İkincisi olmasaydı GeoServer'dan okunan POI'lerde ekleyen kullanıcı
    /// boş görünürdü — WFS bir JOIN yapamaz, JOIN'i görünümün kendisi yapıyor.
    /// </summary>
    private static Poi Cevir(GeoServerFeature feature)
    {
        if (feature.Geometry is not Point nokta)
        {
            throw new GeoServerErisimException(
                $"{GeoServerKatmanlari.Poi} katmanında id={feature.Id} kaydının geometrisi " +
                $"nokta değil: {feature.Geometry?.GeometryType ?? "boş"}. " +
                "Katman doğru tabloya mı bağlı?");
        }

        var kullaniciId = feature.Tamsayi("user_id");
        var kullaniciAdi = feature.Metin("kullanici_adi");

        return new Poi
        {
            Id = feature.Id,
            Isim = feature.Metin("isim") ?? string.Empty,
            KategoriId = feature.Tamsayi("kategori_id") ?? 0,
            MesaiSaatleri = feature.Metin("mesai_saatleri"),
            MesaiPlani = feature.Metin("mesai_plani"),
            Geom = nokta,
            UserId = kullaniciId,
            User = kullaniciId is null || kullaniciAdi is null
                ? null
                : new User { Id = kullaniciId.Value, Username = kullaniciAdi },
            CreatedDate = feature.Tarih("created_date") ?? default,
            ModifiedDate = feature.Tarih("modified_date"),
            IsActive = feature.Mantiksal("is_active", varsayilan: true),
            // Görünüm yalnızca silinmemiş kayıtları yayınlıyor (WHERE is_deleted
            // = false), o yüzden buraya gelen her kayıt tanım gereği yaşıyor.
            IsDeleted = false,
        };
    }

    // =====================================================================
    //  YAZMA — EF Core / PostGIS (değişmeden devrediliyor)
    // =====================================================================

    /// <summary>
    /// Alan içindeki POI'ler — VERİTABANINDAN, GeoServer'dan değil (Ödev 14).
    ///
    /// Bilinçli istisna, gerekçesi somut bir hata: WFS 1.1.0/2.0.0'da
    /// CQL_FILTER'a yazılan geometri EPSG:4326'nın resmî eksen sırasıyla
    /// (enlem, boylam) okunuyor, bizim WKT'lerimiz ise (boylam, enlem).
    /// Sorgu HATA VERMİYOR, sessizce 0 sonuç dönüyor — yani analiz "bu alanda
    /// hiç POI yok" diye tamamen inandırıcı bir yanlış cevap üretirdi.
    /// (Aynı tuzak Ödev 8'de yaşandı; kesişim analizi bu yüzden WFS 1.0.0
    /// kullanıyor.)
    ///
    /// Buradaki sorgu ise il birleşimi gibi ON BİNLERCE köşeli bir geometriyi
    /// süzgeç olarak taşıyor: onu URL'e sığdırmak da ayrı bir sorun.
    /// Analizin doğruluğu, okuma yolunun tekliğinden daha önemli — üstelik
    /// aynı gerekçeyle <see cref="GetCountsByCategoryAsync"/> de veritabanına
    /// gidiyor.
    /// </summary>
    public Task<List<Poi>> AlandakileriGetirAsync(Geometry alan)
        => _yazmaDeposu.AlandakileriGetirAsync(alan);

    public Task<Poi> AddAsync(Poi poi) => _yazmaDeposu.AddAsync(poi);

    public Task TopluEkleAsync(IEnumerable<Poi> poiler) => _yazmaDeposu.TopluEkleAsync(poiler);

    public Task<Poi?> UpdateAsync(Poi poi) => _yazmaDeposu.UpdateAsync(poi);

    public Task<bool> SoftDeleteAsync(int id) => _yazmaDeposu.SoftDeleteAsync(id);

    public Task<bool> RestoreAsync(int id) => _yazmaDeposu.RestoreAsync(id);

    public Task<bool> SetActiveAsync(int id, bool isActive) => _yazmaDeposu.SetActiveAsync(id, isActive);

    /// <summary>
    /// Kategori başına POI sayısı — VERİTABANINDAN.
    ///
    /// Bilinçli istisna: bu sayı haritada gösterilmiyor, "dolu kategori
    /// silinemez" kuralını besliyor. Kural bir YAZMA kararı olduğu için
    /// kaynağı da yazma tarafı olmalı; GeoServer'ın önbelleği bir an geride
    /// kalsa silinmemesi gereken bir kategori silinebilirdi.
    /// </summary>
    public Task<Dictionary<int, int>> GetCountsByCategoryAsync()
        => _yazmaDeposu.GetCountsByCategoryAsync();
}
