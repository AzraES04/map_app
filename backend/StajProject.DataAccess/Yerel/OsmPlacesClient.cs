using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StajProject.DataAccess.Google;

namespace StajProject.DataAccess.Yerel;

/// <summary>
/// OpenStreetMap (Overpass API) tabanlı mekan kaynağı — ANAHTARSIZ KİP.
///
/// <see cref="IPlacesClient"/>'ı uyguluyor, yani iş katmanı için Google
/// istemcisinden ayırt edilemez: tur planlama servisi hangi kaynağın bağlı
/// olduğunu bilmiyor, yalnızca arayüzü çağırıyor. Kaynağı değiştirmek tek bir
/// DI kaydı değiştirmek demek (Ödev 8'deki GeoServer/PostGIS anahtarının aynı
/// fikri).
///
/// ---- NEDEN POI TABLOMUZ DEĞİL DE OSM? ----
/// Sistemdeki POI kategorileri Yeme-İçme, Konaklama, Sağlık ve Eğitim: eczane
/// ve okul var, MÜZE VE PARK YOK. Tur temaları ("kültürel", "doğa") o veriden
/// beslenemezdi; kültürel tur her seferinde boş dönerdi. OSM'de bu mekanların
/// tamamı var, üstelik ücretsiz ve anahtarsız.
///
/// ---- EKSİĞİ NE? ----
/// KULLANICI PUANI. OSM bir harita veritabanı, bir değerlendirme sitesi değil.
/// Bu yüzden <see cref="PuanVerisiVar"/> false ve 4.5+ süzgeci uygulanmıyor;
/// sıralama, "kayda değerlik" işaretlerinden üretilen
/// <see cref="GooglePlace.Onem"/> ile yapılıyor (bkz. OnemHesapla).
/// </summary>
public class OsmPlacesClient : IOsmPlacesClient
{
    /// <summary>
    /// Google tip anahtarı → OSM etiket süzgeçleri.
    ///
    /// Katalog (Business/Tur/TurKatalogu) Google'ın tip adlarıyla yazılmış
    /// durumda; çeviriyi BURADA yapıyoruz ki katalog tek bir sağlayıcıya
    /// bağlı kalmasın. Bir tipe birden çok süzgeç düşebiliyor: OSM'de "anıt"
    /// hem <c>historic=monument</c> hem <c>tourism=attraction</c> olabiliyor.
    /// </summary>
    private static readonly Dictionary<string, string[]> TipSuzgecleri = new(StringComparer.OrdinalIgnoreCase)
    {
        ["museum"] = new[] { "[\"tourism\"=\"museum\"]" },
        ["art_gallery"] = new[] { "[\"tourism\"=\"gallery\"]" },
        ["tourist_attraction"] = new[]
        {
            "[\"tourism\"=\"attraction\"]",
            "[\"tourism\"=\"viewpoint\"]",
            "[\"historic\"=\"monument\"]",
            "[\"historic\"=\"castle\"]",
            "[\"historic\"=\"palace\"]",
            "[\"historic\"=\"ruins\"]",

            // Atakule bunlardan HİÇBİRİ değil: OSM'de man_made=tower.
            // Bu satır olmadan Ankara'nın en tanınan simgelerinden biri
            // hiçbir aramaya düşmüyordu (canlı veriyle doğrulandı).
            //
            // ["wikidata"] DARALTMASI ŞART: kule etiketi elektrik direğinden
            // baz istasyonuna kadar her şeye konuyor. Ankara'da yalnızca
            // wikidata'lı olanları istemek hem sorguyu 5 saniyeye indirdi hem
            // de gelen tek sonuç Atakule oldu.
            "[\"man_made\"=\"tower\"][\"wikidata\"]",

            // historic=memorial ve tourism=artwork BİLEREK YOK.
            // Ankara'da yüzlerce kayıt döndürüyorlar (yol kenarı plaketleri,
            // heykelcikler) ve tur önerisini dolduran şey tam olarak onlardı:
            // canlıda üretilen ilk listede "Mehmet Akif Ersoy Anıtı" gibi
            // duraklar vardı da Anadolu Medeniyetleri Müzesi yoktu. Sorguyu
            // da ağırlaştırıp 504'e sokuyorlardı.
        },
        ["park"] = new[] { "[\"leisure\"=\"park\"]", "[\"leisure\"=\"garden\"]" },
        // İBADET YERLERİNDE de ["wikidata"] daraltması var: Ankara'da
        // yüzlerce mahalle camii kayıtlı ve tur durağı olan yalnızca birkaçı.
        // Daraltma olmadan sorgu hem çok yavaşlıyor (504) hem de liste
        // "Tatlar Mahallesi Camii" gibi kayıtlarla doluyordu; wikidata'lı
        // sorgu Hacı Bayram Veli Camii'ni ilk sırada getiriyor.
        ["mosque"] = new[]
        {
            "[\"amenity\"=\"place_of_worship\"][\"religion\"=\"muslim\"][\"wikidata\"]",
        },
        ["church"] = new[]
        {
            "[\"amenity\"=\"place_of_worship\"][\"religion\"=\"christian\"][\"wikidata\"]",
        },
        ["restaurant"] = new[] { "[\"amenity\"=\"restaurant\"]" },
        ["cafe"] = new[] { "[\"amenity\"=\"cafe\"]" },
        ["shopping_mall"] = new[] { "[\"shop\"=\"mall\"]", "[\"amenity\"=\"marketplace\"]" },

        // KONAKLAMA — yalnızca çok günlü turlarda, günün sonundaki
        // "geceleme bölgesi" durağı için (bkz. TurKatalogu.KonaklamaAramalari).
        //
        // guest_house ve hostel de alınıyor: küçük şehirlerde otel etiketli
        // kayıt sayısı tek haneye düşebiliyor ve öneri boş kalırdı.
        ["lodging"] = new[]
        {
            "[\"tourism\"=\"hotel\"]",
            "[\"tourism\"=\"guest_house\"]",
            "[\"tourism\"=\"hostel\"]",
        },
    };

