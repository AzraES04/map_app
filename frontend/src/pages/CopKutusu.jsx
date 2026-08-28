import { useCallback, useEffect, useMemo, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { copKutusu, copGeriAl } from '../adminApi'

// ============================================================================
//  ÇÖP KUTUSU — silinen her kaydın geri getirilebildiği tek ekran
//
//  ---- NEDEN GEREKLİ OLDU? ----
//
//  Proje Ödev 3'ten beri SOFT DELETE kullanıyor: silinen hiçbir kayıt
//  veritabanından gitmiyor, yalnızca is_deleted = true yapılıyor. Yani veri
//  ZATEN duruyordu — ama arayüzden görmenin bir yolu yoktu.
//
//  Geometri ve POI'de "restore" uçları vardı; ancak silinenleri LİSTELEYEN
//  hiçbir şey olmadığı için o uçları çağırmak, silinen kaydın id'sini bir
//  yerden bilmeyi gerektiriyordu. Pratikte erişilemezlerdi. Silme onay
//  kutusundaki "geri alma yönetim panelinden yapılır" cümlesi de aslında
//  var olmayan bir ekranı işaret ediyordu.
//
//  ---- TEK LİSTE, DOKUZ TÜR ----
//
//  Nokta, çizgi, alan, POI, kategori, durak, güzergah, kullanıcı, rol.
//  Her tür için ayrı sekme/ekran da yapılabilirdi ama kullanıcının sorusu
//  tek: "ne sildim, geri alabilir miyim?" Tür bir SÜZGEÇ, ayrı bir ekran
//  değil.
// ============================================================================

export default function CopKutusu() {
  const navigate = useNavigate()

  // Bu ekran iki adreste açılıyor: `/cop` (tek başına) ve `/admin/cop`
  // (yönetim panelinin içinde). Panelin sol çubuğunda ZATEN bir "Haritaya
  // dön" var; aynı düğmeyi bir de başlıkta göstermek aynı ekranda iki özdeş
  // düğme demek olurdu.
  const panelIcinde = useLocation().pathname.startsWith('/admin')

  const [veri, setVeri] = useState(null)
  const [yukleniyor, setYukleniyor] = useState(true)
  const [turSuzgeci, setTurSuzgeci] = useState(null)   // null = hepsi
  const [islemdeki, setIslemdeki] = useState(null)     // geri alınan kaydın anahtarı
  const [hata, setHata] = useState(null)
  const [bilgi, setBilgi] = useState(null)

  const oturumBitti = useCallback(
    () => navigate('/login', { replace: true, state: { expired: true } }),
    [navigate],
  )

  const yukle = useCallback(async () => {
    setYukleniyor(true)
    setHata(null)
    try {
      setVeri(await copKutusu(oturumBitti))
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    } finally {
      setYukleniyor(false)
    }
  }, [oturumBitti])

  useEffect(() => { yukle() }, [yukle])

  const gosterilen = useMemo(() => {
    if (!veri) return []
    return turSuzgeci ? veri.ogeler.filter((o) => o.tur === turSuzgeci) : veri.ogeler
  }, [veri, turSuzgeci])

  const geriAl = async (oge) => {
    const anahtar = `${oge.tur}-${oge.id}`
    setIslemdeki(anahtar)
    setHata(null)
    setBilgi(null)

    try {
      await copGeriAl(oge.tur, oge.id, oturumBitti)
      setBilgi(`"${oge.ad}" geri alındı.`)
      await yukle()
    } catch (err) {
      if (err.message !== 'Oturum süresi doldu') setHata(err.message)
    } finally {
      setIslemdeki(null)
    }
  }

  const toplam = veri?.ogeler.length ?? 0

  return (
    <div className="admin-sayfa cop-sayfa">
      <header className="admin-baslik">
        <div>
          <h1>Çöp Kutusu</h1>
          <p className="admin-alt-baslik">
            Silinen kayıtlar burada duruyor ve geri alınabilir
          </p>
        </div>
        {!panelIcinde && (
          <button type="button" className="btn-ghost" onClick={() => navigate('/map')}>
            Haritaya dön
          </button>
        )}
      </header>

      {bilgi && <p className="info-banner">{bilgi}</p>}
      {hata && <p className="error-banner">{hata}</p>}

      <section className="admin-kart">
        <h2>
          Silinen kayıtlar
          <span className="admin-rozet notr">{toplam}</span>
        </h2>

        {/* Bu cümle bir açıklama değil, projenin bir TASARIM KARARININ
            görünür hâli: "sildim" demek "yok oldu" demek değil. */}
        <p className="muted">
          Bu uygulamada silme işlemi kaydı veritabanından <strong>çıkarmıyor</strong>;
          yalnızca <code>is_deleted</code> işaretliyor. Yani silinen her şey
          geri alınabilir.
        </p>

        {yukleniyor && <p className="muted">Yükleniyor…</p>}

        {!yukleniyor && toplam === 0 && (
          <p className="muted">
            Çöp kutusu boş — silinmiş kayıt yok.
          </p>
        )}

        {!yukleniyor && toplam > 0 && (
          <>
            {/* Tür süzgeci. "Hepsi" ilk sırada ve varsayılan: kullanıcı
                genelde ne sildiğini hatırlamıyor, türünü de bilmiyor. */}
            <div className="cop-suzgec">
              <button
                type="button"
                className={`cop-sekme${turSuzgeci === null ? ' aktif' : ''}`}
                onClick={() => setTurSuzgeci(null)}
              >
                Hepsi
                <span className="sayi">{toplam}</span>
              </button>

              {veri.ozet.map((o) => (
                <button
                  key={o.tur}
                  type="button"
                  className={`cop-sekme${turSuzgeci === o.tur ? ' aktif' : ''}`}
                  onClick={() => setTurSuzgeci(o.tur)}
                >
                  {o.turAdi}
                  <span className="sayi">{o.adet}</span>
                </button>
              ))}
            </div>

            <ul className="cop-listesi">
              {gosterilen.map((oge) => (
                <li key={`${oge.tur}-${oge.id}`}>
                  <span className="cop-tur">{oge.turAdi}</span>

                  <span className="cop-govde">
                    <strong>{oge.ad}</strong>
                    <small>
                      {oge.detay && <>{oge.detay} · </>}
                      {oge.silinmeZamani
                        ? new Date(oge.silinmeZamani).toLocaleString('tr-TR')
                        : 'silinme zamanı bilinmiyor'}
                      {oge.ekleyen && <> · ekleyen: {oge.ekleyen}</>}
                    </small>
                  </span>

                  {/* Düğme yetkiye göre açık/kapalı ve bu bilgi SUNUCUDAN
                      geliyor. İstemcide hesaplasaydık yetki kurallarını
                      ikinci kez uygulamış olurduk. */}
                  <button
                    type="button"
                    className="btn-primary kucuk"
                    onClick={() => geriAl(oge)}
                    disabled={!oge.geriAlinabilir || islemdeki === `${oge.tur}-${oge.id}`}
                    title={oge.geriAlinabilir
                      ? 'Bu kaydı geri getir'
                      : 'Bu türü geri almak için yetkiniz yok'}
                  >
                    {islemdeki === `${oge.tur}-${oge.id}` ? 'Alınıyor…' : 'Geri al'}
                  </button>
                </li>
              ))}
            </ul>

            <p className="tool-hint muted">
              <strong>Silinme zamanı</strong> ayrı bir kolondan değil,
              kaydın son değişiklik tarihinden okunuyor: silme işlemi bir
              güncellemedir ve silinmiş bir kayıt başka hiçbir yerden
              değiştirilemediği için son değişikliği, silinmesidir.
            </p>
          </>
        )}
      </section>
    </div>
  )
}
