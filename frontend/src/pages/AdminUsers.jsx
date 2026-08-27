import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  kullanicilariListele,
  kullaniciEkle,
  kullaniciGuncelle,
  kullaniciSil,
  kullaniciYetkileri,
  kullaniciYetkileriniKaydet,
  rolleriListele,
  kendiYetkilerim,
  kullaniciOnayla,
} from '../adminApi'
import { EkleIkonu, KilitIkonu, SilIkonu, YetkiIkonu, HaritaIkonu } from '../icons'
import { YETKILER } from '../yetkiler'
import CografiYetkiModal from './CografiYetkiModal.jsx'

// ============================================================================
//  Kullanıcı Listesi ekranı (Ödev 6 / Madde 1)
//
//  Üç iş bir arada:
//    1) Liste  — kullanıcılar, rolleri ve yetki sayıları
//    2) Form   — ekle / güncelle (şifre boş bırakılırsa değişmez)
//    3) Yetki  — kullanıcının yetki matrisi; ROLDEN gelen yetkiler
//                işaretli AMA kilitli gösterilir (Madde 2'nin son cümlesi)
// ============================================================================

/** Form alanlarının başlangıç değeri — "yeni kullanıcı" durumu. */
const BOS_FORM = { id: null, username: '', password: '', isActive: true, roleIds: [] }

