using System.Globalization;
using System.Security;
using System.Text;
using StajProject.Entities;

namespace StajProject.Business.Poiler;

// ============================================================================
//  POI stillerini KATEGORİ TABLOSUNDAN üretir (Ödev 13 iyileştirmesi).
//
//  ÖNCEKİ HÂLİ NEYDİ, NEDEN DEĞİŞTİ?
//  İlk uygulamada beş SLD dosyası depoda elle yazılıydı ve her biri KÖK
//  kategoriye göre süzüyordu ("kok_kategori = 'Sağlık'"). İki sorunu vardı:
//
//   1. Ödev metni "HER BİR POI kategorisi için ayrı bir Style" diyor.
//      Kök başına stil, Restoran ile Kafe'yi aynı simgede birleştiriyordu.
//
//   2. Daha önemlisi: stiller VERİYLE BAĞLI DEĞİLDİ. Yönetici panelden
//      "Ulaşım" adında yeni bir kök açtığında hiçbir stil onu tanımıyordu;
//      POI'ler yedek "Diğer" stiliyle çiziliyor, kimse fark etmiyordu.
//      Sözlük koddaydı, veri tabloda — ikisi sessizce ayrışabiliyordu.
//
//  Şimdi her kategori için bir stil ÜRETİLİYOR ve GeoServer'a yazılıyor.
//  Kategori eklendiğinde stili de oluşuyor; silindiğinde stili de siliniyor.
//
//  YAN KAZANÇ — TÜRKÇE KARAKTER TUZAĞI TAMAMEN KALKTI.
//  Elle yazılan SLD'lerde süzgeç metin karşılaştırmasıydı
//  (<ogc:Literal>Sağlık</ogc:Literal>) ve dosya UTF-8 olarak yüklenmezse
//  filtre hiçbir satırla eşleşmiyordu — hata vermeden yanlış çalışan bir
//  durum. Üretilen stiller "kategori_id = 13" diye SAYIYLA süzüyor.
//  Sayının kodlaması yoktur; tuzak ortadan kalktı.
// ============================================================================

/// <summary>Üretilmiş tek bir POI stili ve arayüzün lejant için ihtiyaç duyduğu bilgiler.</summary>
/// <param name="StilAdi">GeoServer'daki adı — "poi_kat_13".</param>
/// <param name="KategoriId">Süzgecin baktığı kategori.</param>
/// <param name="Ad">Kategorinin kendi adı — "Kütüphane".</param>
/// <param name="TamYol">Kökten yaprağa — "Eğitim › Kütüphane".</param>
/// <param name="Renk">Simgenin dolgu rengi (#rrggbb).</param>
/// <param name="Sekil">
/// SLD WellKnownName — circle / square / triangle / star / cross / x.
/// Ödev 15'ten sonra YEDEK yol: simge dosyası GeoServer'a yazılamazsa
/// (sunucu kapalı, disk yazma hatası) SLD bu işarete düşüyor. İkisini birden
/// tutmak, POI'lerin haritadan tamamen kaybolması ihtimalini kaldırıyor.
/// </param>
/// <param name="Ikon">Çizilecek simgenin anahtarı — "fincan", "eczane"… (Ödev 15)</param>
/// <param name="SvgDosyaAdi">GeoServer'ın stil dizinine yazılacak dosya — "poi_kat_13.svg".</param>
/// <param name="Svg">O dosyanın içeriği (kategori rengiyle boyanmış).</param>
/// <param name="Sld">GeoServer'a yazılacak XML.</param>
public sealed record PoiStili(
    string StilAdi, int KategoriId, string Ad, string TamYol, string Renk, string Sekil,
    string Ikon, string SvgDosyaAdi, string Svg, string Sld);

public static class PoiStilUretici
{
    /// <summary>Stil adlarının ortak öneki — artık stilleri ayıklarken kullanılıyor.</summary>
    public const string StilOneki = "poi_kat_";

    /// <summary>
    /// Hiçbir kategoriye eşleşmeyen POI'ler için yedek stil.
    ///
    /// Üretim kategori tablosundan yapıldığı için normalde her POI'nin bir
    /// stili var. Yine de duruyor: stil yenileme başarısız olmuş ve tablo ile
    /// GeoServer bir süre ayrışmış olabilir. O aralıkta POI'lerin haritadan
    /// TAMAMEN kaybolması, nötr bir simgeyle görünmesinden çok daha kötü.
    /// </summary>
    public const string YedekStil = "poi_diger";

