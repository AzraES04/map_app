using System.Globalization;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using StajProject.Business.DTOs;
using StajProject.Business.Tur;
using StajProject.Business.Validation;
using StajProject.DataAccess.Google;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

/// <summary>
/// Tur rota önerisi — Google Places (mekanlar) + Directions (sıra ve rota).
///
/// ============================ AKIŞ ============================
///   1. Girdiyi doğrula
///   2. Şehrin merkez koordinatını bul            (veritabanı — istek YOK)
///   3. Temaya göre 4.5+ mekanları ara            (≤ TemaBasinaArama istek)
///   4. Adayları süz, tipini ve KALIŞ SÜRESİNİ ata (yerel — istek YOK)
///   5. Süreye sığacak kadar aday seç             (yerel — istek YOK)
///   6. Sırayı ve rotayı çözdür                   (1 istek)
///   7. Toplam süreye sığdır, gerekiyorsa kırp    (yerel)
///   8. Kırpıldıysa BİR KEZ yeniden sıralat       (en fazla 1 istek)
///
/// ============ İSTEK BÜTÇESİ: NEDEN BÖYLE KURULDU? ============
/// Bir öneri, kaç mekan bulunursa bulunsun EN FAZLA 6 dış istek harcıyor
/// (4 Places + 2 Directions) ve önbellek isabet ederse 0.
///
/// Kaçınılan naif yollar ve maliyetleri:
///   • Mekan başına "Place Details" çağrısı            → N istek
///   • Mesafe matrisi + kendi sıralama algoritmamız    → N×N hücre
///   • Her kırpmada yeniden sıralatmak                 → sınırsız döngü
///   • Toplu taşımada bacak bacak süre sorgusu         → N istek
/// Bunların hiçbiri yapılmıyor; her birinin yerine ne konduğu ilgili adımda
/// yazılı.
/// </summary>
public class TurPlanlamaServisi : ITurPlanlamaServisi
{
    /// <summary>
    /// Bir öneride Directions'a atılabilecek en fazla istek.
    ///
    /// İlki sıralama, ikincisi kırpma sonrası yeniden sıralama. ÜÇÜNCÜSÜ YOK
    /// ve bu bilinçli: "kırp → yeniden sırala → yine sığmadı → kırp" döngüsü
    /// teorik olarak durabilir ama her turda kaç kez döneceği önceden
    /// bilinemez. İkinci turda hâlâ sığmıyorsa yeniden sıralatmadan kırpıyoruz;
    /// sonuç birkaç dakika daha az optimal, maliyet ise ÖNGÖRÜLEBİLİR.
    /// </summary>
    private const int MaxDirectionsIstegi = 2;

    /// <summary>
    /// Yemek molası ve konaklama aramalarının yarıçapı (metre).
    ///
    /// Gezi aramalarından (bkz. GoogleMapsSettings.AramaYaricapiMetre) DAR:
    /// mola durağı o günün duraklarının yanında olmak zorunda, uzaktaki bir
    /// aday zaten seçilmiyor. Sabit — ayardan TÜRETİLMİYOR: gezi yarıçapı
    /// değişse bile mola çok daha dar kalmalı, ikisi aynı orandan gelseydi
    /// gezi küçüldükçe mola da anlamsızca daralırdı.
    /// </summary>
    private const int MolaYaricapiMetre = 6_000;


    /// <summary>
    /// Bir güne eklenen serbest zaman (dakika).
    ///
    /// 60: altı duraklı bir yaya günü ~4 saat sürüyor, yemek molası bir saat
    /// daha ekliyor; kalan boşluğu doldurup günü 6-6,5 saate çıkarıyor.
    /// Daha büyük bir değer programı gereksiz şişirir, daha küçüğü fark
    /// yaratmazdı.
    /// </summary>
    private const int SerbestZamanDakika = 60;

    /// <summary>İlçe verildiğinde arama yarıçapı bu oranda daralıyor.</summary>
    private const double IlceYaricapOrani = 0.35;

    private readonly IPlacesClient _places;
    private readonly IDirectionsClient _directions;
    private readonly IIlRepository _iller;
    private readonly IPoiRepository _poiler;
    private readonly IPoiCategoryRepository _poiKategorileri;
    private readonly IIstekButcesi _butce;
    private readonly GoogleMapsSettings _ayarlar;
    private readonly ILogger<TurPlanlamaServisi> _logger;

    public TurPlanlamaServisi(
        IPlacesClient places,
        IDirectionsClient directions,
        IIlRepository iller,
        IPoiRepository poiler,
        IPoiCategoryRepository poiKategorileri,
        IIstekButcesi butce,
        GoogleMapsSettings ayarlar,
        ILogger<TurPlanlamaServisi> logger)
    {
        _places = places;
        _directions = directions;
        _iller = iller;
        _poiler = poiler;
        _poiKategorileri = poiKategorileri;
        _butce = butce;
        _ayarlar = ayarlar;
        _logger = logger;
    }

