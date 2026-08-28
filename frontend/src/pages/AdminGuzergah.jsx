import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  guzergahlariListele, guzergahEkle, guzergahGuncelle, guzergahSil,
  durakSil, durakGuncelle, siralamaKaydet, rotaOlustur,
} from '../ulasimApi'
import {
  EkleIkonu, SilIkonu, DurakIkonu, GuzergahIkonu, SurukleIkonu, KullaniciIkonu,
} from '../icons'

// ============================================================================
//  Güzergah Yönetimi ekranı (Ödev 16 / Madde 2)
//
//  Ödev metni üç şey istiyor:
//    • yeni güzergah ekleme (isim ve RENK belirleme), listeleme, düzenleme
//    • güzergah içindeki durakların SIRA BAZLI listesi
//    • SÜRÜKLE-BIRAK ile sıralamanın değiştirilebilmesi
//
//  YERLEŞİM: solda güzergah listesi, sağda seçilen güzergahın durakları.
//  İki ayrı ekrana bölmek de mümkündü ama sıralama işi tam olarak "hangi
//  hattın hangi durakları" sorusunu gerektiriyor; ikisini yan yana tutmak
//  ekran değiştirmeden çalışmayı sağlıyor.
//
//  ---- ÖDEV 17 EKLERİ ----
//
//    • "Rota Oluştur" (Madde 1): hattın duraklarından geçen sürüş rotasını
//      OSRM'e hesaplatır. Düğme yalnızca ELLE tetiklemek için: durak
//      eklendiğinde/taşındığında/silindiğinde ve SIRA değiştiğinde rota
//      sunucuda zaten kendiliğinden yenileniyor. Düğme, OSRM kapalıyken
//      yapılan değişikliklerden sonra "şimdi hesapla" demenin yolu.
//
//    • DURAK DÜZENLEME (Madde 2: "Güzergahlar ve Duraklar listelenmeli;
//      isim, renk vb. alanlar düzenlenebilmelidir"). Ödev 16'da duraklar
//      yalnızca listelenip sıralanabiliyordu; PUT ucu vardı ama arayüzden
//      erişilemiyordu — ad düzeltmek için durağı silip yeniden eklemek
//      gerekiyordu ki bu sırayı da bozuyordu.
//
//  SÜRÜKLE-BIRAK KÜTÜPHANESİZ. react-beautiful-dnd / dnd-kit eklemek
//  bağımlılık listesini büyütürdü; tarayıcının kendi HTML5 Drag & Drop
//  API'si (draggable + dragstart/dragover/drop) bu liste için yeterli.
//  Erişilebilirlik için klavye alternatifi de var (▲▼ düğmeleri): sürükleme
//  fare gerektiriyor, klavyeyle çalışan kullanıcı da sırayı değiştirebilmeli.
// ============================================================================

const BOS_FORM = { id: null, ad: '', renk: '#2d7dd2', aciklama: '', isActive: true }

/**
 * Hazır renk paleti.
 *
 * Serbest renk seçici de var; bunlar sık kullanılanlar için kısayol ve
 * birbirinden AYIRT EDİLEBİLİR seçilmiş: haritada birkaç hat aynı anda
 * açıkken yakın tonlar hangi durağın hangi hatta ait olduğunu belirsizleştirir.
 */
const RENKLER = [
  '#2d7dd2', '#d64550', '#2e9e63', '#d9822b',
  '#7a6ff0', '#0f8c8c', '#c2557a', '#8a6d3b',
]

