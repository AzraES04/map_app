// ============================================================================
//  TUR PLANI — seçenek katalogları, DOĞRULAMA ve PAYLOAD üretimi
//
//  TourBuilder bileşeninin "beyni". Bileşende yalnızca ekran (girdiler,
//  seçimler, hata gösterimi) var; kuralların tamamı burada ve HİÇBİRİ React'e
//  bağlı değil.
//
//  NEDEN AYRI DOSYA?
//    1. Doğrulama kuralları bileşen ÇİZİLMEDEN sınanabiliyor — jsdom, tıklama
//       ve bekleme olmadan (hatBacagi.js / mesai.js ile aynı gerekçe).
//    2. Aynı kurallar ileride başka bir ekrandan (örn. "turu kopyala")
//       çağrıldığında kopyalanmak zorunda kalmıyor.
//    3. Payload'ın biçimi tek yerde duruyor: rota servisinin sözleşmesi
//       değiştiğinde bakılacak tek dosya bu.
//
//  KATALOGLAR NEDEN SUNUCUDAN GELMİYOR?
//  Ulaşım tipi, tema ve beslenme kısıtı KAPALI listeler: sunucudaki rota
//  servisi de aynı değerleri bekliyor ve yeni bir seçenek eklemek her hâlükârda
//  iki tarafta da kod değişikliği demek. Sözlük tablosuna taşımak, hiç
//  değişmeyecek bir veriyi her açılışta bir istekle çekmek olurdu — POI
//  kategorileri (gerçekten değişken, yönetici düzenliyor) ile arasındaki fark
//  tam olarak bu.
//
//  ÖNEMLİ: buradaki doğrulama kullanıcıyı ERKEN uyarmak içindir. Asıl kontrol
//  sunucuda; istemci doğrulaması atlanabilir bir kolaylıktır (yetkiler.js
//  dosyasının başındaki uyarının aynısı).
// ============================================================================

// ---------------------------------------------------------------------------
//  Kataloglar
// ---------------------------------------------------------------------------

/**
 * Ulaşım tipi (Yaya / Araç / Toplu Taşıma).
 *
 * `ortalamaHizKmS` ve `gunlukAzamiKm` rota servisine GÖNDERİLMİYOR — sunucu
 * kendi hız profillerini biliyor (OSRM). Buradaki değerler yalnızca arayüzün
 * "bu sürede yaklaşık ne kadar yol" özetini yazabilmesi için: kullanıcı
 * göndermeden önce seçiminin ne anlama geldiğini görsün.
 */
export const ULASIM_TIPLERI = [
  { deger: 'Yaya', etiket: 'Yaya', ortalamaHizKmS: 4.5, gunlukAzamiKm: 12 },
  { deger: 'Arac', etiket: 'Araç', ortalamaHizKmS: 30, gunlukAzamiKm: 250 },
  { deger: 'TopluTasima', etiket: 'Toplu Taşıma', ortalamaHizKmS: 18, gunlukAzamiKm: 90 },
]

/**
 * Tur teması ve temanın öncelediği MEKAN TİPLERİ.
 *
 * Mekan tipleri turDurumu.js → MEKAN_TIPLERI değerleriyle (sunucudaki
 * VenueType enum'ı) birebir aynı olmalı. Payload'a temanın kendisi DE, açılımı
 * DA giriyor: rota servisi tema adını tanıyor ama açılımı göndermek, tema
 * tanımını tek yerde (burada) tutup sunucunun sıralama katmanını
 * değiştirmeden yeni tema eklenebilmesini sağlıyor.
 */
