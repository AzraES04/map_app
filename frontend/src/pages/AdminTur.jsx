import { useCallback, useEffect, useState } from 'react'

import {
  turlariGetir, turSil, oturumAc, oturumlarim as oturumlarimIstek,
  turBaglantisi, turuSonlandir,
} from '../turApi'
import { mesafeMetni, dakikaMetni } from '../turIlerleme'
import { GuzergahIkonu, SilIkonu, KullaniciIkonu } from '../icons'

// ============================================================================
//  TUR YÖNETİMİ ekranı — hazır turlar ve canlı gruplar
//
//  ---- BU EKRAN NEDEN GEREKLİ? ----
//  Tur modülü baştan beri tek yönlü çalışıyordu: harita ekranında öneri al →
//  kaydet → paylaş. Kaydedilen tur sunucuda duruyordu ama HİÇBİR YERDE
//  LİSTELENMİYORDU. Yani bir turu ikinci kez kullanmanın yolu yoktu; her
//  grup için baştan öneri üretmek gerekiyordu.
//
//  Oysa gerçek kullanım tam tersi: bir rehber "Ankara Klasik Turu"nu bir kez
//  hazırlar, sonra her grup için yeniden başlatır. HAZIR TUR dediğimiz şey
//  bu — yeni bir veri türü değil, zaten kaydedilen turların görünür hâli.
//
//  ---- "EKİP" NEDEN AYRI BİR MODÜL DEĞİL? ----
//  Ayrı bir ekip/takım tablosu (üyeler, roller, davetler) düşünülebilirdi.
//  Gerekmiyor, çünkü iki soru da mevcut verilerle zaten cevaplanıyor:
//
//    "Kim rehber olabilir?"  → "Tur Yönetimi" yetkisi olan roller
//                              (Rol Yönetimi ekranı).
//    "Grupta kim var?"       → oturumun katılımcı listesi; kişiler
//                              bağlantıyla katılıyor, önceden tanımlanmıyor.
//
//  Üçüncü bir üyelik tablosu eklemek, aynı bilgiyi ikinci kez tutmak ve iki
//  kopyayı senkron tutma sorununu satın almak olurdu. Bu ekran onun yerine
//  var olan cevapları GÖRÜNÜR yapıyor: hangi turun rehberi kim, hangi
//  grupta kaç kişi bağlı.
// ============================================================================

