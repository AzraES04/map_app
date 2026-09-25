# ============================================================
#  StajProject — SUNUM ÖNCESİ ÖNBELLEK ISITMA
#
#  NE İŞE YARAR?
#  Tur önerisi mekanları OpenStreetMap'ten çekiyor. Bir şehir+tema
#  kombinasyonu İLK istendiğinde cevap 8–15 saniye sürebiliyor; aynı
#  kombinasyon ikinci kez istendiğinde önbellekten geliyor ve
#  ~0,2 saniyede açılıyor.
#
#  Önbellek backend'in BELLEĞİNDE ve 6 SAAT yaşıyor. Yani:
#    • Backend yeniden başlatılırsa önbellek SIFIRLANIR.
#    • Akşam ısıtırsan sabaha kadar dayanmaz.
#  Bu yüzden sunumdan 15–30 dakika önce çalıştır.
#
#  Her kombinasyonu İKİ kez çağırıp süreleri yazıyor. İkinci süre hâlâ
#  yüksekse (>3 sn) o kombinasyon canlıda da yavaş olacak demektir:
#  OpenStreetMap o an cevap vermiyor, tur yerel kayıtlardan üretiliyor.
# ============================================================

$ErrorActionPreference = 'Stop'
$API = 'http://localhost:5000'

Write-Host ''
Write-Host ' ================================================' -ForegroundColor Cyan
Write-Host '  SUNUM ÖNCESİ ÖNBELLEK ISITMA' -ForegroundColor Cyan
Write-Host ' ================================================' -ForegroundColor Cyan
Write-Host ''

# ---- Backend ayakta mı? ----
try {
    $null = Invoke-WebRequest -Uri "$API/swagger/index.html" -UseBasicParsing -TimeoutSec 5
} catch {
    Write-Host '  [HATA] Backend çalışmıyor (localhost:5000).' -ForegroundColor Red
    Write-Host '         Önce baslat.bat ile sunucuları açın.' -ForegroundColor Red
    Write-Host ''
    Read-Host '  Kapatmak için Enter'
    exit 1
}

# ---- Giriş ----
Write-Host '  Giriş yapılıyor...'
try {
    $giris = Invoke-RestMethod -Uri "$API/api/auth/login" -Method Post `
        -ContentType 'application/json' `
        -Body (@{ username = 'admin'; password = 'staj123' } | ConvertTo-Json)
    $token = $giris.token
} catch {
    Write-Host '  [HATA] Giriş yapılamadı. Kullanıcı adı/şifre değişmiş olabilir.' -ForegroundColor Red
    Read-Host '  Kapatmak için Enter'
    exit 1
}
Write-Host '  Giriş tamam.' -ForegroundColor Green
Write-Host ''

$baslik = @{ Authorization = "Bearer $token" }

# ---- Isıtılacak kombinasyonlar ----
#  Sunumda göstereceğin HER şehir+tema+mola kombinasyonu burada olmalı:
#  önbellek anahtarı şehre, temaya ve mola/konaklama seçimine göre
#  değişiyor. Süre/gün sayısı anahtarı DEĞİŞTİRMİYOR — onu canlıda
#  serbestçe oynatabilirsin.
$kombinasyonlar = @(
    @{
        Ad = 'Ankara · 2 gün · konaklamalı  (ANA DEMO)'
        Govde = @{
            lokasyon = @{ ilPlaka = 6; ekIlPlakalari = @() }
            ulasimTipi = 'Yaya'
            sure = @{ birim = 'Gun'; deger = 2; gunlukSaat = 8; toplamDakika = 960; baslangicSaati = '09:00' }
            tema = 'Karma'; beslenmeKisiti = 'Yok'
            yemekMolasi = $true; konaklama = $true; serbestZaman = $true
        }
    },
    @{
        Ad = 'Ankara · 6 saat · günübirlik'
        Govde = @{
            lokasyon = @{ ilPlaka = 6; ekIlPlakalari = @() }
            ulasimTipi = 'Yaya'
            sure = @{ birim = 'Saat'; deger = 6; toplamDakika = 360; baslangicSaati = '09:00' }
            tema = 'Karma'; beslenmeKisiti = 'Yok'
            yemekMolasi = $true; konaklama = $false; serbestZaman = $true
        }
    },
    @{
        Ad = 'İzmir · 6 saat  (ikinci şehir sorulursa)'
        Govde = @{
            lokasyon = @{ ilPlaka = 35; ekIlPlakalari = @() }
            ulasimTipi = 'Yaya'
            sure = @{ birim = 'Saat'; deger = 6; toplamDakika = 360; baslangicSaati = '09:00' }
            tema = 'Karma'; beslenmeKisiti = 'Yok'
            yemekMolasi = $true; konaklama = $false; serbestZaman = $true
        }
    },
    @{
        Ad = 'Ankara + İstanbul · çok şehirli'
        Govde = @{
            lokasyon = @{ ilPlaka = 6; ekIlPlakalari = @(34) }
            ulasimTipi = 'Arac'
            sure = @{ birim = 'Gun'; deger = 2; gunlukSaat = 8; toplamDakika = 960; baslangicSaati = '09:00' }
            tema = 'Karma'; beslenmeKisiti = 'Yok'
            yemekMolasi = $true; konaklama = $true; serbestZaman = $true
        }
    }
)