export const TUR_TEMALARI = [
  {
    deger: 'Kulturel',
    etiket: 'Kültürel',
    aciklama: 'Müze, anıt ve tarihî yapılar',
    mekanTipleri: ['Museum', 'Monument', 'ReligiousSite', 'Viewpoint'],
  },
  {
    deger: 'Populer',
    etiket: 'Popüler',
    aciklama: 'En çok ziyaret edilen noktalar',
    mekanTipleri: ['Monument', 'Viewpoint', 'Shopping', 'Restaurant'],
  },
  {
    deger: 'Karma',
    etiket: 'Karma',
    aciklama: 'Kültür, doğa ve mola noktaları dengeli',
    mekanTipleri: ['Museum', 'Monument', 'Park', 'Viewpoint', 'Cafe', 'Restaurant'],
  },
  {
    deger: 'Doga',
    etiket: 'Doğa',
    aciklama: 'Park, seyir noktası ve açık alanlar',
    mekanTipleri: ['Park', 'Viewpoint'],
  },
  {
    deger: 'Gastronomi',
    etiket: 'Gastronomi',
    aciklama: 'Yeme-içme ağırlıklı',
    mekanTipleri: ['Restaurant', 'Cafe', 'Shopping'],
  },
]

/**
 * Beslenme kısıtı.
 *
 * NEDEN TEK SEÇİM (çoklu değil)? Kısıtlar İÇ İÇE: vegan olan zaten
 * vejetaryendir. Çoklu seçim, "Vegan + Vejetaryen" gibi kendini tekrar eden
 * ve "Vegan + Kısıt yok" gibi çelişen kombinasyonlara kapı açardı. Tek seçim
 * en katı kısıtı söyler, gerisi ondan türer.
 *
 * `poiEtiketleri` sunucunun POI süzmesinde kullanacağı anahtarlar; "Yok" için
 * boş dizi — hiçbir süzme yapılmayacak demek.
 */
export const BESLENME_KISITLARI = [
  { deger: 'Yok', etiket: 'Kısıt yok', poiEtiketleri: [] },
  { deger: 'Vejetaryen', etiket: 'Vejetaryen', poiEtiketleri: ['vegetarian'] },
  { deger: 'Vegan', etiket: 'Vegan', poiEtiketleri: ['vegan'] },
]

/** Toplam süre iki biçimde girilebiliyor: günübirlik SAAT ya da GÜN sayısı. */
export const SURE_BIRIMLERI = [
  { deger: 'Saat', etiket: 'Günübirlik (saat)' },
  { deger: 'Gun', etiket: 'Çok günlük (gün)' },
]

/** Sınırlar — doğrulama ve girdi alanlarının min/max değerleri buradan okunuyor. */
export const SINIRLAR = {
  /** Günübirlik turda en az/en çok kaç saat. 12 saat, bir günün gezilebilir üst sınırı. */
  enAzSaat: 1,
  enCokSaat: 12,

  /** Çok günlük turda gün sayısı. 14'ten uzun tur, tek bir rotadan çok bir program. */
  enAzGun: 1,
  enCokGun: 14,

  /** Çok günlük turda BİR GÜNDE kaç saat gezilecek. */
  enAzGunlukSaat: 1,
  enCokGunlukSaat: 12,

  /** İlçe adı için üst sınır — sunucudaki kolon genişliğiyle uyumlu. */
  ilceEnCokKarakter: 100,

  /**
   * Bir turda gezilebilecek en fazla şehir (başlangıç dahil).
   *
   * Sunucudaki TurPlanlamaServisi.AzamiSehir ile AYNI olmalı. Buradaki
   * kopya yalnızca kullanıcıyı reddedilecek bir istek göndermekten
   * korumak için; BAĞLAYICI olan sunucudaki (POI aramasındaki
   * EN_AZ_ARAMA ile aynı kural).
   *
   * Sınırın sebebi hız: her şehir kendi Overpass sorgusunu açıyor.
   */
  enCokSehir: 3,
}

/** "HH:MM" biçim denetimi — 24 saatlik. */
const SAAT_BICIMI = /^([01]\d|2[0-3]):[0-5]\d$/

/** Saat girilmediğinde varsayılan başlangıç. */
export const VARSAYILAN_BASLANGIC_SAATI = '09:00'

/** Çok günlük turda öntanımlı günlük gezi süresi. */
export const VARSAYILAN_GUNLUK_SAAT = 8

// ---------------------------------------------------------------------------
//  Form
// ---------------------------------------------------------------------------

