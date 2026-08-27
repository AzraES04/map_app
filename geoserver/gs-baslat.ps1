# ============================================================================
#  GeoServer'i baslatir.
#
#  Neden ayri bir betik? GeoServer'in kendi bin\startup.bat dosyasi calismak
#  icin JAVA_HOME ortam degiskenini ister. Temurin kurulumu bu degiskeni her
#  zaman ayarlamiyor; ayarlanmadiginda startup.bat sessizce hata verip
#  kapaniyor ve sebebi belli olmuyor. Bu betik Java'yi diskte kendisi bulup
#  degiskeni SADECE bu surec icin ayarliyor - sistem ayarlarina dokunmuyor.
#
#  Kullanim:
#    powershell -ExecutionPolicy Bypass -File geoserver\gs-baslat.ps1
#    powershell -ExecutionPolicy Bypass -File geoserver\gs-baslat.ps1 -GeoServerHome D:\gs
# ============================================================================

param(
    # GeoServer ZIP'inin acildigi klasor (icinde bin\startup.bat olmali)
    [string]$GeoServerHome = "C:\geoserver",

    # Saglik kontrolu icin beklenecek azami sure
    [int]$BeklemeSaniye = 120
)

$ErrorActionPreference = "Stop"

function Yaz($isaret, $mesaj) { Write-Host "$isaret $mesaj" }

# ---------------------------------------------------------------------------
# 1) GeoServer klasoru dogru mu?
# ---------------------------------------------------------------------------
$startup = Join-Path $GeoServerHome "bin\startup.bat"

if (-not (Test-Path $startup)) {
    Yaz "[X]" "GeoServer bulunamadi: $startup"
    Write-Host ""
    Write-Host "  ZIP'i C:\geoserver klasorune acmis olman gerekiyor."
    Write-Host "  Dogru sonuc: C:\geoserver\bin\startup.bat"
    Write-Host "  Ayrinti icin: geoserver\KURULUM.md (2. adim)"
    exit 1
}

# ---------------------------------------------------------------------------
# 2) Java'yi bul
#
#    Sirayla: JAVA_HOME -> PATH uzerindeki java.exe -> bilinen kurulum
#    klasorleri. Ilk bulunan kazanir.
# ---------------------------------------------------------------------------
function Bul-JavaHome {
    # a) Zaten tanimliysa ve gecerliyse onu kullan
    if ($env:JAVA_HOME -and (Test-Path (Join-Path $env:JAVA_HOME "bin\java.exe"))) {
        return $env:JAVA_HOME
    }

    # b) PATH uzerinde java varsa, onun iki ust klasoru JAVA_HOME'dur
    #    (...\jdk-21\bin\java.exe -> ...\jdk-21)
    $cmd = Get-Command java.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        $bin = Split-Path $cmd.Source -Parent
        $kok = Split-Path $bin -Parent
        if (Test-Path (Join-Path $kok "bin\java.exe")) {
            return $kok
        }
    }

    # c) Bilinen kurulum konumlarini tara. En yuksek surumu sec:
    #    isim sirasi surum sirasiyla ortusuyor (jdk-17 < jdk-21).
    $adaylar = @(
        "$env:ProgramFiles\Eclipse Adoptium",
        "$env:ProgramFiles\Java",
        "${env:ProgramFiles(x86)}\Eclipse Adoptium",
        "$env:LOCALAPPDATA\Programs\Eclipse Adoptium"
    )

    foreach ($klasor in $adaylar) {
        if (-not (Test-Path $klasor)) { continue }

        $bulunan = Get-ChildItem $klasor -Directory -ErrorAction SilentlyContinue |
                   Where-Object { Test-Path (Join-Path $_.FullName "bin\java.exe") } |
                   Sort-Object Name -Descending |
                   Select-Object -First 1

        if ($bulunan) { return $bulunan.FullName }
    }

    return $null
}

$javaHome = Bul-JavaHome

if (-not $javaHome) {
    Yaz "[X]" "Java bulunamadi."
    Write-Host ""
    Write-Host "  Kur:  winget install --id EclipseAdoptium.Temurin.21.JRE -e"
    Write-Host "  Sonra PowerShell penceresini KAPATIP yeniden ac ve tekrar dene."
    exit 1
}

Yaz "[i]" "Java   : $javaHome"
Yaz "[i]" "GeoServer: $GeoServerHome"

# ---------------------------------------------------------------------------
# 3) Zaten calisiyor mu? (iki kopya ayni portu tutamaz)
# ---------------------------------------------------------------------------
$adres = "http://localhost:8080/geoserver/web/"

function Ayakta-Mi {
    try {
        # -UseBasicParsing: IE motoru olmadan calissin (Windows PowerShell 5.1)
        $c = Invoke-WebRequest -Uri $adres -UseBasicParsing -TimeoutSec 3
        return ($c.StatusCode -ge 200 -and $c.StatusCode -lt 500)
    } catch {
        # 401 de "ayakta" demektir: sunucu cevap veriyor, sadece giris istiyor
        $yanit = $_.Exception.Response
        if ($yanit -and $yanit.StatusCode) { return $true }
        return $false
    }
}

if (Ayakta-Mi) {
    Yaz "[OK]" "GeoServer zaten calisiyor: http://localhost:8080/geoserver"
    exit 0
}

# ---------------------------------------------------------------------------
# 4) Baslat
#
#    Ortam degiskenlerini SADECE bu surec icin ayarliyoruz; Start-Process ile
#    acilan pencere bunlari miras aliyor. Sistem geneline yazmiyoruz ki
#    makinedeki diger Java isleri etkilenmesin.
# ---------------------------------------------------------------------------
$env:JAVA_HOME      = $javaHome
$env:GEOSERVER_HOME = $GeoServerHome

Yaz "[>]" "GeoServer baslatiliyor (ayri pencere acilacak, KAPATMA)..."

Start-Process -FilePath "cmd.exe" `
              -ArgumentList "/k", "`"$startup`"" `
              -WorkingDirectory (Join-Path $GeoServerHome "bin")

# ---------------------------------------------------------------------------
# 5) Ayaga kalkmasini bekle
#
#    Ilk acilis 30-60 sn surebiliyor (Jetty + katman yapilandirmasi okunuyor).
#    Sabit bir "sleep 60" yerine saniyede bir yoklayip hazir olur olmaz
#    devam ediyoruz.
# ---------------------------------------------------------------------------
$gecen = 0
Write-Host -NoNewline "    bekleniyor"

while ($gecen -lt $BeklemeSaniye) {
    Start-Sleep -Seconds 2
    $gecen += 2
    Write-Host -NoNewline "."

    if (Ayakta-Mi) {
        Write-Host ""
        Yaz "[OK]" "GeoServer hazir: http://localhost:8080/geoserver  (admin / geoserver)"
        Write-Host ""
        Write-Host "  Sirada: powershell -ExecutionPolicy Bypass -File geoserver\gs-yapilandir.ps1"
        exit 0
    }
}

Write-Host ""
Yaz "[!]" "$BeklemeSaniye saniyede ayaga kalkmadi. Acilan pencerede loglara bak."
exit 1
