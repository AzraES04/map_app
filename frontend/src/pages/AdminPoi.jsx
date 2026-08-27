import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  poileriListele, poiSil, poiGeriAl, poiAktiflikDegistir,
  kategorileriListele, kategoriEkle, kategoriGuncelle, kategoriSil,
  poiStilleriniYenile, agaciDuzlestir, poiIkonlariniGetir,
} from '../poiApi'
import { ikonSvg } from '../poiIkon'
import { EkleIkonu, SilIkonu, PoiIkonu, KategoriIkonu, KullaniciIkonu } from '../icons'

// ============================================================================
//  POI Yönetimi ekranı (Ödev 12 / Madde 2)
//
//  Ödev iki işi AYNI menünün altında istiyor: eklenen POI'lerin listesi
//  (ekleyen kullanıcı bilgisiyle) ve kategori yönetimi. İkisini iki ayrı menü
//  maddesine bölmek yerine tek ekranda SEKME yaptık — çünkü ikisi sürekli
//  birlikte kullanılıyor: "şu POI'nin kategorisi yanlış" diyen yönetici
//  kategoriye bakmak için ekran değiştirmek zorunda kalmıyor.
// ============================================================================

const BOS_KATEGORI_FORMU = { id: null, ad: '', aciklama: '', ikon: '', parentId: '', isActive: true }

/**
 * Seçici önizlemelerinin rengi (Ödev 15).
 *
 * Kategorinin GERÇEK rengi kaydedildikten sonra belli oluyor: palet, kökün
 * sırasına göre atıyor (PoiStilUretici.KokPaleti). Bu yüzden seçicide nötr
 * bir ton kullanıyoruz — orada seçilen şey ŞEKİL, renk değil. Rastgele bir
 * renk basmak, kaydedildikten sonra değişen bir önizleme demek olurdu.
 */
const IKON_ONIZLEME_RENGI = '#2d7dd2'

/**
 * Tabloda bir kerede gösterilen azami satır (Ödev 14).
 *
 * 200 bilinçli: bir ekrana sığmıyor ama kaydırılabilir bir liste olarak
 * hâlâ anlamlı. Sayfalama (1/2/3…) da yapılabilirdi; arama zaten aradığını
 * bulmanın daha hızlı yolu olduğu için sayfa numaralarına gerek kalmadı.
 */
const LISTE_SINIRI = 200

