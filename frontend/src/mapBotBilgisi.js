// ============================================================================
//  MAP BOT — bilgi tabanı ve cevap eşleştirme (saf hesap)
//
//  ---- NEDEN BİR DİL MODELİ DEĞİL? ----
//  Bir LLM'e bağlamak cazip ama bu uygulama için yanlış olurdu:
//
//    • Anahtar, kota ve fatura gerektirir — tur önerisinde tam da bundan
//      kaçınmak için anahtarsız kaynağa (OpenStreetMap) geçildi.
//    • Cevaplar DOĞRULANAMAZ. "Tur nasıl paylaşılır?" sorusuna uydurulmuş
//      bir adım listesi, kullanıcıyı olmayan bir düğmeyi aramaya gönderir.
//    • Sorular sonlu: bu uygulamada kullanıcının sorabileceği şeyler
//      sayılabilir kadar az.
//
//  Bilgi tabanı elle yazıldığı için her cevap DOĞRU ve uygulamanın gerçek
//  davranışına bağlı. Bilmediğini de söylüyor — uydurmuyor.
//
//  ---- EŞLEŞTİRME NASIL ÇALIŞIYOR? ----
//  Her konunun anahtar kelimeleri var; kullanıcının cümlesi sadeleştirilip
//  (küçük harf, Türkçe karakterler, ekler) kelimelere bölünüyor ve en çok
//  eşleşen konu kazanıyor. Tam cümle eşleşmesi aramıyoruz: kimse
//  "Tur nasıl paylaşılır?" diye yazmıyor, "paylaşım" diye yazıyor.
// ============================================================================

