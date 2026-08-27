<#
============================================================================
  OSRM veri hazırlığı (Ödev 17 / Madde 1)

  BİR KEZ çalıştırılır. OpenStreetMap'in ham Türkiye dosyasını indirip
  OSRM'in sorgulayabileceği hâle getirir; sonuç veri/ klasörüne yazılır.
  Ardından `docker compose up -d` servisi ayağa kaldırır.

  ---- ÜÇ AŞAMA, NEDEN ÜÇ? ----

    1) osrm-extract    Ham OSM verisinden yol ağını çıkarır. Hangi yolun
                       araçla geçilebilir olduğunu, hız limitlerini ve tek
                       yönleri belirleyen şey PROFİL dosyası (car.lua).
                       En uzun ve en çok bellek isteyen adım.

    2) osrm-partition   Yol ağını hiyerarşik bölgelere ayırır.
    3) osrm-customize   Bölgelere geçiş maliyetlerini yazar.

  2 ve 3, MLD (Multi-Level Dijkstra) algoritmasının hazırlığı. Alternatif
  CH (Contraction Hierarchies) tek adımdır (osrm-contract) ama hazırlığı
  çok daha uzun sürer ve çok daha fazla bellek ister. MLD seçildi; sorgu
  hızındaki fark bu ölçekte hissedilmiyor.

  ÖNEMLİ: buradaki algoritma, docker-compose.yml'deki `--algorithm mld`
  ile AYNI olmak zorunda. Ayrışırsa osrm-routed veriyi reddeder.

  ---- KULLANIM ----

    powershell -ExecutionPolicy Bypass -File osrm-kur.ps1

    # Makine zorlanıyorsa haritayı bir kutuyla daralt (batı,güney,doğu,kuzey):
    powershell -ExecutionPolicy Bypass -File osrm-kur.ps1 -Kutu "31.5,39.0,39.5,42.2"

    # Yeniden indirmeden tekrar hazırla:
    powershell -ExecutionPolicy Bypass -File osrm-kur.ps1 -IndirmeyiAtla
============================================================================
#>

param(
    # "batı,güney,doğu,kuzey" — verilirse harita bu dikdörtgene kırpılır.
    #
    # NE ZAMAN GEREKİR? Türkiye'nin tamamı ~500 MB indirme, ~6 GB geçici disk
    # ve 6-8 GB bellek istiyor. Yetmezse osrm-extract "bad_alloc" ile ölür.
    # Kırpma bunu dakikalar yerine saniyelere indiriyor.
    #
    # BEDELİ: kutunun DIŞINDAKİ duraklar için rota hesaplanamaz. Kutuyu
    # seçerken hatlarınızın tamamını içine aldığından emin olun.
    [string]$Kutu = "",

    # .pbf zaten indirilmişse ağı tekrar meşgul etme.
    [switch]$IndirmeyiAtla,

    # Geofabrik'in Türkiye çıkarımı. Günlük güncelleniyor.
    [string]$Kaynak = "https://download.geofabrik.de/europe/turkey-latest.osm.pbf"
)

$ErrorActionPreference = "Stop"

$kok     = Split-Path -Parent $MyInvocation.MyCommand.Path
$veriDir = Join-Path $kok "veri"
$pbf     = Join-Path $veriDir "harita.osm.pbf"

# Hazırlık aşamalarıyla docker-compose.yml AYNI imajı kullanmalı: veri biçimi
# sürüme bağlı ve ayrışırsa osrm-routed "incompatible file version" der.
$imaj    = "osrm/osrm-backend:v5.25.0"

function Adim($n, $toplam, $mesaj) {
    Write-Host ""
    Write-Host "[$n/$toplam] $mesaj" -ForegroundColor Cyan
}

function DockerCalistir($arglar) {
    # Veri klasörü konteynerin /data'sına bağlanıyor; OSRM araçlarının
    # hepsi orada çalışıyor.
    $tam = @("run", "--rm", "-v", "${veriDir}:/data", $imaj) + $arglar
    & docker @tam
    if ($LASTEXITCODE -ne 0) {
        throw "Docker adımı başarısız (çıkış kodu $LASTEXITCODE): $($arglar -join ' ')"
    }
}