export default function AdminGuzergah() {
  const navigate = useNavigate()

  const [guzergahlar, setGuzergahlar] = useState([])
  const [yukleniyor, setYukleniyor] = useState(true)
  const [bilgi, setBilgi] = useState(null)

  // İKİ AYRI hata durumu (AdminPoi'deki gerekçenin aynısı):
  //   yuklemeHatasi → liste hiç gelmedi; tablonun yerine açıklama yazılır
  //   hata          → liste duruyor ama BİR İŞLEM reddedildi (örn. "dolu
  //                   güzergah silinemez"); listeyi gizlemek yanlış olur
  const [yuklemeHatasi, setYuklemeHatasi] = useState(null)
  const [hata, setHata] = useState(null)

  const [form, setForm] = useState(null)
  const [kaydediliyor, setKaydediliyor] = useState(false)

  /** Sağ panelde durakları gösterilen güzergahın id'si. */
  const [seciliId, setSeciliId] = useState(null)

  // Sürükleme sırasında listeyi ANINDA yeniden diziyoruz (iyimser güncelleme):
  // sunucunun cevabını beklemek, kullanıcının bıraktığı satırın bir an eski
  // yerinde durmasına yol açardı ve sürükleme "tutmadı" gibi hissedilirdi.
  //
  // TAŞINAN SATIR İKİ YERDE TUTULUYOR ve bu bilinçli:
  //
  //   ref   → onDrop bunu okuyor. State'i okusaydı satırın olay işleyicisi,
  //           dragstart'tan SONRAKİ render'da yeniden bağlanana kadar eski
  //           değeri (null) görürdü. Gerçek kullanımda aradaki süre render'a
  //           yetiyor ama davranışı render zamanlamasına bağlamak kırılgan;
  //           ref her zaman güncel.
  //   state → yalnızca GÖRÜNÜM için (taşınan satır solıyor); render
  //           tetiklemesi gerektiği için ref yetmiyor.
  const suruklenenIdRef = useRef(null)
  const [suruklenenId, setSuruklenenId] = useState(null)
  const [siraKaydediliyor, setSiraKaydediliyor] = useState(false)

  /** Ödev 17: rota hesaplanan güzergahın id'si (düğme "hesaplanıyor…" olsun). */
  const [rotaHesaplanan, setRotaHesaplanan] = useState(null)

  /**
   * Ödev 17 / Madde 2: düzenlenen durak — { id, ad, aciklama, guzergahId }.
   *
   * Ayrı bir ekran DEĞİL, satırın yerinde açılan bir form. Sebep: durak
   * düzenlemenin tek bağlamı hangi hattın kaçıncı durağı olduğu ve o bağlam
   * tam da bu listede. Ayrı ekrana gitmek kullanıcıyı sıralamadan koparırdı.
   */
  const [durakFormu, setDurakFormu] = useState(null)
  const [durakKaydediliyor, setDurakKaydediliyor] = useState(false)

  const oturumBitti = useCallback(
    () => navigate('/login', { replace: true, state: { expired: true } }),
    [navigate],
  )

  const yukle = useCallback(async () => {
    setYukleniyor(true)
    setYuklemeHatasi(null)
    setHata(null)
    try {
      setGuzergahlar(await guzergahlariListele(oturumBitti))
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setYuklemeHatasi(err.message)
    } finally {
      setYukleniyor(false)
    }
  }, [oturumBitti])

  useEffect(() => { yukle() }, [yukle])

  // İlk yüklemede bir güzergah seçili gelsin: sağ panel boş bir çerçeve
  // olarak açılırsa kullanıcı önce sol listeden tıklamak zorunda kalıyor.
  useEffect(() => {
    if (seciliId === null && guzergahlar.length > 0) setSeciliId(guzergahlar[0].id)
  }, [guzergahlar, seciliId])

  const secili = useMemo(
    () => guzergahlar.find((g) => g.id === seciliId) ?? null,
    [guzergahlar, seciliId],
  )

  // ------------------------------------------------------------------
  //  Güzergah işlemleri
  // ------------------------------------------------------------------

  const yeniGuzergah = () => setForm({ ...BOS_FORM })

  const guzergahDuzenle = (g) => setForm({
    id: g.id,
    ad: g.ad,
    renk: g.renk,
    aciklama: g.aciklama ?? '',
    isActive: g.isActive,
  })

  const formuKaydet = async (e) => {
    e.preventDefault()
    setKaydediliyor(true)
    setHata(null)

    const govde = {
      ad: form.ad,
      renk: form.renk,
      aciklama: form.aciklama || null,
      isActive: form.isActive,
    }

    try {
      if (form.id === null) {
        const olusan = await guzergahEkle(govde, oturumBitti)
        setBilgi(`"${form.ad}" güzergahı eklendi.`)
        // Yeni hat hemen seçili gelsin: kullanıcının sıradaki işi ona durak
        // eklemek, listede aramak değil.
        setSeciliId(olusan.id)
      } else {
        await guzergahGuncelle(form.id, govde, oturumBitti)
        setBilgi(`"${form.ad}" güzergahı güncellendi.`)
      }
      setForm(null)
      await yukle()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    } finally {
      setKaydediliyor(false)
    }
  }

  const guzergahiSil = async (g) => {
    if (!window.confirm(
      `"${g.ad}" güzergahı silinecek.\n\n`
      + 'Kayıt veritabanından tamamen silinmez; is_deleted = true yapılarak '
      + 'gizlenir.\n\nDevam edilsin mi?',
    )) return

    setHata(null)
    try {
      await guzergahSil(g.id, oturumBitti)
      setBilgi(`"${g.ad}" güzergahı silindi.`)
      if (seciliId === g.id) setSeciliId(null)
      await yukle()
    } catch (err) {
      // "Durağı olan güzergah silinemez" mesajı buradan geliyor — liste
      // ekranda kalıyor ki kullanıcı hangi durakları taşıyacağını görsün.
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    }
  }

  const duragiSil = async (durak) => {
    if (!window.confirm(`"${durak.ad}" durağı silinsin mi?`)) return

    setHata(null)
    try {
      await durakSil(durak.id, oturumBitti)
      setBilgi(`"${durak.ad}" durağı silindi.`)
      await yukle()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    }
  }

  // ------------------------------------------------------------------
  //  Ödev 17 / Madde 1 — "Rota Oluştur"
  // ------------------------------------------------------------------

  const rotayiHesapla = async (g) => {
    setHata(null)
    setRotaHesaplanan(g.id)
    try {
      // Ara nokta YOK: yonetim ekranindan hesaplanan rota, OSRM'in kendi
      // en iyi buldugu yol. Alternatif secimi harita ekraninda, duraga
      // tiklayarak yapiliyor.
      const guncel = await rotaOlustur(g.id, [], oturumBitti)
      const km = ((guncel.rotaMesafeMetre ?? 0) / 1000).toFixed(1)
      const dk = Math.round((guncel.rotaSureSaniye ?? 0) / 60)
      setBilgi(`"${g.ad}" rotası hesaplandı: ${km} km · ~${dk} dk sürüş.`)
      await yukle()
    } catch (err) {
      // OSRM kapalı / rota bulunamadı mesajları buradan geliyor. Liste
      // ekranda KALIYOR: kullanıcı hangi hatta ne olduğunu görmeye devam
      // etmeli, hata yüzünden ekranı boşaltmak yardımcı olmaz.
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    } finally {
      setRotaHesaplanan(null)
    }
  }

  // ------------------------------------------------------------------
  //  Ödev 17 / Madde 2 — durak düzenleme
  // ------------------------------------------------------------------

  const duragiDuzenle = (durak) => setDurakFormu({
    id: durak.id,
    ad: durak.ad,
    aciklama: durak.aciklama ?? '',
    guzergahId: durak.guzergahId,
  })

  const durakFormunuKaydet = async (e) => {
    e.preventDefault()
    setDurakKaydediliyor(true)
    setHata(null)

    try {
      // wkt GÖNDERİLMİYOR → konum değişmiyor.
      //
      // Konumu bu formdan düzenletmiyoruz çünkü koordinatı elle yazmak hem
      // hataya açık hem de anlamsız: durağın yeri haritada seçilir. Burada
      // yalnızca metin alanları var.
      await durakGuncelle(durakFormu.id, {
        ad: durakFormu.ad,
        guzergahId: durakFormu.guzergahId,
        aciklama: durakFormu.aciklama || null,
      }, oturumBitti)

      setBilgi(`"${durakFormu.ad}" durağı güncellendi.`)
      setDurakFormu(null)
      await yukle()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    } finally {
      setDurakKaydediliyor(false)
    }
  }

  // ------------------------------------------------------------------
  //  Sıralama — sürükle-bırak ve klavye
  // ------------------------------------------------------------------

  /**
   * Yeni sırayı önce EKRANDA uygular, sonra sunucuya yazar.
   *
   * Sunucu reddederse (yarış durumu, yetki) listeyi tazeleyip gerçek sırayı
   * geri getiriyoruz — iyimser güncellemenin bedeli bu geri alma, kazancı
   * ise sürüklemenin anında hissedilmesi.
   */
  const siraUygula = async (yeniDuraklar) => {
    if (!secili) return

    setGuzergahlar((onceki) => onceki.map((g) => (
      g.id === secili.id
        // Sıra numaraları da anında yeniden yazılıyor: yalnızca diziyi
        // değiştirseydik satırlardaki "1." "2." etiketleri eski kalırdı.
        ? { ...g, duraklar: yeniDuraklar.map((d, i) => ({ ...d, sira: i + 1 })) }
        : g
    )))

    setSiraKaydediliyor(true)
    setHata(null)
    try {
      await siralamaKaydet(secili.id, yeniDuraklar.map((d) => d.id), oturumBitti)
      setBilgi('Durak sırası güncellendi.')
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
      await yukle()   // sunucudaki gerçek sıraya dön
    } finally {
      setSiraKaydediliyor(false)
    }
  }

  /** Listede bir öğeyi kaynak konumdan hedef konuma taşır. */
  const tasi = (liste, kaynak, hedef) => {
    if (kaynak === hedef || hedef < 0 || hedef >= liste.length) return null
    const kopya = [...liste]
    const [tasinan] = kopya.splice(kaynak, 1)
    kopya.splice(hedef, 0, tasinan)
    return kopya
  }

  const suruklemeBasla = (id) => {
    suruklenenIdRef.current = id
    setSuruklenenId(id)
  }

  const suruklemeBitti = () => {
    suruklenenIdRef.current = null
    setSuruklenenId(null)
  }

  const birak = (hedefIndex) => {
    const tasinanId = suruklenenIdRef.current
    if (tasinanId === null || !secili) return

    const kaynak = secili.duraklar.findIndex((d) => d.id === tasinanId)
    suruklemeBitti()

    const yeni = tasi(secili.duraklar, kaynak, hedefIndex)
    if (yeni) siraUygula(yeni)
  }

  const klavyeTasi = (index, yon) => {
    if (!secili) return
    const yeni = tasi(secili.duraklar, index, index + yon)
    if (yeni) siraUygula(yeni)
  }

  // ------------------------------------------------------------------

  return (
    <div className="admin-sayfa">
      <header className="admin-baslik">
        <div>
          <h1><GuzergahIkonu size={22} /> Güzergah Yönetimi</h1>
          <p className="admin-alt-baslik">
            Hat tanımla, rengini seç, durakları sürükleyerek sırala.
          </p>
        </div>

        <button type="button" className="btn-primary" onClick={yeniGuzergah}>
          <EkleIkonu /> Yeni güzergah
        </button>
      </header>

      {(yuklemeHatasi || hata) && <p className="error-banner">{yuklemeHatasi ?? hata}</p>}
      {bilgi && !hata && !yuklemeHatasi && <p className="info-banner">{bilgi}</p>}

      {/* ---------- Güzergah formu ---------- */}
      {form && (
        <form className="admin-kart admin-form" onSubmit={formuKaydet}>
          <h2>{form.id === null ? 'Yeni güzergah' : `"${form.ad}" güzergahını düzenle`}</h2>

          <div className="admin-form-satir">
            <label>
              Güzergah adı
              <input
                value={form.ad}
                onChange={(e) => setForm({ ...form, ad: e.target.value })}
                required
                maxLength={150}
                placeholder="Örn: 126 Ulus - Çayyolu"
                autoFocus
              />
            </label>

            <label>
              Açıklama
              <input
                value={form.aciklama}
                onChange={(e) => setForm({ ...form, aciklama: e.target.value })}
                maxLength={500}
                placeholder="Hat hakkında not"
              />
            </label>
          </div>

          {/* ---------- Renk seçimi ----------
              Ödev metni rengi açıkça istiyor. Hazır palet + serbest seçici
              birlikte: palet hızlı ve ayırt edilebilir renkler veriyor,
              serbest seçici kurumun kendi hat renklerini girmeyi mümkün
              kılıyor (metro hatları gerçek hayatta belirli renklerdedir). */}
          <fieldset className="renk-secici">
            <legend>Hat rengi</legend>

            <div className="renk-izgara">
              {RENKLER.map((renk) => (
                <button
                  key={renk}
                  type="button"
                  className={`renk-kutu${form.renk === renk ? ' secili' : ''}`}
                  style={{ background: renk }}
                  onClick={() => setForm({ ...form, renk })}
                  title={renk}
                  aria-label={`Renk ${renk}`}
                  aria-pressed={form.renk === renk}
                />
              ))}

              <input
                type="color"
                className="renk-serbest"
                value={form.renk}
                onChange={(e) => setForm({ ...form, renk: e.target.value })}
                title="Serbest renk seç"
                aria-label="Serbest renk"
              />
            </div>

            <p className="muted">
              Seçilen renk haritada hem hattın çizgisini hem duraklarını
              boyuyor. Kod: <code>{form.renk}</code>
            </p>
          </fieldset>

          <label className="admin-onay">
            <input
              type="checkbox"
              checked={form.isActive}
              onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
            />
            Güzergah aktif (pasif hatta yeni durak eklenemez)
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

      {/* ---------- İki sütun: hatlar | duraklar ---------- */}
      <div className="ulasim-duzen">

        {/* ---- Sol: güzergah listesi ---- */}
        <section className="admin-kart">
          <h2>Güzergahlar <span className="admin-rozet notr">{guzergahlar.length}</span></h2>

          {yukleniyor ? (
            <p className="muted">Yükleniyor…</p>
          ) : yuklemeHatasi ? (
            <p className="muted">Liste görüntülenemedi.</p>
          ) : guzergahlar.length === 0 ? (
            <p className="muted">
              Henüz güzergah tanımlanmamış. <strong>Yeni güzergah</strong> ile başlayın;
              durak eklemek için önce bir hat gerekiyor.
            </p>
          ) : (
            <ul className="guzergah-listesi">
              {guzergahlar.map((g) => (
                <li key={g.id} className={g.id === seciliId ? 'secili' : ''}>
                  <button
                    type="button"
                    className="guzergah-satir"
                    onClick={() => setSeciliId(g.id)}
                    aria-pressed={g.id === seciliId}
                  >
                    <span className="guzergah-renk" style={{ background: g.renk }} />
                    <span className="guzergah-govde">
                      <strong>{g.ad}</strong>
                      <small>
                        {g.durakSayisi} durak
                        {!g.isActive && <span className="pasif-rozet">Pasif</span>}
                        {g.kullaniciAdi && <> · <KullaniciIkonu /> {g.kullaniciAdi}</>}
                      </small>

                      {/* Ödev 17: rotanın durumu — üç hâl, üçü farklı şey söylüyor.
                          "yok" bilgi, "güncel değil" UYARI, özet ise sonuç. */}
                      <small className="rota-durum">
                        {!g.rotaWkt && g.durakSayisi >= 2 && (
                          <span className="rota-rozet yok">rota yok</span>
                        )}
                        {g.rotaWkt && !g.rotaGuncel && (
                          <span className="rota-rozet eski">rota güncel değil</span>
                        )}
                        {g.rotaWkt && g.rotaGuncel && (
                          <span className="rota-rozet var">
                            {((g.rotaMesafeMetre ?? 0) / 1000).toFixed(1)} km
                            {' · ~'}{Math.round((g.rotaSureSaniye ?? 0) / 60)} dk sürüş
                          </span>
                        )}
                      </small>
                    </span>
                  </button>

                  <span className="guzergah-eylem">
                    {/* Ödev 17 / Madde 1 — "Rota Oluştur".

                        2 duraktan az olan hatta KAPALI: rota için en az iki
                        nokta gerekiyor ve sunucu zaten reddediyor. Düğmeyi
                        açık bırakıp hata göstermektense, neden basılamadığını
                        title ile söylemek daha az sürtünme. */}
                    <button
                      type="button"
                      className="btn-ghost"
                      onClick={() => rotayiHesapla(g)}
                      disabled={g.durakSayisi < 2 || rotaHesaplanan === g.id}
                      title={g.durakSayisi < 2
                        ? 'Rota için en az 2 durak gerekli.'
                        : 'Durakları izleyen sürüş rotasını OSRM ile hesapla'}
                    >
                      {rotaHesaplanan === g.id ? 'Hesaplanıyor…' : 'Rota Oluştur'}
                    </button>
                    <button type="button" className="btn-ghost"
                            onClick={() => guzergahDuzenle(g)}>
                      Düzenle
                    </button>
                    <button type="button" className="btn-ghost sil"
                            onClick={() => guzergahiSil(g)}>
                      <SilIkonu />
                    </button>
                  </span>
                </li>
              ))}
            </ul>
          )}
        </section>

        {/* ---- Sağ: seçilen hattın durakları ---- */}
        <section className="admin-kart">
          {!secili ? (
            <>
              <h2>Duraklar</h2>
              <p className="muted">Durakları görmek için soldan bir güzergah seçin.</p>
            </>
          ) : (
            <>
              <h2>
                <span className="guzergah-renk" style={{ background: secili.renk }} />
                {secili.ad} — Duraklar
                <span className="admin-rozet notr">{secili.durakSayisi}</span>
                {siraKaydediliyor && <span className="muted"> kaydediliyor…</span>}
              </h2>

              {secili.duraklar.length === 0 ? (
                <p className="muted">
                  Bu güzergahta henüz durak yok. Duraklar <strong>haritadan</strong>
                  {' '}eklenir: "Durak Ekle" aracıyla noktaya tıklayıp bu hattı seçin.
                </p>
              ) : (
                <>
                  <p className="muted surukle-ipucu">
                    Satırı <strong>sürükleyip bırakarak</strong> sırayı değiştirin.
                    Fare kullanmıyorsanız <kbd>▲</kbd> <kbd>▼</kbd> düğmeleri aynı işi yapar.
                  </p>

                  {/*
                    Sürükle-bırak: tarayıcının kendi HTML5 API'si.
                    • draggable       → satır sürüklenebilir
                    • onDragStart     → hangi durak taşınıyor (id'yi state'e koyuyoruz)
                    • onDragOver      → preventDefault ŞART; olmazsa tarayıcı
                                        bırakmayı reddediyor ve drop hiç tetiklenmiyor
                    • onDrop          → hedef sıraya taşı ve sunucuya yaz
                  */}
                  <ol className="durak-listesi">
                    {secili.duraklar.map((durak, index) => (
                      <li
                        key={durak.id}
                        draggable
                        onDragStart={() => suruklemeBasla(durak.id)}
                        onDragEnd={suruklemeBitti}
                        onDragOver={(e) => e.preventDefault()}
                        onDrop={() => birak(index)}
                        className={suruklenenId === durak.id ? 'suruklenen' : ''}
                      >
                        <span className="durak-tutamac" aria-hidden="true">
                          <SurukleIkonu />
                        </span>

                        <span className="durak-sira" style={{ background: secili.renk }}>
                          {durak.sira}
                        </span>

                        <span className="durak-govde">
                          <strong>{durak.ad}</strong>
                          <small>
                            {durak.aciklama || 'açıklama yok'}
                            {durak.kullaniciAdi && <> · <KullaniciIkonu /> {durak.kullaniciAdi}</>}
                          </small>
                        </span>

                        <span className="durak-eylem">
                          <button
                            type="button"
                            className="btn-ghost"
                            onClick={() => klavyeTasi(index, -1)}
                            disabled={index === 0}
                            title="Yukarı taşı"
                            aria-label={`${durak.ad} durağını yukarı taşı`}
                          >
                            ▲
                          </button>
                          <button
                            type="button"
                            className="btn-ghost"
                            onClick={() => klavyeTasi(index, 1)}
                            disabled={index === secili.duraklar.length - 1}
                            title="Aşağı taşı"
                            aria-label={`${durak.ad} durağını aşağı taşı`}
                          >
                            ▼
                          </button>
                          {/* Ödev 17 / Madde 2: duraklar da düzenlenebilmeli.
                              Ödev 16'da PUT ucu vardı ama arayüzden
                              erişilemiyordu; ad düzeltmek için durağı silip
                              yeniden eklemek gerekiyordu ve bu sırayı bozuyordu. */}
                          <button
                            type="button"
                            className="btn-ghost"
                            onClick={() => duragiDuzenle(durak)}
                            title="Durağı düzenle"
                            aria-label={`${durak.ad} durağını düzenle`}
                          >
                            Düzenle
                          </button>
                          <button type="button" className="btn-ghost sil"
                                  onClick={() => duragiSil(durak)}
                                  title="Durağı sil">
                            <SilIkonu />
                          </button>
                        </span>

                        {/* Form SATIRIN İÇİNDE açılıyor: düzenlenen durağın
                            hangi hattın kaçıncı durağı olduğu bağlamı
                            kaybolmasın. Ayrı bir ekrana gitmek kullanıcıyı
                            sıralamadan koparırdı. */}
                        {durakFormu?.id === durak.id && (
                          <form className="durak-form" onSubmit={durakFormunuKaydet}>
                            <label>
                              Durak adı
                              <input
                                type="text"
                                value={durakFormu.ad}
                                onChange={(e) => setDurakFormu({ ...durakFormu, ad: e.target.value })}
                                maxLength={150}
                                required
                                autoFocus
                              />
                            </label>

                            <label>
                              Açıklama
                              <input
                                type="text"
                                value={durakFormu.aciklama}
                                onChange={(e) => setDurakFormu({ ...durakFormu, aciklama: e.target.value })}
                                maxLength={500}
                                placeholder="peron, aktarma bilgisi…"
                              />
                            </label>

                            <div className="durak-form-eylem">
                              <button type="submit" className="btn-primary" disabled={durakKaydediliyor}>
                                {durakKaydediliyor ? 'Kaydediliyor…' : 'Kaydet'}
                              </button>
                              <button type="button" className="btn-ghost"
                                      onClick={() => setDurakFormu(null)}>
                                Vazgeç
                              </button>
                            </div>

                            {/* Konum bilerek düzenlenmiyor: koordinatı elle
                                yazmak hataya açık ve anlamsız — durağın yeri
                                haritada seçilir. */}
                            <p className="muted">
                              Konum bu formdan değişmez; durağı taşımak için harita
                              ekranını kullanın.
                            </p>
                          </form>
                        )}
                      </li>
                    ))}
                  </ol>
                </>
              )}

              <p className="muted">
                <DurakIkonu size={14} /> Yeni durak <strong>harita ekranından</strong>
                {' '}ekleniyor — konumu haritadan almak, koordinatı elle yazmaktan hem
                hızlı hem hatasız.
              </p>
            </>
          )}
        </section>
      </div>
    </div>
  )
}
