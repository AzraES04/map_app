import { useEffect, useRef, useState } from 'react'

import {
  cevapBul, devamSorulari, vurguluParcala, KARSILAMA, ONERILEN_SORULAR,
} from './mapBotBilgisi'

// ============================================================================
//  MAP BOT — uygulamayı anlatan sohbet penceresi
//
//  Sağ alt köşede bir konum işareti; tıklanınca açılıyor. Tur/rota oluşturma,
//  POI ekleme, paylaşım, yoklama gibi konularda soru alıyor.
//
//  ---- NEDEN SAĞ ALTTA VE KÜÇÜK? ----
//  Ekranın geri kalanı haritanın. Bot bir araç değil, bir DANIŞMA noktası:
//  ihtiyaç duyulduğunda açılıp kapanmalı. Panele gömseydik sürekli yer
//  kaplar, kimsenin bakmadığı bir kutu olurdu.
//
//  ---- CEVAPLAR NEREDEN GELİYOR? ----
//  Elle yazılmış bir bilgi tabanından (mapBotBilgisi.js). Bir dil modeline
//  bağlanmama gerekçesi orada; özeti: cevapların doğrulanabilir olması ve
//  botun bilmediğini uydurmaması.
//
//  ---- SADECE ANLATMIYOR, YAPIYOR DA ----
//  Bazı cevapların altında bir EYLEM düğmesi var ("Tur Planla'yı aç").
//  "Soldaki panelden şunu açın" demek, kullanıcıyı botun anlattığı şeyi
//  aramaya gönderiyor; düğme onu doğrudan oraya götürüyor. Eylemi bot
//  kendisi YAPMIYOR — hangi eylemin neye karşılık geldiğini `onEylem`
//  ile MapPage biliyor. Bot uygulamanın state'ine hiç dokunmuyor.
//
//  ---- DEVAM SORULARI ----
//  Sohbetin en zayıf anı cevabın bittiği an. Her cevabın altında ilgili
//  iki-üç soru duruyor; kullanıcı ne soracağını düşünmek zorunda kalmadan
//  konuyu derinleştirebiliyor.
// ============================================================================

/** Cevap gecikmesi (ms) — anında cevap "yazılmış metin" gibi duruyor. */
const DUSUNME_MS = 350

/** Cevaptaki **kalın** işaretlerini gerçek vurguya çevirir. */
function Balon({ metin }) {
  return vurguluParcala(metin).map((parca, i) => (
    parca.kalin ? <strong key={i}>{parca.metin}</strong> : <span key={i}>{parca.metin}</span>
  ))
}

