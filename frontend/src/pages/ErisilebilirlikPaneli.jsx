import { useState } from 'react'

import { UYGUNLUK_GRADYANI } from '../isiIzgarasi'

// ============================================================================
//  TOPLU TAŞIMA ERİŞİLEBİLİRLİK ANALİZİ — panel
//
//  Konum Analizi'nin (KonumAnaliziPaneli.jsx) küçük kardeşi: aynı görsel
//  dili (ısı haritası ızgarası) kullanıyor ama TEK bir soruya bakıyor,
//  kriter ağırlıklandırma yok. Bu yüzden hedef bölge seçimi de BİLEREK
//  DAHA SADE tutuldu: yalnızca il listesinden seçim. Konum Analizi'ndeki
//  "haritaya poligon çiz" seçeneği burada yok — çizim aracının kendi
//  state'ini (aktif araç, taslak geometri) bu panele de bağlamak, tek
//  kriterlik bir analiz için orantısız bir karmaşıklık olurdu. İl seçimi
//  zaten "bu şehirde toplu taşıma ne kadar erişilebilir?" sorusunun
//  doğal birimi.
// ============================================================================

/**
 * Yüzdeyi ÖZET CÜMLEYE uygun metne çevirir.
 *
 * Sunucu iki ondalık gönderiyor (ErisilebilirlikSonucuDto). Üç durum:
 *   0        → "%0’ı"           hiç erişim yok
 *   < 0,1    → "%0,1’inden azı" var ama çok az — "0" yazmak haritadaki
 *                               sarı adacıklarla çelişirdi
 *   diğer    → "%3,4’ü"         Türkçe ondalık virgülüyle
 *
 * Ek ("’ı / ’ü / ’i") Türkçe ünlü uyumuna göre seçilmiyor — sayıdan sonra
 * gelen ek okunuşa bağlı ve genel kural yazmak bu panelin işi değil;
 * "…’i" biçimi tüm yüzdelerde anlaşılır kalıyor.
 */
export function yuzdeMetni(yuzde) {
  const y = Number(yuzde) || 0
  if (y === 0) return '%0’ı'
  if (y < 0.1) return '%0,1’inden azı'
  return `%${y.toLocaleString('tr-TR', { maximumFractionDigits: 1 })}’i`
}

export default function ErisilebilirlikPaneli({
  iller,
  illerHatasi,
  sonuc,
  yukleniyor,
  hata,
  onCalistir,
  onTemizle,
}) {
  const [secilenPlakalar, setSecilenPlakalar] = useState([])
  const [yalnizcaAktif, setYalnizcaAktif] = useState(true)
  const [ilArama, setIlArama] = useState('')

  const gorunenIller = ilArama.trim()
    ? iller.filter((i) => i.ad.toLocaleLowerCase('tr').includes(ilArama.toLocaleLowerCase('tr')))
    : iller

  const ilSec = (plaka) => {
    setSecilenPlakalar((onceki) =>
      onceki.includes(plaka) ? onceki.filter((p) => p !== plaka) : [...onceki, plaka])
  }

  const calistir = () => {
    if (secilenPlakalar.length === 0) return
    onCalistir({ ilPlakalari: secilenPlakalar, yalnizcaAktif })
  }

  return (
    <section className="panel-section erisilebilirlik-paneli">
      <h2>Toplu Taşıma Erişilebilirliği</h2>

      <p className="tool-hint muted">
        Seçilen şehrin her noktası için en yakın durağa uzaklık hesaplanır.
        Sarı: durağa yakın (iyi erişim) · mor: uzak (zayıf erişim).
      </p>

      <label className="yoklama-alan">
        <span>Şehir ara</span>
        <input
          type="text"
          value={ilArama}
          onChange={(e) => setIlArama(e.target.value)}
          placeholder="İl adı yazın"
        />
      </label>

      {illerHatasi && <p className="analiz-hata">{illerHatasi}</p>}

      <div className="erisilebilirlik-il-listesi">
        {gorunenIller.map((il) => (
          <label key={il.plaka} className="erisilebilirlik-il-satiri">
            <input
              type="checkbox"
              checked={secilenPlakalar.includes(il.plaka)}
              onChange={() => ilSec(il.plaka)}
            />
            <span>{il.ad}</span>
          </label>
        ))}
      </div>

      <label className="yoklama-alan-misafir" style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
        <input
          type="checkbox"
          checked={yalnizcaAktif}
          onChange={(e) => setYalnizcaAktif(e.target.checked)}
        />
        <span>Yalnızca aktif güzergahların durakları</span>
      </label>

      <div className="admin-eylemler" style={{ marginTop: 10 }}>
        <button
          type="button"
          className="btn-primary genis"
          onClick={calistir}
          disabled={yukleniyor || secilenPlakalar.length === 0}
        >
          {yukleniyor ? 'Hesaplanıyor…' : 'Analiz Et'}
        </button>

        {sonuc && (
          <button type="button" className="btn-ghost" onClick={onTemizle}>
            Temizle
          </button>
        )}
      </div>

      {hata && <p className="analiz-hata">{hata}</p>}

      {sonuc && (
        <div className="erisilebilirlik-sonuc">
          <p>
            <strong>{sonuc.alanAdi}</strong> · {sonuc.durakSayisi} durak
          </p>

          {sonuc.durakSayisi === 0 ? (
            <p className="analiz-engel">
              Bu bölgede kayıtlı durak yok — erişilebilirlik hesaplanamadı.
            </p>
          ) : (
            <>
              {/* ÖZET CÜMLE — sayının kendisi kadar önemli olan, ne anlama
                  geldiği. "%62" tek başına referanssız bir sayı. */}
              <p className="yoklama-ozet">
                Alanın <strong>{yuzdeMetni(sonuc.iyiErisimYuzdesi)}</strong> bir durağa
                400 metreden (yürüyerek ~5 dk) daha yakın.
              </p>

              {/* SUNUCUNUN NOTLARI — "hücreler kaba", "oran ilin tamamı
                  üzerinden", "yalnızca 15 durak kayıtlı". Canlıda iki il
                  seçilince "%0" çıktı ve kullanıcı analizi bozuk sandı;
                  sayı doğruydu ama bağlamı yoktu. Bunlar o bağlam. */}
              {sonuc.uyarilar?.length > 0 && (
                <ul className="erisilebilirlik-notlar">
                  {sonuc.uyarilar.map((u) => <li key={u}>{u}</li>)}
                </ul>
              )}

              <div className="isi-lejant" aria-hidden="true">
                <div className="isi-lejant-cubuk" style={{ background: UYGUNLUK_GRADYANI }} />
                <div className="isi-lejant-etiket">
                  <span>uzak</span>
                  <span>yakın</span>
                </div>
              </div>

              {sonuc.izgara?.hucreMetre > 0 && (
                <p className="muted erisilebilirlik-cozunurluk">
                  Çözünürlük: hücre ≈ {Math.round(sonuc.izgara.hucreMetre)} m
                </p>
              )}
            </>
          )}
        </div>
      )}
    </section>
  )
}