    public async Task<TurOneriDto> RotaOnerAsync(
        TurRotaIstegiDto istek,
        CancellationToken iptal = default)
    {
        Dogrula(istek);

        if (!_places.Etkin || !_directions.Etkin)
        {
            // İki kaynak da kapalı: ne Google anahtarı var ne de anahtarsız
            // kip (OpenStreetMap) açık. Mesaj ikisini de söylüyor çünkü
            // çözüm hangisini açmak istediğine göre değişiyor.
            throw new DisServisException(
                "Tur öneri servisi kullanılamıyor: Google anahtarı tanımlı değil " +
                "(GoogleMaps:ApiKey) ve anahtarsız kaynak kapalı " +
                "(YerelTurKaynagi:Enabled).");
        }

        // Maliyeti ölçmek için başlangıç sayacı. Önbellekten karşılanan
        // istekler sayacı artırmadığı için fark, GERÇEKTEN Google'a giden
        // istek sayısını veriyor.
        var baslangicSayaci = _butce.BugunKullanilan;

        var uyarilar = new List<string>();

        // ---- 2. Merkez: il sınırının ağırlık merkezi ----
        //
        // Google Geocoding'e "Ankara" diye sormak bir istek daha demekti;
        // 81 ilin sınırı zaten veritabanında (Ödev 10). Ücretsiz ve daha
        // hızlı olan yol bu.
        // ---- ÇOK ŞEHİRLİ TUR ----
        // Şehirler SIRAYLA: ilki başlangıç şehri, turun ucu oraya oturuyor.
        // Tek şehirli istekte liste tek elemanlı ve akış birebir eskisi gibi.
        var plakalar = SehirleriTopla(istek.Lokasyon, uyarilar);

        var sehirler = new List<(int Plaka, Coordinate Merkez)>();

        foreach (var plaka in plakalar)
        {
            sehirler.Add((plaka, await MerkezBulAsync(plaka)));
        }

        // Merkez = BAŞLANGIÇ şehrinin merkezi. Şehirlerin ortalamasını
        // almak cazip ama yanlış olurdu: Ankara-İstanbul turunda ortalama
        // Bolu ormanlarına düşer ve "turun ucu" oraya çakılırdı.
        var merkez = sehirler[0].Merkez;

        // Başlıkta kullanılacak adlar İL TABLOSUNDAN; istemcinin gönderdiği
        // IlAdi'ye güvenilmiyor (gerekçe TurLokasyonDto'da). Tek sorgu:
        // özet listede geometri yok, 81 satır zaten küçük.
        var sehirAdlari = await SehirAdlariniGetirAsync(plakalar);

        var kip = TurKatalogu.SeyahatKipi(istek.UlasimTipi);
        var sureCarpani = kip == SeyahatKipi.ToplaTasima ? TurKatalogu.TopluTasimaSureCarpani : 1.0;

        // Gün sayısı burada gerekiyor: konaklama araması yalnızca çok günlü
        // turda yapılıyor ve aramaların TAMAMI tek seferde gidiyor (aşağıda).
        var gunSayisi = Math.Max(1, istek.Sure.Birim == "Gun" ? istek.Sure.Deger : 1);

        // GÖNDERİLMEDİYSE ÇOK GÜNLÜ TURDA AÇIK (gerekçe TurRotaIstegiDto'da).
        var konaklamaIstendi = (istek.Konaklama ?? true) && gunSayisi > 1;

        // ---- 3-4. Aramalar ve adaylar ----
        //
        // ---- TEMA, YEMEK ve KONAKLAMA TEK İSTEKTE ----
        // Önce üçü ayrı ayrı aranıyordu ve bu iki somut soruna yol açtı:
        //
        //   1. YAVAŞLIK. Overpass sorguları 5-30 saniye sürüyor; üç ayrı
        //      çağrı, öneri süresini üç katına çıkarıyordu. Kullanıcı
        //      "rota oluşturmak çok uzun sürüyor" diye bildirdi.
        //   2. BÜTÇE TÜKENMESİ. Her çağrı istek bütçesinden bir hak
        //      harcıyor; bütçe dolduğunda SONRAKİ çağrılar sessizce boş
        //      dönüyordu. Sıradaki konaklama olduğu için "konaklama
        //      bulunamadı" hatası çıkıyordu — veri vardı, istek hakkı yoktu.
        //
        // AraAsync zaten bir LİSTE alıp tek bir birleşik sorgu üretiyor;
        // üç ayrı çağrı, o yeteneği kullanmamaktı.
        var geziAramalari = TurKatalogu.TemaAramalari(istek.Tema)
            .Take(Math.Max(1, _ayarlar.TemaBasinaArama))
            .ToList();

        // ---- GEZİ ve MOLA AYRI İKİ SORGU ----
        //
        // Bir süre tek birleşik sorgu denendi (üç ayrı çağrının yavaşlığına
        // çözüm olarak) ama İKİ yeni sorun doğurdu ve ikisi de ölçüldü:
        //
        //   1. OTELLER KAYBOLUYORDU. Overpass'in `out N` sınırı BİRLEŞİK
        //      sonuca uygulanıyor ve sonuçlar tipe/id'ye göre sıralı geliyor.
        //      Şehirdeki binlerce restoranın yanında ~50 otel, sınırın
        //      altında kalıyordu. "Gece konaklama hâlâ yok" şikâyeti buydu.
        //   2. SORGU 504 VERİYORDU. Restoranları 20 km yarıçapta taramak
        //      tek başına gezi süzgeçlerinin tamamından pahalı.
        //
        // Ölçüm (Ankara, overpass-api.de):
        //     gezi 12 km / tavan 250        → 6,4 sn
        //     mola  6 km / tavan 120        → 1,4 sn, 12 otel geldi
        //     birleşik 20 km / tavan 700    → 504 Gateway Timeout
        //
        // İki sorgu ayrı olunca her birinin kendi sonuç bütçesi oluyor ve
        // toplam süre birleşik sorgudan KISA. Paralel de denendi: Overpass
        // aynı IP'den eşzamanlı istekleri 429 ile reddediyor.
        //
        // ---- HER ŞEHİR AYRI ARANIYOR ----
        // Tek bir merkezden yarıçapı büyütmek daha ucuz olurdu ama işe
        // yaramazdı: Ankara merkezli 250 km'lik bir daire İstanbul'u da
        // kapsar, Bolu'yu ve Eskişehir'i de. Sonuç "iki şehirli tur" değil,
        // "iki şehir arasındaki her şey" olurdu. Her şehrin kendi merkezi ve
        // kendi (dar) yarıçapı, turun gerçekten SEÇİLEN şehirlerde geçmesini
        // sağlıyor.
        var sehirAdaylari = new List<List<AdayMekan>>();
        var yerelKullanildi = false;

        foreach (var sehir in sehirler)
        {
            var bulunan = await AdaylariTopla(istek, sehir.Merkez, iptal, geziAramalari);

            // ---- OVERPASS CEVAP VERMEDİYSE YEREL POI'YE DÜŞ ----
            //
            // Boş liste "bu şehirde müze yok" demek değil; ölçülen davranış
            // şu: Overpass genel sunucusu yoğunken aynı sorgu bir seferinde
            // 6 saniyede, bir seferinde 504 ile, bir seferinde 17 saniyede
            // dönüyor. İki deneme de zaman aşımına uğradığında kullanıcı 50
            // saniye bekleyip HİÇ rota alamıyordu (canlıda yaşandı).
            //
            // Oysa aradığımız veri zaten bizde: turistik POI aktarımı aynı
            // OpenStreetMap kayıtlarını veritabanına yazmıştı. Dış servis
            // düştüğünde uygulamanın en görünür özelliğinin de düşmesi için
            // hiçbir sebep yok.
            if (bulunan.Count == 0)
            {
                bulunan = await YerelAdaylariTopla(istek, sehir.Merkez, geziAramalari);

                if (bulunan.Count > 0)
                {
                    yerelKullanildi = true;
                    _logger.LogWarning(
                        "Overpass {Plaka} plakası için sonuç vermedi; yerel POI kaydına düşüldü ({Adet} aday).",
                        sehir.Plaka, bulunan.Count);
                }
            }

            sehirAdaylari.Add(bulunan);
        }

        if (yerelKullanildi)
        {
            // Sessiz kalmak, kullanıcının güncel veriyle çalıştığını
            // sanmasına yol açardı. Kayıtlar gerçek (aktarımdan gelme) ama
            // aktarım anındaki hâlleri.
            uyarilar.Add(
                "OpenStreetMap servisi yanıt vermediği için duraklar sistemdeki " +
                "kayıtlı mekanlardan seçildi. Rota geçerli; birkaç dakika sonra " +
                "tekrar denerseniz daha güncel bir öneri alabilirsiniz.");
        }

        var adaylar = sehirAdaylari.SelectMany(a => a).ToList();

        var molaAramalari = new List<TemaAramasi>();

        if (istek.YemekMolasi)
        {
            molaAramalari.AddRange(TurKatalogu.YemekMolasiAramalari);
        }

        if (konaklamaIstendi)
        {
            molaAramalari.AddRange(TurKatalogu.KonaklamaAramalari);
        }

        // Mola adayları da HER ŞEHİRDEN toplanıyor. Yalnızca başlangıç
        // şehrinden toplasaydık, ikinci şehirde geçen bir güne 350 km
        // uzaktaki bir restoran mola olarak düşerdi — mola yerleştirme
        // en yakın adayı seçiyor ama elindeki tek liste oradan gelirdi.
        var molaAdaylari = new List<AdayMekan>();

        if (molaAramalari.Count > 0)
        {
            foreach (var sehir in sehirler)
            {
                // ---- OVERPASS AZ ÖNCE DÜŞTÜYSE BİR DAHA SORMA ----
                //
                // Mola, gezi sorgusundan AYRI bir Overpass çağrısı (ayrılma
                // gerekçesi yukarıda). Gezi sorgusu zaman aşımına uğradıysa
                // aynı servise saniyeler içinde ikinci bir ağır sorgu
                // göndermek, kullanıcının beklemesini 15 saniyeden 30
                // saniyeye çıkarıyordu — ölçüldü, dört isteğin dördü de 30
                // saniye sürdü. Servis o an yoğun; ikinci sorgunun
                // başarılı olma ihtimali düşük, bedeli ise kesin.
                //
                // Doğrudan yerele düşüyoruz: yemek molasının veritabanında
                // karşılığı zaten var ("Yöresel Lezzet" kategorisi).
                var bulunan = yerelKullanildi
                    ? new List<AdayMekan>()
                    : await AdaylariTopla(istek, sehir.Merkez, iptal, molaAramalari);

                // Gezi adaylarındaki yedeğin aynısı: Overpass mola sorgusuna
                // da cevap vermeyebiliyor. Konaklamanın yerel karşılığı YOK
                // (aktarımda otel kategorisi yok) — o durumda liste boş
                // kalıyor ve mola yerleştirme zaten sessizce atlıyor.
                if (bulunan.Count == 0)
                {
                    bulunan = await YerelAdaylariTopla(istek, sehir.Merkez, molaAramalari);
                }

                molaAdaylari.AddRange(bulunan);
            }
        }

        // Gastronomi temasında yeme-içme mekanları TURUN KENDİSİ; diğer
        // temalarda ise yalnızca mola adayı.
        var gastronomi = string.Equals(istek.Tema, "Gastronomi", StringComparison.OrdinalIgnoreCase);

        if (!gastronomi)
        {
            // Süzgeç ŞEHİR LİSTELERİNE de uygulanıyor: aşağıdaki seçim
            // şehir başına kota kullanıyor ve süzülmemiş bir listeden
            // seçim yapsaydı kota, turda hiç yer almayacak kafelere
            // harcanırdı.
            for (var i = 0; i < sehirAdaylari.Count; i++)
            {
                sehirAdaylari[i] = sehirAdaylari[i]
                    .Where(a => !MekanKalitesi.YemeIcme(a.Tip))
                    .ToList();
            }

            adaylar = sehirAdaylari.SelectMany(a => a).ToList();
        }

        var yemekAdaylari = molaAdaylari.Where(a => MekanKalitesi.YemeIcme(a.Tip)).ToList();
        var otelAdaylari = molaAdaylari.Where(a => a.Tip == VenueType.Hotel).ToList();

        if (adaylar.Count == 0)
        {
            var puanNotu = _places.PuanVerisiVar
                ? $"{_ayarlar.EnAzPuan.ToString("0.0", CultureInfo.InvariantCulture)}+ puanlı "
                : string.Empty;

            throw new DisServisException(
                $"Seçilen bölgede {puanNotu}uygun mekan bulunamadı. " +
                "Temayı ya da şehri değiştirmeyi deneyin.");
        }

        // ---- 5. Süreye göre aday sayısını kıs ----
        var ortalamaKalis = (int)adaylar.Average(a => a.KalisDakika);
        var kapasite = AdaySecici.TahminiKapasite(
            istek.Sure.ToplamDakika, ortalamaKalis, kip, _ayarlar.MaxAraNokta + 2);

        // GÜNLÜK DURAK SINIRI.
        //
        // Süre bütçesi tek başına yetmiyordu: araçla 12 saatlik bir programa
        // 15 durak "sığıyor" ama kimse bir günde 15 yer gezmiyor. Canlıda
        // üretilen ilk tur tam olarak böyleydi. Sınır kip başına
        // (TurKatalogu.GunlukAzamiDurak) ve gün sayısıyla çarpılıyor.
        var durakTavani = Math.Min(kapasite, TurKatalogu.GunlukAzamiDurak(kip) * gunSayisi);

        var secilen = SehirlereBolerekSec(sehirAdaylari, durakTavani);

        if (secilen.Count < 2)
        {
            // Tek duraklı "tur" olmaz; rota da çizilemez.
            throw new DisServisException(
                "Bu süre ve tema için en az iki uygun durak bulunamadı.");
        }

        // ---- 6. Başlangıç ve varış: sıranın SABİT iki ucu ----
        var sirali = UclariYerlestir(secilen, merkez);

        var directionsCagrisi = 1;
        var rota = await _directions.SiraliRotaAsync(Koordinatlar(sirali), kip, iptal);

        if (rota is null)
        {
            throw new DisServisException(
                "Rota servisi şu an cevap vermiyor. Lütfen daha sonra tekrar deneyin.");
        }

        sirali = AdaySecici.SirayiUygula(sirali, rota.Sira);

        // ---- 7. Süre bütçesi ----
        var butce = AdaySecici.ButceyeSigdir(
            sirali, rota.Bacaklar, istek.Sure.ToplamDakika, sureCarpani);

        // ---- 8. Kırpıldıysa BİR KEZ yeniden sırala ----
        //
        // Kırpma, rotanın kuyruğunu atıyor; kalan duraklar için EN İYİ sıra
        // artık farklı olabilir (Google sırayı 12 durak için optimize etti,
        // elimizde 7 kaldı). İkinci istek bunu düzeltiyor. Sığmama devam
        // ederse üçüncü istek YOK (bkz. MaxDirectionsIstegi).
        var yenidenSiralandi = false;

        if (butce.Kirpildi
            && directionsCagrisi < MaxDirectionsIstegi
            && butce.Duraklar.Count >= 2
            && butce.Duraklar.Count < sirali.Count)
        {
            directionsCagrisi++;
            var ikinciRota = await _directions.SiraliRotaAsync(
                Koordinatlar(butce.Duraklar), kip, iptal);

            if (ikinciRota is not null)
            {
                var yeniSira = AdaySecici.SirayiUygula(butce.Duraklar, ikinciRota.Sira);
                var yeniButce = AdaySecici.ButceyeSigdir(
                    yeniSira, ikinciRota.Bacaklar, istek.Sure.ToplamDakika, sureCarpani);

                // İkinci deneme daha çok durak sığdırabildiyse onu alıyoruz;
                // sığdıramadıysa ilk sonuç yerinde kalıyor — yeniden sıralama
                // bir İYİLEŞTİRME denemesi, zorunluluk değil.
                if (yeniButce.Duraklar.Count >= butce.Duraklar.Count)
                {
                    butce = yeniButce;
                    rota = ikinciRota;
                    yenidenSiralandi = true;
                }
            }
        }

        if (butce.Duraklar.Count < 2)
        {
            throw new DisServisException(
                "Verilen süreye en az iki durak sığmıyor. Süreyi artırmayı deneyin.");
        }

        // Uyarı, SON durum ile başlangıçtaki seçim karşılaştırılarak veriliyor —
        // butce.Kirpildi bayrağına DEĞİL. Fark şurada: ikinci (yeniden sıralama)
        // turunda kalan duraklar bütçeye tam oturursa bayrak false döner, oysa
        // ilk turda atılan duraklar hâlâ öneride yok. Bayrağa baksaydık o
        // durumda kullanıcı eksik bir turu tam sanırdı.
        var atilan = sirali.Count - butce.Duraklar.Count;

        if (atilan > 0)
        {
            uyarilar.Add($"Süreye sığmayan {atilan} durak öneriden çıkarıldı.");
        }

        // ---- 9. YEMEK MOLASI ve KONAKLAMA ----
        //
        // Bütçe hesabından SONRA ekleniyorlar, önce değil. Sebep: bunlar
        // "sığarsa gelsin" durakları değil, kullanıcının AÇIKÇA istediği
        // duraklar. Bütçeye sokup elemeye bıraksaydık, süre dar geldiğinde
        // sessizce düşen ilk şey öğle yemeği olurdu — oysa kullanıcı turu
        // tam da onun için işaretledi. Programın günlük saat sınırını aşan
        // kısmı zaten gün bölmesinde bir sonraki güne kayıyor
        // (turProgrami.js), yani süre bilgisi kaybolmuyor.
        var duraklar = butce.Duraklar.ToList();
        var molaEklendi = false;

        if (istek.YemekMolasi)
        {
            // Adaylar YUKARIDA, tema aramasıyla BİRLİKTE toplandı: burada
            // yeni bir dış istek yok.
            var eklendi = AraDuraklariYerlestir(
                duraklar, yemekAdaylari, gunSayisi, gunSonuna: false,
                not: "Yemek molası");

            molaEklendi |= eklendi;

            // İSTENDİ AMA OLMADI → SÖYLE.
            // Sessiz kalmak, kullanıcının kutucuğu işaretleyip hiçbir şey
            // görmemesi ve sebebini bilmemesi demekti; canlıda tam olarak
            // bu yaşandı ("konaklama kısmı önermedi").
            if (!eklendi)
            {
                uyarilar.Add(istek.BeslenmeKisiti is "Vegan" or "Vejetaryen"
                    ? $"Bu bölgede {istek.BeslenmeKisiti.ToLowerInvariant()} uygun " +
                      "yemek molası mekanı bulunamadı."
                    : "Bu bölgede yemek molası için uygun mekan bulunamadı.");
            }
        }

        // Konaklama YALNIZCA çok günlü turda: günübirlik turda akşam eve
        // dönülüyor, otel durağı anlamsız olurdu.
        if (konaklamaIstendi)
        {
            var eklendi = AraDuraklariYerlestir(
                duraklar, otelAdaylari, gunSayisi, gunSonuna: true,
                not: "Önerilen konaklama bölgesi");

            molaEklendi |= eklendi;

            // Neden eklenemediğini AYIRT EDEREK söylüyoruz: "aday yok" ile
            // "aday var ama yerleştirilemedi" farklı sorunlar ve ikisine de
            // aynı cümleyi yazmak, hatayı aramayı zorlaştırıyordu.
            if (!eklendi)
            {
                uyarilar.Add(otelAdaylari.Count == 0
                    ? "Bu bölgede konaklama noktası bulunamadı."
                    : "Konaklama noktaları programa yerleştirilemedi.");
            }

            _logger.LogInformation(
                "Konaklama: {Aday} aday, eklendi={Eklendi}, gun={Gun}.",
                otelAdaylari.Count, eklendi, gunSayisi);
        }

        // Serbest zaman YENİ DURAK EKLEMİYOR, var olanın kalış süresini
        // uzatıyor — bu yüzden ROTAYI değiştirmiyor, yalnızca bütçeyi.
        var serbestEklendi = istek.SerbestZaman && SerbestZamanUygula(duraklar, gunSayisi);

        if (molaEklendi)
        {
            // Rota yeni durakların İÇİNDEN geçmeli; geçmezse haritadaki çizgi
            // ile durak listesi ayrışır ve "bu durağa nasıl gidiyoruz?" sorusu
            // cevapsız kalır. Bu, MaxDirectionsIstegi tavanının DIŞINDA bir
            // çağrı: tavan "sığdırma denemeleri" için, bu ise kullanıcının
            // istediği durakların rotaya işlenmesi.
            var molaliRota = await _directions.SiraliRotaAsync(
                Koordinatlar(duraklar), kip, iptal);

            if (molaliRota is not null)
            {
                rota = molaliRota;
                butce = new ButceSonucu(
                    duraklar,
                    (int)Math.Round(molaliRota.ToplamSaniye / 60.0 * sureCarpani),
                    duraklar.Sum(d => d.KalisDakika),
                    butce.Kirpildi);
            }
            else
            {
                // Rota çizilemedi ama duraklar geçerli: listeyi koruyoruz,
                // yalnızca süre tahmini eski rotadan kalıyor.
                butce = butce with { Duraklar = duraklar };
                uyarilar.Add("Mola durakları eklendi ancak rota yeniden çizilemedi.");
            }

            if (butce.ToplamDakika > istek.Sure.ToplamDakika)
            {
                uyarilar.Add(
                    "Mola ve konaklama durakları programı uzattı; günlük süreye " +
                    "sığmayan duraklar sonraki güne kayacak.");
            }
        }
        else if (serbestEklendi)
        {
            // ROTA DEĞİŞMEDİ (durak eklenmedi), yalnızca kalış süreleri
            // uzadı — ama bütçedeki liste hâlâ ESKİ kayıtları tutuyor.
            //
            // Bu satır bir hatanın sonucu: `duraklar` butce.Duraklar'ın
            // KOPYASI ve serbest zaman o kopyaya yazılıyor. Mola eklenmediği
            // durumlarda kopya hiç geri işlenmiyordu; sonuç, hiçbir hata
            // vermeden serbest zamanı yutan bir öneriydi.
            butce = butce with
            {
                Duraklar = duraklar,
                KalisDakika = duraklar.Sum(d => d.KalisDakika),
            };
        }

        if (istek.BeslenmeKisiti is "Vegan" or "Vejetaryen" && !_places.PuanVerisiVar)
        {
            uyarilar.Add(
                $"{istek.BeslenmeKisiti} kısıtı, mekanların mutfak bilgisine göre " +
                "uygulandı: OpenStreetMap'te diyet etiketi neredeyse hiç " +
                "girilmemiş. Mola durağını gitmeden önce teyit edin.");
        }

        if (!_places.PuanVerisiVar)
        {
            uyarilar.Add(
                "Mekanlar OpenStreetMap'ten geliyor: bu kaynakta kullanıcı puanı " +
                "olmadığı için 4.5+ süzgeci uygulanmadı.");
        }

        if (kip == SeyahatKipi.ToplaTasima)
        {
            uyarilar.Add(
                "Toplu taşıma süreleri tahminidir: sıralama sürüş rotasına göre " +
                "yapılıp bekleme ve aktarma payı eklenmiştir.");
        }

        var maliyet = _butce.BugunKullanilan - baslangicSayaci;

        _logger.LogInformation(
            "Tur önerisi hazırlandı: {Durak} durak, {Dakika} dk, {Istek} Google isteği " +
            "(yeniden sıralama: {Yeniden}).",
            butce.Duraklar.Count, butce.ToplamDakika, maliyet, yenidenSiralandi);

        return OneriKur(istek, butce, rota, uyarilar, maliyet, kip, sehirAdlari);
    }

