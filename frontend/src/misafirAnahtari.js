// ============================================================================
//  MİSAFİR ANAHTARI — kimliksiz ama TEKİL
//
//  Yoklamada "15 kişinin 13'ü" diyebilmek için aynı kişinin iki kez
//  sayılmaması gerekiyor. Misafirin hesabı yok, çerez de yok; tarayıcıda
//  üretilen rastgele bir anahtar bu boşluğu dolduruyor.
//
//  ---- BU BİR KİMLİK DEĞİL ----
//  Kim olduğunu söylemiyor, hiçbir kişisel veriye bağlı değil ve sunucuda
//  yalnızca bellekteki yoklama sayacında yaşıyor (IYoklamaServisi). Tur
//  bitince yoklama da silindiği için anahtarın hiçbir izi kalmıyor.
//
//  ---- NEDEN localStorage? ----
//  Misafir sayfayı yenilediğinde ya da yoklama sırasında sekmeyi
//  kapatıp açtığında aynı kişi sayılmalı. Bellekte tutsaydık her yenileme
//  yeni bir kişi üretir ve sayılar şişerdi.
//
//  Depolama erişilemezse (gizli sekme, site verisi kapalı) oturum boyunca
//  yaşayan bir anahtara düşülüyor: sayılar biraz şişebilir ama özellik
//  tamamen çalışmaz hâle gelmiyor.
// ============================================================================

const ANAHTAR = 'staj_misafir_anahtari'

let bellektekiAnahtar = null

/** Rastgele, tahmin edilmesi gerekmeyen bir anahtar üretir. */
function uret() {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) {
    return `m-${crypto.randomUUID()}`
  }

  // randomUUID olmayan tarayıcılarda: zaman + rastgele. Kriptografik
  // olması gerekmiyor, yalnızca çakışmaması yeterli.
  return `m-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
}

/** Bu tarayıcının misafir anahtarı; yoksa üretip saklar. */
export function misafirAnahtari() {
  if (bellektekiAnahtar) return bellektekiAnahtar

  try {
    const kayitli = localStorage.getItem(ANAHTAR)

    if (kayitli && kayitli.length >= 8) {
      bellektekiAnahtar = kayitli
      return kayitli
    }

    bellektekiAnahtar = uret()
    localStorage.setItem(ANAHTAR, bellektekiAnahtar)
    return bellektekiAnahtar
  } catch {
    // Depolama kapalı: oturum boyunca yaşayan anahtara düş.
    bellektekiAnahtar = bellektekiAnahtar || uret()
    return bellektekiAnahtar
  }
}