/**
 * @typedef {Object} TurFormu
 * @property {string} bolge         Coğrafi bölge; '' = süzme yok
 * @property {number} ilPlaka       BAŞLANGIÇ şehri (plaka kodu); 0 = seçilmedi
 * @property {number[]} ekIlPlakalari  Tura eklenen diğer şehirler; boş = tek şehirli
 * @property {string} ilce          İlçe adı; boş bırakılabilir
 * @property {string} ulasimTipi    ULASIM_TIPLERI değerlerinden biri
 * @property {string} sureBirimi    'Saat' | 'Gun'
 * @property {number} sureDegeri    Saat ya da gün sayısı
 * @property {number} gunlukSaat    Yalnızca 'Gun' biriminde anlamlı
 * @property {string} baslangicSaati "09:00" — turun başlama saati
 * @property {string} tema          TUR_TEMALARI değerlerinden biri
 * @property {string} beslenme      BESLENME_KISITLARI değerlerinden biri
 */

/** @returns {TurFormu} Boş form — bileşenin başlangıç durumu. */
export const bosTurFormu = () => ({
  bolge: '',
  ilPlaka: 0,
  ekIlPlakalari: [],
  ilce: '',
  ulasimTipi: 'Yaya',
  sureBirimi: 'Saat',
  sureDegeri: 4,
  gunlukSaat: VARSAYILAN_GUNLUK_SAAT,

  // Programın başlangıç saati. Çok günlü turda HER GÜN bu saatte başlıyor:
  // "3. gün 07:20'de başlar" gibi bir program, önceki günün bitişinden
  // türetilseydi ortaya çıkardı ve kimse öyle gezmiyor.
  baslangicSaati: VARSAYILAN_BASLANGIC_SAATI,
  tema: 'Karma',
  beslenme: 'Yok',

  /**
   * Programa günde bir YEMEK MOLASI eklensin mi?
   *
   * Varsayılan AÇIK: bir günlük gezi programında öğle yemeği istisna değil,
   * kural. Kapalı başlatsaydık kullanıcıların çoğu bu satırı hiç görmeden
   * yemeksiz bir program alırdı.
   */
  yemekMolasi: true,

  /**
   * Çok günlü turda her günün sonuna KONAKLAMA durağı.
   *
   * Varsayılan AÇIK: kutu zaten yalnızca çok günlü turda çiziliyor ve orada
   * "bu gece nerede kalacağım" gerçek bir soru. Kapalı başlatıldığında
   * kullanıcı kutuyu hiç fark etmeden konaklamasız bir program aldı ve
   * "konaklama kısmı önermedi" diye bildirdi — varsayılan yanlıştı.
   */
  konaklama: true,

  /**
   * Güne serbest zaman eklensin mi? Varsayılan AÇIK.
   *
   * Günlük durak sınırı (yaya 6) ile günün saat bütçesi (8 sa) uyuşmuyor;
   * altı durak ancak dört saat ediyor ve program öğlen bitiyordu. Gerçek
   * turlarda o boşluk zaten serbest zamandır.
   */
  serbestZaman: true,
})

const katalogda = (katalog, deger) => katalog.some((k) => k.deger === deger)

/** Katalog kaydını değerinden bulur; bulunamazsa undefined. */
export const ulasimTipi = (deger) => ULASIM_TIPLERI.find((u) => u.deger === deger)
export const turTemasi = (deger) => TUR_TEMALARI.find((t) => t.deger === deger)
export const beslenmeKisiti = (deger) => BESLENME_KISITLARI.find((b) => b.deger === deger)

/** Tam sayı mı? ('4' gibi metinler de kabul, 4.5 ve NaN değil.) */
const tamSayiMi = (deger) => Number.isInteger(Number(deger))

// ---------------------------------------------------------------------------
//  Süre hesabı
// ---------------------------------------------------------------------------

