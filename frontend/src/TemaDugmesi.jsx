import { useEffect, useState } from 'react'
import { TEMALAR, etkinTema, sistemTemasiniDinle, temaTercihi, temayiSec } from './tema'
import { AyIkonu, GunesIkonu, SistemIkonu } from './icons'

// ============================================================================
//  Tema değiştirme düğmesi — üst barda, hem haritada hem yönetim panelinde.
//
//  ÜÇ DURUMLU BİR DÖNGÜ: sistem → aydınlık → karanlık → sistem …
//
//  Neden üç durumlu bir düğme, iki durumlu bir anahtar değil?
//  Anahtar yapsaydık "sistemi takip et" seçeneği kaybolurdu; kullanıcı bir kez
//  dokunduktan sonra makinesi akşam karanlığa geçtiğinde uygulama takip
//  edemezdi. Ayrı bir açılır menü de bu kadar küçük bir tercih için fazla
//  geldi — üç adımlık döngü tek tıklamayla dönüyor ve düğmenin üstündeki
//  başlık hangi durumda olduğunu yazıyor.
// ============================================================================

const SIRA = [TEMALAR.sistem, TEMALAR.aydinlik, TEMALAR.karanlik]

const ETIKET = {
  [TEMALAR.sistem]: 'Sistem',
  [TEMALAR.aydinlik]: 'Aydınlık',
  [TEMALAR.karanlik]: 'Karanlık',
}

const IKON = {
  [TEMALAR.sistem]: SistemIkonu,
  [TEMALAR.aydinlik]: GunesIkonu,
  [TEMALAR.karanlik]: AyIkonu,
}

export default function TemaDugmesi() {
  const [tercih, setTercih] = useState(temaTercihi)

  // 'sistem' seçiliyken işletim sisteminin gece moduna geçişini takip et.
  // Dinleyici temayı zaten uyguluyor; buradaki state yalnızca düğmenin
  // ikonunu tazelemek için (sistem → aydınlık/karanlık ikonu değişiyor).
  const [, tazele] = useState(0)
  useEffect(() => sistemTemasiniDinle(() => tazele((n) => n + 1)), [])

  const sonraki = SIRA[(SIRA.indexOf(tercih) + 1) % SIRA.length]
  const Ikon = IKON[tercih === TEMALAR.sistem ? etkinTema(tercih) : tercih]

  return (
    <button
      type="button"
      className="tema-dugmesi"
      onClick={() => setTercih(temayiSec(sonraki) && sonraki)}
      // Başlık HEM şu anki durumu HEM sıradakini söylüyor: üç durumlu bir
      // döngüde "bir daha basarsam ne olur?" sorusunun cevabı görünmüyorsa
      // kullanıcı deneme yanılmaya mecbur kalır.
      title={`Tema: ${ETIKET[tercih]}${tercih === TEMALAR.sistem ? ` (${ETIKET[etkinTema(tercih)]})` : ''}`
             + ` — tıklayınca: ${ETIKET[sonraki]}`}
      aria-label={`Tema: ${ETIKET[tercih]}. Değiştirmek için tıklayın.`}
    >
      <Ikon size={14} />
      <span className="tema-etiket">{ETIKET[tercih]}</span>
    </button>
  )
}
