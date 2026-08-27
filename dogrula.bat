@echo off
chcp 65001 >nul
setlocal

REM ============================================================
REM  StajProject - butun denetimleri calistirir
REM
REM  CI'nin (.github/workflows/ci.yml) yerel karsiligi: ayni dort
REM  adim, ayni sirayla. Amac, "CI'de kirmizi yaniyor ama bende
REM  calisiyordu" durumunu itmeden ONCE gormek.
REM
REM  Veritabani GEREKMEZ: testlerin hepsi EF Core'un bellek ici
REM  saglayicisini kullaniyor, PostGIS'e baglanan tek satir yok.
REM  Bu yuzden sunucular kapaliyken de calisir.
REM
REM  Cift tiklayin ya da: dogrula.bat
REM ============================================================

echo.
echo  ============================================
echo   StajProject - dogrulama
echo  ============================================
echo.

set HATA=0

REM ---- 1) Backend derleme --------------------------------------------
REM Release yapilandirmasi CI ile ayni. Debug'da gorunmeyen bir sorun
REM (kosullu derleme, iyilestirici uyarilari) burada yakalanabilir.
echo  [1/4] Backend derleniyor (Release)...
dotnet build "%~dp0backend\StajProject.sln" --configuration Release --nologo --verbosity quiet
if errorlevel 1 (
    echo        BASARISIZ - derleme hatasi
    set HATA=1
    goto :ozet
)
echo        tamam
echo.

REM ---- 2) Backend testleri -------------------------------------------
echo  [2/4] Backend testleri...
dotnet test "%~dp0backend\StajProject.sln" --no-build --configuration Release --nologo --verbosity quiet
if errorlevel 1 (
    echo        BASARISIZ - test kirmizi
    set HATA=1
) else (
    echo        tamam
)
echo.

REM ---- 3) Frontend testleri ------------------------------------------
REM npm ci DEGIL npm test: ci komutu node_modules'u silip bastan kurar ve
REM Windows'ta calisan bir dev sunucusu dosyalari kilitliyorsa yarida
REM kalir. CI'de temiz bir checkout oldugu icin orada npm ci dogru;
REM burada mevcut kurulumla test etmek yeterli.
echo  [3/4] Frontend testleri...
pushd "%~dp0frontend"
call npm test
if errorlevel 1 (
    echo        BASARISIZ - test kirmizi
    set HATA=1
) else (
    echo        tamam
)
echo.

REM ---- 4) Frontend uretim derlemesi -----------------------------------
REM Testler gecse de derleme kirilabilir: kullanilmayan import, cozulemeyen
REM yol, JSX hatasi... Yayina giden ciktinin uretilebildigini de goruyoruz.
echo  [4/4] Frontend uretim derlemesi...
call npm run build
if errorlevel 1 (
    echo        BASARISIZ - derleme hatasi
    set HATA=1
) else (
    echo        tamam
)
popd
echo.

:ozet
echo  ============================================
if "%HATA%"=="1" (
    echo   SONUC: BASARISIZ - yukaridaki hatalara bakin
) else (
    echo   SONUC: HEPSI GECTI
)
echo  ============================================
echo.

pause
exit /b %HATA%