/**
 * Formdaki süreyi DAKİKAYA çevirir — payload'ın tek süre birimi bu.
 *
 * NEDEN TEK BİRİM? Rota servisine "3 gün" ile "24 saat"i ayrı alanlarda
 * gönderseydik, hangisinin geçerli olduğunu sunucunun da bilmesi gerekirdi ve
 * ikisi birden dolu geldiğinde davranış belirsiz kalırdı. Dakikaya çevirip
 * ham seçimi ayrıca (bilgi olarak) taşımak bu belirsizliği ortadan kaldırıyor.
 *
 * Çok günlük turda çarpan GÜNLÜK GEZİ SAATİ: 3 günlük tur 72 saat değil,
 * 3 × 8 = 24 saattir. 72 ile hesaplamak, geceleri de gezilen bir rota
 * önerirdi.
 */
export function toplamDakika(form) {
  const deger = Number(form.sureDegeri) || 0

  if (form.sureBirimi === 'Gun') {
    const gunluk = Number(form.gunlukSaat) || VARSAYILAN_GUNLUK_SAAT
    return deger * gunluk * 60
  }

  return deger * 60
}

/** "6 sa 30 dk" — özet satırında ve testlerde okunur çıktı. */
export function sureMetni(dakika) {
  const saat = Math.floor(dakika / 60)
  const kalan = Math.round(dakika % 60)

  if (saat === 0) return `${kalan} dk`
  return kalan === 0 ? `${saat} sa` : `${saat} sa ${kalan} dk`
}

// ---------------------------------------------------------------------------
//  Doğrulama
// ---------------------------------------------------------------------------

/**
 * Formu doğrular.
 *
 * NEDEN ALAN BAZLI SÖZLÜK (tek bir mesaj değil)?
 * Tek mesaj döndürseydik kullanıcı hataları sırayla, birer birer görürdü:
 * şehri düzeltip gönder, sonra süreyi düzeltip gönder... Alan bazlı sözlük
 * hepsini aynı anda, ilgili girdinin yanında gösterebiliyor. `ilkHata` ise
 * gönder düğmesinin ipucu metni için var — kapalı bir düğmenin NEDEN kapalı
 * olduğu tek satırda yazılabilsin (KonumAnaliziPaneli'ndeki `engel` ile aynı
 * fikir).
 *
 * @param {TurFormu} form
 * @param {{iller?: {plaka: number, ad: string, bolge: string}[]}} [baglam]
 *        İl listesi verilirse "seçilen şehir gerçekten bu bölgede mi?" gibi
 *        çapraz kontroller de yapılır. Verilmezse o kontroller ATLANIR —
 *        liste henüz yüklenmediği için form doğrulanamaz duruma düşmesin.
 * @returns {{gecerli: boolean, hatalar: Object<string, string>,
 *            ilkHata: string|null, uyarilar: string[]}}
 */