    // ---------------------------------------------------------------------
    //  1 — Doğrulama
    // ---------------------------------------------------------------------

    /// <summary>
    /// Girdi kontrolü.
    ///
    /// Öznitelikler (DataAnnotations) zaten controller'da çalışıyor; buradaki
    /// kontroller servisi DOĞRUDAN çağıran testler için de geçerli olsun diye
    /// tekrarlanıyor — projedeki ikili savunma deseninin aynısı.
    /// </summary>
    private static void Dogrula(TurRotaIstegiDto istek)
    {
        if (istek.Lokasyon is null || istek.Lokasyon.IlPlaka is < 1 or > 81)
        {
            throw new IsKuraliException("Geçerli bir şehir (plaka) seçilmelidir.");
        }

        if (istek.Sure is null || istek.Sure.ToplamDakika < 30)
        {
            throw new IsKuraliException("Tur süresi en az 30 dakika olmalıdır.");
        }

        if (istek.Sure.ToplamDakika > 14 * 12 * 60)
        {
            throw new IsKuraliException("Tur süresi 14 günü aşamaz.");
        }

        if (istek.UlasimTipi is not ("Yaya" or "Arac" or "TopluTasima"))
        {
            throw new IsKuraliException("Ulaşım tipi Yaya, Arac veya TopluTasima olmalıdır.");
        }

        if (istek.BeslenmeKisiti is not ("Yok" or "Vejetaryen" or "Vegan"))
        {
            throw new IsKuraliException("Beslenme kısıtı Yok, Vejetaryen veya Vegan olmalıdır.");
        }
    }