export default function AdminTur() {
  const [turlar, setTurlar] = useState([])
  const [oturumlar, setOturumlar] = useState([])
  const [yukleniyor, setYukleniyor] = useState(true)
  const [hata, setHata] = useState(null)
  const [islemdeki, setIslemdeki] = useState(null)
  const [paylasim, setPaylasim] = useState(null)

  const yukle = useCallback(async () => {
    setYukleniyor(true)
    setHata(null)

    try {
      // İki liste BİRLİKTE isteniyor: "bu turun açık oturumu var mı?"
      // sorusunu cevaplamak için ikisi de gerekiyor ve sırayla beklemek
      // ekranı iki kat geç açardı.
      const [t, o] = await Promise.all([turlariGetir(), oturumlarimIstek()])
      setTurlar(t)
      setOturumlar(o)
    } catch (err) {
      setHata(err.message || 'Turlar yüklenemedi.')
    } finally {
      setYukleniyor(false)
    }
  }, [])

  useEffect(() => { yukle() }, [yukle])

  /** Bu turun AÇIK oturumu (varsa) — "Paylaş" mı "Sonlandır" mı gösterileceğini belirliyor. */
  const acikOturum = (turId) => oturumlar.find((o) => o.tourId === turId) ?? null

  const paylas = async (tur) => {
    setIslemdeki(tur.id)
    setHata(null)

    try {
      const oturum = await oturumAc(tur.id, true)
      setPaylasim({ turId: tur.id, kod: oturum.joinCode, baglanti: turBaglantisi(oturum.joinCode) })
      setOturumlar((liste) => [...liste.filter((o) => o.id !== oturum.id), oturum])
    } catch (err) {
      setHata(err.message || 'Oturum açılamadı.')
    } finally {
      setIslemdeki(null)
    }
  }

  const sonlandir = async (oturum) => {
    if (!window.confirm('Bu turun canlı oturumu sonlandırılsın mı? Katılım kodu geçersiz olur.')) return

    setIslemdeki(oturum.tourId)
    try {
      await turuSonlandir(oturum.id)
      setOturumlar((liste) => liste.filter((o) => o.id !== oturum.id))
      setPaylasim((p) => (p?.turId === oturum.tourId ? null : p))
    } catch (err) {
      setHata(err.message || 'Oturum sonlandırılamadı.')
    } finally {
      setIslemdeki(null)
    }
  }

  const sil = async (tur) => {
    // Silme geri alınabilir (çöp kutusu) ama canlı bir grubu olan turu
    // silmek, o gruptaki herkesin ekranını bozardı — önce oturum kapanmalı.
    if (acikOturum(tur.id)) {
      setHata('Canlı oturumu olan tur silinemez; önce turu sonlandırın.')
      return
    }

    if (!window.confirm(`"${tur.name}" silinsin mi? (Çöp kutusundan geri alınabilir.)`)) return

    setIslemdeki(tur.id)
    try {
      await turSil(tur.id)
      setTurlar((liste) => liste.filter((t) => t.id !== tur.id))
    } catch (err) {
      setHata(err.message || 'Tur silinemedi.')
    } finally {
      setIslemdeki(null)
    }
  }

  return (
    <div className="admin-sayfa">
      <header className="admin-baslik">
        <div>
          <h1>Tur Yönetimi</h1>
          <p className="admin-alt-metin">
            Kaydedilmiş turlar. Bir turu her grup için yeniden başlatabilir,
            paylaşım bağlantısını buradan alabilirsiniz.
          </p>
        </div>
      </header>

      {hata && <p className="analiz-hata">{hata}</p>}

      <div className="admin-kart">
        <h2>
          <span className="tool-icon"><GuzergahIkonu /></span>
          Hazır turlar <span className="sayi">{turlar.length}</span>
        </h2>

        {yukleniyor && <p className="muted">Yükleniyor…</p>}

        {!yukleniyor && turlar.length === 0 && (
          <p className="bos-durum">
            Henüz kaydedilmiş tur yok. Harita ekranında <strong>Tur Planla</strong> ile
            bir öneri alıp <strong>Turu Kaydet ve Paylaş</strong> dediğinizde tur
            burada görünür.
          </p>
        )}

        <ul className="admin-liste tur-yonetim-listesi">
          {turlar.map((tur) => {
            const oturum = acikOturum(tur.id)
            const mesgul = islemdeki === tur.id

            return (
              <li key={tur.id}>
                <div className="tur-yonetim-bilgi">
                  <strong>
                    <span className="dot" style={{ background: tur.color }} />
                    {tur.name}
                  </strong>

                  <span className="muted">
                    {tur.waypointCount} durak
                    {tur.routeDistanceMeters ? ` · ${mesafeMetni(tur.routeDistanceMeters)}` : ''}
                    {tur.routeDurationSeconds
                      ? ` · ≈ ${dakikaMetni(Math.round(tur.routeDurationSeconds / 60))} yol`
                      : ''}
                  </span>

                  {/* REHBER — "ekip" sorusunun ilk yarısı: bu turun sahibi kim? */}
                  <span className="muted">
                    <KullaniciIkonu size={12} />{' '}
                    {tur.guideUserName ?? 'rehber kaydı yok'}
                  </span>

                  {/* CANLI GRUP — ikinci yarısı: şu an kaç kişi bağlı? */}
                  {oturum && (
                    <span className="tur-yonetim-canli">
                      Canlı · {oturum.participantCount} kişi
                      {oturum.joinCode && <> · kod <strong>{oturum.joinCode}</strong></>}
                    </span>
                  )}
                </div>

                <div className="admin-eylemler">
                  {oturum ? (
                    <button
                      type="button"
                      className="btn-ghost kucuk"
                      onClick={() => sonlandir(oturum)}
                      disabled={mesgul}
                    >
                      Turu sonlandır
                    </button>
                  ) : (
                    <button
                      type="button"
                      className="btn-ghost kucuk"
                      onClick={() => paylas(tur)}
                      disabled={mesgul}
                      title="Bu turdan canlı oturum aç ve paylaşım bağlantısı üret"
                    >
                      {mesgul ? 'Açılıyor…' : 'Başlat ve paylaş'}
                    </button>
                  )}

                  <button
                    type="button"
                    className="btn-ghost kucuk tehlike"
                    onClick={() => sil(tur)}
                    disabled={mesgul}
                    aria-label={`${tur.name} turunu sil`}
                  >
                    <SilIkonu size={13} />
                  </button>
                </div>

                {/* Bağlantı, üretildiği turun ALTINDA çıkıyor: ayrı bir kutuda
                    gösterseydik hangi tura ait olduğu belirsiz kalırdı. */}
                {paylasim?.turId === tur.id && (
                  <div className="tur-yonetim-baglanti">
                    <input
                      type="text"
                      value={paylasim.baglanti ?? ''}
                      readOnly
                      onFocus={(e) => e.target.select()}
                      aria-label="Paylaşım bağlantısı"
                    />
                    <p className="muted">
                      Bu bağlantıyı gruba gönderin; açan herkes turu canlı takip eder.
                    </p>
                  </div>
                )}
              </li>
            )
          })}
        </ul>
      </div>
    </div>
  )
}
