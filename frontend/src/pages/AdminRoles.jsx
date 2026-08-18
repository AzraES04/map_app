import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  rolleriListele, rolEkle, rolGuncelle, rolSil, yetkileriListele, kendiYetkilerim,
} from '../adminApi'
import { EkleIkonu, SilIkonu, RolIkonu, HaritaIkonu } from '../icons'
import { YETKILER } from '../yetkiler'
import CografiYetkiModal from './CografiYetkiModal.jsx'

// ============================================================================
//  Rol Listesi ekranı (Ödev 6 / Madde 1)
//
//  Rol = yetki demeti. Bu yüzden rol formu, yetki işaret kutularını da içeriyor:
//  rolü kaydetmek ile yetkilerini kaydetmek tek işlem. İkisini ayırsaydık
//  "rolü ekledim ama yetkilerini vermeyi unuttum" durumu mümkün olurdu.
// ============================================================================

const BOS_FORM = { id: null, name: '', description: '', isActive: true, permissionIds: [] }

export default function AdminRoles() {
  const navigate = useNavigate()

  const [roller, setRoller] = useState([])
  const [yetkiler, setYetkiler] = useState([])
  const [yukleniyor, setYukleniyor] = useState(true)
  const [hata, setHata] = useState(null)
  const [bilgi, setBilgi] = useState(null)

  const [form, setForm] = useState(null)
  const [kaydediliyor, setKaydediliyor] = useState(false)

  // Ödev 7: coğrafi yetki modalı + düğmeyi göstermeye yetkim var mı?
  const [cografiSahip, setCografiSahip] = useState(null)
  const [cografiYetkim, setCografiYetkim] = useState(false)

  const oturumBitti = useCallback(
    () => navigate('/login', { replace: true, state: { expired: true } }),
    [navigate],
  )

  const yukle = useCallback(async () => {
    setYukleniyor(true)
    setHata(null)
    try {
      const [gelenRoller, gelenYetkiler] = await Promise.all([
        rolleriListele(oturumBitti),
        yetkileriListele(oturumBitti),
      ])
      setRoller(gelenRoller)
      setYetkiler(gelenYetkiler)
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    } finally {
      setYukleniyor(false)
    }
  }, [oturumBitti])

  useEffect(() => { yukle() }, [yukle])

  // Yetkisi olmayana "Coğrafi Yetki" düğmesi HİÇ gösterilmiyor (ödevin ek maddesi).
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

  const yeniRol = () => setForm({ ...BOS_FORM })

  const duzenle = (rol) => setForm({
    id: rol.id,
    name: rol.name,
    description: rol.description ?? '',
    isActive: rol.isActive,
    permissionIds: rol.permissions.map((y) => y.id),
  })

  const yetkiDegistir = (yetkiId) => {
    setForm((onceki) => ({
      ...onceki,
      permissionIds: onceki.permissionIds.includes(yetkiId)
        ? onceki.permissionIds.filter((id) => id !== yetkiId)
        : [...onceki.permissionIds, yetkiId],
    }))
  }

  const formuKaydet = async (e) => {
    e.preventDefault()
    setKaydediliyor(true)
    setHata(null)

    const govde = {
      name: form.name,
      description: form.description || null,
      isActive: form.isActive,
      permissionIds: form.permissionIds,
    }

    try {
      if (form.id === null) {
        await rolEkle(govde, oturumBitti)
        setBilgi(`"${form.name}" rolü eklendi.`)
      } else {
        await rolGuncelle(form.id, govde, oturumBitti)
        setBilgi(`"${form.name}" rolü güncellendi.`)
      }
      setForm(null)
      await yukle()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    } finally {
      setKaydediliyor(false)
    }
  }

  const sil = async (rol) => {
    // Roldeki kullanıcı sayısını onay metnine koyuyoruz: silmenin kimleri
    // etkileyeceğini görmeden karar vermek zor.
    const uyari = rol.userCount > 0
      ? `"${rol.name}" rolü ${rol.userCount} kullanıcıda tanımlı. Silinirse bu kullanıcılar rolden gelen yetkilerini kaybeder. Devam edilsin mi?`
      : `"${rol.name}" rolü silinsin mi?`

    if (!window.confirm(uyari)) return

    setHata(null)
    try {
      await rolSil(rol.id, oturumBitti)
      setBilgi(`"${rol.name}" rolü silindi.`)
      await yukle()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    }
  }

  // ------------------------------------------------------------------

  return (
    <div className="admin-sayfa">
      <header className="admin-baslik">
        <div>
          <h1>Rol Listesi</h1>
          <p className="muted">Rol ekleyin, güncelleyin, silin; her rolün yetkilerini buradan belirleyin.</p>
        </div>
        <button type="button" className="btn-primary" onClick={yeniRol}>
          <EkleIkonu /> Yeni Rol
        </button>
      </header>

      {hata && <p className="error-banner">{hata}</p>}
      {bilgi && !hata && <p className="info-banner">{bilgi}</p>}

      {/* ---------------- Ekle / Güncelle formu ---------------- */}
      {form && (
        <form className="admin-kart admin-form" onSubmit={formuKaydet}>
          <h2>{form.id === null ? 'Yeni rol' : `"${form.name}" rolünü düzenle`}</h2>

          <div className="admin-form-satir">
            <label>
              Rol adı
              <input
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                required
                maxLength={100}
                autoFocus
              />
            </label>

            <label>
              Açıklama
              <input
                value={form.description}
                onChange={(e) => setForm({ ...form, description: e.target.value })}
                maxLength={500}
                placeholder="Bu rol ne işe yarıyor?"
              />
            </label>
          </div>

          <label className="admin-onay">
            <input
              type="checkbox"
              checked={form.isActive}
              onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
            />
            Rol aktif (pasif rol, kullanıcılarına yetki dağıtmaz)
          </label>

          <fieldset className="admin-secim">
            <legend>Yetkiler</legend>
            <div className="admin-yetki-liste">
              {yetkiler.map((yetki) => (
                <label
                  key={yetki.id}
                  className={`admin-yetki${form.permissionIds.includes(yetki.id) ? ' verili' : ''}`}
                >
                  <input
                    type="checkbox"
                    checked={form.permissionIds.includes(yetki.id)}
                    onChange={() => yetkiDegistir(yetki.id)}
                  />
                  <span className="admin-yetki-govde">
                    <strong>{yetki.name}</strong>
                    {yetki.description && <small>{yetki.description}</small>}
                  </span>
                </label>
              ))}
            </div>
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

      {/* ---------------- Coğrafi yetki haritası ---------------- */}
      {cografiSahip && (
        <CografiYetkiModal
          sahip={cografiSahip}
          onKapat={() => setCografiSahip(null)}
          onOturumBitti={oturumBitti}
        />
      )}

      {/* ---------------- Liste ---------------- */}
      <div className="admin-kart">
        {yukleniyor ? (
          <p className="muted">Yükleniyor…</p>
        ) : hata ? (
          // Bkz. AdminUsers: hata varken "kayıt yok" demek yanıltıcı olur.
          <p className="muted">Liste görüntülenemedi.</p>
        ) : roller.length === 0 ? (
          <p className="muted">Henüz rol tanımlanmamış.</p>
        ) : (
          <table className="admin-tablo">
            <thead>
              <tr>
                <th>Rol</th>
                <th>Yetkiler</th>
                <th>Kullanıcı</th>
                <th>Durum</th>
                <th className="sag">İşlemler</th>
              </tr>
            </thead>
            <tbody>
              {roller.map((rol) => (
                <tr key={rol.id}>
                  <td>
                    <strong><RolIkonu size={14} /> {rol.name}</strong>
                    {rol.description && <small className="admin-alt-metin">{rol.description}</small>}
                  </td>
                  <td className="admin-rozet-hucre">
                    {rol.permissions.length === 0 ? (
                      <span className="muted">—</span>
                    ) : (
                      rol.permissions.map((yetki) => (
                        <span key={yetki.id} className="admin-rozet">{yetki.name}</span>
                      ))
                    )}
                  </td>
                  <td>
                    <span className="admin-rozet notr">{rol.userCount}</span>
                  </td>
                  <td>
                    <span className={`admin-durum${rol.isActive ? '' : ' pasif'}`}>
                      {rol.isActive ? 'Aktif' : 'Pasif'}
                    </span>
                  </td>
                  <td className="sag">
                    {/* Ödev 7 / Madde 2: rol bazlı coğrafi yetki —
                        alan role verilince o roldeki HERKESE uygulanır. */}
                    {cografiYetkim && (
                      <button
                        type="button"
                        className="btn-ghost"
                        onClick={() => setCografiSahip({ tur: 'rol', id: rol.id, ad: rol.name })}
                        title="Bu roldeki kullanıcıların çizim yapabileceği alanı tanımla"
                      >
                        <HaritaIkonu /> Coğrafi Yetki
                      </button>
                    )}
                    <button type="button" className="btn-ghost" onClick={() => duzenle(rol)}>
                      Düzenle
                    </button>
                    <button type="button" className="btn-ghost sil" onClick={() => sil(rol)}>
                      <SilIkonu /> Sil
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