    // ---------------------------------------------------------------------
    //  2 — Şehirler ve merkez koordinatları
    // ---------------------------------------------------------------------

    /// <summary>Bir turda gezilebilecek en fazla şehir (gerekçe DTO'da).</summary>
    private const int AzamiSehir = 3;

    /// <summary>
    /// İstekteki şehirleri SIRAYLA döndürür: başlangıç şehri her zaman ilk.
    ///
    /// Tekrarlar eleniyor (aynı şehri iki kez seçmek arama sayısını boşuna
    /// ikiye katlardı) ve sınırı aşan şehirler ATILIP UYARILIYOR — sessizce
    /// kırpsaydık kullanıcı seçtiği şehrin neden turda olmadığını anlamazdı.
    /// </summary>
    private static List<int> SehirleriTopla(TurLokasyonDto lokasyon, List<string> uyarilar)
    {
        var plakalar = new List<int> { lokasyon.IlPlaka };

        foreach (var plaka in lokasyon.EkIlPlakalari ?? new List<int>())
        {
            // Geçersiz plaka burada sessizce atlanıyor: MerkezBulAsync zaten
            // "şehir bulunamadı" diye patlardı ama bir tur önerisini
            // istemcinin listesindeki tek bozuk sayı yüzünden tamamen
            // reddetmenin faydası yok.
            if (plaka is < 1 or > 81 || plakalar.Contains(plaka)) continue;

            plakalar.Add(plaka);
        }

        if (plakalar.Count > AzamiSehir)
        {
            uyarilar.Add(
                $"Bir turda en fazla {AzamiSehir} şehir planlanabiliyor; " +
                $"ilk {AzamiSehir} şehir kullanıldı.");

            plakalar = plakalar.Take(AzamiSehir).ToList();
        }

        return plakalar;
    }

    /// <summary>
    /// Durak kotasını ŞEHİRLERE BÖLEREK seçer.
    ///
    /// ---- NEDEN HAVUZU BİRLEŞTİRİP TEK SEFERDE SEÇMİYORUZ? ----
    /// Seçim önem puanına göre sıralıyor. Ankara + Eskişehir turunda
    /// Ankara'nın kayıtları neredeyse her sırada önde olurdu ve on duraklık
    /// bir turun onu da Ankara'dan çıkardı — kullanıcı iki şehir seçmesine
    /// rağmen tek şehirli bir tur alırdı. Şehir başına kota, "birden fazla
    /// şehirle tur" isteğinin gerçekten karşılığı.
    ///
    /// Artan kota YENİDEN DAĞITILIYOR: bir şehirde yeterli aday yoksa
    /// (küçük ilçe, dar tema) o hak boşa gitmiyor, kalan şehirlerden
    /// tamamlanıyor. Aksi hâlde iki şehirli bir tur, ikinci şehirde üç aday
    /// bulunduğu için toplam beş durakta kalırdı.
    ///
    /// Tek şehirli istekte davranış eskisiyle AYNI: tek liste, tam kota.
    /// </summary>
    private static IReadOnlyList<AdayMekan> SehirlereBolerekSec(
        IReadOnlyList<List<AdayMekan>> sehirAdaylari,
        int durakTavani)
    {
        if (sehirAdaylari.Count <= 1)
        {
            return AdaySecici.Sec(sehirAdaylari.FirstOrDefault() ?? new List<AdayMekan>(), durakTavani);
        }

        var kota = Math.Max(1, durakTavani / sehirAdaylari.Count);
        var secilen = new List<AdayMekan>();

        foreach (var liste in sehirAdaylari)
        {
            secilen.AddRange(AdaySecici.Sec(liste, kota));
        }

        // Kotalardan artan hak: aynı listelerden tamamlanıyor. Seçim zaten
        // aynı PlaceId'yi eliyor, o yüzden hepsini tek havuz olarak verip
        // toplam tavana kadar doldurmak güvenli.
        if (secilen.Count < durakTavani)
        {
            var secilenIdler = secilen
                .Select(a => a.Mekan.PlaceId)
                .ToHashSet(StringComparer.Ordinal);

            var kalanlar = sehirAdaylari
                .SelectMany(a => a)
                .Where(a => !secilenIdler.Contains(a.Mekan.PlaceId));

            secilen.AddRange(AdaySecici.Sec(kalanlar, durakTavani - secilen.Count));
        }

        return secilen;
    }

    /// <summary>
    /// Plakaların il adları, VERİLEN SIRAYLA.
    ///
    /// Tabloda karşılığı olmayan plaka atlanıyor: başlığın eksik kalması,
    /// öneriyi hiç üretmemekten iyi.
    /// </summary>
    private async Task<List<string>> SehirAdlariniGetirAsync(IReadOnlyList<int> plakalar)
    {
        var iller = await _iller.OzetGetirAsync();
        var adlar = iller.ToDictionary(i => i.Id, i => i.Ad);

        return plakalar
            .Select(p => adlar.TryGetValue(p, out var ad) ? ad : null)
            .Where(ad => ad is not null)
            .Select(ad => ad!)
            .ToList();
    }