$yavasOlanlar = @()

foreach ($k in $kombinasyonlar) {
    Write-Host ' --------------------------------------------------'
    Write-Host "  $($k.Ad)"

    $govde = $k.Govde | ConvertTo-Json -Depth 6
    $sonSure = $null
    $sonuc = $null

    # İki kez: birincisi önbelleği dolduruyor, ikincisi gerçek demo hızı.
    foreach ($tur in 1, 2) {
        $etiket = if ($tur -eq 1) { '1. istek (ısıtma)' } else { '2. istek (gerçek)' }
        $olcum = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $sonuc = Invoke-RestMethod -Uri "$API/api/tur/rota-oner" -Method Post `
                -Headers $baslik -ContentType 'application/json' -Body $govde
            $olcum.Stop()
            $sonSure = $olcum.Elapsed.TotalSeconds
            Write-Host ("     {0} : {1,6:N2} sn" -f $etiket, $sonSure)
        } catch {
            $olcum.Stop()
            Write-Host ("     {0} : HATA — {1}" -f $etiket, $_.Exception.Message) -ForegroundColor Red
            $sonuc = $null
        }
    }

    if ($sonuc -and $sonuc.waypoints) {
        # Yerel yedek kullanıldıysa sunucu bunu uyarı olarak söylüyor.
        $yerel = @($sonuc.uyarilar | Where-Object { $_ -match 'kay.tl. mekanlardan' }).Count -gt 0
        $kaynak = if ($yerel) { 'YEREL YEDEK' } else { 'canlı OpenStreetMap' }
        $renk = if ($yerel) { 'Yellow' } else { 'Green' }
        Write-Host ("     → {0} durak · kaynak: {1}" -f $sonuc.waypoints.Count, $kaynak) -ForegroundColor $renk

        if ($sonSure -gt 3) { $yavasOlanlar += $k.Ad }
    } else {
        Write-Host '     → tur üretilemedi' -ForegroundColor Red
        $yavasOlanlar += $k.Ad
    }
}

Write-Host ''
Write-Host ' ================================================' -ForegroundColor Cyan
Write-Host '  ISITMA BİTTİ' -ForegroundColor Cyan
Write-Host ' ================================================' -ForegroundColor Cyan
Write-Host ''

if ($yavasOlanlar.Count -eq 0) {
    Write-Host '  Bütün kombinasyonlar hazır — hepsi 3 saniyenin altında.' -ForegroundColor Green
} else {
    Write-Host '  Şunlar hâlâ yavaş (canlıda da ~15 sn sürecek):' -ForegroundColor Yellow
    foreach ($y in $yavasOlanlar) { Write-Host "    • $y" -ForegroundColor Yellow }
    Write-Host ''
    Write-Host '  Sebep: OpenStreetMap o an cevap vermiyor. Tur yine oluşuyor'
    Write-Host '  (yerel kayıtlardan) ama beklemeli. Birkaç dakika sonra tekrar'
    Write-Host '  çalıştırmayı dene ya da sunumda o kombinasyonu seçme.'
}

Write-Host ''
Write-Host '  ÖNEMLİ: Backend yeniden başlatılırsa önbellek silinir —'
Write-Host '  bu betiği tekrar çalıştır.'
Write-Host ''
Read-Host '  Kapatmak için Enter'
