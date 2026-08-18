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
    };
}
