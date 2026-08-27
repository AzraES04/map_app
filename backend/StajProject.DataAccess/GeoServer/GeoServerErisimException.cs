namespace StajProject.DataAccess.GeoServer;

/// <summary>
/// GeoServer'a ulaşılamadığında veya GeoServer hata döndüğünde fırlatılır.
///
/// Neden ayrı bir tip? Bu hata ne istemcinin verisiyle ilgilidir (400 değil)
/// ne de bizim kodumuzun çökmesidir (500 yanıltıcı olur). Doğru karşılığı
/// <b>503 Service Unavailable</b>: "bağımlı olduğum bir servis şu an yok".
/// Kullanıcı mesajı da ona göre yazılıyor — "GeoServer'ı başlat" diyebilmek
/// için hatanın türünü bilmemiz gerekiyor.
///
/// Aynı ayrım projede zaten var: WktFormatException → 400,
/// IsKuraliException → 400, bu → 503.
/// </summary>
public class GeoServerErisimException : Exception
{
    public GeoServerErisimException(string message) : base(message) { }
    public GeoServerErisimException(string message, Exception inner) : base(message, inner) { }
}
