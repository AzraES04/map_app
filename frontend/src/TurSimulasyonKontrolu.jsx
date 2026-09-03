// ============================================================================
//  TUR SİMÜLASYONU — oynat/durdur düğmesi ve durum satırı
//
//  ---- NEDEN AYRI BİLEŞEN? ----
//  Aynı kontrol İKİ YERDE gerekiyor: tur önerisi panelinde (rehber
//  paylaşmadan önce rotayı görsün) ve canlı tur ekranında (katılımcı da
//  oynatabilsin). Kopyalasaydık düğmenin metni ya da davranışı bir yerde
//  değişip diğerinde kalırdı.
//
//  ---- KATILIMCIDA DA VAR, BİLİNÇLİ ----
//  Turu İLERLETMEK yalnızca rehberin işi — o gerçek bir durum değişikliği
//  ve gruptaki herkesi etkiliyor. Simülasyon ise yalnızca bir GÖSTERİM:
//  izleyicinin kendi ekranında oynuyor, kimsenin ekranını değiştirmiyor.
//  Bu yüzden yetkiye bağlamıyoruz (gerekçenin uzunu turSimulasyonu.js'te).
// ============================================================================

/**
 * @param {object} props
 * @param {ReturnType<import('./useTurSimulasyonu').useTurSimulasyonu>} props.simulasyon
 * @param {string} [props.ipucu] Düğmenin üstünde duran açıklama.
 */
export default function TurSimulasyonKontrolu({ simulasyon, ipucu }) {
  // Rotası olmayan turda oynatacak bir şey yok: düğmeyi kapalı göstermek
  // yerine hiç göstermiyoruz. Kapalı bir düğme "neden çalışmıyor?" diye
  // sorulacak bir şey; olmayan düğme sorulmuyor.
  if (!simulasyon?.oynatilabilir) return null

  const { oynuyor, durum, oynat, durdur, canli } = simulasyon

  return (
    <div className="tur-simulasyon">
      {ipucu && <p className="tool-hint muted">{ipucu}</p>}

      <div className="tur-simulasyon-dugmeler">
        {/* CANLI TURDA metin farklı: orada oynatma "rotayı göster" değil,
            "bulunduğumuz duraktan sıradakine giden yolu canlandır" demek.
            İki durumda aynı metni kullansaydık, canlı turda düğmenin turu
            baştan alacağı sanılırdı. */}
        <button
          type="button"
          className="btn-ghost kucuk"
          onClick={oynat}
          disabled={oynuyor}
          title={canli
            ? 'Bulunduğunuz duraktan sıradakine giden yolu haritada canlandır'
            : 'Rotayı baştan sona haritada oynat'}
        >
          {oynuyor
            ? 'Oynuyor…'
            : (canli ? 'Sıradaki durağa git' : (durum ? 'Baştan oynat' : 'Rotayı oynat'))}
        </button>

        {/* Durdurma yalnızca OYNARKEN anlamlı.
            Canlı turda simge zaten sürekli haritada (grubun durağında) duruyor;
            oynamazken "Durdur" göstermek, o simgeyi kaldıracakmış gibi
            okunurdu — oysa durdurmak simgeyi mevcut durağa geri getiriyor. */}
        {oynuyor && (
          <button type="button" className="btn-ghost kucuk" onClick={durdur}>
            Durdur
          </button>
        )}
      </div>

      {durum && (
        <div className="tur-simulasyon-durum">
          {/* Yüzde çubuğu: haritadaki rozette de aynı sayı var ama kullanıcı
              o an haritaya değil panele bakıyor olabilir. */}
          <div className="tur-simulasyon-cubuk" aria-hidden="true">
            <span style={{ width: `${durum.yuzde}%` }} />
          </div>

          <p className="muted">
            {/* Canlı turda ekranın söylediği şey "grup nerede": bulunulan
                durak önce geliyor, yüzde ikincil bir ayrıntı. */}
            {canli && durum.oncekiDurak && (
              <><strong>{durum.oncekiDurak.name}</strong>{' · '}</>
            )}
            %{durum.yuzde}
            {durum.sonrakiDurak
              ? <> · sıradaki: {durum.sonrakiDurak.name}</>
              : <> · son durak</>}
          </p>
        </div>
      )}
    </div>
  )
}
