using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

/// <summary>
/// Ulaşım modülünün veri erişim sözleşmesi (Ödev 16).
///
/// GÜZERGAH VE DURAK TEK ARAYÜZDE. Projede genelde her tablo kendi
/// repository'sini alıyor (IPoiRepository, IIlRepository…) ama burada ikisi
/// ayrılamıyor: sürükle-bırak sıralaması ve "güzergahı sil" kuralı iki
/// tabloya birden dokunuyor ve TEK işlemde tamamlanmalı. İki ayrı repository
/// olsaydı çağıran taraf işlemi kendisi yönetmek zorunda kalırdı — yani
/// veri katmanının sorumluluğu servise sızardı.
///
/// GEOSERVER YOK, doğrudan EF/PostGIS. Ödev 8'den beri listeleme GeoServer'a
/// gidiyor ama duraklar için bilinçli bir istisna:
///   • Durak listesi sürekli DEĞİŞİYOR (sürükle-bırak her taşımada yazıyor);
///     WMS önbelleği bir adım geride kalırsa kullanıcı kendi taşıdığı durağı
///     eski yerinde görürdü.
///   • Güzergah çizgisi duraklardan TÜRETİLİYOR ve istemcide çiziliyor;
///     bunun için koordinatlar gerekiyor, hazır bir resim değil.
/// Aynı gerekçeyle konum analizi de PostGIS'e gidiyor (bkz. Ödev 14).
/// </summary>
public interface IUlasimRepository
{
    // ---------- Güzergah ----------

    /// <summary>
    /// Bütün güzergahlar — durakları SIRALI, ekleyen kullanıcı dahil.
    /// Sahiplik süzgeci YOK: hatlar ortak referans verisidir, giriş yapan
    /// herkes hepsini görür (POI ile aynı kural).
    /// </summary>
    Task<List<Guzergah>> GuzergahlariGetirAsync();

    /// <summary>Tek güzergah, durakları sıralı; yoksa null.</summary>
    Task<Guzergah?> GuzergahGetirAsync(int id);

    Task<Guzergah> GuzergahEkleAsync(Guzergah guzergah);

    /// <summary>Ad / renk / açıklama / aktiflik günceller; kayıt yoksa null.</summary>
    Task<Guzergah?> GuzergahGuncelleAsync(Guzergah guzergah);

    /// <summary>Soft delete. Kayıt yoksa false.</summary>
    Task<bool> GuzergahSilAsync(int id);

    // ---------- Durak ----------

    /// <summary>Bütün duraklar — güzergah ve kullanıcı bilgisiyle, sıralı.</summary>
    Task<List<Durak>> DuraklariGetirAsync();

    Task<Durak?> DurakGetirAsync(int id);

    Task<Durak> DurakEkleAsync(Durak durak);

    Task<Durak?> DurakGuncelleAsync(Durak durak);

    Task<bool> DurakSilAsync(int id);

    /// <summary>
    /// Bir güzergahın duraklarının sırasını TEK İŞLEMDE yazar.
    /// </summary>
    /// <param name="guzergahId">Sırası yazılacak güzergah.</param>
    /// <param name="siraliIdler">Durak id'leri, yeni sırasıyla; 1..N yazılır.</param>
    /// <returns>Yazılan durak sayısı.</returns>
    Task<int> SiralariYazAsync(int guzergahId, IReadOnlyList<int> siraliIdler);

    /// <summary>
    /// Güzergahtaki en büyük sıra numarası; hiç durak yoksa 0.
    /// Yeni durağı sona eklemek için kullanılıyor.
    /// </summary>
    Task<int> SonSiraAsync(int guzergahId);
}
