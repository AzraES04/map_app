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

REM %~dp0 = bu .bat dosyasinin bulundugu klasor (sonunda ters bolu var)
start "StajProject - BACKEND (kapatmayin)" cmd /k "cd /d "%~dp0backend" && dotnet run --project StajProject.API --urls http://localhost:5000"

REM Backend'in ayaga kalkmasi icin kisa bir bekleme: frontend proxy'si
REM ilk istekte backend'i bulamazsa konsola hata basar.
timeout /t 4 /nobreak >nul

start "StajProject - FRONTEND (kapatmayin)" cmd /k "cd /d "%~dp0frontend" && npm run dev"

echo  Iki pencere acildi.
echo.
echo   Backend  : http://localhost:5000/swagger
echo   Frontend : http://localhost:5173
echo.
echo   Giris    : admin / staj123   veya   ayse / staj123
echo.
echo  Tarayici birazdan aciliyor...
timeout /t 6 /nobreak >nul
start "" http://localhost:5173

exit
