@echo off
chcp 65001 >nul
REM ============================================================
REM  StajProject - gelistirme sunucularini baslatir
REM
REM  Cift tiklayin. Iki pencere acilir:
REM    1) Backend  -> http://localhost:5000  (Swagger: /swagger)
REM    2) Frontend -> http://localhost:5173
REM
REM  Pencereleri KAPATMAYIN; kapatirsaniz sunucular durur.
REM  Durdurmak icin ilgili pencerede Ctrl+C.
REM ============================================================

echo.
echo  StajProject baslatiliyor...
echo.

REM ---- Odev 8: GeoServer ayakta mi? -------------------------------------
REM Backend'in listeleme uclari veriyi GeoServer'dan cekiyor; kapaliysa
REM harita bos gelir ve sebebi ilk bakista anlasilmaz. Once kontrol edip
REM gerekirse baslatiyoruz.
REM
REM 401 de "ayakta" sayilir: sunucu cevap veriyor, sadece giris istiyor.
powershell -NoProfile -Command "try { $null = Invoke-WebRequest -Uri 'http://localhost:8080/geoserver/web/' -UseBasicParsing -TimeoutSec 3; exit 0 } catch { if ($_.Exception.Response) { exit 0 } else { exit 1 } }"

if errorlevel 1 (
    echo  GeoServer calismiyor - baslatiliyor...
    start "StajProject - GEOSERVER (kapatmayin)" powershell -ExecutionPolicy Bypass -File "%~dp0geoserver\gs-baslat.ps1"
    echo  GeoServer penceresi acildi. Ilk acilis 30-60 saniye surebilir.
    echo.
) else (
    echo  GeoServer calisiyor: http://localhost:8080/geoserver
    echo.
)

REM ---- Odev 17: OSRM ayakta mi? -----------------------------------------
REM Rota hesabi OSRM'e gidiyor. KAPALI OLMASI HATA DEGIL: hatlar Odev 16'daki
REM duz cizgi haline doner ve uygulama eksiksiz calisir. Bu yuzden asagidaki
REM adim basarisiz olursa akis DURMUYOR, sadece uyariyor.
REM
REM Veri hazirlanmamissa (osrm\veri\harita.osrm yok) konteyneri hic
REM baslatmiyoruz: veri olmadan acilan OSRM porta cevap verir ama her istege
REM hata doner - "calisiyor" gibi gorunen bozuk bir servis, hic calismayandan
REM daha kafa karistiricidir.
if not exist "%~dp0osrm\veri\harita.osrm" (
    echo  OSRM verisi hazir degil - rota ozelligi kapali.
    echo    Hazirlamak icin: powershell -ExecutionPolicy Bypass -File osrm\osrm-kur.ps1
    echo.
) else (
    powershell -NoProfile -Command "try { $null = Invoke-WebRequest -Uri 'http://localhost:5001/nearest/v1/driving/32.8597,39.9334' -UseBasicParsing -TimeoutSec 3; exit 0 } catch { exit 1 }"
    if errorlevel 1 (
        echo  OSRM calismiyor - baslatiliyor...
        pushd "%~dp0osrm"
        docker compose up -d
        popd
        echo.
    ) else (
        echo  OSRM calisiyor: http://localhost:5001
        echo.
    )
)

REM %~dp0 = bu .bat dosyasinin bulundugu klasor (sonunda ters bolu var)
start "StajProject - BACKEND (kapatmayin)" cmd /k "cd /d "%~dp0backend" && dotnet run --project StajProject.API --urls http://localhost:5000"

REM Backend'in ayaga kalkmasi icin kisa bir bekleme: frontend proxy'si
REM ilk istekte backend'i bulamazsa konsola hata basar.
timeout /t 4 /nobreak >nul

start "StajProject - FRONTEND (kapatmayin)" cmd /k "cd /d "%~dp0frontend" && npm run dev"

echo  Pencereler acildi.
echo.
echo   Backend   : http://localhost:5000/swagger
echo   Frontend  : http://localhost:5173
echo   GeoServer : http://localhost:8080/geoserver   (admin / geoserver)
echo   OSRM      : http://localhost:5001                (rota motoru)
echo.
echo   Giris    : admin / staj123   veya   ayse / staj123
echo.
echo  Tarayici birazdan aciliyor...
timeout /t 6 /nobreak >nul
start "" http://localhost:5173

exit
