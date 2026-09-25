import { useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import { saveSession, girisAnimasyonunuSifirla } from '../auth'

// Girişten sonra haritaya geçmeden önceki çıkış animasyonunun süresi.
// CSS'teki .login-wrapper.cikis geçişleriyle aynı tutulmalı.
const CIKIS_SURESI = 850

/**
 * Sunucudan gelen hata gövdesini okur. Backend hataları { message } biçiminde
 * dönüyor; gövde JSON değilse duruma göre genel bir cümleye düşüyoruz.
 */
async function hataMesaji(res, varsayilan) {
  try {
    const govde = await res.json()
    if (govde?.message) return govde.message
    if (govde?.errors) return Object.values(govde.errors).flat().join(' ')
  } catch {
    /* JSON değil */
  }
  return varsayilan
}

export default function Login() {
  const location = useLocation()

  // Ödev 10: aynı kart iki iş yapıyor — giriş ve kayıt.
  // Ayrı sayfa yapmadık: kullanıcı "kayıt olayım mı, giriş mi yapayım" diye
  // düşünürken sayfa değiştirmek zorunda kalmasın, iki adım da bir tık uzakta.
  const [mod, setMod] = useState('giris')

  // Ödev 11: hesap değiştiriciden gelindiyse kullanıcı adı DOLU başlıyor,
  // yalnızca şifre isteniyor. Şifre saklanmıyor (bkz. auth.js).
  const [username, setUsername] = useState(() => location.state?.username ?? '')
  const [password, setPassword] = useState('')
  const [davetKodu, setDavetKodu] = useState('')
  const [error, setError] = useState(null)
  const [bilgi, setBilgi] = useState(null)
  const [loading, setLoading] = useState(false)
  // Giriş başarılı: kart soluyor, gezegen yaklaşıyor, ekran kararıp haritaya devrediyor
  const [cikis, setCikis] = useState(false)

  /**
   * "Beni hatırla" — oturumun hangi depoda tutulacağını belirliyor.
   *
   *   işaretli   → localStorage   : tarayıcı kapansa da oturum sürer
   *   işaretsiz  → sessionStorage : SEKME KAPANINCA oturum biter
   *
   * VARSAYILAN İŞARETSİZ. Ortak bir bilgisayarda oturumu açık bırakmak,
   * kullanıcının istemediği hâlde başına gelebilecek bir şey olmamalı;
   * kalıcılık bilinçli bir seçim olsun. (Bankacılık uygulamalarının
   * varsayılanı da budur.)
   */
  const [beniHatirla, setBeniHatirla] = useState(false)

  /**
   * İKİNCİ ADIM — şifre doğrulandı, 6 haneli kod bekleniyor.
   *
   * Ayrı bir SAYFA değil, aynı kartın ikinci hâli. Sayfa değiştirseydik
   * "beni hatırla" tercihi ve kullanıcı adı taşınmak zorunda kalırdı;
   * üstelik kullanıcı için tek bir işin (giriş yapmak) ortasında adres
   * değişmesi gereksiz bir kopukluk.
   *
   * null → birinci adımdayız. Dolu → { araToken, username }
   */
  const [ikinciAdim, setIkinciAdim] = useState(null)
  const [kod, setKod] = useState('')
  const navigate = useNavigate()

  // Token süresi dolup login'e yönlendirildiysek bilgi mesajı göster
  const sessionExpired = location.state?.expired

  // Hesap değiştiriciden gelindiyse odak doğrudan şifre alanına gitsin.
  const hesapDegistirme = !!location.state?.username

  const modDegistir = (yeni) => {
    if (yeni === mod) return
    setMod(yeni)
    setError(null)
    setBilgi(null)
    setPassword('')   // şifre alanı modlar arasında taşınmasın
  }

  const girisYap = async () => {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    })

    if (res.status === 401) {
      setError('Kullanıcı adı veya şifre hatalı.')
      return
    }

    // Ödev 10: şifre doğru ama hesap yönetici onayı bekliyor.
    // Sunucu bu durumu 403 ile ayırıyor; mesajı olduğu gibi gösteriyoruz.
    if (res.status === 403) {
      setError(await hataMesaji(res, 'Hesabınız henüz onaylanmadı.'))
      return
    }

    if (res.status === 429) {
      setError(await hataMesaji(res, 'Çok fazla deneme yaptınız, biraz bekleyin.'))
      return
    }

    if (!res.ok) throw new Error(await sunucuHatasi(res))

    const cevap = await res.json()

    // İKİ ADIMLI DOĞRULAMA: şifre doğru ama oturum HENÜZ AÇILMADI.
    // Sunucu token yerine kısa ömürlü bir ara token gönderdi.
    if (cevap.ikinciAdimGerekli) {
      setIkinciAdim({ araToken: cevap.araToken, username: cevap.username })
      setPassword('')   // şifre ekranda gereksiz yere durmasın
      setKod('')
      return
    }

    oturumuAc(cevap)
  }

  /**
   * Girişin İKİNCİ adımı: ara token + kod → gerçek oturum.
   */
  const kodDogrula = async () => {
    const res = await fetch('/api/auth/login/2fa', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ araToken: ikinciAdim.araToken, kod }),
    })

    if (res.status === 401) {
      // Ara token'ın ömrü 5 dakika. Süresi dolduysa kod doğru olsa bile
      // reddedilir ve kullanıcı baştan başlamalı — mesaj bunu söylüyor,
      // yoksa "kodu doğru yazdım ama olmuyor" diye döner durur.
      setError('Kod geçersiz ya da süresi doldu. Kod 30 saniyede bir yenileniyor;'
             + ' uzun sürdüyse baştan giriş yapın.')
      setKod('')
      return
    }

    if (res.status === 429) {
      setError(await hataMesaji(res, 'Çok fazla deneme yaptınız, biraz bekleyin.'))
      return
    }

    if (!res.ok) throw new Error(await sunucuHatasi(res))

    oturumuAc(await res.json())
  }

  /** Oturumu saklayıp haritaya geçen ortak son adım (iki yol da buraya çıkıyor). */
  const oturumuAc = (oturum) => {
    saveSession(oturum, beniHatirla)

    // Haritadaki açılış sahnesi MUTLAKA oynasın: login'deki uzay teması
    // "dünyadan Türkiye'ye iniş" ile kesintisiz devam etsin.
    girisAnimasyonunuSifirla()

    // Önce çıkış animasyonu, sonra sayfa geçişi. Ekran karardığı an harita
    // ekranı da uzay sahnesiyle açıldığı için arada görsel kopukluk olmuyor.
    setCikis(true)

    // Giriş öncesi gitmek istediği adres varsa oraya (paylaşılan tur
    // bağlantısı), yoksa haritaya. RequireAuth hedefi state ile taşıyor.
    const hedef = location.state?.hedef ?? '/map'

    setTimeout(() => navigate(hedef, { replace: true }), CIKIS_SURESI)
  }

  /**
   * Beklenmeyen bir cevabın mesajı.
   *
   * NEDEN ÖZEL BİR DURUM VAR?
   * Geliştirmede en sık karşılaşılan hata "backend henüz ayağa kalkmadı"dır:
   * Vite'ın vekili arkadaki sunucuya bağlanamayınca KENDİ ürettiği bir 500
   * döndürüyor ve gövdesi JSON değil. Ekranda "Sunucu hatası: 500" yazıyordu —
   * teknik olarak doğru ama yanıltıcı: sunucu hata vermiyor, sunucu HENÜZ YOK.
   * Kullanıcı da haklı olarak kodda hata arıyordu.
   *
   * Ayrımı GÖVDEDEN yapıyoruz: uygulamamızın ürettiği her hata cevabı
   * { message } taşıyan bir JSON'dur. JSON gelmiyorsa cevap bizden çıkmamıştır.
   */
  const sunucuHatasi = async (res) => {
    try {
      const govde = await res.json()
      if (govde?.message) return govde.message
    } catch {
      /* JSON değil — aşağıda ele alınıyor */
    }

    // 502/503/504 zaten "ara sunucu arkadakine ulaşamadı" demek; gövdesi
    // JSON olmayan 500'ü de aynı kümeye alıyoruz.
    if ([500, 502, 503, 504].includes(res.status)) {
      return 'Sunucuya ulaşılamıyor. Backend penceresi hazır olana kadar bekleyip '
        + 'tekrar deneyin (ilk açılış birkaç saniye sürebilir).'
    }

    return `Sunucu beklenmeyen bir cevap verdi (${res.status}).`
  }

  const kayitOl = async () => {
    const res = await fetch('/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      // Davet kodu İSTEĞE BAĞLI: boşsa hiç göndermiyoruz — sunucu
      // tarafında "" ile null ayrımı yapmak yerine alanı yokmuş gibi
      // bırakmak, eski (kodsuz) akışı hiç bozmuyor.
      body: JSON.stringify({
        username,
        password,
        ...(davetKodu.trim() ? { inviteCode: davetKodu.trim().toUpperCase() } : {}),
      }),
    })

    if (!res.ok) {
      // 500/502/503/504 + JSON olmayan gövde = backend ayakta değil;
      // "Kayıt yapılamadı" demek sebebi gizlerdi.
      setError([500, 502, 503, 504].includes(res.status)
        ? await sunucuHatasi(res)
        : await hataMesaji(res, 'Kayıt yapılamadı.'))
      return
    }

    const data = await res.json()

    // Kayıttan sonra otomatik giriş YOK — hesap zaten onay bekliyor.
    // Kullanıcıyı giriş sekmesine alıp ne olduğunu tek cümleyle söylüyoruz.
    setMod('giris')
    setPassword('')
    setBilgi(data.message)
  }

  const handleSubmit = async (e) => {
    e.preventDefault()
    setLoading(true)
    setError(null)
    setBilgi(null)
    try {
      if (ikinciAdim) await kodDogrula()
      else if (mod === 'giris') await girisYap()
      else await kayitOl()
    } catch (err) {
      setError(`İşlem tamamlanamadı: ${err.message}`)
    } finally {
      setLoading(false)
    }
  }

  const kayitModu = mod === 'kayit'

  return (
    <div className={`login-wrapper${cikis ? ' cikis' : ''}`}>
      {/* Yıldız alanı: üç katman, farklı boyut ve hızda. Tek katman düz bir
          desen gibi görünürdü; farklı hızlar derinlik (paralaks) hissi veriyor.
          aria-hidden — ekran okuyucuya anlatılacak bir bilgi taşımıyor. */}
      <div className="yildiz-alani" aria-hidden="true">
        <div className="yildiz-katman uzak" />
        <div className="yildiz-katman orta" />
        <div className="yildiz-katman yakin" />
        {/* Ara ara geçen kayan yıldız — hareket sürekli olmasın diye
            uzun aralıklı bir animasyon. */}
        <div className="kayan-yildiz" />
        <div className="kayan-yildiz ikinci" />
      </div>

      {/* Ekranın altından yükselen gezegen kavsi: dev bir dairenin yalnızca
          üst kenarı görünüyor, üstündeki ince parlak çizgi atmosfer. */}
      <div className="login-gezegen" aria-hidden="true" />

      <form className="login-card" onSubmit={handleSubmit}>
        <div className="login-logo" aria-hidden="true">
          <svg viewBox="0 0 48 48" width="46" height="46" fill="none"
               stroke="currentColor" strokeWidth="1.5" strokeLinecap="round">
            <circle cx="24" cy="24" r="19" />
            <path d="M5 24h38" />
            <path d="M24 5a30 30 0 0 1 0 38 30 30 0 0 1 0-38Z" />
            <path d="M9 14c6 2.6 10 3.8 15 3.8S33 16.6 39 14M9 34c6-2.6 10-3.8 15-3.8S33 31.4 39 34"
                  opacity="0.5" />
          </svg>
        </div>

        <h1>Harita Uygulaması</h1>
        <p className="login-subtitle">
          {ikinciAdim
            ? 'Doğrulama kodunu girin'
            : kayitModu ? 'Yeni hesap oluşturun' : 'Devam etmek için giriş yapın'}
        </p>

        {/* Ödev 10: giriş / kayıt seçimi.
            İKİNCİ ADIMDA GİZLİ: kullanıcı bir işin ortasında; "Kayıt Ol"a
            basmak yarım kalmış girişi sessizce çöpe atardı. */}
        <div
          className="login-sekme"
          role="tablist"
          aria-label="Giriş veya kayıt"
          hidden={Boolean(ikinciAdim)}
        >
          <button
            type="button"
            role="tab"
            aria-selected={!kayitModu}
            className={!kayitModu ? 'active' : ''}
            onClick={() => modDegistir('giris')}
          >
            Giriş Yap
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={kayitModu}
            className={kayitModu ? 'active' : ''}
            onClick={() => modDegistir('kayit')}
          >
            Kayıt Ol
          </button>
        </div>

        {sessionExpired && !bilgi && (
          <p className="info-banner">Oturum süreniz doldu, lütfen tekrar giriş yapın.</p>
        )}

        {hesapDegistirme && !bilgi && !sessionExpired && (
          <p className="info-banner">
            <strong>{location.state.username}</strong> hesabının oturumu kapanmış.
            Devam etmek için şifrenizi girin.
          </p>
        )}

        {bilgi && <p className="info-banner">{bilgi}</p>}

        {/* ---------- İKİNCİ ADIM ---------- */}
        {ikinciAdim && (
          <>
            <p className="info-banner">
              <strong>{ikinciAdim.username}</strong> hesabı iki adımlı
              doğrulama kullanıyor. Telefonunuzdaki uygulamanın gösterdiği
              6 haneli kodu girin.
            </p>

            <label htmlFor="kod">Doğrulama kodu</label>
            <input
              id="kod"
              value={kod}
              onChange={(e) => setKod(e.target.value)}
              placeholder="000000"
              inputMode="numeric"
              /* one-time-code: iOS ve Android bu ipucuyla kodu klavyenin
                 üstünde önerebiliyor. */
              autoComplete="one-time-code"
              maxLength={7}
              required
              // eslint-disable-next-line jsx-a11y/no-autofocus -- kullanıcı
              // bu ekrana yalnızca kod girmek için geliyor; tek alan var.
              autoFocus
            />

            <button type="submit" disabled={loading || cikis}>
              {cikis ? 'Harita hazırlanıyor…' : loading ? 'Doğrulanıyor…' : 'Doğrula'}
            </button>

            {error && <p className="error-banner">{error}</p>}

            <button
              type="button"
              className="login-vazgec"
              onClick={() => { setIkinciAdim(null); setKod(''); setError(null) }}
            >
              Baştan giriş yap
            </button>
          </>
        )}

        {/* ---------- BİRİNCİ ADIM ---------- */}
        {!ikinciAdim && (
        <>
        <label htmlFor="username">Kullanıcı Adı</label>
        <input
          id="username"
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          placeholder={kayitModu ? 'En az 3 karakter' : 'Kullanıcı adınız'}
          autoComplete="username"
          minLength={kayitModu ? 3 : undefined}
          required
        />

        <label htmlFor="password">Şifre</label>
        <input
          id="password"
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          placeholder={kayitModu ? 'En az 6 karakter' : '••••••••'}
          // eslint-disable-next-line jsx-a11y/no-autofocus -- kullanıcı adı
          // zaten dolu geldiyse tek eksik şifre; odağı elle taşımak gereksiz adım.
          autoFocus={hesapDegistirme}
          // Tarayıcının şifre yöneticisine "bu yeni bir şifre" demek:
          // kayıt sırasında kayıtlı şifreyi doldurmaya çalışmasın.
          autoComplete={kayitModu ? 'new-password' : 'current-password'}
          minLength={kayitModu ? 6 : undefined}
          required
        />

        {/* DAVET KODU — yalnızca kayıt modunda ve İSTEĞE BAĞLI.
            Bir admin bu kodu paylaştıysa hesap otomatik ona bağlanır ve
            onay beklemeden aktif olur; boş bırakılırsa eski akış (yönetici
            onayı bekleyen kayıt) aynen sürer. */}
        {kayitModu && (
          <>
            {/* Etiket "(isteğe bağlı)" — uygulamanın geri kalanıyla aynı
                söz ("Bölge (isteğe bağlı)", "Telefon (isteğe bağlı)").
                Önce "(varsa)" yazıyordu; aynı şeyi iki türlü söylemek
                kullanıcıya iki farklı kural varmış gibi geliyor. */}
            <label htmlFor="davet-kodu">Davet kodu (isteğe bağlı)</label>
            {/* PLACEHOLDER BİR CÜMLE DEĞİL, ÖRNEK KOD.
                Alan girileni büyük harfe çevirip harf aralığını açıyor
                (aşağıdaki style) — bu biçim bir KOD için doğru ama bir
                cümleyi okunmaz hâle getiriyordu: "Yöneticinizden aldıysanız
                girin" ekranda "YÖNETİCİNİZDEN ALDIYSANIZ G" diye ortadan
                kesiliyordu. Örnek kod hem sığıyor hem de beklenen biçimi
                (8 karakter) gösteriyor. Karakterler sunucunun alfabesinden:
                karıştırılan harfler (O/0, I/1) o alfabede yok. */}
            <input
              id="davet-kodu"
              value={davetKodu}
              onChange={(e) => setDavetKodu(e.target.value.toUpperCase())}
              placeholder="K7QF2MDA"
              maxLength={24}
              autoComplete="off"
              style={{ letterSpacing: '2px', textTransform: 'uppercase' }}
            />
            {/* "Nereden alınır" bilgisi placeholder'dan BURAYA taşındı:
                burada metin sarabiliyor, kırpılmıyor. */}
            <p className="login-hint" style={{ marginTop: 0 }}>
              Yöneticiniz size bir kod verdiyse girin: hesabınız onun ekibine
              bağlanır ve onay beklemeden hemen açılır.
            </p>
          </>
        )}

        {/* "Beni hatırla" YALNIZCA giriş modunda. Kayıt olurken oturum
            açılmıyor (hesap yönetici onayı bekliyor), dolayısıyla
            hatırlanacak bir oturum da yok. */}
        {!kayitModu && (
          <label className="login-hatirla">
            <input
              type="checkbox"
              checked={beniHatirla}
              onChange={(e) => setBeniHatirla(e.target.checked)}
            />
            <span>Beni hatırla</span>
            <small>
              İşaretlemezseniz oturum, sekmeyi kapattığınızda sona erer.
            </small>
          </label>
        )}

        <button type="submit" disabled={loading || cikis}>
          {cikis ? 'Harita hazırlanıyor…'
            : loading ? (kayitModu ? 'Kaydediliyor…' : 'Giriş yapılıyor…')
              : (kayitModu ? 'Kayıt Ol' : 'Giriş Yap')}
        </button>

        {error && <p className="error-banner">{error}</p>}

        {/* Giriş modunda ipucu YOK.
            Önceden burada "Demo kullanıcı: admin / staj123" yazıyordu; bir
            giriş ekranının üstünde geçerli bir kullanıcı adı ve şifre
            göstermek, kimlik doğrulamanın kendisini anlamsızlaştırıyor.
            Kayıt modundaki cümle kalıyor: o bir uyarı, bir kimlik bilgisi
            değil. */}
        {/* MESAJ GİRİLEN KODA GÖRE DEĞİŞİYOR.
            Sabit "yönetici onayından sonra açılır" cümlesi, hemen üstteki
            davet kodu ipucuyla ("onay beklemeden hemen açılır") açıkça
            çelişiyordu: kod yazmış bir kullanıcı aynı ekranda birbirini
            yalanlayan iki cümle okuyordu. */}
        {kayitModu && (
          <p className="login-hint">
            {davetKodu.trim()
              ? 'Kod geçerliyse hesabınız onay beklemeden açılır.'
              : 'Kaydınız yönetici onayından sonra kullanıma açılır.'}
          </p>
        )}

        {/* MİSAFİR GİRİŞİ.
            Turu izlemek için hesap gerekmiyor; katılım kodu yeterli. Bu
            bağlantı olmasaydı, elinde yalnızca kod olan biri giriş ekranına
            takılıp kalırdı — kullanıcının bildirdiği durum tam olarak
            buydu. */}
        {!kayitModu && (
          <div className="login-misafir">
            <span>Bir tura katılım kodunuz mu var?</span>
            <a href="/tur">Hesapsız olarak görüntüleyin</a>
          </div>
        )}
        </>
        )}
      </form>

      {/* Çıkışta ekranı karartıp haritaya devreden perde */}
      <div className="login-karartma" aria-hidden="true" />
    </div>
  )
}