    private async Task<Coordinate> MerkezBulAsync(int plaka)
    {
        // İlin var olduğunu yine de doğruluyoruz: olmayan bir plakaya
        // koordinat vermek, kullanıcıya "şehir bulunamadı" yerine boş bir
        // öneri döndürmek olurdu.
        var geometri = await _iller.IllerinBirlesimiAsync(new[] { plaka });

        if (geometri is null)
        {
            throw new IsKuraliException($"{plaka} plakalı şehir bulunamadı.");
        }

        // ---- ŞEHİR MERKEZİ, İL SINIRININ ORTASI DEĞİL ----
        //
        // İlk gerçeklemede merkez, sınırın geometrik iç noktasıydı ve bu
        // SESSİZ bir hata üretiyordu: Ankara için (32.60, 39.69) — Kızılay'ın
        // 35 km güneybatısı — İstanbul için Çatalca civarı. Öneri "çalışıyor"
        // görünüyor ama Polatlı'daki bir müzeyi Ankara turuna koyuyordu.
        //
        // Şehrin nerede kurulduğu idari sınırdan TÜRETİLEMEZ; veri olarak
        // duruyor (bkz. SehirMerkezleri, 81 koordinat PostGIS ile denetlendi).
        var merkez = SehirMerkezleri.Bul(plaka);

        if (merkez is not null)
        {
            return new Coordinate(merkez.Value.Lon, merkez.Value.Lat);
        }

        // Listede yoksa eski davranış: sınırın içinde bir nokta. Kaba ama
        // öneriyi hiç üretmemekten iyi.
        _logger.LogWarning(
            "{Plaka} plakası için şehir merkezi tanımlı değil; il sınırının iç noktası kullanılıyor.",
            plaka);

        var icNokta = geometri.InteriorPoint;

        return new Coordinate(icNokta.X, icNokta.Y);
    }

    // ---------------------------------------------------------------------
    //  3-4 — Aramalar, süzme ve kalış süresi
    // ---------------------------------------------------------------------

    /// <summary>
    /// Temanın aramalarını çalıştırıp adayları üretir.
    ///
    /// Aramalar TEK ÇAĞRIDA veriliyor; kaç isteğe bölüneceğine kaynak karar
    /// veriyor (bkz. IPlacesClient.AraAsync): Google dördünü paralel atıyor,
    /// Overpass hepsini tek sorguda birleştiriyor. Servis bu farkı bilmiyor.
    /// </summary>
    /// <param name="ozelAramalar">
    /// Temanın kendi aramaları yerine kullanılacak liste. Yemek molası ve
    /// konaklama durakları bu yoldan aranıyor: ikisi de temaya AİT DEĞİL
    /// (gezi temalarında yeme-içme araması bilerek yok) ama aynı toplama,
    /// eleme ve kalış süresi mantığından geçmeleri gerekiyor.
    /// </param>
    private async Task<List<AdayMekan>> AdaylariTopla(
        TurRotaIstegiDto istek,
        Coordinate merkez,
        CancellationToken iptal,
        IReadOnlyList<TemaAramasi>? ozelAramalar = null)
    {
        var ilce = istek.Lokasyon.Ilce?.Trim();
        var yaricap = string.IsNullOrWhiteSpace(ilce)
            ? _ayarlar.AramaYaricapiMetre
            : (int)(_ayarlar.AramaYaricapiMetre * IlceYaricapOrani);

        // ÖZEL LİSTE VERİLMİŞSE OLDUĞU GİBİ KULLANILIYOR: çağıran taraf
        // tema aramalarını zaten kırpıp yemek/konaklama aramalarını üstüne
        // eklemiş oluyor. Burada bir kez daha kırpsaydık, listenin sonundaki
        // konaklama aramaları düşerdi.
        var aramalar = ozelAramalar?.ToList()
            ?? TurKatalogu.TemaAramalari(istek.Tema)
                .Take(Math.Max(1, _ayarlar.TemaBasinaArama))
                .ToList();

        // Tip anahtarı → temanın o arama için öngördüğü mekan tipi.
        // Cevapta tip etiketleri eşleşmezse bu tabloya düşülüyor.
        var aramaTipleri = aramalar
            .GroupBy(a => a.GoogleTipi, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().MekanTipi, StringComparer.OrdinalIgnoreCase);

        var istekler = aramalar
            .Select(a => new PlacesAramasi(
                Sorgu: SorguKur(a, ilce, istek.BeslenmeKisiti),
                Tip: a.GoogleTipi,
                Lat: merkez.Y,
                Lon: merkez.X,

                // MOLA ARAMALARI DAHA DAR BİR ALANDA.
                //
                // Yemek molası ve konaklama, o günün duraklarının YANINDA
                // olmalı; 25 km öteki bir lokanta zaten seçilmiyor. Ama
                // sorgu onu yine de tarıyordu: şehir çapında restoran ve
                // otel aramak, Overpass'te gezi süzgeçlerinin toplamından
                // daha pahalı. Dar yarıçap hem daha hızlı hem daha doğru.
                YaricapMetre: a.DarYaricap ? MolaYaricapiMetre : yaricap,
                EnAzPuan: _ayarlar.EnAzPuan,
                // TAVAN NEDEN BU KADAR YÜKSEK?
                //
                // Overpass sonuçları SIRASIZ döndürüyor; tavan neyin
                // geleceğini de belirliyor. 20'yle Ankara'da Anadolu
                // Medeniyetleri Müzesi cevaba HİÇ girmiyordu — kesilip
                // atılıyordu. Sıralamayı biz yapıyoruz (AdaySecici), o yüzden
                // önce geniş alıp sonra elemek doğru sıra.
                //
                // Google istemcisi bu değeri kendi API sınırına (20)
                // kırpıyor; orada sonuçlar zaten alaka sırasında geliyor.
                EnFazlaSonuc: _ayarlar.YerelAramaSonucu))
            .ToList();

        var mekanlar = await _places.AraAsync(istekler, iptal);

        var adaylar = new List<AdayMekan>();

        foreach (var mekan in mekanlar)
        {
            // Aramanın öngördüğü tip: mekanın birincil tipi hangi aramadan
            // geliyorsa o. Bulunamazsa "Other" — kalış süresi de ona göre.
            var varsayilanTip = mekan.BirincilTip is not null
                && aramaTipleri.TryGetValue(mekan.BirincilTip, out var eslesen)
                ? eslesen
                : aramaTipleri.Values.FirstOrDefault();

            var tip = TurKatalogu.MekanTipi(mekan, varsayilanTip);

            if (!Uygun(mekan, istek.BeslenmeKisiti, tip))
            {
                continue;
            }

            adaylar.Add(new AdayMekan(
                mekan,
                tip,
                TurKatalogu.KalisSuresiDakika(tip, mekan.DegerlendirmeSayisi)));
        }

        return adaylar;
    }