    /// <summary>
    /// Kök kategori paleti. Renk VE şekil birlikte değişiyor: yalnızca renkle
    /// ayırmak renk körlüğünde okunmaz; yalnızca şekille ayırmak 14 pikselde
    /// zor seçilir.
    ///
    /// SLD'nin standart <c>WellKnownName</c> kümesi altı şekil tanıyor
    /// (circle, square, triangle, star, cross, x). Kök sayısı altıyı aşarsa
    /// şekiller başa dönüyor ama renk farklı kalıyor.
    /// </summary>
    private static readonly (string Renk, string Sekil)[] KokPaleti =
    {
        ("#d9822b", "circle"),     // Yeme-İçme
        ("#2d7dd2", "square"),     // Konaklama
        ("#d64550", "cross"),      // Sağlık
        ("#2e9e63", "triangle"),   // Eğitim
        ("#7a6ff0", "star"),
        ("#c2557a", "x"),
        ("#0f8c8c", "circle"),
        ("#8a6d3b", "square"),
    };

    /// <summary>Yedek stilin rengi/şekli — palette hiçbirine benzemesin.</summary>
    private const string YedekRenk = "#7a7f87";
    private const string YedekSekil = "star";

    /// <summary>
    /// Etiketlerin göründüğü ölçek eşiği.
    ///
    /// Web Mercator'da ölçek paydası kabaca 559.082.264 / 2^zoom:
    ///     z=11 → ~273.000     z=13 → ~68.000
    ///     z=12 → ~136.000     z=14 → ~34.000
    ///
    /// EŞİK NEDEN 75.000 DEĞİL DE 150.000?
    /// İlk değer 75.000'di ve kâğıt üzerinde z≈13'ten itibaren açıyordu. Ama
    /// gerçek ekranlarda adlar çok daha geç çıkıyordu; sebebi PİKSEL YOĞUNLUĞU:
    ///
    /// OpenLayers'ın WMS kaynağı <c>serverType: 'geoserver'</c> ile
    /// kurulduğunda, yüksek yoğunluklu ekranlarda isteğe
    /// <c>FORMAT_OPTIONS=dpi:…</c> ekliyor (90 × devicePixelRatio). GeoServer
    /// bu bilgiyi ölçek hesabına katıyor, yani 1.25× bir ekranda eşik
    /// fiilen 75.000/1.25 = 60.000'e iniyor ve etiket ancak z≈14'te
    /// görünüyor. 2× bir ekranda z≈14'ü de geçiyor.
    ///
    /// 150.000, 1× ekranda z≥12, 1.25×'te z≥13, 2×'te z≥13-14 demek —
    /// yani hangi ekranda olursa olsun "POI'ye yaklaşınca adı görünüyor".
    /// Çakışan etiketleri zaten SLD içindeki <c>conflictResolution</c>
    /// eliyor, o yüzden erken açmanın bedeli kalabalık değil, yalnızca
    /// birkaç adın çizilmemesi.
    /// </summary>
    private const int EtiketEsigi = 150000;