Write-Host "============================================" -ForegroundColor Green
Write-Host " OSRM veri hazirligi - Odev 17" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green

# ---------------------------------------------------------------- 0) Docker
try {
    & docker info --format "{{.ServerVersion}}" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw }
} catch {
    Write-Host ""
    Write-Host "Docker calismiyior. Docker Desktop'i baslatip tekrar deneyin." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $veriDir)) { New-Item -ItemType Directory -Path $veriDir | Out-Null }

# ---------------------------------------------------------------- 1) indirme
Adim 1 4 "OpenStreetMap Turkiye verisi indiriliyor (~500 MB)..."

if ($IndirmeyiAtla -and (Test-Path $pbf)) {
    Write-Host "      atlandi (-IndirmeyiAtla), mevcut dosya kullanilacak"
} elseif ((Test-Path $pbf) -and -not $IndirmeyiAtla) {
    $mb = [math]::Round((Get-Item $pbf).Length / 1MB, 1)
    Write-Host "      dosya zaten var ($mb MB) - yeniden indirmek icin silin"
} else {
    # Invoke-WebRequest yerine curl: 500 MB'lik indirmede IWR tum govdeyi
    # BELLEGE aliyor ve ilerleme cubugu her parcada ekrani yeniden ciziyor
    # (indirmeyi kat kat yavaslatan bilinen bir davranis).
    & curl.exe -L --fail --progress-bar -o $pbf $Kaynak
    if ($LASTEXITCODE -ne 0) { throw "Indirme basarisiz: $Kaynak" }
}

# ---------------------------------------------------------------- 2) kirpma
if ($Kutu -ne "") {
    Adim 2 4 "Harita $Kutu kutusuna kirpiliyor..."

    # osmium ayri bir imaj: OSRM araclari kirpma yapamiyor.
    $kirpik = Join-Path $veriDir "kirpik.osm.pbf"
    & docker run --rm -v "${veriDir}:/data" stadtnavi/osmium-tool `
        osmium extract --bbox $Kutu --overwrite -o /data/kirpik.osm.pbf /data/harita.osm.pbf
    if ($LASTEXITCODE -ne 0) { throw "Kirpma basarisiz" }

    Move-Item -Force $kirpik $pbf
    $mb = [math]::Round((Get-Item $pbf).Length / 1MB, 1)
    Write-Host "      kirpildi: $mb MB"
} else {
    Adim 2 4 "Kirpma atlandi - Turkiye'nin TAMAMI islenecek"
    Write-Host "      (makine zorlanirsa: -Kutu ""31.5,39.0,39.5,42.2"")" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------- 3) extract
Adim 3 4 "osrm-extract - yol agi cikariliyor (EN UZUN ADIM, 5-20 dk)..."
Write-Host "      Bellek yetmezse 'bad_alloc' hatasi alirsiniz; o durumda" -ForegroundColor DarkGray
Write-Host "      -Kutu parametresiyle haritayi daraltin." -ForegroundColor DarkGray

# car.lua: aracla gecilebilen yollari, hiz limitlerini ve tek yonleri
# tanimlayan profil. Imajin icinde hazir geliyor.
DockerCalistir @("osrm-extract", "-p", "/opt/car.lua", "/data/harita.osm.pbf")

# ------------------------------------------------- 4) partition + customize
Adim 4 4 "osrm-partition + osrm-customize - MLD hazirligi..."
DockerCalistir @("osrm-partition", "/data/harita.osrm")
DockerCalistir @("osrm-customize", "/data/harita.osrm")

# ---------------------------------------------------------------- bitis
$toplamMb = [math]::Round(((Get-ChildItem $veriDir -File | Measure-Object Length -Sum).Sum / 1MB), 1)

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host " HAZIR - veri/ klasoru $toplamMb MB" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host " Servisi baslatmak icin:" -ForegroundColor Yellow
Write-Host "     cd osrm"
Write-Host "     docker compose up -d"
Write-Host ""
Write-Host " Dogrulamak icin (Kizilay -> Ulus):" -ForegroundColor Yellow
Write-Host '     curl "http://localhost:5001/route/v1/driving/32.8541,39.9208;32.852,39.942?overview=false"'
Write-Host ""