    /// <summary>
    /// YEDEK ADAY KAYNAĞI — veritabanındaki turistik POI kayıtları.
    ///
    /// Overpass cevap vermediğinde çağrılıyor (gerekçe: YerelPoiKaynagi).
    /// Aynı temayı, aynı mekan tiplerini kullanıyor — yalnızca kaynak
    /// değişiyor, tur mantığı değil.
    ///
    /// ---- ALAN SORGUSU NEDEN ZARF + ELEME? ----
    /// Depo bir Geometry alıyor ve PostGIS'te ST_Intersects çalıştırıyor.
    /// Daire üretmek için noktayı "buffer"lamak gerekirdi ama derece
    /// cinsinden buffer, enlemde ve boylamda farklı metreye denk gelir
    /// (Türkiye'de 1° boylam ≈ 85 km, 1° enlem ≈ 111 km) — daire yerine
    /// elips çıkardı. Bunun yerine KARE zarfla veritabanından kabaca
    /// çekip, kesin yarıçap elemesini burada metre cinsinden yapıyoruz.
    /// Zarf en fazla dairenin 4/π katı kayıt getiriyor; şehir ölçeğinde
    /// birkaç yüz satır.
    /// </summary>
    private async Task<List<AdayMekan>> YerelAdaylariTopla(
        TurRotaIstegiDto istek,
        Coordinate merkez,
        IReadOnlyList<TemaAramasi> aramalar)
    {
        // Aramaların istediği mekan tipleri → kategori adları.
        var tipler = aramalar
            .Select(a => a.MekanTipi)
            .Distinct()
            .ToList();

        var kategoriAdlari = tipler
            .SelectMany(YerelPoiKaynagi.KategoriAdlari)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (kategoriAdlari.Count == 0)
        {
            // Konaklama gibi yerel karşılığı olmayan aramalar: yedek yok.
            return new List<AdayMekan>();
        }

        var kategoriler = await _poiKategorileri.GetAllAsync();

        // Kategori id → o kategorinin karşılık geldiği mekan tipi.
        var tipEslemesi = new Dictionary<int, VenueType>();

        foreach (var tip in tipler)
        {
            foreach (var ad in YerelPoiKaynagi.KategoriAdlari(tip))
            {
                foreach (var kategori in kategoriler.Where(
                    k => string.Equals(k.Ad, ad, StringComparison.OrdinalIgnoreCase)))
                {
                    // İlk eşleşen tip kazanıyor: aynı kategoriyi iki tip
                    // isterse (Park ↔ Viewpoint) kalış süresi ilkine göre.
                    tipEslemesi.TryAdd(kategori.Id, tip);
                }
            }
        }

        if (tipEslemesi.Count == 0)
        {
            _logger.LogWarning(
                "Yerel yedek için kategori bulunamadı ({Adlar}). Turistik POI aktarımı yapılmamış olabilir.",
                string.Join(", ", kategoriAdlari));
            return new List<AdayMekan>();
        }

        var ilce = istek.Lokasyon.Ilce?.Trim();
        var temelYaricap = string.IsNullOrWhiteSpace(ilce)
            ? _ayarlar.AramaYaricapiMetre
            : (int)(_ayarlar.AramaYaricapiMetre * IlceYaricapOrani);

        // Mola aramaları dar yarıçapta (gezi duraklarının yanında olmalı).
        var yaricap = aramalar.All(a => a.DarYaricap) ? MolaYaricapiMetre : temelYaricap;

        var derece = ZarfDerecesi(merkez.Y, yaricap);
        var zarf = ZarfPoligonu(merkez, derece.Lon, derece.Lat);

        var poiler = await _poiler.AlandakileriGetirAsync(zarf);

        var adaylar = new List<AdayMekan>();

        foreach (var poi in poiler)
        {
            if (!poi.IsActive || poi.Geom is null) continue;
            if (!tipEslemesi.TryGetValue(poi.KategoriId, out var tip)) continue;

            // Kesin yarıçap elemesi: zarf kare, aradığımız daire.
            if (MetrikMesafe(merkez, poi.Geom.X, poi.Geom.Y) > yaricap) continue;

            var aday = YerelPoiKaynagi.Cevir(poi, tip);

            // Beslenme kısıtı yerel kayıtlarda da uygulanıyor: vegan tur
            // isteyen kullanıcıya kebapçı önermek, kaynağın değişmesiyle
            // mazur görülebilir bir şey değil.
            //
            // puanliKaynak: false — yerel POI'lerin puanı YOK ve olmayacak.
            // Sağlayıcıya baksaydık Google kipinde yedek sessizce ölürdü.
            // etiketliKaynak: false — yerel tabloda OSM etiketleri yok.
            // Mutfak/wikidata elemesini uygulamak, etiketi olmayan HER
            // kaydi elemek olurdu (bkz. MekanKalitesi).
            if (!Uygun(aday.Mekan, istek.BeslenmeKisiti, tip,
                       puanliKaynak: false, etiketliKaynak: false)) continue;

            adaylar.Add(aday);
        }

        return adaylar;
    }

    /// <summary>Verilen metre yarıçapının o enlemde kaç dereceye denk geldiği.</summary>
    private static (double Lon, double Lat) ZarfDerecesi(double enlem, double metre)
    {
        const double metreDereceEnlem = 110_540;
        const double metreDereceBoylam = 111_320;

        var boylamMetre = metreDereceBoylam * Math.Cos(enlem * Math.PI / 180.0);

        return (metre / Math.Max(1.0, boylamMetre), metre / metreDereceEnlem);
    }

    /// <summary>Merkez çevresinde kare zarf (SRID 4326).</summary>
    private static Polygon ZarfPoligonu(Coordinate merkez, double dLon, double dLat)
    {
        var fabrika = NetTopologySuite.NtsGeometryServices.Instance
            .CreateGeometryFactory(srid: 4326);

        return fabrika.CreatePolygon(new[]
        {
            new Coordinate(merkez.X - dLon, merkez.Y - dLat),
            new Coordinate(merkez.X + dLon, merkez.Y - dLat),
            new Coordinate(merkez.X + dLon, merkez.Y + dLat),
            new Coordinate(merkez.X - dLon, merkez.Y + dLat),
            new Coordinate(merkez.X - dLon, merkez.Y - dLat),
        });
    }