export function turFormunuDogrula(form, baglam = {}) {
  const hatalar = {}
  const uyarilar = []
  const iller = baglam.iller ?? []

  // ---- Lokasyon ----
  const plaka = Number(form.ilPlaka) || 0
  const il = iller.find((i) => i.plaka === plaka)

  if (!plaka) {
    hatalar.ilPlaka = 'Şehir seçilmelidir.'
  } else if (iller.length > 0 && !il) {
    // Liste yüklendiği hâlde plaka listede yoksa seçim bayatlamış demektir.
    hatalar.ilPlaka = 'Seçilen şehir listede yok.'
  } else if (form.bolge && il && il.bolge !== form.bolge) {
    // Bölge süzgeci değiştiğinde eski şehir seçili kalabilir; sessizce
    // göndermek kullanıcının gördüğü bölgeyle turun bölgesini ayırırdı.
    hatalar.ilPlaka = `Seçilen şehir ${form.bolge} bölgesinde değil.`
  }

  // ---- Ek şehirler (çok şehirli tur) ----
  const ekPlakalar = (form.ekIlPlakalari ?? []).map(Number).filter((p) => p > 0 && p !== plaka)

  if (ekPlakalar.length + 1 > SINIRLAR.enCokSehir) {
    hatalar.ekIlPlakalari =
      `Bir turda en fazla ${SINIRLAR.enCokSehir} şehir seçilebilir.`
  }

  if (ekPlakalar.length > 0 && iller.length > 0) {
    const bilinmeyen = ekPlakalar.filter((p) => !iller.some((i) => i.plaka === p))
    if (bilinmeyen.length > 0) {
      hatalar.ekIlPlakalari = 'Seçilen ek şehirlerden biri listede yok.'
    }
  }

  const ilce = (form.ilce ?? '').trim()
  if (ilce.length > SINIRLAR.ilceEnCokKarakter) {
    hatalar.ilce = `İlçe adı en fazla ${SINIRLAR.ilceEnCokKarakter} karakter olabilir.`
  }

  // İLÇE + ÇOK ŞEHİR ÇELİŞKİSİ.
  // İlçe, aramayı şehrin içinde bir noktaya daraltmak için; birden fazla
  // şehir seçildiğinde o daraltma yalnızca ilk şehre uygulanabilirdi ve
  // diğer şehirlerde "Çankaya" diye bir yer aranırdı. Hata değil UYARI:
  // öneri yine üretiliyor, kullanıcı sonucu görüp karar veriyor.
  if (ekPlakalar.length > 0 && ilce.length > 0) {
    uyarilar.push('İlçe süzgeci yalnızca başlangıç şehrine uygulanır.')
  }

  // ÇOK ŞEHİR + YAYA ÇELİŞKİSİ.
  // İki şehir arası yürünmüyor; rota servisi çizgiyi çizer ama süre
  // tahmini anlamsız olur (Ankara-Eskişehir yaya ≈ 45 saat).
  if (ekPlakalar.length > 0 && form.ulasimTipi === 'Yaya') {
    uyarilar.push('Şehirler arası yürüme süresi gerçekçi olmaz; araç seçmeyi deneyin.')
  }

  // ---- Ulaşım tipi ----
  if (!katalogda(ULASIM_TIPLERI, form.ulasimTipi)) {
    hatalar.ulasimTipi = 'Ulaşım tipi seçilmelidir.'
  }

  // ---- Süre ----
  if (!katalogda(SURE_BIRIMLERI, form.sureBirimi)) {
    hatalar.sureBirimi = 'Süre birimi seçilmelidir.'
  } else if (!tamSayiMi(form.sureDegeri)) {
    // Yarım saatlik turlar anlamlı olabilirdi ama gün için "2.5 gün" değil;
    // tek bir alan iki birimi taşıdığı için kural ikisinde de tam sayı.
    hatalar.sureDegeri = 'Süre tam sayı olmalıdır.'
  } else {
    const deger = Number(form.sureDegeri)

    if (form.sureBirimi === 'Saat') {
      if (deger < SINIRLAR.enAzSaat || deger > SINIRLAR.enCokSaat) {
        hatalar.sureDegeri =
          `Günübirlik tur ${SINIRLAR.enAzSaat}-${SINIRLAR.enCokSaat} saat arasında olmalıdır.`
      } else if (deger < 2) {
        uyarilar.push('1 saatlik turda yalnızca 1-2 durak önerilebilir.')
      }
    } else if (deger < SINIRLAR.enAzGun || deger > SINIRLAR.enCokGun) {
      hatalar.sureDegeri =
        `Tur ${SINIRLAR.enAzGun}-${SINIRLAR.enCokGun} gün arasında olmalıdır.`
    }
  }

  if (form.sureBirimi === 'Gun') {
    if (!tamSayiMi(form.gunlukSaat)) {
      hatalar.gunlukSaat = 'Günlük süre tam sayı olmalıdır.'
    } else {
      const gunluk = Number(form.gunlukSaat)
      if (gunluk < SINIRLAR.enAzGunlukSaat || gunluk > SINIRLAR.enCokGunlukSaat) {
        hatalar.gunlukSaat =
          `Günlük gezi süresi ${SINIRLAR.enAzGunlukSaat}-${SINIRLAR.enCokGunlukSaat} saat olmalıdır.`
      } else if (form.ulasimTipi === 'Yaya' && gunluk > 8) {
        // Engel DEĞİL, uyarı: isteyen yapar. Sessiz kalmak, sonucu görünce
        // "bu kadar yürünür mü?" diye şaşırmasına yol açardı.
        uyarilar.push('Yaya turda günde 8 saatten fazla yürüyüş yorucu olabilir.')
      }
    }
  }

  // Saat VERİLMEMİŞSE hata değil: varsayılana düşüyor. Başlangıç saati
  // yalnızca programın GÖSTERİMİNİ etkiliyor (turProgrami.js) — rota
  // servisinin ona ihtiyacı yok. Zorunlu tutsaydık, saati hiç doldurmayan
  // bir çağrı yüzünden rota önerisi komple engellenirdi; oysa yapılacak
  // doğru şey 09:00 varsaymak.
  //
  // BİÇİMİ BOZUKSA hata veriliyor: "sabah 9" yazan kullanıcı sessizce
  // 09:00'a düşürülseydi, girdiğinin dikkate alınmadığını fark etmezdi.
  if (form.baslangicSaati && !SAAT_BICIMI.test(form.baslangicSaati)) {
    hatalar.baslangicSaati = 'Başlangıç saati SS:DD biçiminde olmalıdır.'
  }

  // ---- Tema ve beslenme ----
  if (!katalogda(TUR_TEMALARI, form.tema)) {
    hatalar.tema = 'Tur teması seçilmelidir.'
  }

  if (!katalogda(BESLENME_KISITLARI, form.beslenme)) {
    hatalar.beslenme = 'Beslenme kısıtı seçilmelidir (kısıt yoksa "Kısıt yok").'
  } else if (form.beslenme !== 'Yok' && form.tema === 'Gastronomi') {
    uyarilar.push('Gastronomi temasında kısıtlı beslenmeye uygun mekan sayısı az olabilir.')
  }

  // Sözlükteki İLK hata: alanların ekrandaki sırasıyla aynı olsun diye
  // Object.values kullanılıyor (ekleme sırası korunuyor).
  const mesajlar = Object.values(hatalar)

  return {
    gecerli: mesajlar.length === 0,
    hatalar,
    ilkHata: mesajlar[0] ?? null,
    uyarilar,
  }
}