export default function AdminPoi() {
  const navigate = useNavigate()

  const [sekme, setSekme] = useState('poi')      // 'poi' | 'kategori'

  // Stil yenileme sürüyor mu? (Ödev 13 iyileştirmesi)
  const [stilYenileniyor, setStilYenileniyor] = useState(false)

  const [poiler, setPoiler] = useState([])
  const [agac, setAgac] = useState([])
  const [yukleniyor, setYukleniyor] = useState(true)
  const [bilgi, setBilgi] = useState(null)

  // İKİ AYRI hata durumu, çünkü sonuçları farklı:
  //
  //   yuklemeHatasi → liste hiç gelmedi; tablonun yerine açıklama yazılır.
  //   hata          → liste duruyor ama BİR İŞLEM reddedildi (örn. "dolu
  //                   kategori silinemez"). Bu durumda tabloyu gizlemek
  //                   yanlış olur: kullanıcı tam da hatanın hangi satırla
  //                   ilgili olduğunu görmek istiyor.
  //
  // Tek state ile başlamıştık; "kategori silinemez" uyarısı çıktığında bütün
  // liste "Liste görüntülenemedi" ile kayboluyordu.
  const [yuklemeHatasi, setYuklemeHatasi] = useState(null)
  const [hata, setHata] = useState(null)

  const [form, setForm] = useState(null)
  const [kaydediliyor, setKaydediliyor] = useState(false)

  // Listeyi kategoriye göre süzme: kategori ağacı büyüdüğünde "şu kategoride
  // kaç POI var?" sorusu tablodan gözle sayılamaz hâle geliyor.
  const [suzgec, setSuzgec] = useState('')

  // Ada göre arama ve gösterim sınırı (Ödev 14).
  //
  // NEDEN GEREKTİ? Ödev 14 ile birlikte tabloya analiz veri seti girdi:
  // binlerce POI. Hepsini birden DOM'a basmak tarayıcıyı gözle görülür
  // şekilde yavaşlatıyordu ve zaten kimse iki bin satırı gözle taramıyor.
  // İlk N kayıt gösteriliyor, gerisi arama ya da "tümünü göster" ile geliyor.
  const [aramaMetni, setAramaMetni] = useState('')
  const [tumunuGoster, setTumunuGoster] = useState(false)

  // Ödev 15: seçilebilir simge kataloğu — sunucudan bir kez indiriliyor.
  // Kod tarafında ÇİZİM VERİSİ TUTMUYORUZ; yollar backend'deki tek katalogtan
  // (PoiIkonlari) geliyor, GeoServer'ın bastığı SVG ile aynı kaynak.
  const [ikonKatalogu, setIkonKatalogu] = useState(null)

  // Silinen POI'nin bildirimindeki "Geri al" düğmesi için: hangi kayıt?
  const [geriAlinacak, setGeriAlinacak] = useState(null)

  const oturumBitti = useCallback(
    () => navigate('/login', { replace: true, state: { expired: true } }),
    [navigate],
  )

  const yukle = useCallback(async () => {
    setYukleniyor(true)
    setYuklemeHatasi(null)
    setHata(null)
    try {
      // İkisi birbirinden bağımsız; sırayla beklemenin anlamı yok.
      const [gelenPoiler, gelenAgac, gelenIkonlar] = await Promise.all([
        poileriListele(oturumBitti),
        kategorileriListele(oturumBitti),
        // Katalog sabit; yine de listeyle birlikte indiriliyor ki simge
        // seçici, form ilk açıldığı anda dolu gelsin (açılırken indirseydik
        // kullanıcı bir an boş bir ızgara görürdü).
        poiIkonlariniGetir(oturumBitti),
      ])
      setPoiler(gelenPoiler)
      setAgac(gelenAgac)
      setIkonKatalogu(gelenIkonlar)
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setYuklemeHatasi(err.message)
    } finally {
      setYukleniyor(false)
    }
  }, [oturumBitti])

  useEffect(() => { yukle() }, [yukle])

  /**
   * Anahtardan simge SVG'si üretir; katalog henüz inmemişse null.
   *
   * Kataloga her satırda tek tek bakmak yerine bir kez sözlüğe çevirmek
   * (useMemo) tabloyu 14 değil 14×N kez aramaktan kurtarıyor.
   */
  const ikonSozlugu = useMemo(
    () => new Map((ikonKatalogu?.ikonlar ?? []).map((i) => [i.anahtar, i.parcalar])),
    [ikonKatalogu],
  )

  const ikonAnahtarindanSvg = (anahtar) => {
    const parcalar = ikonSozlugu.get(anahtar)
    return parcalar ? ikonSvg(parcalar, IKON_ONIZLEME_RENGI, 16) : null
  }

  /** Ağaç → düz liste. Tablo satırları ve "üst kategori" listesi bunu kullanıyor. */
  const duzKategoriler = useMemo(() => agaciDuzlestir(agac), [agac])

  const suzulmusPoiler = useMemo(() => {
    const q = aramaMetni.trim().toLocaleLowerCase('tr')

    return poiler.filter((p) => {
      if (suzgec && String(p.kategoriId) !== suzgec) return false
      if (!q) return true

      // Ad VE kategori yolu birlikte aranıyor: "eczane" yazan biri hem
      // "Bağlar Eczanesi"ni hem de Sağlık › Eczane altındakileri bulsun.
      return p.isim.toLocaleLowerCase('tr').includes(q)
        || (p.kategoriYolu ?? '').toLocaleLowerCase('tr').includes(q)
    })
  }, [poiler, suzgec, aramaMetni])

  /** Tabloya basılan satır sayısı — gerisi "tümünü göster" ile açılıyor. */
  const gosterilenPoiler = useMemo(
    () => (tumunuGoster ? suzulmusPoiler : suzulmusPoiler.slice(0, LISTE_SINIRI)),
    [suzulmusPoiler, tumunuGoster],
  )

  // ------------------------------------------------------------------
  //  POI işlemleri
  // ------------------------------------------------------------------

  const aktiflikDegistir = async (poi) => {
    setHata(null)
    try {
      await poiAktiflikDegistir(poi.id, !poi.isActive, oturumBitti)
      setBilgi(poi.isActive ? `"${poi.isim}" askıya alındı.` : `"${poi.isim}" aktif edildi.`)
      await yukle()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    }
  }

  const poiyiSil = async (poi) => {
    if (!window.confirm(
      `"${poi.isim}" POI'si silinecek.\n\n`
      + 'Kayıt veritabanından tamamen silinmez; is_deleted = true yapılarak '
      + 'gizlenir ve geri alınabilir.\n\nDevam edilsin mi?',
    )) return

    setHata(null)
    try {
      await poiSil(poi.id, oturumBitti)
      await yukle()

      // "Sil + geri al" deseni: soft delete olduğu için silme tek UPDATE ile
      // geri alınabiliyor. Onay kutusu yanlış tıklamayı, geri alma yanlış
      // KAYDI seçmeyi kurtarıyor.
      setBilgi(`"${poi.isim}" silindi.`)
      setGeriAlinacak(poi)
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    }
  }

  const geriAl = async () => {
    if (!geriAlinacak) return
    setHata(null)
    try {
      await poiGeriAl(geriAlinacak.id, oturumBitti)
      setBilgi(`"${geriAlinacak.isim}" geri alındı.`)
      setGeriAlinacak(null)
      await yukle()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    }
  }

  // ------------------------------------------------------------------
  //  Kategori işlemleri
  // ------------------------------------------------------------------

  const yeniKategori = (parentId = '') => setForm({ ...BOS_KATEGORI_FORMU, parentId })

  /**
   * POI stillerini GeoServer'a yeniden yazar (Ödev 13 iyileştirmesi).
   *
   * NORMALDE GEREKMEZ: kategori eklendiğinde/güncellendiğinde/silindiğinde
   * stiller kendiliğinden yenileniyor. Bu düğme, o sırada GeoServer kapalı
   * olduğu için sessizce atlanan yenilemeyi tamamlamak için — ve GeoServer
   * sıfırdan kurulduğunda stilleri geri yazmak için.
   *
   * Otomatik yenilemenin aksine burada hata GİZLENMİYOR: yönetici düğmeye
   * bastıysa sonucu görmeli.
   */
  const stilleriYenile = async () => {
    setHata(null)
    setStilYenileniyor(true)
    try {
      const sonuc = await poiStilleriniYenile(oturumBitti)
      setBilgi(
        `Harita stilleri yenilendi: ${sonuc.yazilan} stil yazıldı`
        + (sonuc.silinen > 0 ? `, ${sonuc.silinen} eski stil silindi.` : '.'),
      )
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    } finally {
      setStilYenileniyor(false)
    }
  }

  const kategoriDuzenle = (kategori) => setForm({
    id: kategori.id,
    ad: kategori.ad,
    aciklama: kategori.aciklama ?? '',
    // KENDİ ikonu, etkin ikonu DEĞİL: kategoriye simge seçilmemişse form
    // boş gelmeli. `etkinIkon` yazsaydık kullanıcının hiç seçmediği bir
    // simge seçilmiş gibi görünür, kaydedince miras kırılırdı.
    ikon: kategori.ikon ?? '',
    parentId: kategori.parentId ?? '',
    isActive: kategori.isActive,
  })

  const formuKaydet = async (e) => {
    e.preventDefault()
    setKaydediliyor(true)
    setHata(null)

    const govde = {
      ad: form.ad,
      aciklama: form.aciklama || null,
      // Boş metin "simge seçilmedi" demek; sunucu null bekliyor.
      ikon: form.ikon || null,
      // Boş seçenek "" değeri taşıyor; sunucu null bekliyor (kök kategori).
      parentId: form.parentId === '' ? null : Number(form.parentId),
      isActive: form.isActive,
    }

    try {
      if (form.id === null) {
        await kategoriEkle(govde, oturumBitti)
        setBilgi(`"${form.ad}" kategorisi eklendi.`)
      } else {
        await kategoriGuncelle(form.id, govde, oturumBitti)
        setBilgi(`"${form.ad}" kategorisi güncellendi.`)
      }
      setForm(null)
      await yukle()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    } finally {
      setKaydediliyor(false)
    }
  }

  const kategoriyiSil = async (kategori) => {
    if (!window.confirm(`"${kategori.ad}" kategorisi silinsin mi?`)) return

    setHata(null)
    try {
      await kategoriSil(kategori.id, oturumBitti)
      setBilgi(`"${kategori.ad}" kategorisi silindi.`)
      await yukle()
    } catch (err) {
      // Alt kategorisi veya bağlı POI'si varsa sunucu sebebini yazıyor;
      // mesajı olduğu gibi gösteriyoruz.
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    }
  }

  /**
   * Bir kategoriyi düzenlerken üst kategori olarak SEÇİLEMEYECEKLER:
   * kendisi ve bütün torunları. Sunucu bunu zaten reddediyor; listeden
   * çıkarmak kullanıcıyı reddedilecek bir seçime hiç götürmüyor.
   */
  const secilebilirAtalar = useMemo(() => {
    if (!form?.id) return duzKategoriler

    const yasakli = new Set([form.id])
    // Düz liste ata sırasında geldiği için tek geçiş yetiyor: bir düğümün
    // atası yasaklıysa kendisi de yasaklı.
    duzKategoriler.forEach((k) => {
      if (k.parentId !== null && yasakli.has(k.parentId)) yasakli.add(k.id)
    })

    return duzKategoriler.filter((k) => !yasakli.has(k.id))
  }, [duzKategoriler, form])

  // ------------------------------------------------------------------

  return (
    <div className="admin-sayfa">
      <header className="admin-baslik">
        <div>
          <h1>POI Yönetimi</h1>
          <p className="muted">
            Operatörlerin haritaya eklediği ilgi noktaları ve onların bağlandığı
            hiyerarşik kategori sözlüğü.
          </p>
        </div>
        {sekme === 'kategori' && (
          <div className="admin-baslik-eylemler">
            {/* Her kategorinin haritadaki simgesi bu ekrandan yönetiliyor:
                kategori eklemek yeni bir SLD üretiyor. Düğme, GeoServer o
                sırada kapalıysa işi tamamlamak için. */}
            <button type="button" className="btn-ghost" onClick={stilleriYenile}
                    disabled={stilYenileniyor}
                    title="Kategorilerden üretilen SLD stillerini GeoServer'a yeniden yaz">
              {stilYenileniyor ? 'Yenileniyor…' : 'Harita stillerini yenile'}
            </button>
            <button type="button" className="btn-primary" onClick={() => yeniKategori()}>
              <EkleIkonu /> Yeni Kategori
            </button>
          </div>
        )}
      </header>

      <div className="admin-sekmeler" role="tablist">
        <button
          type="button"
          role="tab"
          aria-selected={sekme === 'poi'}
          className={`admin-sekme${sekme === 'poi' ? ' active' : ''}`}
          onClick={() => setSekme('poi')}
        >
          <PoiIkonu size={15} /> POI Listesi <span className="sayi">{poiler.length}</span>
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={sekme === 'kategori'}
          className={`admin-sekme${sekme === 'kategori' ? ' active' : ''}`}
          onClick={() => setSekme('kategori')}
        >
          <KategoriIkonu size={15} /> Kategoriler <span className="sayi">{duzKategoriler.length}</span>
        </button>
      </div>

      {(yuklemeHatasi || hata) && <p className="error-banner">{yuklemeHatasi ?? hata}</p>}
      {bilgi && !hata && !yuklemeHatasi && (
        <p className="info-banner">
          {bilgi}
          {geriAlinacak && (
            <button type="button" className="banner-eylem" onClick={geriAl}>Geri al</button>
          )}
        </p>
      )}

      {/* ================= POI LİSTESİ ================= */}
      {sekme === 'poi' && (
        <div className="admin-kart">
          <label className="admin-suzgec">
            POI ara
            <input
              type="search"
              value={aramaMetni}
              onChange={(e) => setAramaMetni(e.target.value)}
              placeholder="Ad veya kategori…"
            />
          </label>

          {duzKategoriler.length > 0 && (
            <label className="admin-suzgec">
              Kategoriye göre süz
              <select value={suzgec} onChange={(e) => setSuzgec(e.target.value)}>
                <option value="">Tümü ({poiler.length})</option>
                {duzKategoriler.map((k) => (
                  <option key={k.id} value={k.id}>
                    {' '.repeat(k.seviye * 4)}{k.seviye > 0 ? '└ ' : ''}{k.ad} ({k.poiSayisi})
                  </option>
                ))}
              </select>
            </label>
          )}

          {yukleniyor ? (
            <p className="muted">Yükleniyor…</p>
          ) : yuklemeHatasi ? (
            /* Yükleme hatasında "kayıt yok" demek yanıltıcı olur: liste boş
               değil, okunamadı. */
            <p className="muted">Liste görüntülenemedi.</p>
          ) : suzulmusPoiler.length === 0 ? (
            <p className="muted">
              {poiler.length === 0
                ? 'Henüz POI eklenmemiş. Operatörler haritadaki "POI Ekle" aracıyla ekler.'
                : 'Bu kategoride POI yok.'}
            </p>
          ) : (
            <table className="admin-tablo">
              <thead>
                <tr>
                  <th>POI</th>
                  <th>Kategori</th>
                  <th>Mesai saatleri</th>
                  <th>Ekleyen</th>
                  <th>Durum</th>
                  <th className="sag">İşlemler</th>
                </tr>
              </thead>
              <tbody>
                {gosterilenPoiler.map((poi) => (
                  <tr key={poi.id} className={poi.isActive ? '' : 'pasif-satir'}>
                    <td>
                      <strong><PoiIkonu size={14} /> {poi.isim}</strong>
                      <small className="admin-alt-metin">
                        {new Date(poi.createdDate).toLocaleString('tr-TR')}
                      </small>
                    </td>
                    <td><span className="admin-rozet">{poi.kategoriYolu}</span></td>
                    <td>{poi.mesaiSaatleri || <span className="muted">—</span>}</td>
                    <td>
                      {/* Ödevin açıkça istediği sütun: "eklenen POI'lerin
                          listesi ve ekleyen kullanıcı bilgileri". */}
                      {poi.kullaniciAdi
                        ? <span className="admin-rozet notr"><KullaniciIkonu /> {poi.kullaniciAdi}</span>
                        : <span className="muted">bilinmiyor</span>}
                    </td>
                    <td>
                      <span className={`admin-durum${poi.isActive ? '' : ' pasif'}`}>
                        {poi.isActive ? 'Aktif' : 'Pasif'}
                      </span>
                    </td>
                    <td className="sag">
                      <button type="button" className="btn-ghost" onClick={() => aktiflikDegistir(poi)}>
                        {poi.isActive ? 'Askıya al' : 'Aktif et'}
                      </button>
                      <button type="button" className="btn-ghost sil" onClick={() => poiyiSil(poi)}>
                        <SilIkonu /> Sil
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {/* Kaç kaydın gizlendiğini SÖYLEMEK şart: sessizce kesilen bir liste
              "kayıtlarım kaybolmuş" diye okunurdu. */}
          {!yukleniyor && suzulmusPoiler.length > gosterilenPoiler.length && (
            <p className="muted liste-siniri">
              {suzulmusPoiler.length.toLocaleString('tr-TR')} kayıttan ilk{' '}
              {gosterilenPoiler.length.toLocaleString('tr-TR')} tanesi gösteriliyor.
              {' '}
              <button type="button" className="btn-ghost" onClick={() => setTumunuGoster(true)}>
                Tümünü göster
              </button>
            </p>
          )}

          {tumunuGoster && suzulmusPoiler.length > LISTE_SINIRI && (
            <p className="muted liste-siniri">
              {suzulmusPoiler.length.toLocaleString('tr-TR')} kaydın tamamı gösteriliyor.
              {' '}
              <button type="button" className="btn-ghost" onClick={() => setTumunuGoster(false)}>
                Kısalt
              </button>
            </p>
          )}
        </div>
      )}

      {/* ================= KATEGORİLER ================= */}
      {sekme === 'kategori' && (
        <>
          {form && (
            <form className="admin-kart admin-form" onSubmit={formuKaydet}>
              <h2>{form.id === null ? 'Yeni kategori' : `"${form.ad}" kategorisini düzenle`}</h2>

              <div className="admin-form-satir">
                <label>
                  Kategori adı
                  <input
                    value={form.ad}
                    onChange={(e) => setForm({ ...form, ad: e.target.value })}
                    required
                    maxLength={150}
                    placeholder="Örn: Restoran"
                    autoFocus
                  />
                </label>

                <label>
                  Üst kategori
                  <select
                    value={form.parentId}
                    onChange={(e) => setForm({ ...form, parentId: e.target.value })}
                  >
                    <option value="">— kök kategori —</option>
                    {secilebilirAtalar.map((k) => (
                      <option key={k.id} value={k.id}>
                        {' '.repeat(k.seviye * 4)}{k.seviye > 0 ? '└ ' : ''}{k.ad}
                      </option>
                    ))}
                  </select>
                </label>
              </div>

              <label>
                Açıklama
                <input
                  value={form.aciklama}
                  onChange={(e) => setForm({ ...form, aciklama: e.target.value })}
                  maxLength={500}
                  placeholder="Bu kategori neyi kapsıyor?"
                />
              </label>

              {/* ---------- Ödev 15: SİMGE SEÇİCİ ----------
                  Açılır liste yerine IZGARA: simge seçimi görsel bir karardır,
                  "fincan" yazısını okuyup hayal etmek yerine çizimin kendisine
                  bakılmalı. Önizlemeler de sunucudan gelen yollarla çiziliyor,
                  yani burada görülen şey haritada çıkacak olanın ta kendisi. */}
              <fieldset className="ikon-secici">
                <legend>Harita simgesi</legend>

                {!ikonKatalogu ? (
                  <p className="muted">Simgeler yükleniyor…</p>
                ) : (
                  <div className="ikon-izgara">
                    {/* "Miras al" seçeneği: boş değer, kategorinin kendi simgesi
                        olmadığı anlamına geliyor ve harita atasınınkini kullanıyor.
                        Seçenek olarak durmasaydı yönetici bir kez simge seçtikten
                        sonra mirasa GERİ DÖNEMEZDİ. */}
                    <button
                      type="button"
                      className={`ikon-kutu${form.ikon === '' ? ' secili' : ''}`}
                      onClick={() => setForm({ ...form, ikon: '' })}
                      title="Üst kategorinin simgesini kullan"
                    >
                      <span className="ikon-miras" aria-hidden="true">↱</span>
                      <small>Miras</small>
                    </button>

                    {ikonKatalogu.ikonlar.map((ikon) => (
                      <button
                        key={ikon.anahtar}
                        type="button"
                        className={`ikon-kutu${form.ikon === ikon.anahtar ? ' secili' : ''}`}
                        onClick={() => setForm({ ...form, ikon: ikon.anahtar })}
                        title={ikon.ad}
                        aria-pressed={form.ikon === ikon.anahtar}
                      >
                        <span
                          aria-hidden="true"
                          dangerouslySetInnerHTML={{
                            __html: ikonSvg(ikon.parcalar, IKON_ONIZLEME_RENGI, 24),
                          }}
                        />
                        <small>{ikon.ad}</small>
                      </button>
                    ))}
                  </div>
                )}

                <p className="muted">
                  Simge seçilmezse üst kategorininki kullanılır; o da yoksa harita
                  iğnesi çizilir. Renk simgeyle birlikte değişmez — rengi kök
                  kategorinin palet sırası belirliyor.
                </p>
              </fieldset>

              <label className="admin-onay">
                <input
                  type="checkbox"
                  checked={form.isActive}
                  onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
                />
                Kategori aktif (pasif kategori, POI formundaki listede çıkmaz —
                alt kategorileri de çıkmaz)
              </label>

              <div className="admin-eylemler">
                <button type="submit" className="btn-primary" disabled={kaydediliyor}>
                  {kaydediliyor ? 'Kaydediliyor…' : 'Kaydet'}
                </button>
                <button type="button" className="btn-ghost" onClick={() => setForm(null)}
                        disabled={kaydediliyor}>
                  Vazgeç
                </button>
              </div>
            </form>
          )}

          <div className="admin-kart">
            {yukleniyor ? (
              <p className="muted">Yükleniyor…</p>
            ) : yuklemeHatasi ? (
              <p className="muted">Liste görüntülenemedi.</p>
            ) : duzKategoriler.length === 0 ? (
              <p className="muted">Henüz kategori tanımlanmamış.</p>
            ) : (
              <table className="admin-tablo">
                <thead>
                  <tr>
                    <th>Kategori</th>
                    <th>POI</th>
                    <th>Durum</th>
                    <th className="sag">İşlemler</th>
                  </tr>
                </thead>
                <tbody>
                  {duzKategoriler.map((kategori) => (
                    <tr key={kategori.id}>
                      <td>
                        {/* Girinti hiyerarşiyi TEK BAKIŞTA gösteriyor; tam yolu
                            ayrıca yazmak satırı gereksiz uzatırdı. */}
                        <strong style={{ paddingLeft: `${kategori.seviye * 18}px` }}>
                          {kategori.seviye > 0 && <span className="agac-dal" aria-hidden="true">└</span>}

                          {/* Ödev 15: satırda HARİTADA ÇIZİLECEK simge duruyor.
                              Kategorinin kendi simgesi yoksa miras alınanı
                              gösteriyoruz (`etkinIkon`) ve satırı solık çiziyoruz —
                              yönetici "bu seçilmiş mi, miras mı?" sorusunu tabloya
                              bakarak cevaplayabilsin. */}
                          {ikonAnahtarindanSvg(kategori.etkinIkon) ? (
                            <span
                              className={`kategori-simge${kategori.ikon ? '' : ' miras'}`}
                              aria-hidden="true"
                              title={kategori.ikon
                                ? `Simge: ${kategori.ikon}`
                                : `Miras alınan simge: ${kategori.etkinIkon}`}
                              dangerouslySetInnerHTML={{
                                __html: ikonAnahtarindanSvg(kategori.etkinIkon),
                              }}
                            />
                          ) : (
                            <KategoriIkonu size={14} />
                          )}
                          {' '}{kategori.ad}
                        </strong>
                        {kategori.aciklama && (
                          <small className="admin-alt-metin"
                                 style={{ paddingLeft: `${kategori.seviye * 18}px` }}>
                            {kategori.aciklama}
                          </small>
                        )}
                      </td>
                      <td><span className="admin-rozet notr">{kategori.poiSayisi}</span></td>
                      <td>
                        <span className={`admin-durum${kategori.isActive ? '' : ' pasif'}`}>
                          {kategori.isActive ? 'Aktif' : 'Pasif'}
                        </span>
                      </td>
                      <td className="sag">
                        <button type="button" className="btn-ghost"
                                onClick={() => yeniKategori(String(kategori.id))}
                                title="Bu kategorinin altına alt kategori ekle">
                          <EkleIkonu /> Alt kategori
                        </button>
                        <button type="button" className="btn-ghost"
                                onClick={() => kategoriDuzenle(kategori)}>
                          Düzenle
                        </button>
                        <button type="button" className="btn-ghost sil"
                                onClick={() => kategoriyiSil(kategori)}>
                          <SilIkonu /> Sil
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </>
      )}
    </div>
  )
}
