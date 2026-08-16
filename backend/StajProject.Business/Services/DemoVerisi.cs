namespace StajProject.Business.Services;

/// <summary>
/// Uygulama ilk kez çalıştırıldığında yüklenecek örnek envanter.
///
/// Amaç: projeyi ilk açan kişi BOŞ bir harita değil, dolu ve gezilebilir bir
/// uygulama görsün; envanter analizi aracı da anında denenebilsin.
///
/// Koordinatlar EPSG:4326 (WGS84), WKT biçiminde — yani veritabanına gidecek
/// hâliyle. Sıra her zaman "boylam enlem"dir.
/// </summary>
internal static class DemoVerisi
{
    internal record Ornek(string Ad, string? Aciklama, string Renk, string Wkt);

    /// <summary>Noktalar → tbl_point</summary>
    internal static readonly Ornek[] Noktalar =
    {
        new("Anıtkabir", "Ankara · Atatürk'ün anıt mezarı",
            "#2e8fa8", "POINT (32.836563 39.925019)"),
        new("Ayasofya", "İstanbul · Fatih",
            "#d9553f", "POINT (28.980175 41.008583)"),
        new("Efes Antik Kenti", "İzmir · Selçuk",
            "#6a4c93", "POINT (27.341944 37.939722)"),
        new("Nemrut Dağı", "Adıyaman · Dev heykeller",
            "#e07b39", "POINT (38.741389 37.980833)"),
        new("Sümela Manastırı", "Trabzon · Maçka",
            "#57a05a", "POINT (39.658611 40.690278)"),
        new("Pamukkale Travertenleri", "Denizli",
            "#1f7a8c", "POINT (29.124722 37.920556)"),
        new("Van Kalesi", "Van",
            "#2e8fa8", "POINT (43.339722 38.503611)"),
    };

    /// <summary>Çizgiler (güzergâhlar) → tbl_line</summary>
    internal static readonly Ornek[] Cizgiler =
    {
        new("Ankara – İstanbul koridoru", "Yaklaşık ana ulaşım hattı",
            "#e07b39",
            "LINESTRING (32.85 39.93, 31.6 40.15, 30.4 40.45, 29.9 40.78, 29.0 41.0)"),
        new("Ege kıyı hattı", "İzmir'den Muğla'ya kabaca kıyı çizgisi",
            "#1f7a8c",
            "LINESTRING (27.14 38.42, 27.26 37.87, 27.4 37.4, 27.85 36.95, 28.36 36.83)"),
        new("Doğu Karadeniz yolu", "Samsun – Trabzon – Rize",
            "#57a05a",
            "LINESTRING (36.33 41.29, 37.94 40.96, 39.72 40.99, 40.52 41.02)"),
    };

    // ---------------------------------------------------------------------
    //  İKİNCİ KULLANICININ VERİSİ (Ödev 5 / Madde 3 gösterimi)
    //
    //  Bilerek başka bir coğrafyada: "ayse" ile giriş yapıldığında harita
    //  Akdeniz kıyısını gösterir, admin'in Ankara/İstanbul kayıtları hiç
    //  görünmez. Sahiplik süzgecinin en görünür kanıtı bu.
    // ---------------------------------------------------------------------

    /// <summary>ayse kullanıcısının noktaları</summary>
    internal static readonly Ornek[] IkinciKullaniciNoktalari =
    {
        new("Ölüdeniz", "Muğla · Fethiye",
            "#1f7a8c", "POINT (29.121944 36.550278)"),
        new("Aspendos Tiyatrosu", "Antalya · Serik",
            "#6a4c93", "POINT (31.171944 36.938889)"),
        new("Kaputaş Plajı", "Antalya · Kaş",
            "#57a05a", "POINT (29.474167 36.213889)"),
        new("Marmaris Limanı", "Muğla",
            "#d9553f", "POINT (28.271389 36.855278)"),
    };

    /// <summary>ayse kullanıcısının alanları</summary>
    internal static readonly Ornek[] IkinciKullaniciPoligonlari =
    {
        new("Fethiye körfezi", "Turistik kıyı şeridi",
            "#1f7a8c",
            "POLYGON ((28.95 36.45, 29.35 36.45, 29.35 36.72, 28.95 36.72, 28.95 36.45))"),
        new("Antalya sahil bandı", "Konyaaltı – Lara arası",
            "#e07b39",
            "POLYGON ((30.60 36.80, 31.00 36.80, 31.00 36.95, 30.60 36.95, 30.60 36.80))"),
    };

    /// <summary>Poligonlar (alanlar) → tbl_polygon</summary>
    internal static readonly Ornek[] Poligonlar =
    {
        new("Ankara metropol alanı", "Kabaca il merkezi çevresi",
            "#2e8fa8",
            "POLYGON ((32.62 39.80, 33.05 39.80, 33.05 40.06, 32.62 40.06, 32.62 39.80))"),
        new("İstanbul metropol alanı", "İki yakayı kapsayan kaba sınır",
            "#d9553f",
            "POLYGON ((28.60 40.90, 29.40 40.90, 29.40 41.20, 28.60 41.20, 28.60 40.90))"),
        new("Kapadokya bölgesi", "Nevşehir – Ürgüp – Göreme çevresi",
            "#6a4c93",
            "POLYGON ((34.68 38.55, 35.00 38.55, 35.00 38.75, 34.68 38.75, 34.68 38.55))"),
        new("Göller yöresi", "Isparta – Burdur çevresi",
            "#57a05a",
            "POLYGON ((30.20 37.55, 31.10 37.55, 31.10 38.15, 30.20 38.15, 30.20 37.55))"),
    };
}