    /// <summary>Tanınmayan tip için: en azından "gezilecek yer" ara.</summary>
    private static readonly string[] VarsayilanSuzgec = { "[\"tourism\"=\"attraction\"]" };

    private readonly HttpClient _http;
    private readonly YerelKaynakSettings _ayarlar;
    private readonly GoogleMapsSettings _planAyarlari;
    private readonly IIstekButcesi _butce;
    private readonly IMemoryCache _onbellek;
    private readonly ILogger<OsmPlacesClient> _logger;

    public OsmPlacesClient(
        HttpClient http,
        YerelKaynakSettings ayarlar,
        GoogleMapsSettings planAyarlari,
        IIstekButcesi butce,
        IMemoryCache onbellek,
        ILogger<OsmPlacesClient> logger)
    {
        _http = http;
        _ayarlar = ayarlar;
        _planAyarlari = planAyarlari;
        _butce = butce;
        _onbellek = onbellek;
        _logger = logger;

        _http.Timeout = TimeSpan.FromSeconds(ayarlar.TimeoutSeconds);

        // KİMLİK BAŞLIĞI ZORUNLU — süs değil.
        //
        // Overpass'ın genel sunucusu User-Agent göndermeyen isteklere
        // 406 (Not Acceptable) dönüyor; başlıksız istemci hiçbir sorgu
        // çalıştıramıyor (denendi). Kullanım şartları da uygulamanın kendini
        // tanıtmasını istiyor: sunucu yöneticisi sorunlu bir istemciyi ancak
        // böyle ayırt edebiliyor.
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(KimlikBasligi);
        }
    }

    /// <summary>Overpass'a gönderilen istemci kimliği.</summary>
    private const string KimlikBasligi = "StajProject/1.0 (harita-uygulamasi; tur-onerisi)";

    public bool Etkin => _ayarlar.Enabled;

    /// <summary>OSM'de kullanıcı puanı yok — 4.5+ süzgeci uygulanamaz.</summary>
    public bool PuanVerisiVar => false;

    public async Task<IReadOnlyList<GooglePlace>> AraAsync(
        IReadOnlyList<PlacesAramasi> aramalar,
        CancellationToken iptal = default)
    {
        var bos = Array.Empty<GooglePlace>();

        if (!Etkin || aramalar.Count == 0)
        {
            return bos;
        }

        var anahtar = OnbellekAnahtari(aramalar);

        if (_onbellek.TryGetValue<IReadOnlyList<GooglePlace>>(anahtar, out var onbellekten)
            && onbellekten is not null)
        {
            _logger.LogDebug("Overpass onbellekten karsilandi.");
            return onbellekten;
        }

        // Butce onbellekten SONRA: onbellekten donen cevap aga hic cikmiyor.
        // Overpass ucretsiz ama GONULLU bir altyapi; sayaci burada da
        // tutuyoruz ki kacak bir dongu kimsenin sunucusunu mesgul etmesin.
        if (!_butce.IzinIste())
        {
            _logger.LogWarning("Overpass istegi butce doldugu icin atilmadi.");
            return bos;
        }

        var cevapMetni = await IstekAtAsync(SorguKur(aramalar), iptal);

        if (cevapMetni is null)
        {
            return bos;
        }

        try
        {
            var icerik = JsonSerializer.Deserialize<OverpassCevabi>(
                cevapMetni,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var mekanlar = (icerik?.Elements ?? new List<OverpassOgesi>())
                .Select(Cevir)
                .Where(m => m is not null)
                .Select(m => m!)
                // Ayni mekan hem node hem way olarak donebiliyor ya da iki
                // suzgece birden uyabiliyor; PlaceId ile tekillestiriyoruz.
                .GroupBy(m => m.PlaceId, StringComparer.Ordinal)
                .Select(g => g.First())
                .Take(aramalar.Sum(a => a.EnFazlaSonuc ?? _planAyarlari.AramaBasinaSonuc))
                .ToList();

            // Bos sonuc da onbellekleniyor: "bu sehirde muze yok" da bir cevap
            // ve tekrar soruldugunda yine bos donecek (Places istemcisiyle
            // ayni gerekce).
            _onbellek.Set(anahtar, (IReadOnlyList<GooglePlace>)mekanlar,
                TimeSpan.FromMinutes(_planAyarlari.OnbellekDakika));

            return mekanlar;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Overpass cevabi cozulemedi.");
            return bos;
        }
    }

    // ---------------------------------------------------------------------
    //  DEVRE KESICI (circuit breaker)
    //
    //  Olculen sorun: overpass-api.de bu IP'ye HTTP 429 donmeye basladiginda
    //  bu durum dakikalarca suruyor. Her tur onerisi 2 sorgu atiyor ve her
    //  sorgu zaman asimina kadar (15 sn) bekliyor — yani kullanici, sonucu
    //  ZATEN yerel POI yedeginden gelecek bir rota icin 30 saniye bekliyor.
    //  Ilk deneyde bunu olcmek gerekiyor; ONUNCU deneyde beklemek sacma.
    //
    //  Kesici, arka arkaya birkac basarisizliktan sonra Overpass'i bir sure
    //  hic denemiyor: istek aninda "sonuc yok" donuyor ve cagiran taraf
    //  dogrudan yerel yedege gecip rotayi ~1 saniyede uretiyor. Sure dolunca
    //  bir sonraki istek yeniden deniyor — servis toparlandiginda kendiliginden
    //  guncel veriye donuyoruz, elle mudahale gerekmiyor.
    //
    //  ---- NEDEN STATIC? ----
    //  Bu istemci typed HttpClient olarak TRANSIENT kayitli: her istek yeni
    //  bir ornek aliyor. Ornek alaninda tutsaydik kesici hicbir sey
    //  hatirlamaz, her istek yeniden 30 saniye beklerdi. Devrenin durumu
    //  istege degil SURECE ait: "Overpass su an bize kapali" bilgisi
    //  uygulamanin tamaminda ayni.
    // ---------------------------------------------------------------------

    /// <summary>Devreyi acan ard arda basarisizlik sayisi.</summary>
    private const int DevreEsigi = 2;

    /// <summary>Devre acikken Overpass'in hic denenmedigi sure.</summary>
    private static readonly TimeSpan DevreAcikKalmaSuresi = TimeSpan.FromMinutes(3);

    private static int _ardArdaHata;

    /// <summary>Devrenin yeniden denenecegi an (UTC); gecmisse devre kapali.</summary>
    private static long _devreAcikSonuTick;

    /// <summary>Devre su an acik mi (yani Overpass denenmemeli mi)?</summary>
    private static bool DevreAcik
        => DateTime.UtcNow.Ticks < Interlocked.Read(ref _devreAcikSonuTick);

    /// <summary>Basarili istek: sayac sifirlanir, devre kapanir.</summary>
    private static void BasariBildir()
    {
        Interlocked.Exchange(ref _ardArdaHata, 0);
        Interlocked.Exchange(ref _devreAcikSonuTick, 0);
    }

    /// <summary>Basarisiz istek: esik asilirsa devre acilir.</summary>
    private void HataBildir()
    {
        if (Interlocked.Increment(ref _ardArdaHata) < DevreEsigi) return;

        Interlocked.Exchange(
            ref _devreAcikSonuTick,
            DateTime.UtcNow.Add(DevreAcikKalmaSuresi).Ticks);

        _logger.LogWarning(
            "Overpass ard arda {Adet} kez basarisiz oldu; {Dakika} dakika boyunca " +
            "denenmeyecek (yerel POI yedegi kullanilacak).",
            DevreEsigi, DevreAcikKalmaSuresi.TotalMinutes);
    }

    /// <summary>
    /// Sorguyu gonderir; 429'da BIR KEZ bekleyip tekrar dener.
    ///
    /// ---- NEDEN TEKRAR DENEME? ----
    /// Overpass'in genel sunucusu IP basina eszamanli istek sayisini
    /// sinirliyor ve sinira takilan istek aninda 429 donuyor. Tek denemede
    /// pes etseydik, arka arkaya iki tur onerisi isteyen kullanici ikincisinde
    /// "mekan bulunamadi" gorurdu — oysa sorun veride degil, zamanlamada.
    ///
    /// Tek tekrar ve sabit bekleme: ustel geri cekilme daha "dogru" olurdu ama
    /// kullanici ekranin basinda bekliyor; ikinci basarisizlikta hata vermek
    /// dakikalarca denemekten iyi.
    /// </summary>
    private async Task<string?> IstekAtAsync(string sorgu, CancellationToken iptal)
    {
        // Devre acikken hic denemiyoruz: cagiran taraf bos sonucu gorup
        // yerel yedege gececek ve kullanici 15 saniye beklemeyecek.
        if (DevreAcik)
        {
            _logger.LogInformation(
                "Overpass devresi acik; istek atlanip yerel yedege birakildi.");
            return null;
        }

        for (var deneme = 1; deneme <= 2; deneme++)
        {
            try
            {
                using var govde = new FormUrlEncodedContent(
                    new[] { new KeyValuePair<string, string>("data", sorgu) });

                using var cevap = await _http.PostAsync(_ayarlar.OverpassUrl, govde, iptal);

                if (cevap.IsSuccessStatusCode)
                {
                    BasariBildir();
                    return await cevap.Content.ReadAsStringAsync(iptal);
                }

                // 429 (hiz siniri) TEKRAR DENENIYOR, 504 (sunucu mesgul) HAYIR.
                //
                // Ikisi de "gecici" ama davranislari farkli ve olculdu:
                //
                //   429 → istegimiz hic calismadi, sadece siraya alinmadik.
                //         Iki saniye sonra tekrar sormak genelde calisiyor
                //         ve bize hicbir sey mal olmuyor.
                //
                //   504 → sunucu sorguyu CALISTIRDI ama yetistiremedi. Ayni
                //         agir sorguyu hemen tekrar sormak yazi-tura: Ankara
                //         icin 10 km yaricapta arka arkaya olculdu →
                //         504 (11,2 sn), 200 (7,4 sn), 504 (10,2 sn).
                //         Yani ikinci deneme ~%33 sansla ise yariyor ama
                //         kullanicinin beklemesini 15 saniye daha uzatiyor.
                //
                // Tur onerisinin artik YEREL POI YEDEGI var (bkz.
                // YerelPoiKaynagi): 504'te beklemek yerine hemen pes etmek,
                // kullaniciyi 30 saniye yerine 15 saniyede calisan bir
                // rotaya kavusturuyor.
                var gecici = (int)cevap.StatusCode is 429;

                _logger.LogWarning(
                    "Overpass {Kod} dondu ({Durum}, deneme {Deneme}/2).",
                    (int)cevap.StatusCode, gecici ? "gecici" : "kalici", deneme);

                if (!gecici || deneme == 2)
                {
                    HataBildir();
                    return null;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), iptal);
            }
            catch (TaskCanceledException) when (!iptal.IsCancellationRequested)
            {
                _logger.LogWarning("Overpass {Saniye} saniyede cevap vermedi.", _ayarlar.TimeoutSeconds);
                HataBildir();
                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Overpass'a ulasilamadi.");
                HataBildir();
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Overpass QL sorgusunu kurar.
    ///
    /// Her süzgeç için hem <c>node</c> hem <c>way</c> sorgulanıyor: OSM'de
    /// bir müze kimi zaman tek nokta, kimi zaman binanın alanı olarak
    /// çizilmiş. Yalnızca node sorsaydık büyük müzelerin çoğu (alan olarak
    /// çizilenler) hiç bulunamazdı.
    ///
    /// <c>out center</c> — alanların (way) MERKEZ koordinatını istiyor;
    /// köşe köşe geometrisini değil. Tur durağı için tek bir nokta yeterli
    /// ve cevabı onlarca kat küçültüyor.
    /// </summary>
    /// <summary>
    /// Aramaları TEK bir Overpass sorgusuna çevirir.
    ///
    /// ---- SÜZGEÇLER TEKİLLEŞTİRİLİYOR ----
    /// Aynı süzgeç birden çok aramadan gelebiliyor: yemek molası kataloğunda
    /// üç arama var ve üçü de <c>amenity=restaurant</c>; konaklamada iki
    /// arama, ikisi de aynı üç etiketi soruyor. Sorgu metni bunları olduğu
    /// gibi yan yana diziyordu ve sonuç ÖLÇÜLDÜ:
    ///
    ///     gezi süzgeçleri (11 tane)         → 6,8 sn
    ///     + yemek + konaklama (tekrarlı)    → 504 Gateway Timeout
    ///     + yemek + konaklama (tekilsiz)    → 7,5 sn
    ///
    /// Yani tekrar, sorguyu çalışmaz hâle getiriyordu. Kullanıcının
    /// bildirdiği iki sorun ("rota oluşturmak çok uzun sürüyor" ve
    /// "konaklama sunulmuyor") aynı satırdan geliyordu: sorgu zaman aşımına
    /// uğrayınca cevap boş dönüyor, konaklama adayı da bulunamıyordu.
    ///
    /// ---- SONUÇ TAVANI SINIRLI ----
    /// Tavan, aramaların EnFazlaSonuc toplamıydı: dokuz arama × 150 = 1350.
    /// Overpass'in cevabı üretme maliyeti bu sayıyla doğru orantılı ve tur
    /// önerisi o kadar adaya bakmıyor — seçim zaten ilk birkaç yüz kayıttan
    /// yapılıyor. Üst sınır, sorguyu makul sürede tutuyor.
    /// </summary>
    private string SorguKur(IReadOnlyList<PlacesAramasi> aramalar)
    {
        var sb = new StringBuilder();
        sb.Append("[out:json][timeout:").Append(_ayarlar.TimeoutSeconds).Append("];(");

        // Aynı (süzgeç + yarıçap + merkez) üçlüsü bir kez soruluyor.
        var yazilanlar = new HashSet<string>(StringComparer.Ordinal);

        foreach (var arama in aramalar)
        {
            var suzgecler = arama.Tip is not null && TipSuzgecleri.TryGetValue(arama.Tip, out var liste)
                ? liste
                : VarsayilanSuzgec;

            var lat = arama.Lat.ToString("0.#####", CultureInfo.InvariantCulture);
            var lon = arama.Lon.ToString("0.#####", CultureInfo.InvariantCulture);

            foreach (var suzgec in suzgecler)
            {
                var alan = $"(around:{arama.YaricapMetre},{lat},{lon});";

                if (!yazilanlar.Add(suzgec + alan))
                {
                    continue;
                }

                // ISIM SUZGECI SORGUDA YOK — bilerek. ["name"] eklemek
                // Overpass'in genel sunucusunda sorguyu zaman asimina
                // ugratiyor (denendi: 29 saniyede timeout). Adsiz kayitlar
                // cevabi okurken eleniyor (bkz. Cevir): birkac fazla satir
                // tasimak, hic cevap alamamaktan iyi.
                sb.Append("node").Append(suzgec).Append(alan);

                // Alanlar da (way) sorgulaniyor: OSM'de buyuk muzeler ve
                // parklar nokta degil, binanin/alanin kendisi olarak cizili.
                sb.Append("way").Append(suzgec).Append(alan);
            }
        }

        // TAVAN DA TEKİL TİPLER ÜZERİNDEN.
        //
        // Ham toplam alsaydık (dokuz arama × 150 = 1350) aynı süzgeci üç kez
        // sormanın bedelini tavana da yansıtmış olurduk — oysa süzgeç bir kez
        // soruluyor. Üstteki mutlak sınır yalnızca emniyet supabı: POI içe
        // aktarımı şehrin tamamını istiyor ve onun tipleri zaten tekil,
        // dolayısıyla bu hesap onu kısmıyor.
        var tavan = Math.Min(
            _planAyarlari.ToplamSonucTavani,
            aramalar
                .GroupBy(a => a.Tip ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Sum(g => g.Max(a => a.EnFazlaSonuc ?? _planAyarlari.AramaBasinaSonuc)));

        // "out center" — alanlarin KOSE geometrisi yerine merkez noktasi.
        // Tur duragi icin tek nokta yeterli ve cevabi onlarca kat kucultuyor.
        sb.Append(");out center ").Append(tavan).Append(';');

        return sb.ToString();
    }

    private static GooglePlace? Cevir(OverpassOgesi oge)
    {
        var lat = oge.Lat ?? oge.Center?.Lat;
        var lon = oge.Lon ?? oge.Center?.Lon;
        var etiketler = oge.Tags ?? new Dictionary<string, string>();

        if (lat is null || lon is null || !etiketler.TryGetValue("name", out var ad))
        {
            return null;
        }

        return new GooglePlace(
            // Sağlayıcı ön ekiyle: Waypoint.PlaceId sözleşmesi bunu istiyor.
            PlaceId: $"osm:{oge.Type}/{oge.Id}",
            Ad: ad,
            Lat: lat.Value,
            Lon: lon.Value,
            // OSM'de puan YOK. Sıfır yazmak "kötü mekan" gibi okunurdu;
            // null "bilinmiyor" demek ve iş katmanı bunu böyle ele alıyor.
            Puan: null,
            DegerlendirmeSayisi: 0,
            // Tip ETIKETLERDEN cikariliyor, aramadan degil: toplu sorguda bir
            // kaydin hangi suzgecten geldigi belli degil ve zaten etiketler
            // daha kesin bilgi ("gezilecek yer" diye aranip bulunan bir yer
            // aslinda muze olabilir; kalis suresi buna gore ataniyor).
            Tipler: TipleriCikar(etiketler),
            BirincilTip: TipleriCikar(etiketler).FirstOrDefault(),
            Onem: OnemHesapla(etiketler),
            VejetaryenSecenegiVar: VejetaryenVarMi(etiketler),
            // Ham etiketler çağıran tarafa aynen geçiyor: "zincir şubesi mi",
            // "mutfağı ne" gibi sorular ancak burada cevaplanabiliyor
            // (bkz. TuristikPoiAktarici).
            Ekstra: etiketler);
    }

    /// <summary>
    /// Mekan tipinin "tur durağı olma" ağırlığı.
    ///
    /// Bir müze ile bir "memorial" (yol kenarındaki plaket) aynı şey değil.
    /// EN YÜKSEK eşleşme kazanıyor: Anıtkabir hem <c>historic=monument</c> hem
    /// başka etiketler taşıyor; ilk eşleşmeyi almak onu listenin dibine
    /// atıyordu (canlıda tam olarak bu oldu — Anıtkabir 19. sıradaydı).
    /// </summary>
    private static readonly (string Anahtar, string Deger, double Agirlik)[] TipAgirliklari =
    {
        ("tourism", "museum", 1.00),
        ("historic", "castle", 0.95),
        ("historic", "palace", 0.95),
        ("historic", "ruins", 0.75),
        ("tourism", "gallery", 0.70),
        ("tourism", "attraction", 0.70),
        ("man_made", "tower", 0.70),
        ("tourism", "viewpoint", 0.60),
        ("historic", "monument", 0.55),
        ("amenity", "place_of_worship", 0.55),
        ("leisure", "park", 0.50),
        ("historic", "memorial", 0.30),
        ("tourism", "artwork", 0.25),
    };

    /// <summary>
    /// Ham puanın üst sınırı — 0-1 aralığına indirmek için.
    /// Tip (1.0) + wikidata (0.60+0.45) + wikipedia (0.30) + diller (1.10)
    /// + miras (0.25) + iletişim (0.10) ≈ 3.8.
    /// </summary>
    private const double AzamiHamPuan = 3.8;

    /// <summary>
    /// OSM etiketlerinden "kayda değerlik" işareti üretir (0-1).
    ///
    /// ---- BU FONKSİYON NEDEN BÖYLE? ----
    /// Canlıda üretilen ilk Ankara turu şöyleydi: Mehmet Akif Ersoy Anıtı,
    /// Zafer Anıtı, Feza Gürsey Bilim Merkezi, İbni Sina Parkı… ve TEK BİR
    /// anıtın beş kulesi ayrı duraklar olarak. Anadolu Medeniyetleri Müzesi
    /// listede HİÇ YOKTU.
    ///
    /// Sebep: OSM'de puan yok ve bütün kayıtlar eşit ağırlıktaydı. Aşağıdaki
    /// dört sinyal, 515 Ankara kaydı üzerinde denenerek seçildi; sonuç
    /// sıralaması Anadolu Medeniyetleri Müzesi → Anıtkabir → Etnografya
    /// Müzesi → Ankara Kalesi → Roma Hamamı oldu.
    ///
    ///   1. TİP AĞIRLIĞI — müze mi, yol kenarı plaketi mi.
    ///
    ///   2. WIKIDATA + Q NUMARASI — Wikidata kimlikleri sırayla veriliyor,
    ///      yani KÜÇÜK numara erken eklenmiş/daha bilinen varlık demek.
    ///      Anıtkabir Q615404 ve Ankara Kalesi Q206225'e karşılık
    ///      Cin Ali Müzesi Q108816562. Tek başına "wikidata var mı?" sorusu
    ///      bu ikisini ayırt etmiyordu.
    ///
    ///   3. ÇEVİRİ SAYISI (<c>name:xx</c>) — uluslararası bilinirliğin en
    ///      güçlü işareti: Anıtkabir 19 dilde, Anadolu Medeniyetleri Müzesi
    ///      4 dilde, Cin Ali Müzesi hiç. Bu sinyal olmadan küçük müzeler
    ///      büyükleriyle aynı puanı alıyordu.
    ///
    ///   4. MİRAS/İLETİŞİM — tescilli yapı, açılış saati, web sitesi.
    /// </summary>
    private static double OnemHesapla(IDictionary<string, string> etiketler)
    {
        var puan = TipAgirligi(etiketler);

        if (etiketler.TryGetValue("wikidata", out var wikidata))
        {
            puan += 0.60;

            if (int.TryParse(wikidata.TrimStart('Q', 'q'), out var q))
            {
                if (q < 1_000_000) puan += 0.45;
                else if (q < 10_000_000) puan += 0.20;
            }
        }

        if (etiketler.ContainsKey("wikipedia")) puan += 0.30;

        var dilSayisi = etiketler.Keys.Count(k => k.StartsWith("name:", StringComparison.Ordinal));
        puan += Math.Min(1.0, dilSayisi / 8.0) * 1.10;

        if (etiketler.ContainsKey("heritage")) puan += 0.25;

        if (etiketler.ContainsKey("website") || etiketler.ContainsKey("contact:website")
            || etiketler.ContainsKey("opening_hours"))
        {
            puan += 0.10;
        }

        return Math.Clamp(puan / AzamiHamPuan, 0, 1);
    }

    /// <summary>Etiketlere uyan EN YÜKSEK tip ağırlığı.</summary>
    private static double TipAgirligi(IDictionary<string, string> etiketler)
    {
        var enIyi = 0.20;

        foreach (var (anahtar, deger, agirlik) in TipAgirliklari)
        {
            if (etiketler.TryGetValue(anahtar, out var mevcut)
                && string.Equals(mevcut, deger, StringComparison.OrdinalIgnoreCase)
                && agirlik > enIyi)
            {
                enIyi = agirlik;
            }
        }

        return enIyi;
    }

    /// <summary>
    /// OSM etiketlerini Google'ın tip anahtarlarına çevirir.
    ///
    /// İş katmanı mekan tipini (dolayısıyla KALIŞ SÜRESİNİ) bu listeden
    /// buluyor; boş bırakırsak her mekan aramanın tipini alırdı ve
    /// "gezilecek yer" diye aranıp bulunan bir müzeye anıt süresi verilirdi.
    /// </summary>
    private static List<string> TipleriCikar(IDictionary<string, string> etiketler)
    {
        var tipler = new List<string>();

        if (etiketler.TryGetValue("tourism", out var turizm))
        {
            tipler.Add(turizm switch
            {
                "museum" => "museum",
                "gallery" => "art_gallery",
                // Seyir noktası kendi tipi: kalış süresi ve tavanı anıttan
                // farklı (bir turda bir manzara durağı yeter).
                "viewpoint" => "viewpoint",

                // KONAKLAMA — bu üç değer eskiden "tourist_attraction"a
                // düşüyordu ve otel, gezilecek bir yer sanılıyordu: kalış
                // süresi 20 dakika (otel için 10 olmalı), tip Monument
                // görünüyordu. Konaklama durakları eklendiğinde ortaya çıktı.
                "hotel" or "guest_house" or "hostel" => "lodging",

                _ => "tourist_attraction",
            });
        }

        if (etiketler.ContainsKey("historic")) tipler.Add("monument");
        if (etiketler.TryGetValue("leisure", out var bos) && bos is "park" or "garden") tipler.Add("park");
        if (etiketler.TryGetValue("amenity", out var tesis))
        {
            if (tesis == "restaurant") tipler.Add("restaurant");
            if (tesis == "cafe") tipler.Add("cafe");
            if (tesis == "place_of_worship") tipler.Add("mosque");
            if (tesis == "marketplace") tipler.Add("shopping_mall");
        }

        if (etiketler.ContainsKey("shop")) tipler.Add("shopping_mall");

        // Atakule gibi kuleler SEYİR NOKTASI: ziyaretçi oraya manzara için
        // çıkıyor, anıt gezmeye değil. Kalış süresi de ona göre (30 dk).
        if (etiketler.TryGetValue("man_made", out var yapi) && yapi == "tower")
        {
            tipler.Add("viewpoint");
        }

        // Hicbir etiket eslesmediyse liste bos kaliyor; is katmani o zaman
        // aramanin tipine dusuyor (TurKatalogu.MekanTipi).
        return tipler;
    }

    /// <summary>
    /// OSM'nin beslenme etiketleri: <c>diet:vegetarian</c> / <c>diet:vegan</c>.
    ///
    /// "yes"/"only" → var, "no" → yok, etiket hiç yoksa BİLİNMİYOR (null).
    /// Bilinmeyeni "yok" saymak, etiketi girilmemiş bütün mekanları elerdi —
    /// Türkiye'de bu etiket seyrek doldurulduğu için listeyi boşaltırdı.
    /// </summary>
    private static bool? VejetaryenVarMi(IDictionary<string, string> etiketler)
    {
        foreach (var anahtar in new[] { "diet:vegan", "diet:vegetarian" })
        {
            if (!etiketler.TryGetValue(anahtar, out var deger)) continue;

            if (deger is "yes" or "only") return true;
            if (deger is "no") return false;
        }

        return null;
    }

    /// <summary>
    /// Önbellek anahtarı — merkez ~1 km ızgaraya yuvarlanıyor (PlacesClient
    /// ile aynı gerekçe). Puan burada anahtarın parçası DEĞİL: bu kaynakta
    /// puan süzgeci hiç uygulanmıyor.
    /// </summary>
    private static string OnbellekAnahtari(IReadOnlyList<PlacesAramasi> aramalar)
    {
        var parcalar = aramalar.Select(a =>
        {
            var lat = Math.Round(a.Lat, 2).ToString("0.00", CultureInfo.InvariantCulture);
            var lon = Math.Round(a.Lon, 2).ToString("0.00", CultureInfo.InvariantCulture);
            return $"{a.Tip}@{lat},{lon}/{a.YaricapMetre}";
        });

        return "osm:" + string.Join('|', parcalar);
    }

    // ---- Overpass cevabının JSON karşılığı ----

    private class OverpassCevabi
    {
        [JsonPropertyName("elements")]
        public List<OverpassOgesi>? Elements { get; set; }
    }

    private class OverpassOgesi
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "node";

        [JsonPropertyName("id")]
        public long Id { get; set; }

        /// <summary>Nokta (node) kayıtlarında dolu.</summary>
        [JsonPropertyName("lat")]
        public double? Lat { get; set; }

        [JsonPropertyName("lon")]
        public double? Lon { get; set; }

        /// <summary>Alan (way) kayıtlarında merkez — "out center" bunu üretiyor.</summary>
        [JsonPropertyName("center")]
        public OverpassMerkez? Center { get; set; }

        [JsonPropertyName("tags")]
        public Dictionary<string, string>? Tags { get; set; }
    }

    private class OverpassMerkez
    {
        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lon")]
        public double Lon { get; set; }
    }
}