export default function AdminUsers() {
  const navigate = useNavigate()

  const [kullanicilar, setKullanicilar] = useState([])
  const [roller, setRoller] = useState([])
  const [yukleniyor, setYukleniyor] = useState(true)
  const [hata, setHata] = useState(null)
  const [bilgi, setBilgi] = useState(null)

  // Form açık mı ve hangi kaydı düzenliyor? id null ise yeni kayıt.
  const [form, setForm] = useState(null)
  const [kaydediliyor, setKaydediliyor] = useState(false)

  // Yetki paneli: { userId, username, roles, permissions } + seçili doğrudan yetkiler
  const [yetkiPaneli, setYetkiPaneli] = useState(null)
  const [seciliYetkiler, setSeciliYetkiler] = useState([])

  // Ödev 7: coğrafi yetki modalı — açıksa hangi kullanıcı için açık?
  const [cografiSahip, setCografiSahip] = useState(null)

  // "Coğrafi Yetki" düğmesi, yetkisi olmayana HİÇ gösterilmiyor (ödevin ek maddesi).
  const [cografiYetkim, setCografiYetkim] = useState(false)

  // Token düştüğünde login'e dön. useCallback: aşağıdaki useEffect'in
  // bağımlılık listesinde duruyor, her render'da yeniden üretilirse
  // veri sonsuz döngüyle yeniden çekilirdi.
  const oturumBitti = useCallback(
    () => navigate('/login', { replace: true, state: { expired: true } }),
    [navigate],
  )

  const yukle = useCallback(async () => {
    setYukleniyor(true)
    setHata(null)
    try {
      // İki istek PARALEL gidiyor: rol listesi kullanıcı listesini beklemiyor.
      const [gelenKullanicilar, gelenRoller] = await Promise.all([
        kullanicilariListele(oturumBitti),
        rolleriListele(oturumBitti),
      ])
      setKullanicilar(gelenKullanicilar)
      setRoller(gelenRoller)
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    } finally {
      setYukleniyor(false)
    }
  }, [oturumBitti])

  useEffect(() => { yukle() }, [yukle])

  // Kendi yetkilerimi bir kez oku: düğmeyi gösterip göstermeyeceğimize karar ver.
  useEffect(() => {
    let iptal = false
    kendiYetkilerim(oturumBitti)
      .then((matris) => {
        if (iptal) return
        setCografiYetkim(matris.permissions.some(
          (y) => y.granted && y.name === YETKILER.cografiYetkiTanimlama,
        ))
      })
      .catch(() => { /* okunamadıysa düğme gizli kalır */ })
    return () => { iptal = true }
  }, [oturumBitti])

  // ------------------------------------------------------------------
  //  Form
  // ------------------------------------------------------------------

  const yeniKullanici = () => {
    setYetkiPaneli(null)
    setForm({ ...BOS_FORM })
  }

  const duzenle = (kullanici) => {
    setYetkiPaneli(null)
    setForm({
      id: kullanici.id,
      username: kullanici.username,
      password: '',                                  // boş = şifre değişmesin
      isActive: kullanici.isActive,
      roleIds: kullanici.roles.map((r) => r.id),
    })
  }

  /** Rol işaret kutusu: listede varsa çıkar, yoksa ekle. */
  const rolDegistir = (rolId) => {
    setForm((onceki) => ({
      ...onceki,
      roleIds: onceki.roleIds.includes(rolId)
        ? onceki.roleIds.filter((id) => id !== rolId)
        : [...onceki.roleIds, rolId],
    }))
  }

  const formuKaydet = async (e) => {
    e.preventDefault()
    setKaydediliyor(true)
    setHata(null)
    try {
      if (form.id === null) {
        await kullaniciEkle(
          {
            username: form.username,
            password: form.password,
            isActive: form.isActive,
            roleIds: form.roleIds,
          },
          oturumBitti,
        )
        setBilgi(`"${form.username}" eklendi.`)
      } else {
        await kullaniciGuncelle(
          form.id,
          {
            username: form.username,
            // Boş şifre alanını GÖNDERMİYORUZ: backend null gelince mevcut
            // hash'i koruyor, boş metin gelseydi doğrulamaya takılırdı.
            password: form.password ? form.password : null,
            isActive: form.isActive,
            roleIds: form.roleIds,
          },
          oturumBitti,
        )
        setBilgi(`"${form.username}" güncellendi.`)
      }
      setForm(null)
      await yukle()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    } finally {
      setKaydediliyor(false)
    }
  }

  const sil = async (kullanici) => {
    // Silme geri alınabilir (soft delete) ama listeden kaybolduğu için
    // yine de onay istiyoruz — yanlış satıra tıklamak kolay.
    if (!window.confirm(`"${kullanici.username}" kullanıcısı silinsin mi?`)) return

    setHata(null)
    try {
      await kullaniciSil(kullanici.id, oturumBitti)
      setBilgi(`"${kullanici.username}" silindi.`)
      if (yetkiPaneli?.userId === kullanici.id) setYetkiPaneli(null)
      await yukle()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    }
  }

  /**
   * Kayıt olan kullanıcıyı onaylar (Ödev 10).
   *
   * Onay ROL VERMİYOR: hesap girebilir hâle geliyor ama yetkisi olmadığı için
   * hiçbir araç görünmüyor. Rol atamak yöneticinin ayrı adımı — bu yüzden
   * onaydan sonra kullanıcıyı doğrudan düzenleme formuna alıyoruz.
   */
  const onayla = async (kullanici) => {
    setHata(null)
    try {
      const guncel = await kullaniciOnayla(kullanici.id, oturumBitti)
      setBilgi(`"${kullanici.username}" onaylandı. Şimdi rol atayabilirsiniz.`)
      await yukle()
      duzenle(guncel)
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    }
  }

  // ------------------------------------------------------------------
  //  Yetki matrisi
  // ------------------------------------------------------------------

  const yetkileriAc = async (kullanici) => {
    setForm(null)
    setHata(null)

    // Aynı satıra ikinci kez basmak paneli kapatsın.
    if (yetkiPaneli?.userId === kullanici.id) {
      setYetkiPaneli(null)
      return
    }

    try {
      const matris = await kullaniciYetkileri(kullanici.id, oturumBitti)
      setYetkiPaneli(matris)
      // Yalnızca DOĞRUDAN verilenler düzenlenebilir; rolden gelenler kilitli.
      setSeciliYetkiler(matris.permissions.filter((y) => y.direct).map((y) => y.permissionId))
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    }
  }

  const yetkiDegistir = (permissionId) => {
    setSeciliYetkiler((onceki) =>
      onceki.includes(permissionId)
        ? onceki.filter((id) => id !== permissionId)
        : [...onceki, permissionId],
    )
  }

  const yetkileriKaydet = async () => {
    setKaydediliyor(true)
    setHata(null)
    try {
      const guncelMatris = await kullaniciYetkileriniKaydet(
        yetkiPaneli.userId,
        seciliYetkiler,
        oturumBitti,
      )
      setYetkiPaneli(guncelMatris)
      setSeciliYetkiler(
        guncelMatris.permissions.filter((y) => y.direct).map((y) => y.permissionId),
      )
      setBilgi(`"${guncelMatris.username}" kullanıcısının yetkileri kaydedildi.`)
      await yukle()          // listedeki yetki sayıları da tazelensin
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    } finally {
      setKaydediliyor(false)
    }
  }

  // ------------------------------------------------------------------
  //  Arayüz
  // ------------------------------------------------------------------

  return (
    <div className="admin-sayfa">
      <header className="admin-baslik">
        <div>
          <h1>Kullanıcı Listesi</h1>
          <p className="muted">Kullanıcı ekleyin, güncelleyin, çıkarın; rol ve yetkilerini yönetin.</p>
        </div>
        <button type="button" className="btn-primary" onClick={yeniKullanici}>
          <EkleIkonu /> Yeni Kullanıcı
        </button>
      </header>

      {hata && <p className="error-banner">{hata}</p>}
      {bilgi && !hata && <p className="info-banner">{bilgi}</p>}

      {/* ---------------- Ekle / Güncelle formu ---------------- */}
      {form && (
        <form className="admin-kart admin-form" onSubmit={formuKaydet}>
          <h2>{form.id === null ? 'Yeni kullanıcı' : `#${form.id} kullanıcısını düzenle`}</h2>

          <div className="admin-form-satir">
            <label>
              Kullanıcı adı
              <input
                value={form.username}
                onChange={(e) => setForm({ ...form, username: e.target.value })}
                required
                maxLength={100}
                autoFocus
              />
            </label>

            <label>
              Şifre
              <input
                type="password"
                value={form.password}
                onChange={(e) => setForm({ ...form, password: e.target.value })}
                // Yalnızca YENİ kullanıcıda zorunlu; güncellemede boş = değişmesin
                required={form.id === null}
                minLength={6}
                placeholder={form.id === null ? 'En az 6 karakter' : 'Değiştirmek istemiyorsanız boş bırakın'}
              />
            </label>
          </div>

          <label className="admin-onay">
            <input
              type="checkbox"
              checked={form.isActive}
              onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
            />
            Hesap aktif (pasif hesap giriş yapamaz)
          </label>

          <fieldset className="admin-secim">
            <legend>Roller</legend>
            {roller.length === 0 ? (
              <p className="muted">Henüz rol tanımlanmamış. Rol Listesi ekranından ekleyebilirsiniz.</p>
            ) : (
              <div className="admin-secim-liste">
                {roller.map((rol) => (
                  <label key={rol.id} className="admin-secim-oge">
                    <input
                      type="checkbox"
                      checked={form.roleIds.includes(rol.id)}
                      onChange={() => rolDegistir(rol.id)}
                    />
                    <span>
                      <strong>{rol.name}</strong>
                      <small>{rol.permissions.length} yetki</small>
                    </span>
                  </label>
                ))}
              </div>
            )}
          </fieldset>

          <div className="admin-eylemler">
            <button type="submit" className="btn-primary" disabled={kaydediliyor}>
              {kaydediliyor ? 'Kaydediliyor…' : 'Kaydet'}
            </button>
            <button type="button" className="btn-ghost" onClick={() => setForm(null)} disabled={kaydediliyor}>
              Vazgeç
            </button>
          </div>
        </form>
      )}

      {/* ---------------- Liste ---------------- */}
      <div className="admin-kart">
        {yukleniyor ? (
          <p className="muted">Yükleniyor…</p>
        ) : hata ? (
          // Liste hiç gelmediyse "kayıt yok" demek yanıltıcı olurdu: kayıt
          // olabilir, biz göremiyoruz (örn. yetki reddi). Sebebi üstteki
          // kırmızı şerit söylüyor, burada sadece boşluğu dürüstçe dolduruyoruz.
          <p className="muted">Liste görüntülenemedi.</p>
        ) : kullanicilar.length === 0 ? (
          <p className="muted">Kayıtlı kullanıcı yok.</p>
        ) : (
          <table className="admin-tablo">
            <thead>
              <tr>
                <th>Kullanıcı</th>
                <th>Roller</th>
                <th>Yetki</th>
                <th>Durum</th>
                <th className="sag">İşlemler</th>
              </tr>
            </thead>
            <tbody>
              {kullanicilar.map((kullanici) => (
                <tr key={kullanici.id} className={yetkiPaneli?.userId === kullanici.id ? 'secili' : ''}>
                  <td>
                    <strong>{kullanici.username}</strong>
                    <small className="admin-alt-metin">#{kullanici.id}</small>
                  </td>
                  <td>
                    {kullanici.roles.length === 0 ? (
                      <span className="muted">—</span>
                    ) : (
                      kullanici.roles.map((rol) => (
                        <span key={rol.id} className="admin-rozet">{rol.name}</span>
                      ))
                    )}
                  </td>
                  <td>
                    {/* Etkin = rolden gelenler + doğrudan verilenler (tekrarsız) */}
                    <span className="admin-rozet notr">{kullanici.effectivePermissionCount} etkin</span>
                    {kullanici.directPermissionCount > 0 && (
                      <span className="admin-rozet notr">{kullanici.directPermissionCount} doğrudan</span>
                    )}
                  </td>
                  <td>
                    {/* Ödev 10: "onay bekliyor" ile "pasif" AYRI şeyler.
                        Onay bekleyen hesap hiç kullanılmadı; pasif hesap
                        bir zamanlar çalışıyordu ve askıya alındı. */}
                    {kullanici.isApproved ? (
                      <span className={`admin-durum${kullanici.isActive ? '' : ' pasif'}`}>
                        {kullanici.isActive ? 'Aktif' : 'Pasif'}
                      </span>
                    ) : (
                      <span className="onay-rozeti">Onay bekliyor</span>
                    )}
                  </td>
                  <td className="sag">
                    {/* Onay düğmesi yalnızca gerekliyken görünüyor — onaylı
                        hesaplarda duran ve hiçbir şey yapmayan bir düğme
                        listeyi gereksiz kalabalıklaştırırdı. */}
                    {!kullanici.isApproved && (
                      <button
                        type="button"
                        className="btn-primary kucuk"
                        onClick={() => onayla(kullanici)}
                        title="Bu hesabın girişine izin ver"
                      >
                        Onayla
                      </button>
                    )}
                    <button type="button" className="btn-ghost" onClick={() => yetkileriAc(kullanici)}>
                      <YetkiIkonu /> Yetkiler
                    </button>
                    {/* Ödev 7 / Madde 2: kullanıcı bazlı coğrafi yetki */}
                    {cografiYetkim && (
                      <button
                        type="button"
                        className="btn-ghost"
                        onClick={() => setCografiSahip({
                          tur: 'kullanici', id: kullanici.id, ad: kullanici.username,
                        })}
                        title="Bu kullanıcının çizim yapabileceği alanı haritadan tanımla"
                      >
                        <HaritaIkonu /> Coğrafi Yetki
                      </button>
                    )}
                    <button type="button" className="btn-ghost" onClick={() => duzenle(kullanici)}>
                      Düzenle
                    </button>
                    <button type="button" className="btn-ghost sil" onClick={() => sil(kullanici)}>
                      <SilIkonu /> Çıkar
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* ---------------- Coğrafi yetki haritası ---------------- */}
      {cografiSahip && (
        <CografiYetkiModal
          sahip={cografiSahip}
          onKapat={() => setCografiSahip(null)}
          onOturumBitti={oturumBitti}
        />
      )}

      {/* ---------------- Yetki matrisi ---------------- */}
      {yetkiPaneli && (
        <div className="admin-kart">
          <h2>
            <YetkiIkonu /> {yetkiPaneli.username} — yetkiler
          </h2>
          <p className="muted admin-aciklama">
            Rolden gelen yetkiler işaretli ve kilitlidir; onları değiştirmek için rolü düzenleyin.
            Buradan yalnızca <strong>doğrudan</strong> yetki eklenip kaldırılır.
            {yetkiPaneli.roles.length > 0 && (
              <> Roller: {yetkiPaneli.roles.map((r) => r.name).join(', ')}.</>
            )}
          </p>

          <div className="admin-yetki-liste">
            {yetkiPaneli.permissions.map((yetki) => {
              // Rolden geliyorsa: kutu işaretli VE kilitli. Aynı yetkiyi bir de
              // kullanıcıya işaretletmek anlamsız olurdu — zaten geçerli.
              const roldenGeliyor = yetki.fromRole
              const isaretli = roldenGeliyor || seciliYetkiler.includes(yetki.permissionId)

              return (
                <label
                  key={yetki.permissionId}
                  className={`admin-yetki${roldenGeliyor ? ' rolden' : ''}${isaretli ? ' verili' : ''}`}
                >
                  <input
                    type="checkbox"
                    checked={isaretli}
                    disabled={roldenGeliyor}
                    onChange={() => yetkiDegistir(yetki.permissionId)}
                  />
                  <span className="admin-yetki-govde">
                    <strong>{yetki.name}</strong>
                    {yetki.description && <small>{yetki.description}</small>}
                  </span>
                  {roldenGeliyor && (
                    <span className="admin-rozet rol" title="Bu yetki rolden geliyor, buradan değiştirilemez">
                      <KilitIkonu /> {yetki.roleNames.join(', ')} rolünden
                    </span>
                  )}
                </label>
              )
            })}
          </div>

          <div className="admin-eylemler">
            <button type="button" className="btn-primary" onClick={yetkileriKaydet} disabled={kaydediliyor}>
              {kaydediliyor ? 'Kaydediliyor…' : 'Yetkileri kaydet'}
            </button>
            <button type="button" className="btn-ghost" onClick={() => setYetkiPaneli(null)}>
              Kapat
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