    /// <summary>Kaba (ekvirektangüler) metre mesafe — şehir ölçeğinde yeterli.</summary>
    private static double MetrikMesafe(Coordinate merkez, double lon, double lat)
    {
        const double metreDereceEnlem = 110_540;
        const double metreDereceBoylam = 111_320;

        var dx = (lon - merkez.X) * metreDereceBoylam * Math.Cos(merkez.Y * Math.PI / 180.0);
        var dy = (lat - merkez.Y) * metreDereceEnlem;

        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Arama metnini kurar: tema sorgusu + yer adı + beslenme ipucu.
    ///
    /// Beslenme kısıtı SORGUYA giriyor çünkü Places'te "vegan" diye bir süzme
    /// parametresi yok — yalnızca yeme-içme mekanlarında dolan
    /// <c>servesVegetarianFood</c> alanı var ve o da veganı kapsamıyor.
    /// Anahtar kelimeyi sorguya koymak, ayrı bir istek eklemeden sonuçları
    /// doğru yöne çekiyor; kesin süzme cevap üzerinde yapılıyor (bkz. Uygun).
    /// </summary>
    private static string SorguKur(TemaAramasi arama, string? ilce, string beslenme)
    {
        var sorgu = arama.Sorgu;

        if (TurKatalogu.YemeIcme(arama.MekanTipi) && beslenme is "Vegan" or "Vejetaryen")
        {
            sorgu = beslenme == "Vegan" ? $"vegan {sorgu}" : $"vejetaryen {sorgu}";
        }

        return string.IsNullOrWhiteSpace(ilce) ? sorgu : $"{sorgu} {ilce}";
    }

    /// <summary>
    /// Mekan öneriye girebilir mi?
    ///
    /// Puan süzgeci SUNUCUDA uygulanıyor (minRating) ama burada bir kez daha
    /// bakılıyor: puansız bir kayıt sızarsa 4.5 koşulu sessizce delinirdi.
    /// </summary>
    /// <param name="puanliKaynak">
    /// Aday PUAN TAŞIYAN bir kaynaktan mı geliyor?
    ///
    /// Varsayılan olarak sağlayıcıya bakılıyor ama YEREL YEDEK bunu açıkça
    /// <c>false</c> geçiyor: veritabanındaki POI kayıtlarının puanı hiç yok
    /// (OSM'den geldiler). Sağlayıcıya bakmakla yetinseydik, Google kipi
    /// açıkken yedek SESSİZCE ÖLÜRDÜ — her yerel aday "puansız" diye elenir
    /// ve Overpass düştüğünde yine "mekan bulunamadı" çıkardı. Yedeğin
    /// varlık sebebi tam olarak o durumdu.
    /// </param>
    private bool Uygun(
        GooglePlace mekan,
        string beslenme,
        VenueType aramaTipi,
        bool? puanliKaynak = null,
        bool etiketliKaynak = true)
    {
        var puanlanabilir = puanliKaynak ?? _places.PuanVerisiVar;

        // PUAN SÜZGECİ YALNIZCA PUAN TAŞIYAN KAYNAKTA.
        //
        // Anahtarsız kipte mekanlar OpenStreetMap'ten geliyor ve OSM'de
        // kullanıcı puanı yok. Süzgeci yine de uygulasaydık HİÇBİR aday
        // geçemez, öneri her seferinde "uygun mekan bulunamadı" derdi.
        // Atlandığı kullanıcıya da söyleniyor (bkz. RotaOnerAsync → uyarılar):
        // puansız veriyi "4.5+ seçildi" diye sunmak olmayan bir doğruluk
        // iddia etmek olurdu.
        if (puanlanabilir)
        {
            if (mekan.Puan is null || mekan.Puan < _ayarlar.EnAzPuan)
            {
                return false;
            }

            // Tek oyla 5.0 alan bir yer "4.5+" süzgecinden geçiyor ama bir tur
            // durağı olarak güvenilir değil.
            if (mekan.DegerlendirmeSayisi < _ayarlar.EnAzDegerlendirme)
            {
                return false;
            }
        }

        // ---- YEME-İÇME KALİTE ELEMESİ ----
        //
        // Kural POI aktarımında vardı ama ÖNERİ onu hiç çağırmıyordu: canlıda
        // üretilen Ankara turuna "Melek Cafem" ve "476. Yurt Kantini" düştü.
        // Artık ikisi de aynı yerden (MekanKalitesi) geçiyor.
        if (!MekanKalitesi.YemeIcmeUygunMu(mekan, aramaTipi, puanlanabilir, etiketliKaynak))
        {
            return false;
        }

        // ---- BESLENME KISITI ----
        //
        // Eskiden yalnızca sağlayıcının tipli alanına bakıyordu ve OSM'de o
        // alan HİÇ dolu gelmiyor: Ankara'da ölçüldü, dönen mekanların
        // hiçbirinde diyet etiketi yok. Yani kısıt sessizce uygulanmıyordu —
        // kullanıcı "vejetaryen seçeneklere dikkat etmiyor" diye bildirdi.
        //
        // Artık etiket + mutfak birlikte değerlendiriliyor (MekanKalitesi).
        if (!MekanKalitesi.BeslenmeyeUygunMu(mekan, aramaTipi, beslenme))
        {
            return false;
        }

        return true;
    }

    // ---------------------------------------------------------------------
    //  6 — Sıranın sabit uçları
    // ---------------------------------------------------------------------

    /// <summary>
    /// Başlangıcı ve varışı seçer.
    ///
    /// Directions <c>optimize:true</c> yalnızca ARA noktaları sıralıyor; ilk ve
    /// son nokta sabit kalıyor. Bu iki ucu biz seçmek zorundayız:
    ///
    ///   başlangıç = merkeze EN YAKIN aday  → tur şehir merkezinden başlar,
    ///                                        kullanıcının beklediği yer orası
    ///   varış     = merkeze EN UZAK aday   → rota merkezden dışarı doğru açılır
    ///
    /// Alternatifi ikisini de rastgele bırakmaktı; o zaman tur şehrin
    /// kenarından başlayıp merkeze dönen, ilk bacağı yarım saatlik bir rota
    /// olabilirdi. En uzağı VARIŞ yapmak ayrıca Google'a en geniş sıralama
    /// serbestliğini bırakıyor: aradaki her şey optimize edilebilir hâle
    /// geliyor.
    /// </summary>
    /// <summary>
    /// Yemek molası / konaklama duraklarını GÜN GÜN araya yerleştirir.
    ///
    /// ---- GÜN SINIRLARI NEREDEN BİLİNİYOR? ----
    /// Program gün gün bölünürken duraklar günlere DENGELİ dağıtılıyor
    /// (turProgrami.js): n durak, g gün için taban = n/g, artan ilk günlere.
    /// Buradaki bölme onun aynısı — aynı olmazsa mola öğlen değil, rastgele
    /// bir saatte çıkardı.
    ///
    /// ---- NEDEN "EN YAKIN" ADAY? ----
    /// Mola o günün ortasındaki durağın YANINDA olmalı. Şehir merkezine en
    /// yakın restoranı seçseydik, gün Anıtkabir çevresinde geçerken yemek
    /// Ulus'a düşerdi. Aynı mekan iki güne konmuyor: seçilenler işaretleniyor.
    ///
    /// <paramref name="gunSonuna"/> true ise durak günün SONUNA ekleniyor
    /// (konaklama), false ise ORTASINA (yemek). Son güne konaklama
    /// eklenmiyor — o akşam gidiliyor.
    /// </summary>
    /// <returns>En az bir durak eklendiyse true.</returns>
    private static bool AraDuraklariYerlestir(
        List<AdayMekan> duraklar,
        IReadOnlyList<AdayMekan> adaylar,
        int gunSayisi,
        bool gunSonuna,
        string not)
    {
        if (adaylar.Count == 0 || duraklar.Count == 0)
        {
            return false;
        }

        var gun = Math.Max(1, Math.Min(gunSayisi, duraklar.Count));
        var taban = duraklar.Count / gun;
        var kalan = duraklar.Count % gun;

        var kullanilan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var eklendi = false;

        // SONDAN BAŞA gidiyoruz: araya ekleme yaptıkça öndeki indisler
        // kayıyor, sondan başlayınca henüz işlenmemiş günlerin indisleri
        // bozulmuyor.
        var sinirlar = new List<(int Baslangic, int Bitis)>();
        var imlec = 0;

        for (var g = 0; g < gun; g++)
        {
            var boy = taban + (g < kalan ? 1 : 0);
            sinirlar.Add((imlec, imlec + boy - 1));
            imlec += boy;
        }

        for (var g = gun - 1; g >= 0; g--)
        {
            // Son güne konaklama yok: o akşam şehirden ayrılınıyor.
            if (gunSonuna && g == gun - 1)
            {
                continue;
            }

            var (bas, bit) = sinirlar[g];
            if (bit < bas)
            {
                continue;
            }

            // Yemek günün ortasındaki durağın yanında, konaklama son durağın.
            var referansIndis = gunSonuna ? bit : bas + (bit - bas) / 2;
            var referans = duraklar[referansIndis];

            var secilen = adaylar
                .Where(a => !kullanilan.Contains(a.Mekan.PlaceId))
                .OrderBy(a => Mesafe(a.Mekan, referans.Mekan))
                .FirstOrDefault();

            if (secilen is null)
            {
                continue;
            }

            kullanilan.Add(secilen.Mekan.PlaceId);

            // Notu mekanın üstüne yazıyoruz: kullanıcı listede "Yemek molası"
            // ya da "Önerilen konaklama bölgesi" ibaresini görsün, sıradan bir
            // gezi durağı sanmasın.
            var etiketli = secilen with { Not = not };

            duraklar.Insert(referansIndis + 1, etiketli);
            eklendi = true;
        }

        return eklendi;
    }

    /// <summary>
    /// Her güne SERBEST ZAMAN ekler — yeni durak açmadan.
    ///
    /// ---- NEDEN VAR OLAN DURAĞIN SÜRESİ UZATILIYOR? ----
    /// Serbest zaman bir yere GİTMEK değil, bir yerde KALMAK. Ayrı bir durak
    /// olarak eklenseydi haritada koordinatı olmayan ya da başka bir durakla
    /// aynı noktada duran hayalet bir işaret çıkardı; rota da kendi üstüne
    /// dönerdi.
    ///
    /// ---- HANGİ DURAK SEÇİLİYOR? ----
    /// Gezinmeye uygun olanlar: çarşı/pazar, park, ardından anıt-tarihi doku
    /// (Hamamönü, Ulus gibi semt duraklarının çoğu bu tipte). Müze seçilmiyor —
    /// müzede geçirilecek süre zaten kalış süresinde var ve orada "serbest
    /// zaman" demek kapalı bir mekanda beklemek olurdu.
    /// </summary>
    /// <returns>En az bir güne serbest zaman eklendiyse true.</returns>
    private static bool SerbestZamanUygula(List<AdayMekan> duraklar, int gunSayisi)
    {
        var eklendi = false;

        // Tercih sırası: en gezilebilir tip önce.
        var oncelik = new Dictionary<VenueType, int>
        {
            [VenueType.Shopping] = 0,
            [VenueType.Park] = 1,
            [VenueType.Monument] = 2,
            [VenueType.Viewpoint] = 3,
            [VenueType.Other] = 4,
        };

        var gun = Math.Max(1, Math.Min(gunSayisi, duraklar.Count));
        var taban = duraklar.Count / gun;
        var kalan = duraklar.Count % gun;
        var imlec = 0;

        for (var g = 0; g < gun; g++)
        {
            var boy = taban + (g < kalan ? 1 : 0);
            var bas = imlec;
            var bit = imlec + boy - 1;
            imlec += boy;

            if (boy <= 0)
            {
                continue;
            }

            var enIyi = -1;
            var enIyiSira = int.MaxValue;

            for (var i = bas; i <= bit; i++)
            {
                // Mola durakları atlanıyor: yemek zaten bir duraklama ve
                // üstüne serbest zaman eklemek öğle arasını iki saate çıkarırdı.
                if (duraklar[i].Not is not null)
                {
                    continue;
                }

                if (oncelik.TryGetValue(duraklar[i].Tip, out var sira) && sira < enIyiSira)
                {
                    enIyiSira = sira;
                    enIyi = i;
                }
            }

            // GEZİNMEYE UYGUN TİP YOKSA GÜNÜN SON DURAĞI.
            //
            // Salt müzelerden oluşan bir kültür gününde tercih listesindeki
            // hiçbir tip bulunmuyor ve serbest zaman hiç eklenmiyordu; oysa
            // özelliğin çözdüğü sorun (gün öğlen bitiyor) tam da o günde
            // vardı. Son durak seçiliyor: gezinme payı turun sonunda,
            // dönüşten önce kalıyor.
            if (enIyi < 0)
            {
                for (var i = bit; i >= bas; i--)
                {
                    if (duraklar[i].Not is null)
                    {
                        enIyi = i;
                        break;
                    }
                }
            }

            if (enIyi < 0)
            {
                continue;
            }

            duraklar[enIyi] = duraklar[enIyi] with
            {
                KalisDakika = duraklar[enIyi].KalisDakika + SerbestZamanDakika,
                Not = $"Serbest zaman (+{SerbestZamanDakika} dk)",
            };

            eklendi = true;
        }

        return eklendi;
    }

    /// <summary>İki mekan arasındaki kaba (kare) uzaklık — yalnızca sıralama için.</summary>
    private static double Mesafe(GooglePlace a, GooglePlace b)
    {
        var dx = (a.Lon - b.Lon) * Math.Cos(a.Lat * Math.PI / 180);
        var dy = a.Lat - b.Lat;
        return dx * dx + dy * dy;
    }

    private static IReadOnlyList<AdayMekan> UclariYerlestir(
        IReadOnlyList<AdayMekan> adaylar,
        Coordinate merkez)
    {
        var sirali = adaylar
            .OrderBy(a => Uzaklik(a, merkez))
            .ToList();

        if (sirali.Count < 3)
        {
            return sirali;
        }

        var baslangic = sirali[0];
        var varis = sirali[^1];
        var ara = sirali.Skip(1).Take(sirali.Count - 2);

        var sonuc = new List<AdayMekan>(sirali.Count) { baslangic };
        sonuc.AddRange(ara);
        sonuc.Add(varis);

        return sonuc;
    }

    /// <summary>
    /// Kaba (derece cinsinden) uzaklık — yalnızca SIRALAMA için kullanılıyor,
    /// gösterilen bir değer değil. Metreye çevirmek (Haversine) sıralamayı
    /// değiştirmezdi; gerçek mesafeler zaten Directions'tan geliyor.
    /// </summary>
    private static double Uzaklik(AdayMekan aday, Coordinate merkez)
    {
        var dx = aday.Mekan.Lon - merkez.X;
        var dy = aday.Mekan.Lat - merkez.Y;
        return dx * dx + dy * dy;
    }

    private static IReadOnlyList<Coordinate> Koordinatlar(IReadOnlyList<AdayMekan> adaylar)
        => adaylar.Select(a => new Coordinate(a.Mekan.Lon, a.Mekan.Lat)).ToList();

    // ---------------------------------------------------------------------
    //  Elle düzenlenmiş rota
    // ---------------------------------------------------------------------

    /// <summary>
    /// Kullanıcı durak ekleyip çıkardıktan sonra rotayı yeniden çizer.
    ///
    /// ---- SIRAYI DEĞİŞTİRMİYORUZ ----
    /// <c>SiraliRotaAsync</c> ara noktaları optimize ediyor ama dönen
    /// <c>Sira</c> alanını burada KULLANMIYORUZ: kullanıcı durağı bilerek
    /// araya soktu, sunucunun onu "daha kısa olur" diye taşıması yaptığı işi
    /// geri almak olurdu. Çizgi yine de gerçek sıraya ait — istek, noktaları
    /// verildiği sırayla gönderiyor.
    /// </summary>
    public async Task<TurRotaHesapSonucuDto> RotaHesaplaAsync(
        TurRotaHesapIstegiDto istek,
        CancellationToken iptal = default)
    {
        if (istek.Duraklar.Count < 2)
        {
            throw new IsKuraliException("Rota için en az iki durak gerekir.");
        }

        if (!_directions.Etkin)
        {
            // Rota yoksa da durak listesi geçerli bir tur: 503 yerine uyarı.
            return new TurRotaHesapSonucuDto
            {
                Uyari = "Rota servisi yapılandırılmadığı için yol çizilemedi.",
            };
        }

        var kip = TurKatalogu.SeyahatKipi(istek.UlasimTipi);
        var sureCarpani = kip == SeyahatKipi.ToplaTasima ? TurKatalogu.TopluTasimaSureCarpani : 1.0;

        var noktalar = istek.Duraklar
            .Select(d => new Coordinate(d.Lon, d.Lat))
            .ToList();

        var rota = await _directions.SiraliRotaAsync(noktalar, kip, iptal);

        if (rota is null)
        {
            return new TurRotaHesapSonucuDto
            {
                Uyari = "Yol servisine ulaşılamadı; duraklar korundu ama rota çizilemedi.",
            };
        }

        _logger.LogInformation(
            "Tur rotası yeniden hesaplandı: {Durak} durak, {Metre:0} m.",
            istek.Duraklar.Count, rota.ToplamMetre);

        return new TurRotaHesapSonucuDto
        {
            RouteWkt = rota.Cizgi?.AsText(),
            RouteDistanceMeters = rota.ToplamMetre,

            // Toplu taşımada çarpan uygulanıyor: öneri ucunda da aynı hesap
            // var, iki uç farklı süre verseydi ekrandaki toplam düzenlemeden
            // sonra sıçrardı.
            RouteDurationSeconds = rota.ToplamSaniye * sureCarpani,
        };
    }

    // ---------------------------------------------------------------------
    //  Cevap
    // ---------------------------------------------------------------------

    private TurOneriDto OneriKur(
        TurRotaIstegiDto istek,
        ButceSonucu butce,
        DirectionsRotasi rota,
        List<string> uyarilar,
        int maliyet,
        SeyahatKipi kip,
        IReadOnlyList<string> sehirAdlari)
    {
        var duraklar = butce.Duraklar
            .Select((aday, i) => new WaypointDto
            {
                // Id = 0: öneri henüz KAYDEDİLMEDİ. Sahte bir id üretmek,
                // arayüzün onu var olan bir kayıt sanmasına yol açardı.
                Id = 0,
                TourId = 0,
                Order = i + 1,

                // Sağlayıcı ön ekiyle: Waypoint.PlaceId sözleşmesi bunu
                // istiyor (tek kolonda iki sağlayıcı çakışmasın).
                PlaceId = $"google:{aday.Mekan.PlaceId}",
                PoiId = null,

                Name = aday.Mekan.Ad,
                VenueType = aday.Tip.ToString(),
                DwellMinutes = aday.KalisDakika,
                Wkt = string.Create(
                    CultureInfo.InvariantCulture,
                    $"POINT ({aday.Mekan.Lon:0.######} {aday.Mekan.Lat:0.######})"),

                // Not önceliği: durağın ROLÜ (yemek molası / konaklama) varsa
                // o yazılıyor. Puan ikincil — "neden bu mekan?" sorusundan
                // önce "bu durak ne işe yarıyor?" sorusu geliyor.
                Note = aday.Not ?? (aday.Mekan.Puan is null
                    ? null
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Google puanı {aday.Mekan.Puan:0.0} ({aday.Mekan.DegerlendirmeSayisi} değerlendirme)")),

                IsActive = true,
                CreatedDate = DateTime.UtcNow,
            })
            .ToList();

        return new TurOneriDto
        {
            Id = 0,
            Name = OneriAdi(istek, sehirAdlari),
            Description = null,
            Color = "#7b5cd6",
            Waypoints = duraklar,

            RouteWkt = rota.Cizgi?.AsText(),
            RouteDistanceMeters = rota.ToplamMetre,

            // Yol süresi BÜTÇE HESABINDAKİ değerle aynı olmalı: cevapta
            // Directions'ın ham süresini verseydik, toplu taşıma turlarında
            // arayüzdeki toplam ile sunucunun sığdırdığı süre ayrışırdı.
            RouteDurationSeconds = butce.YolDakika * 60.0,

            // Rota bu durak dizilimi için AZ ÖNCE hesaplandı.
            RouteUpToDate = true,

            IsActive = true,
            CreatedDate = DateTime.UtcNow,

            Uyarilar = uyarilar,

            // Aday seçerken kullanılan sınırın AYNISI: gün gün bölen istemci
            // hesabı da aynı sayıya uysun (gerekçe TurOneriDto'da).
            GunlukAzamiDurak = TurKatalogu.GunlukAzamiDurak(kip),

            IstekMaliyeti = maliyet,
        };
    }

    /// <summary>
    /// Öneri başlığı: "Ankara · Kültürel tur (4 sa)", çok şehirliyse
    /// "Ankara – Eskişehir · Kültürel tur (2 gün)".
    /// </summary>
    /// <param name="sehirAdlari">
    /// Şehir adları SUNUCUDAN (il tablosundan) geliyor, istekten değil:
    /// <c>IlAdi</c> alanına güvenmeme kuralının (bkz. TurLokasyonDto) aynısı.
    /// Başlangıç şehri ilk sırada.
    /// </param>
    private static string OneriAdi(TurRotaIstegiDto istek, IReadOnlyList<string> sehirAdlari)
    {
        // İlçe verildiyse tur zaten tek şehirli ve o ilçeye odaklı; şehir
        // listesini yazmak başlığı gereksiz uzatırdı.
        var sehirler = sehirAdlari.Count > 0
            ? string.Join(" – ", sehirAdlari)
            : istek.Lokasyon.IlAdi ?? $"{istek.Lokasyon.IlPlaka} plaka";

        var yer = string.IsNullOrWhiteSpace(istek.Lokasyon.Ilce)
            ? sehirler
            : $"{istek.Lokasyon.Ilce}, {sehirler}";

        var tema = istek.Tema switch
        {
            "Kulturel" => "Kültürel",
            "Populer" => "Popüler",
            "Doga" => "Doğa",
            "Gastronomi" => "Gastronomi",
            _ => "Karma",
        };

        var saat = istek.Sure.ToplamDakika / 60;
        var dakika = istek.Sure.ToplamDakika % 60;
        var sure = dakika == 0 ? $"{saat} sa" : $"{saat} sa {dakika} dk";

        return $"{yer} · {tema} tur ({sure})";
    }
}