// ---------------------------------------------------------------------------
//  Payload
// ---------------------------------------------------------------------------

/**
 * Formu rota servisinin beklediği PAYLOAD nesnesine çevirir.
 *
 * Doğrulama YAPMAZ — çağıran taraf önce doğruluyor (bkz. rotaIstegiHazirla).
 * İkisini tek fonksiyonda toplamak, "sadece önizleme istiyorum" gibi
 * durumlarda doğrulamayı da mecbur kılardı.
 *
 * NORMALLEŞTİRME BURADA: metinler trim'leniyor, sayılar Number'a çevriliyor,
 * boş metinler null oluyor. Bu iş formda yapılsaydı her girdi kendi
 * temizliğini tekrarlardı ve biri unutulduğunda sunucuya " Çankaya " gibi bir
 * değer giderdi.
 *
 * @param {TurFormu} form
 * @param {{iller?: {plaka: number, ad: string, bolge: string}[]}} [baglam]
 * @returns {Object} rota servisine gidecek gövde
 */
export function turPayloadu(form, baglam = {}) {
  const iller = baglam.iller ?? []
  const plaka = Number(form.ilPlaka) || 0
  const il = iller.find((i) => i.plaka === plaka)
  const ilce = (form.ilce ?? '').trim()

  const tema = turTemasi(form.tema)
  const beslenme = beslenmeKisiti(form.beslenme)
  const gunMu = form.sureBirimi === 'Gun'

  return {
    lokasyon: {
      /** Bölge süzgeci; seçilmediyse null (sunucu "fark etmez" diye okuyor). */
      bolge: form.bolge || null,
      ilPlaka: plaka,
      /**
       * İl ADI da gönderiliyor: plaka tek başına yeterli ama servis günlüğüne
       * ve öneri başlığına ("Ankara · Kültürel tur") ad yazılıyor. İl listesi
       * henüz yüklenmediyse null gider — plaka her hâlükârda dolu.
       */
      ilAdi: il?.ad ?? null,

      /**
       * ÇOK ŞEHİRLİ TUR — başlangıç şehrine EKLENEN şehirler.
       *
       * Başlangıç şehri bu listede YOK: `ilPlaka` onu zaten söylüyor ve
       * turun nereden başladığı bilgisi kaybolmamalı. Tekrar girmiş olsa
       * bile burada eleniyor — sunucu da eliyor ama iki yerde de elemek,
       * gövdeyi okuyan birinin "başlangıç iki kez mi geziliyor?" diye
       * duraksamasını engelliyor.
       */
      ekIlPlakalari: (form.ekIlPlakalari ?? [])
        .map(Number)
        .filter((p) => p > 0 && p !== plaka),

      /** Boş ilçe null: "" göndermek sunucuda "adı boş olan ilçe" araması olurdu. */
      ilce: ilce || null,
    },

    ulasimTipi: form.ulasimTipi,

    sure: {
      birim: form.sureBirimi,
      deger: Number(form.sureDegeri),
      /** Yalnızca çok günlük turda anlamlı; günübirlikte null. */
      gunlukSaat: gunMu ? Number(form.gunlukSaat) : null,
      /** Servisin ASIL okuduğu alan — tek birim, belirsizlik yok. */
      toplamDakika: toplamDakika(form),

      /**
       * Programın başlangıç saati.
       *
       * Sunucu bunu KULLANMIYOR: gün gün bölme ve saat hesabı istemcide
       * yapılıyor (turProgrami.js), çünkü kullanıcı saati değiştirdiğinde
       * yeni bir rota isteği atmanın anlamı yok — duraklar aynı kalıyor,
       * yalnızca saatler kayıyor. Yine de payload'da taşınıyor: öneriyi
       * üreten isteğin tamamı tek bir nesnede duruyor ve arayüz programı
       * ondan kuruyor.
       */
      baslangicSaati: form.baslangicSaati || VARSAYILAN_BASLANGIC_SAATI,
    },

    tema: form.tema,
    /** Temanın açılımı — sunucunun POI süzgecine doğrudan verilebilir. */
    tercihEdilenMekanTipleri: tema?.mekanTipleri ?? [],

    beslenmeKisiti: form.beslenme,
    /** "Yok" seçildiyse boş dizi: süzme yapılmayacak demek. */
    beslenmeEtiketleri: beslenme?.poiEtiketleri ?? [],

    yemekMolasi: Boolean(form.yemekMolasi),

    /**
     * Konaklama YALNIZCA çok günlü turda gönderiliyor.
     *
     * Günübirlik turda kutu zaten çizilmiyor ama form durumu birimden
     * bağımsız yaşıyor: kullanıcı "3 gün" seçip konaklamayı işaretledikten
     * sonra "6 saat"e dönerse bayrak açık kalırdı ve akşam eve dönülen bir
     * turda otel durağı çıkardı.
     */
    konaklama: gunMu && Number(form.sureDegeri) > 1 && Boolean(form.konaklama),

    serbestZaman: Boolean(form.serbestZaman),
  }
}

/**
 * FORMU TOPLAYIP İSTEĞE HAZIRLAYAN fonksiyon — bileşenin gönder düğmesi
 * ve doğrudan çağıran testler bunu kullanıyor.
 *
 * Doğrulama ile payload üretimini tek çağrıda birleştiriyor ve payload'ı
 * YALNIZCA form geçerliyse üretiyor: geçersiz formdan payload dönseydi,
 * çağıran taraf `gecerli` bayrağını kontrol etmeyi unuttuğunda yarım veri
 * sunucuya giderdi.
 *
 * @returns {{gecerli: boolean, hatalar: Object<string,string>,
 *            ilkHata: string|null, uyarilar: string[], payload: Object|null}}
 */
export function rotaIstegiHazirla(form, baglam = {}) {
  const dogrulama = turFormunuDogrula(form, baglam)

  return {
    ...dogrulama,
    payload: dogrulama.gecerli ? turPayloadu(form, baglam) : null,
  }
}