    /// <summary>
    /// Kategori ağacından stilleri üretir. Dönen sıra = ÇİZİM sırası:
    /// kök, sonra çocukları, sonra sıradaki kök… en sonda yedek stil.
    ///
    /// Yalnızca AKTİF kategoriler stil alıyor. Pasif kategoriye yeni POI
    /// eklenemiyor; mevcut POI'leri varsa yedek stille görünüyorlar.
    /// </summary>
    /// <param name="kategoriler">Silinmemiş bütün kategoriler (ağaç düzleştirilmiş hâlde).</param>
    public static List<PoiStili> Uret(IReadOnlyCollection<PoiCategory> kategoriler)
    {
        var aktifler = kategoriler.Where(k => k.IsActive).ToList();

        // Simge mirası için ata zinciri gerekiyor; PASİFLER DE sözlükte olmalı.
        // Yalnızca aktifleri koysaydık, pasif bir ara kategorinin altındaki
        // aktif çocuk atasının simgesini bulamaz, varsayılan iğneye düşerdi.
        var idIle = kategoriler.ToDictionary(k => k.Id);

        // Kökleri kararlı bir sırada gez: id sırası kurulumdan kuruluma aynı
        // paleti üretiyor, yani ekran görüntüleri ve lejant tutarlı kalıyor.
        var kokler = aktifler.Where(k => k.ParentId is null).OrderBy(k => k.Id).ToList();

        var sonuc = new List<PoiStili>();

        // Ata → çocukları önden gruplanıyor: ağacı her düğümde baştan taramak
        // (Where(k => k.ParentId == ...)) derinlik arttıkça kare karmaşıklık
        // demek olurdu.
        var atayaGore = aktifler.ToLookup(k => k.ParentId);

        // AĞACIN TAMAMI GEZİLİYOR (Ödev 15 düzeltmesi).
        //
        // Önceki hâli yalnızca İKİ SEVİYE üretiyordu: kökler ve doğrudan
        // çocukları. Üçüncü seviyede açılan bir kategori ("Eğitim › Kütüphane ›
        // B blok") hiç stil almıyor, yedek "Diğer" stiliyle çiziliyordu.
        // Renkler birbirine yakın olduğu için bu gözden kaçıyordu; simgeler
        // gelince açıkça görünür oldu (kitap yerine gri iğne).
        void Gez(PoiCategory kategori, string tamYol, string renk, string sekil)
        {
            sonuc.Add(Olustur(kategori, tamYol, renk, sekil, idIle));

            // Çocuklar aynı şekli paylaşıyor, TONU değişiyor: bir bakışta
            // "bunlar aynı ailedendir" mesajı korunuyor, yine de ayırt
            // edilebiliyorlar. Tamamen farklı renkler vermek hiyerarşiyi
            // haritadan silerdi.
            var cocuklar = atayaGore[kategori.Id].OrderBy(k => k.Id).ToList();

            for (var j = 0; j < cocuklar.Count; j++)
            {
                // Ton HER ZAMAN kök rengine göre hesaplanıyor, ebeveynin zaten
                // açılmış tonuna değil: üst üste bindirseydik üçüncü seviyede
                // renk beyaza yaklaşıp OSM zemininde kaybolurdu.
                var ton = TonAyarla(renk, TonAdimi(j, cocuklar.Count));
                Gez(cocuklar[j], $"{tamYol} › {cocuklar[j].Ad}", ton, sekil);
            }
        }

        for (var i = 0; i < kokler.Count; i++)
        {
            var (renk, sekil) = KokPaleti[i % KokPaleti.Length];
            Gez(kokler[i], kokler[i].Ad, renk, sekil);
        }

        sonuc.Add(YedekStiliUret(aktifler.Select(k => k.Id).ToList()));
        return sonuc;
    }

    /// <summary>
    /// Alt kategorilerin ton kaydırması: %12 ile %40 arasında, hepsi kökten
    /// DAHA AÇIK.
    ///
    /// NEDEN HEPSİ AÇIK TARAFTA, kökün iki yanına yayılmıyor?
    /// İlk denemede aralık -%22 … +%22 idi; üç kardeşte ORTADAKİNİN kayması
    /// sıfır çıkıyor ve o çocuk kökle BİREBİR aynı rengi alıyordu. Haritada
    /// "Yeme-İçme" ile "Kafe" ayırt edilemez hâle geliyordu. Tek yönlü
    /// aralıkta sıfır hiç üretilmiyor; üstelik "kök koyu, altları açık"
    /// ilişkisi hiyerarşiyi de okunur kılıyor.
    ///
    /// Üst sınır %40'ta duruyor: daha fazlası beyaza yaklaşıp OSM zemininde
    /// kayboluyor.
    /// </summary>
    private static double TonAdimi(int sira, int toplam)
        => toplam <= 1 ? 0.12 : 0.12 + (0.28 * sira / (toplam - 1));

