import { useState } from 'react'
import { useNavigate } from 'react-router-dom'

import {
  getUsername, kayitliHesaplar, hesabaGec, hesabiUnut, aktifOturumuBirak,
} from './auth'
import { KullaniciIkonu } from './icons'

// ============================================================================
//  HESAP DEĞİŞTİRİCİ (Ödev 11)
//
//  Üst bardaki kullanıcı rozeti bir menü: bu tarayıcıda daha önce giriş yapmış
//  hesaplar listeleniyor, tıklayınca geçiliyor — Instagram'ın hesap değiştirme
//  davranışının aynısı.
//
//  ŞİFRELER SAKLANMIYOR. "Şifre kayıtlıysa direkt geçsin" isteğinin güvenli
//  karşılığı, şifreyi değil OTURUMU saklamak (bkz. auth.js):
//    • kayıtlı token hâlâ geçerli → tek tıkla geçiş
//    • süresi dolmuş            → giriş ekranı, kullanıcı adı DOLU, sadece şifre
//
//  NEDEN AYRI BİLEŞEN? Aynı menü hem harita ekranının üst barında hem yönetim
//  panelinin kenar çubuğunda duruyor. İki kopya olsaydı biri güncellenip
//  diğeri unutulduğunda iki ekran farklı davranırdı.
// ============================================================================

export default function HesapSecici() {
  const navigate = useNavigate()
  const [acik, setAcik] = useState(false)

  /**
   * Geçişten sonra sayfa yeniden yükleniyor: harita, yetkiler, çalışma alanı
   * ve kayıtlar tamamen yeni kullanıcıya ait. Tek tek tazelemek yerine temiz
   * bir başlangıç hem daha kısa hem daha güvenli — eski kullanıcının verisi
   * ekranda kalamıyor. Hedef her zaman /map: yeni hesabın yönetim yetkisi
   * olmayabilir, panelde açılıp "yetkiniz yok" ekranına düşmesin.
   */
  const hesapDegistir = (username) => {
    setAcik(false)

    if (hesabaGec(username)) {
      window.location.assign('/map')
      return
    }

    // Oturumu dolmuş hesaba geçiliyor: şifre gerekiyor. Buradaki fark önemli —
    // clearSession() DEĞİL aktifOturumuBirak() çağrılıyor, yoksa o an açık
    // olan hesabın oturumu da silinir ve geri dönerken o da şifre isterdi.
    aktifOturumuBirak()
    navigate('/login', { replace: true, state: { username } })
  }

  return (
    <span className="hesap-secici">
      <button
        type="button"
        className="badge user"
        onClick={() => setAcik((a) => !a)}
        aria-expanded={acik}
        aria-haspopup="menu"
        title="Hesap değiştir"
      >
        <KullaniciIkonu /> {getUsername()}
        <span className="hesap-ok" aria-hidden="true">▾</span>
      </button>

      {acik && (
        <>
          {/* Dışarı tıklayınca kapanması için saydam bir perde:
              her yere ayrı dinleyici takmaktan basit ve güvenilir. */}
          <span className="hesap-perde" onClick={() => setAcik(false)} />

          <div className="hesap-menu" role="menu">
            <p className="hesap-baslik">Hesaplar</p>

            {kayitliHesaplar().map((h) => (
              <span key={h.username} className={`hesap-satir${h.aktif ? ' aktif' : ''}`}>
                <button
                  type="button"
                  role="menuitem"
                  onClick={() => (h.aktif ? setAcik(false) : hesapDegistir(h.username))}
                >
                  <KullaniciIkonu size={13} />
                  <span className="hesap-ad">{h.username}</span>
                  {h.aktif
                    ? <small>şu an</small>
                    : <small>{h.oturumVar ? 'oturum açık' : 'şifre ister'}</small>}
                </button>

                {!h.aktif && (
                  <button
                    type="button"
                    className="hesap-unut"
                    title="Bu hesabı listeden kaldır"
                    onClick={() => { hesabiUnut(h.username); setAcik(false) }}
                  >
                    ×
                  </button>
                )}
              </span>
            ))}

            <button
              type="button"
              role="menuitem"
              className="hesap-ekle"
              onClick={() => { aktifOturumuBirak(); navigate('/login', { replace: true }) }}
            >
              Başka hesapla giriş yap
            </button>
          </div>
        </>
      )}
    </span>
  )
}