/** Türkçe karakterleri sadeleştirir — "Şehir" ile "sehir" aynı sayılsın. */
export function sadelestir(metin) {
  return (metin ?? '')
    .toLocaleLowerCase('tr')
    .replace(/ı/g, 'i').replace(/ş/g, 's').replace(/ğ/g, 'g')
    .replace(/ü/g, 'u').replace(/ö/g, 'o').replace(/ç/g, 'c')
    // Şapkalı harfler: "güzergâh" ile "guzergah" aynı kelime.
    .replace(/[âÂ]/g, 'a').replace(/[îÎ]/g, 'i').replace(/[ûÛ]/g, 'u')
    .replace(/[^a-z0-9\s]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}

/**
 * BİLGİ TABANI.
 *
 * Her kayıt: anahtar kelimeler + cevap. Cevaplar kısa tutuldu — sohbet
 * balonunda üç satırdan uzun metin okunmuyor, kullanıcı zaten bir işi
 * yapmaya çalışıyor.
 */
export const KONULAR = [
  {
    ad: 'selam',
    anahtarlar: ['selam', 'merhaba', 'iyi gunler', 'hey', 'naber', 'kimsin', 'nesin'],
    cevap: 'Merhaba! Ben Map Bot. Rota ve tur oluşturma, POI ekleme, paylaşım '
      + 've harita araçları hakkında soru sorabilirsiniz.',
  },
  {
    ad: 'tur-olustur',
    anahtarlar: ['tur', 'rota', 'olustur', 'gezi', 'plan', 'planla', 'nasil tur'],
    cevap: 'Soldaki panelden **Tur Planla**\'yı açın: şehir, ulaşım tipi, süre ve '
      + 'tema seçip **Rota Oluştur** deyin. Öneri haritada çizilir, altında da '
      + 'gün gün saatli program çıkar.',
    eylem: { ad: 'tur-panel', etiket: 'Tur Planla’yı aç' },
  },
  {
    ad: 'tur-duzenle',
    anahtarlar: ['durak ekle', 'durak', 'cikar', 'sil', 'duzenle', 'siralama', 'tasi'],
    cevap: 'Program listesindeki her durağın sağında **↑ ↓ ×** düğmeleri var: '
      + 'sırayı değiştirir ya da durağı çıkarır. Listenin altındaki arama '
      + 'kutusundan POI arayıp rotaya ekleyebilirsiniz — rota her değişiklikte '
      + 'yeniden çizilir.',
  },
  {
    ad: 'paylasim',
    anahtarlar: ['paylas', 'paylasim', 'link', 'baglanti', 'kod', 'davet', 'gonder'],
    cevap: 'Öneri panelinde **Turu Kaydet ve Paylaş** deyin; size bir bağlantı ve '
      + '6 haneli katılım kodu verilir. Bağlantıyı açan kişi **hesap açmadan** '
      + 'turu görür, kodu olan da giriş ekranındaki bağlantıdan katılabilir.',
  },
  {
    ad: 'yoklama',
    anahtarlar: ['yoklama', 'kimler', 'burada', 'sayim', 'acil', 'kayip', 'iletisim'],
    cevap: 'Canlı tur ekranındaki **Yoklama** bölümünden soru sorup grup sayısını '
      + 'girin. Katılımcılar "Buradayım / Değilim / Acil" der, siz de '
      + '"15 kişinin 13\'ü buradayım" gibi bir özet görürsünüz.',
  },
  {
    ad: 'canli-konum',
    anahtarlar: ['konum', 'canli', 'gps', 'neredeyiz', 'takip', 'nerede'],
    cevap: 'Rehber, canlı tur ekranında **Konumumu paylaş** derse bağlantıyı açan '
      + 'herkes grubu haritada canlı görür. Kapattığınızda konum sunucudan da '
      + 'silinir.',
  },
  {
    ad: 'mola-konaklama',
    anahtarlar: ['yemek', 'mola', 'ogle', 'konaklama', 'otel', 'uyku', 'gece', 'serbest zaman'],
    cevap: 'Tur formunun **6 · Molalar** adımında üç seçenek var: yemek molası '
      + '(güne bir öğle arası), serbest zaman (bir saat gezinme payı) ve çok '
      + 'günlü turlarda gece konaklaması. Beslenme kısıtınız mola durağına da '
      + 'uygulanır.',
  },
  {
    ad: 'beslenme',
    anahtarlar: ['vegan', 'vejetaryen', 'beslenme', 'kisit', 'et yemem'],
    cevap: 'Tur formunun **5 · Beslenme kısıtı** adımından Vegan ya da Vejetaryen '
      + 'seçin. Yalnızca yeme-içme durakları süzülür; müze ve park duraklarına '
      + 'dokunulmaz.',
  },
  {
    ad: 'poi',
    anahtarlar: ['poi', 'nokta', 'isaret', 'kategori', 'ekle', 'mekan'],
    cevap: 'Araç paletinden **POI**\'yi seçip haritada yere tıklayın; açılan formda '
      + 'isim, kategori ve mesai girin. Kategoriler yönetim panelindeki '
      + 'POI Yönetimi ekranından tanımlanır.',
  },
  {
    ad: 'poi-gorunmuyor',
    anahtarlar: ['gorunmuyor', 'gozukmuyor', 'yok', 'bos', 'kayboldu', 'cikmiyor'],
    cevap: 'Harita bilerek temiz açılıyor: **POI Kategorileri** bölümünden '
      + '"Hepsini göster" deyin ya da görmek istediğiniz kategorileri '
      + 'işaretleyin.',
    eylem: { ad: 'poi-hepsi', etiket: 'Bütün POI’leri göster' },
  },
  {
    ad: 'simulasyon',
    anahtarlar: ['simulasyon', 'oynat', 'animasyon', 'canlandir', 'arac'],
    cevap: 'Tur panelinde **Rotayı oynat** ile güzergâh baştan sona oynatılır. '
      + 'Canlı bir turdaysanız simge grubun bulunduğu durakta durur, düğme de '
      + '"Sıradaki durağa git" olur.',
  },
  {
    ad: 'sure',
    anahtarlar: ['ne kadar surer', 'yavas', 'uzun suruyor', 'bekliyor', 'hizli'],
    cevap: 'Rota önerisi mekanları OpenStreetMap\'ten çekiyor; ilk istek birkaç '
      + 'saniye sürebilir. Aynı şehir ve tema için sonraki istekler önbellekten '
      + 'gelir ve çok daha hızlıdır.',
  },
  {
    ad: 'yetki',
    anahtarlar: ['yetki', 'rol', 'goremiyorum', 'izin', 'admin', 'yonetim'],
    cevap: 'Bir düğmeyi hiç göremiyorsanız o özelliğin yetkisi hesabınızda yok. '
      + 'Yetkiler yönetim panelindeki **Rol Yönetimi** ekranından veriliyor.',
  },
  {
    ad: 'harita-araclari',
    anahtarlar: ['turkiye', 'dunya', 'yakinlastir', 'uzaklastir', 'geri don', 'animasyon'],
    cevap: 'Haritanın sol üstündeki **Türkiye** düğmesi görünümü ülkeye döndürür, '
      + '**Dünya** ise açılış animasyonunu tekrar oynatır.',
  },
  {
    ad: 'kaydedilmis-tur',
    anahtarlar: ['hazir tur', 'kaydettigim', 'eski tur', 'tekrar', 'yeniden baslat'],
    cevap: 'Yönetim panelindeki **Tur Yönetimi** ekranında kayıtlı turlarınız '
      + 'listelenir. Her biri için "Başlat ve paylaş" diyerek yeni bir grup '
      + 'açabilirsiniz.',
  },
  {
    ad: 'konum-analizi',
    anahtarlar: ['konum analizi', 'isi haritasi', 'uygunluk', 'skor', 'nereye acsam'],
    cevap: 'Panelin **Konum Analizi** bölümünde bir bölge ve ağırlıklı kriterler '
      + '(okula yakın, parka yakın…) seçin. Sonuç bir ısı yüzeyi: sarı bölgeler '
      + 'kriterlere en uygun yerler.',
    eylem: { ad: 'konum-analizi', etiket: 'Konum Analizi’ni aç' },
  },
  {
    ad: 'erisilebilirlik',
    anahtarlar: ['erisilebilirlik', 'toplu tasima', 'otobus', 'duraga uzaklik', 'yuruyerek'],
    cevap: '**Toplu Taşıma Erişilebilirliği** bölümünden bir şehir seçip Analiz Et '
      + 'deyin. Her noktanın en yakın durağa uzaklığı hesaplanır ve "alanın %62\'si '
      + 'bir durağa 400 metreden yakın" gibi bir özet çıkar.',
    eylem: { ad: 'erisilebilirlik', etiket: 'Erişilebilirlik analizini aç' },
  },
  {
    ad: 'kesisim',
    anahtarlar: ['kesisim', 'cakisma', 'ust uste', 'icine dusen'],
    cevap: 'Bir alan çizip **Kesişim Analizi** deyin: o alana giren bütün kayıtlar '
      + 'listelenir. Çizdiğiniz alan veritabanına kaydedilmez, yalnızca sorgu '
      + 'olarak kullanılır.',
  },
  {
    ad: 'cizim',
    anahtarlar: ['ciz', 'cizim', 'polygon', 'alan', 'cizgi', 'geometri', 'wkt'],
    cevap: 'Araç paletinden **Nokta / Çizgi / Alan**\'dan birini seçip haritaya '
      + 'tıklayın; bitirmek için çift tıklayın. Kaydettiğiniz geometriler soldaki '
      + 'listede görünür ve oradan düzenlenip silinebilir.',
  },
  {
    ad: 'guzergah',
    anahtarlar: ['guzergah', 'hat', 'sefer', 'ulasim hatti', 'alternatif'],
    cevap: '**Ulaşım** bölümünden hat ve duraklarını yönetirsiniz. Bir durağa '
      + 'tıklayınca oraya giden yol alternatifleri çıkar; birine tıklarsanız '
      + 'güzergâhın tamamı o yoldan çizilir.',
  },
  {
    ad: 'cop-kutusu',
    anahtarlar: ['cop kutusu', 'silinen', 'geri al', 'kurtar', 'kalici sil'],
    cevap: 'Silinen kayıtlar yok olmaz, **Çöp Kutusu**\'na düşer; oradan geri '
      + 'alınabilir. Gerçekten kurtulmak isterseniz aynı ekrandaki **Kalıcı Sil** '
      + 'düğmesi kaydı veritabanından çıkarır — bu işlem geri alınamaz.',
    eylem: { ad: 'cop-kutusu', etiket: 'Çöp Kutusu’nu aç' },
  },
  {
    ad: 'davet-kodu',
    anahtarlar: ['davet kodu', 'admin kodu', 'bagli kullanici', 'ekibim', 'onay bekliyor'],
    cevap: 'Her yöneticinin bir **davet kodu** var (yönetim panelinde görünür). '
      + 'Yeni kullanıcı kayıt olurken bu kodu girerse hesabı doğrudan onaylanır '
      + 've o yöneticinin ekibine bağlanır; kodsuz kayıtlar onay bekler.',
  },
  {
    ad: 'misafir',
    // "katilim kodu" BİLEREK yok: o soruyu paylaşım konusu cevaplıyor (kodun
    // nereden geldiğini anlatan taraf orası). Buraya da koysaydık çok
    // kelimeli anahtar yüksek puan alıp paylaşımı bastırırdı.
    anahtarlar: ['hesapsiz', 'misafir', 'uye olmadan', 'kayit olmadan'],
    cevap: 'Giriş ekranındaki **Hesapsız girin** bağlantısına 6 haneli katılım '
      + 'kodunu ve adınızı yazın. Ad, rehberin yoklama listesinde görünür — '
      + '"13 kişi buradayım" yerine kimin cevapladığı bilinsin diye.',
  },
  {
    ad: 'rehbere-ulas',
    anahtarlar: ['rehbere', 'telefon', 'numara', 'acil cagri', 'yardim cagir'],
    cevap: 'Canlı tur ekranında **Rehberi ara** düğmesi rehberin numarasını '
      + 'telefonunuzda açar. Yoklamada "Acil" derseniz kendi numaranızı da '
      + 'bırakabilirsiniz; rehber sizi listede en üstte görür.',
  },
  {
    ad: 'katman',
    anahtarlar: ['katman', 'wms', 'geoserver', 'uydu', 'harita altligi'],
    cevap: 'Panelin üstündeki katman düğmeleriyle altlığı ve WMS katmanını '
      + 'açıp kapatabilirsiniz. WMS düğmesi ancak GeoServer bağlantısı varken '
      + 'etkinleşir; yoksa uygulama yine çalışır, yalnızca o katman gelmez.',
  },
  {
    ad: 'ne-yapabilirsin',
    anahtarlar: ['ne yapabilirsin', 'neler biliyorsun', 'yardim', 'komut', 'ozellik'],
    cevap: 'Şunları anlatabilirim: tur ve rota oluşturma, durak düzenleme, mola ve '
      + 'konaklama, beslenme kısıtı, paylaşım ve katılım kodu, yoklama, canlı '
      + 'konum, POI ekleme, çizim araçları, konum ve erişilebilirlik analizi, '
      + 'çöp kutusu, davet kodu ve yetkiler.',
  },
]

/**
 * Sohbet açılışında gösterilen ÖRNEK SORULAR.
 *
 * Boş bir sohbet kutusu "ne sorabilirim?" diye düşündürüyor ve çoğu kullanıcı
 * hiç yazmadan kapatıyor. Hazır sorular botun kapsamını tek bakışta gösteriyor
 * — bilgi tabanının reklam panosu gibi. Tıklanınca doğrudan soruluyorlar.
 */
export const ONERILEN_SORULAR = [
  'Nasıl tur oluştururum?',
  'Turu nasıl paylaşırım?',
  'Yoklama nasıl çalışır?',
  'Konaklama nasıl eklenir?',
  'Davet kodu ne işe yarar?',
  'Erişilebilirlik analizi nedir?',
]

/**
 * Her konunun DOĞAL SORU HÂLİ — "devam soruları" için.
 *
 * Konu nesnesinin içine değil ayrı bir tabloya yazıldı: konu kaydının işi
 * cevap vermek, soru üretmek değil. Ayrıca burada eksik kalan bir konu
 * sessizce atlanıyor (devam sorusu çıkmıyor), bilgi tabanını bozmuyor.
 */
const SORU_METNI = {
  'tur-olustur': 'Nasıl tur oluştururum?',
  'tur-duzenle': 'Durak nasıl eklenir veya çıkarılır?',
  paylasim: 'Turu nasıl paylaşırım?',
  yoklama: 'Yoklama nasıl çalışır?',
  'canli-konum': 'Canlı konum nasıl paylaşılır?',
  'mola-konaklama': 'Konaklama nasıl eklenir?',
  beslenme: 'Vejetaryen seçenek var mı?',
  poi: 'POI nasıl eklenir?',
  'poi-gorunmuyor': 'POI’ler neden görünmüyor?',
  simulasyon: 'Rotayı nasıl oynatırım?',
  sure: 'Rota neden yavaş geliyor?',
  yetki: 'Bir düğmeyi neden göremiyorum?',
  'harita-araclari': 'Türkiye ve Dünya düğmeleri ne yapıyor?',
  'kaydedilmis-tur': 'Kayıtlı turlarımı nerede görürüm?',
  'konum-analizi': 'Konum analizi nasıl yapılır?',
  erisilebilirlik: 'Erişilebilirlik analizi nedir?',
  kesisim: 'Kesişim analizi nedir?',
  cizim: 'Haritaya nasıl alan çizerim?',
  guzergah: 'Güzergâh alternatifleri nasıl çalışıyor?',
  'cop-kutusu': 'Sildiğim kayıt nereye gidiyor?',
  'davet-kodu': 'Davet kodu ne işe yarar?',
  misafir: 'Hesapsız nasıl girilir?',
  'rehbere-ulas': 'Rehbere nasıl ulaşırım?',
  katman: 'Harita katmanlarını nasıl değiştiririm?',
}

/**
 * DEVAM SORULARI — bir cevabın ardından hangi konular mantıklı geliyor.
 *
 * Sohbetin en zayıf anı cevabın bittiği an: kullanıcı ne soracağını
 * bilmiyorsa pencereyi kapatıyor. Buradaki bağlar, tek bir cevabı bir
 * öğrenme zincirine çeviriyor — "tur oluşturdum, peki nasıl paylaşırım?"
 * sorusu zaten sıradaki adım.
 */
const ILGILI = {
  'tur-olustur': ['tur-duzenle', 'mola-konaklama', 'paylasim'],
  'tur-duzenle': ['mola-konaklama', 'simulasyon', 'tur-olustur'],
  paylasim: ['misafir', 'yoklama', 'canli-konum'],
  yoklama: ['rehbere-ulas', 'canli-konum', 'misafir'],
  'canli-konum': ['simulasyon', 'yoklama'],
  'mola-konaklama': ['beslenme', 'tur-duzenle'],
  beslenme: ['mola-konaklama', 'tur-olustur'],
  poi: ['poi-gorunmuyor', 'cizim', 'tur-duzenle'],
  'poi-gorunmuyor': ['poi', 'katman'],
  simulasyon: ['canli-konum', 'paylasim'],
  sure: ['tur-olustur', 'katman'],
  yetki: ['davet-kodu', 'kaydedilmis-tur'],
  'harita-araclari': ['katman', 'cizim'],
  'kaydedilmis-tur': ['paylasim', 'yoklama'],
  'konum-analizi': ['erisilebilirlik', 'kesisim', 'cizim'],
  erisilebilirlik: ['konum-analizi', 'guzergah'],
  kesisim: ['cizim', 'konum-analizi'],
  cizim: ['kesisim', 'poi'],
  guzergah: ['erisilebilirlik', 'cizim'],
  'cop-kutusu': ['yetki', 'poi'],
  'davet-kodu': ['yetki', 'misafir'],
  misafir: ['paylasim', 'yoklama', 'rehbere-ulas'],
  'rehbere-ulas': ['yoklama', 'canli-konum'],
  katman: ['harita-araclari', 'poi-gorunmuyor'],
  selam: ['tur-olustur', 'paylasim', 'yoklama'],
  'ne-yapabilirsin': ['tur-olustur', 'konum-analizi', 'yoklama'],
}

/**
 * Bir cevabın ardından sorulabilecek soruları verir (en fazla 3).
 *
 * Metni olmayan konu atlanıyor: yarım bir düğme göstermektense hiç
 * göstermemek daha iyi.
 */
export function devamSorulari(konuAdi) {
  return (ILGILI[konuAdi] ?? [])
    .map((ad) => SORU_METNI[ad])
    .filter(Boolean)
    .slice(0, 3)
}

/**
 * Cevap metnini **kalın** parçalarına ayırır.
 *
 * Bilgi tabanındaki cevaplar düğme adlarını `**Tur Planla**` biçiminde
 * işaretliyor. Bunu ham metin olarak basınca kullanıcı ekranda yıldızları
 * görüyordu. Küçük bir Markdown ayrıştırıcısı — tek kural, tek biçim:
 * `dangerouslySetInnerHTML` gerekmiyor, cevaplar React düğümü olarak
 * basılıyor (bilgi tabanı bizim yazdığımız metin olsa da HTML enjeksiyonuna
 * kapı açmanın bir sebebi yok).
 *
 * @returns {{kalin: boolean, metin: string}[]}
 */
export function vurguluParcala(metin) {
  return (metin ?? '')
    .split(/(\*\*[^*]+\*\*)/g)
    .filter((parca) => parca.length > 0)
    .map((parca) => (parca.startsWith('**') && parca.endsWith('**')
      ? { kalin: true, metin: parca.slice(2, -2) }
      : { kalin: false, metin: parca }))
}

/** Hiçbir konu eşleşmediğinde. */
export const BILMIYORUM =
  'Bunu bilmiyorum. Tur oluşturma, durak düzenleme, paylaşım, yoklama, canlı '
  + 'konum, POI ekleme ve yetkiler hakkında sorabilirsiniz.'

export const KARSILAMA =
  'Sana yardımcı olmak için buradayım, adım Map Bot. Tur ve rota oluşturma, '
  + 'POI ekleme, paylaşım… ne merak ediyorsan sor.'

/**
 * Soruya en uygun cevabı bulur.
 *
 * ---- NEDEN ALT DİZE DEĞİL, KELİME BAŞI EŞLEŞMESİ? ----
 * İlk sürüm anahtarı cümlenin içinde alt dize olarak arıyordu ve sessizce
 * yanlış konuyu seçiyordu: "nasıl" kelimesi "sil" anahtarını içerdiği için
 * "yoklama nasıl yapılır" sorusu durak DÜZENLEME konusuna düşüyordu.
 *
 * Türkçe eklemeli bir dil — "tur" değil "turu", "poi" değil "poiler"
 * yazılıyor — bu yüzden tam kelime eşleşmesi de yetmiyor. Doğru ölçü,
 * kelimenin anahtarla BAŞLAMASI: "poiler".startsWith("poi") doğru,
 * "nasil".startsWith("sil") yanlış.
 *
 * ---- NEDEN PUAN ANAHTARIN UZUNLUĞU? ----
 * Uzun anahtar daha ÖZEL bir işaret. "poiler görünmüyor" sorusunda hem
 * "poi" (POI ekleme) hem "gorunmuyor" (POI görünmüyor) eşleşiyor; sayıya
 * baksaydık berabere kalır, listede önce gelen kazanırdı. Uzunluğa bakınca
 * daha özgül olan konu kazanıyor.
 *
 * Çok kelimeli anahtarlar ("serbest zaman") cümlede olduğu gibi aranıyor ve
 * doğal olarak en yüksek puanı alıyorlar.
 *
 * @returns {{ad: string, cevap: string}}
 */
export function cevapBul(soru) {
  const metin = sadelestir(soru)

  if (metin.length === 0) return { ad: 'bos', cevap: BILMIYORUM, eylem: null }

  const kelimeler = metin.split(' ')

  let enIyi = null
  let enIyiPuan = 0

  for (const konu of KONULAR) {
    let puan = 0

    for (const anahtar of konu.anahtarlar) {
      const eslesti = anahtar.includes(' ')
        ? metin.includes(anahtar)                              // kalıp
        : kelimeler.some((k) => k.startsWith(anahtar))         // kelime + ek

      if (eslesti) puan += anahtar.length
    }

    if (puan > enIyiPuan) {
      enIyiPuan = puan
      enIyi = konu
    }
  }

  return enIyi
    ? { ad: enIyi.ad, cevap: enIyi.cevap, eylem: enIyi.eylem ?? null }
    : { ad: 'bilinmiyor', cevap: BILMIYORUM, eylem: null }
}
