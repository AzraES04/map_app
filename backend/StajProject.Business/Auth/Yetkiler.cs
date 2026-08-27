namespace StajProject.Business.Auth;

/// <summary>
/// Sistemin tanıdığı yetki ADLARI (Ödev 6 / Madde 2).
///
/// Yetkilerin kendisi VERİDİR — permissions tablosunda durur, yönetici
/// ekrandan role/kullanıcıya dağıtır. Ama kodun bir yerde "şu yetkiyi arıyorum"
/// diyebilmesi için ada ihtiyacı var. O adları elle string yazmak yerine burada
/// topluyoruz: yazım hatası derleme zamanında yakalanır, yeniden adlandırma
/// tek dosyadan yapılır.
///
/// Yeni bir yetki eklemek için: buraya sabit ekle + <c>Tumu</c> listesine yaz.
/// Seed açılışta eksik olanları permissions tablosuna ekler.
/// </summary>
public static class Yetkiler
{
    public const string NoktaEkleme = "Point Ekleme";
    public const string CizgiEkleme = "Line Ekleme";
    public const string PoligonEkleme = "Polygon Ekleme";
    public const string KayitGuncelleme = "Kayıt Güncelleme";
    public const string KayitSilme = "Kayıt Silme";
    public const string AnalizCalistirma = "Analiz Çalıştırma";
    public const string KullaniciYonetimi = "Kullanıcı Yönetimi";
    public const string RolYonetimi = "Rol Yönetimi";

    /// <summary>Ödev 7 / Madde 2: kullanıcı ve rollere çizim alanı tanımlama.</summary>
    public const string CografiYetkiTanimlama = "Coğrafi Yetki Tanımlama";

    /// <summary>
    /// Ödev 12: operatörün haritadan POI eklemesi (ve kendi POI'lerini düzenlemesi).
    /// </summary>
    public const string PoiEkleme = "POI Ekleme";

    /// <summary>
    /// Ödev 12: yönetim panelindeki "POI Yönetimi" ekranı — bütün POI'lerin
    /// listesi ve kategori sözlüğünün düzenlenmesi.
    ///
    /// <see cref="PoiEkleme"/>'den AYRI bir yetki: operatör POI girer ama
    /// kategori ağacını değiştiremez; kategoriler yöneticinin belirlediği
    /// ortak sözlüktür. Tek yetki olsaydı "POI ekleyebilen herkes kategori de
    /// açabilir" olurdu ve sözlük kısa sürede birbirinin eşi girdilerle dolardı.
    /// </summary>
    public const string PoiYonetimi = "POI Yönetimi";

    /// <summary>
    /// Ödev 16: haritadaki "Durak Ekle" aracı — ulaşım operatörünün işi.
    ///
    /// POI Ekleme'den AYRI bir yetki. Aynı yetkiye bağlasaydık, ulaşım
    /// operatörü kategori ağacına POI de ekleyebilirdi; ödev notu bunu
    /// açıkça istemiyor ("bu roldekiler POI ekleme işlemlerini yapmasınlar").
    /// </summary>
    public const string DurakEkleme = "Durak Ekleme";

    /// <summary>
    /// Ödev 16: güzergah tanımlama, düzenleme ve durak SIRALAMASINI değiştirme.
    ///
    /// <see cref="DurakEkleme"/>'den ayrı: bir operatör sahadan durak
    /// girebilir ama hattın kendisini (ad, renk, sıra) değiştirmek hat
    /// sorumlusunun işi. Aynı ayırım POI tarafında da var
    /// (POI Ekleme ↔ POI Yönetimi).
    /// </summary>
    public const string GuzergahYonetimi = "Güzergah Yönetimi";

    /// <summary>Seed'in kullandığı tanım listesi: ad + açıklama.</summary>
    public static readonly (string Ad, string Aciklama)[] Tumu =
    {
        (NoktaEkleme,      "Haritaya yeni nokta (POINT) çizebilir."),
        (CizgiEkleme,      "Haritaya yeni çizgi (LINESTRING) çizebilir."),
        (PoligonEkleme,    "Haritaya yeni alan (POLYGON) çizebilir."),
        (KayitGuncelleme,  "Var olan çizimlerin adını, rengini ve konumunu değiştirebilir."),
        (KayitSilme,       "Çizimleri silebilir (soft delete) ve geri alabilir."),
        (AnalizCalistirma, "Kesişim analizi çalıştırabilir."),
        (KullaniciYonetimi,"Yönetim panelinden kullanıcı ekleyebilir, güncelleyebilir, silebilir."),
        (RolYonetimi,      "Yönetim panelinden rol ve rol yetkilerini düzenleyebilir."),
        (CografiYetkiTanimlama, "Kullanıcı ve rollere haritadan çizim alanı tanımlayabilir."),
        (PoiEkleme,        "Haritaya POI (ilgi noktası) ekleyebilir, kendi eklediklerini düzenleyip silebilir."),
        (PoiYonetimi,      "Yönetim panelinden bütün POI'leri ve kategori ağacını yönetebilir."),
        (DurakEkleme,      "Haritaya durak (Point) ekleyebilir ve kendi eklediği durakları düzenleyip silebilir."),
        (GuzergahYonetimi, "Güzergah tanımlayabilir, düzenleyebilir ve durakların sırasını değiştirebilir."),
    };
}