export default function MapBot({ onEylem }) {
  const [acik, setAcik] = useState(false)
  const [metin, setMetin] = useState('')
  const [yaziyor, setYaziyor] = useState(false)
  const [mesajlar, setMesajlar] = useState([{ kim: 'bot', metin: KARSILAMA }])

  const listeRef = useRef(null)
  const girdiRef = useRef(null)

  // Açılıştaki hazır sorular yalnızca sohbetin BAŞINDA duruyor; sonrasında
  // yerlerini cevaba özel devam soruları alıyor.
  const acilisOnerileri = mesajlar.length === 1

  // Devam soruları YALNIZCA SON cevabın altında: her balonun altında
  // dursaydı sohbet düğme yığınına dönerdi.
  const sonMesaj = mesajlar[mesajlar.length - 1]
  const devam = !yaziyor && sonMesaj?.kim === 'bot' && !acilisOnerileri
    ? (sonMesaj.devam ?? [])
    : []

  // Yeni mesaj gelince en alta kaydır: konuşma yukarıda kalırsa kullanıcı
  // kendi sorusunun cevaplanıp cevaplanmadığını göremiyor.
  useEffect(() => {
    if (listeRef.current) {
      listeRef.current.scrollTop = listeRef.current.scrollHeight
    }
  }, [mesajlar, yaziyor])

  // Açılınca imleç girdide: bot bir soru sormak için açılıyor, kullanıcıyı
  // bir de tıklamaya zorlamanın anlamı yok.
  useEffect(() => {
    if (acik) girdiRef.current?.focus()
  }, [acik])

  /** Soruyu akışa yazar ve cevabı ekler. Hem formdan hem öneri düğmesinden. */
  const sor = (soru) => {
    if (soru.length === 0 || yaziyor) return

    setMesajlar((oncekiler) => [...oncekiler, { kim: 'ben', metin: soru }])
    setMetin('')
    setYaziyor(true)

    // Kısa bir gecikme: cevap anında belirdiğinde kullanıcı onu bir mesaj
    // değil, önceden yazılmış bir metin gibi okuyor ve okumadan geçiyor.
    setTimeout(() => {
      const bulunan = cevapBul(soru)

      setMesajlar((oncekiler) => [...oncekiler, {
        kim: 'bot',
        metin: bulunan.cevap,
        // Eylem düğmesi yalnızca onEylem verilmişse anlamlı: bot başka bir
        // ekranda kullanılırsa hiçbir yere gitmeyen bir düğme çıkmasın.
        eylem: onEylem ? bulunan.eylem : null,
        devam: devamSorulari(bulunan.ad),
      }])
      setYaziyor(false)
    }, DUSUNME_MS)
  }

  const gonder = (e) => {
    e.preventDefault()
    sor(metin.trim())
  }

  /** Eylem düğmesi: paneli açar ve botu kapatır — kullanıcı artık orada. */
  const eylemiCalistir = (eylem) => {
    onEylem?.(eylem.ad)
    setAcik(false)
  }

  if (!acik) {
    return (
      <button
        type="button"
        className="mapbot-dugme"
        onClick={() => setAcik(true)}
        aria-label="Map Bot'u aç"
        title="Map Bot — uygulama hakkında soru sorun"
      >
        {/* Konum işareti: uygulamanın konusu harita, bot da onun rehberi. */}
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <path d="M12 2C8.1 2 5 5.1 5 9c0 5.2 7 13 7 13s7-7.8 7-13c0-3.9-3.1-7-7-7z" />
          <circle cx="12" cy="9" r="2.6" className="mapbot-ic" />
        </svg>

        {/* Nabız halkası: haritanın üstünde duran küçük bir düğme kolayca
            gözden kaçıyor. Tek seferlik değil sürekli — ama çok yavaş, göz
            yormasın diye. */}
        <span className="mapbot-nabiz" aria-hidden="true" />
      </button>
    )
  }

  return (
    <section className="mapbot" aria-label="Map Bot sohbeti">
      <header className="mapbot-baslik">
        <span className="mapbot-avatar" aria-hidden="true">
          <svg viewBox="0 0 24 24">
            <path d="M12 2C8.1 2 5 5.1 5 9c0 5.2 7 13 7 13s7-7.8 7-13c0-3.9-3.1-7-7-7z" />
            <circle cx="12" cy="9" r="2.6" className="mapbot-ic" />
          </svg>
        </span>

        <div className="mapbot-kimlik">
          <strong>Map Bot</strong>
          <span className="mapbot-durum">uygulama rehberiniz</span>
        </div>

        <button
          type="button"
          className="mapbot-kapat"
          onClick={() => setAcik(false)}
          aria-label="Map Bot'u kapat"
        >
          ×
        </button>
      </header>

      <div className="mapbot-akis" ref={listeRef}>
        {mesajlar.map((m, i) => (
          <div key={i} className={`mapbot-satir ${m.kim}`}>
            <p className={`mapbot-mesaj ${m.kim}`}>
              {m.kim === 'bot' ? <Balon metin={m.metin} /> : m.metin}
            </p>

            {/* EYLEM: anlatılan yere GÖTÜREN düğme. */}
            {m.eylem && (
              <button
                type="button"
                className="mapbot-eylem"
                onClick={() => eylemiCalistir(m.eylem)}
              >
                {m.eylem.etiket} →
              </button>
            )}
          </div>
        ))}

        {yaziyor && (
          <p className="mapbot-mesaj bot yaziyor" aria-label="Map Bot yazıyor">
            <span /><span /><span />
          </p>
        )}

        {(acilisOnerileri || devam.length > 0) && (
          <div className="mapbot-oneriler">
            {(acilisOnerileri ? ONERILEN_SORULAR : devam).map((soru) => (
              <button key={soru} type="button" onClick={() => sor(soru)}>{soru}</button>
            ))}
          </div>
        )}
      </div>

      <form className="mapbot-girdi" onSubmit={gonder}>
        <input
          ref={girdiRef}
          type="text"
          value={metin}
          onChange={(e) => setMetin(e.target.value)}
          placeholder="Bir şey sorun…"
          aria-label="Map Bot'a sorunuz"
          maxLength={200}
        />
        <button type="submit" className="btn-primary kucuk" disabled={yaziyor}>Sor</button>
      </form>
    </section>
  )
}