    private static PoiStili Olustur(
        PoiCategory kategori, string tamYol, string renk, string sekil,
        IReadOnlyDictionary<int, PoiCategory> idIle)
    {
        var stilAdi = StilOneki + kategori.Id.ToString(CultureInfo.InvariantCulture);
        var ikonAnahtari = PoiIkonlari.EtkinAnahtar(kategori, idIle, k => k.Ikon, k => k.ParentId);
        var ikon = PoiIkonlari.BulYaDaVarsayilan(ikonAnahtari);
        var svgAdi = stilAdi + ".svg";

        return new PoiStili(
            stilAdi, kategori.Id, kategori.Ad, tamYol, renk, sekil,
            ikonAnahtari, svgAdi, PoiIkonlari.SvgUret(ikon, renk),
            SldUret(stilAdi, tamYol, KategoriFiltresi(kategori.Id), renk, sekil, svgAdi));
    }

    private static PoiStili YedekStiliUret(IReadOnlyCollection<int> bilinenIdler)
    {
        var svgAdi = YedekStil + ".svg";

        return new PoiStili(
            YedekStil, 0, "Diğer", "Diğer", YedekRenk, YedekSekil,
            PoiIkonlari.Varsayilan, svgAdi,
            PoiIkonlari.SvgUret(PoiIkonlari.BulYaDaVarsayilan(null), YedekRenk),
            SldUret(
                stilAdi: YedekStil,
                baslik: "Diğer (stili olmayan kategoriler)",
                filtre: YedekFiltre(bilinenIdler),
                renk: YedekRenk,
                sekil: YedekSekil,
                svgDosyaAdi: svgAdi));
    }

    // ----------------------------------------------------------------------
    //  Süzgeçler
    // ----------------------------------------------------------------------

    /// <summary>
    /// <c>kategori_id = N</c>. Metin değil SAYI karşılaştırması: kodlama
    /// sorunu olamaz ve kategori adı değiştiğinde stil çalışmaya devam eder
    /// (ada göre süzseydik yeniden adlandırma stili sessizce bozardı).
    /// </summary>
    private static string KategoriFiltresi(int kategoriId) => $"""
                  <ogc:PropertyIsEqualTo>
                    <ogc:PropertyName>kategori_id</ogc:PropertyName>
                    <ogc:Literal>{kategoriId}</ogc:Literal>
                  </ogc:PropertyIsEqualTo>
        """;

    /// <summary>
    /// Bilinen kategorilerin HİÇBİRİ olmayan kayıtlar.
    /// Hiç kategori yoksa (boş kurulum) "her şey" anlamına gelen basit bir
    /// karşılaştırma üretiyoruz — boş bir <c>ogc:Or</c> geçersiz XML olurdu.
    /// </summary>
    private static string YedekFiltre(IReadOnlyCollection<int> idler)
    {
        if (idler.Count == 0)
        {
            return """
                          <ogc:PropertyIsGreaterThan>
                            <ogc:PropertyName>kategori_id</ogc:PropertyName>
                            <ogc:Literal>0</ogc:Literal>
                          </ogc:PropertyIsGreaterThan>
                """;
        }

        var esitlikler = string.Join("\n", idler.Select(id => $"""
                          <ogc:PropertyIsEqualTo>
                            <ogc:PropertyName>kategori_id</ogc:PropertyName>
                            <ogc:Literal>{id}</ogc:Literal>
                          </ogc:PropertyIsEqualTo>
            """));

        // Tek elemanlı ogc:Or bazı ayrıştırıcılarda sorun çıkarıyor; tek
        // kategorili kurulumda Or'u atlıyoruz.
        var ic = idler.Count == 1
            ? esitlikler
            : $"""
                          <ogc:Or>
            {esitlikler}
                          </ogc:Or>
            """;

        return $"""
                      <ogc:Not>
            {ic}
                      </ogc:Not>
            """;
    }

    // ----------------------------------------------------------------------
    //  SLD gövdesi
    // ----------------------------------------------------------------------

    /// <summary>
    /// Üç kurallı SLD üretir: aktif simge, pasif (soluk) simge ve yakın
    /// zoom'da isim etiketi.
    ///
    /// XML metin olarak kuruluyor, XmlWriter ile değil. Gerekçe: şablon sabit
    /// ve okunabilirliği önemli — üretilen dosya GeoServer arayüzünde
    /// görülebiliyor, sunumda açılıp gösterilebiliyor. Dışarıdan gelen tek
    /// değişken metin kategori ADI ve o da kaçırılıyor
    /// (<see cref="SecurityElement.Escape"/>).
    /// </summary>
    private static string SldUret(
        string stilAdi, string baslik, string filtre, string renk, string sekil, string svgDosyaAdi)
    {
        var guvenliBaslik = SecurityElement.Escape(baslik) ?? baslik;

        var yaziRengi = TonAyarla(renk, -0.45);   // etiket, simgenin koyu tonu

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!--
              ============================================================
               ÜRETİLMİŞ DOSYA — elle düzenlemeyin.
               Kaynak: Business/Poiler/PoiStilUretici.cs
               Kategori tablosu her değiştiğinde yeniden üretilir
               (POST /api/admin/poi-categories/stilleri-yenile).

               Süzgeç kategori ID'sine bakıyor, adına değil: ad değişse de
               stil çalışmaya devam eder ve metin kodlaması sorun olamaz.
              ============================================================
            -->
            <StyledLayerDescriptor version="1.0.0"
                xsi:schemaLocation="http://www.opengis.net/sld StyledLayerDescriptor.xsd"
                xmlns="http://www.opengis.net/sld"
                xmlns:ogc="http://www.opengis.net/ogc"
                xmlns:xlink="http://www.w3.org/1999/xlink"
                xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <NamedLayer>
                <Name>{stilAdi}</Name>
                <UserStyle>
                  <Title>POI — {guvenliBaslik}</Title>

                  <FeatureTypeStyle>

                    <Rule>
                      <Name>{stilAdi}_aktif</Name>
                      <Title>{guvenliBaslik}</Title>
                      <ogc:Filter>
                        <ogc:And>
            {filtre}
                          <ogc:PropertyIsEqualTo>
                            <ogc:PropertyName>is_active</ogc:PropertyName>
                            <ogc:Literal>true</ogc:Literal>
                          </ogc:PropertyIsEqualTo>
                        </ogc:And>
                      </ogc:Filter>
                      <PointSymbolizer>
                        <Graphic>
                          <!--
                            ÖDEV 15 — KATEGORİYE ÖZGÜ SİMGE.
                            ExternalGraphic, stilin kendi dizinindeki SVG'yi
                            gösteriyor (dosya adı görelidir: GeoServer onu
                            workspaces/<ws>/styles/ altında arar). Dosyayı
                            PoiStyleService, stili yazmadan ÖNCE yüklüyor.

                            ALTINDAKİ <Mark> YEDEK: SLD'de birden çok grafik
                            tanımı sıralanabilir ve GeoServer ilk ÇÖZÜLEBİLENİ
                            kullanır. SVG bir sebeple okunamazsa POI haritadan
                            kaybolmuyor, eski geometrik işaretiyle çiziliyor.
                          -->
                          <ExternalGraphic>
                            <OnlineResource xlink:type="simple" xlink:href="{svgDosyaAdi}"/>
                            <Format>image/svg+xml</Format>
                          </ExternalGraphic>
                          <Mark>
                            <WellKnownName>{sekil}</WellKnownName>
                            <Fill><CssParameter name="fill">{renk}</CssParameter></Fill>
                            <Stroke>
                              <CssParameter name="stroke">#ffffff</CssParameter>
                              <CssParameter name="stroke-width">2</CssParameter>
                            </Stroke>
                          </Mark>
                          <Size>18</Size>
                        </Graphic>
                      </PointSymbolizer>
                    </Rule>

                    <Rule>
                      <Name>{stilAdi}_pasif</Name>
                      <Title>{guvenliBaslik} (pasif)</Title>
                      <ogc:Filter>
                        <ogc:And>
            {filtre}
                          <ogc:PropertyIsEqualTo>
                            <ogc:PropertyName>is_active</ogc:PropertyName>
                            <ogc:Literal>false</ogc:Literal>
                          </ogc:PropertyIsEqualTo>
                        </ogc:And>
                      </ogc:Filter>
                      <PointSymbolizer>
                        <Graphic>
                          <!-- Pasif kayıt: aynı simge, yarı saydam ve bir tık küçük.
                               Saydamlık <Graphic> düzeyinde çünkü ExternalGraphic'in
                               içindeki renklere CssParameter ile karışılamıyor. -->
                          <ExternalGraphic>
                            <OnlineResource xlink:type="simple" xlink:href="{svgDosyaAdi}"/>
                            <Format>image/svg+xml</Format>
                          </ExternalGraphic>
                          <Mark>
                            <WellKnownName>{sekil}</WellKnownName>
                            <Fill><CssParameter name="fill">{renk}</CssParameter></Fill>
                            <Stroke>
                              <CssParameter name="stroke">#ffffff</CssParameter>
                              <CssParameter name="stroke-width">1.5</CssParameter>
                            </Stroke>
                          </Mark>
                          <Opacity>0.35</Opacity>
                          <Size>15</Size>
                        </Graphic>
                      </PointSymbolizer>
                    </Rule>

                    <Rule>
                      <Name>{stilAdi}_etiket</Name>
                      <Title>{guvenliBaslik} — isim etiketi</Title>
                      <ogc:Filter>
            {filtre}
                      </ogc:Filter>
                      <MaxScaleDenominator>{EtiketEsigi}</MaxScaleDenominator>
                      <TextSymbolizer>
                        <Label><ogc:PropertyName>isim</ogc:PropertyName></Label>
                        <Font>
                          <CssParameter name="font-family">Noto Sans</CssParameter>
                          <CssParameter name="font-family">DejaVu Sans</CssParameter>
                          <CssParameter name="font-family">Arial</CssParameter>
                          <CssParameter name="font-size">12</CssParameter>
                          <CssParameter name="font-weight">bold</CssParameter>
                        </Font>
                        <LabelPlacement>
                          <PointPlacement>
                            <AnchorPoint>
                              <AnchorPointX>0.5</AnchorPointX>
                              <AnchorPointY>0.0</AnchorPointY>
                            </AnchorPoint>
                            <Displacement>
                              <DisplacementX>0</DisplacementX>
                              <DisplacementY>14</DisplacementY>
                            </Displacement>
                          </PointPlacement>
                        </LabelPlacement>
                        <Halo>
                          <Radius>2</Radius>
                          <Fill>
                            <CssParameter name="fill">#ffffff</CssParameter>
                            <CssParameter name="fill-opacity">0.85</CssParameter>
                          </Fill>
                        </Halo>
                        <Fill><CssParameter name="fill">{yaziRengi}</CssParameter></Fill>
                        <VendorOption name="conflictResolution">true</VendorOption>
                        <VendorOption name="goodnessOfFit">0.5</VendorOption>
                        <VendorOption name="spaceAround">4</VendorOption>
                      </TextSymbolizer>
                    </Rule>

                  </FeatureTypeStyle>
                </UserStyle>
              </NamedLayer>
            </StyledLayerDescriptor>
            """;
    }

    // ----------------------------------------------------------------------
    //  Renk yardımcısı
    // ----------------------------------------------------------------------

    /// <summary>
    /// Hex rengi açar (pozitif oran) veya koyulaştırır (negatif oran).
    ///
    /// HSL'e çevirip parlaklıkla oynamak daha "doğru" olurdu ama üç kanalı
    /// beyaza/siyaha doğru kaydırmak bu ölçekte gözle ayırt edilemeyecek kadar
    /// yakın sonuç veriyor ve harici bir renk kütüphanesi gerektirmiyor.
    /// </summary>
    /// <param name="hex">"#rrggbb"</param>
    /// <param name="oran">-1 (siyah) … +1 (beyaz)</param>
    public static string TonAyarla(string hex, double oran)
    {
        if (oran == 0) return hex;

        var temiz = hex.TrimStart('#');
        if (temiz.Length != 6) return hex;

        var sonuc = new StringBuilder("#", 7);

        for (var i = 0; i < 3; i++)
        {
            var kanal = int.Parse(temiz.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            var hedef = oran > 0 ? 255 : 0;
            var yeni = (int)Math.Round(kanal + ((hedef - kanal) * Math.Abs(oran)));

            sonuc.Append(Math.Clamp(yeni, 0, 255).ToString("x2", CultureInfo.InvariantCulture));
        }

        return sonuc.ToString();
    }
}
