@echo off
chcp 65001 >nul
REM ============================================================
REM  StajProject - SUNUM ONCESI ONBELLEK ISITMA
REM
REM  Cift tiklayin. Sunumda gosterecek oldugunuz tur
REM  kombinasyonlarini onceden calistirip onbellege alir; boylece
REM  juri onunde "Rota Olustur" saniyenin altinda cevap verir.
REM
REM  SUNUMDAN 15-30 DAKIKA ONCE calistirin: onbellek 6 saat yasiyor
REM  ve backend yeniden baslatilirsa sifirlaniyor.
REM
REM  Isin tamami sunum-isit.ps1 icinde (baslat.bat'in GeoServer
REM  betigini cagirmasiyla ayni desen): JSON govdeleri ve olcumler
REM  PowerShell'de cok daha guvenli yaziliyor.
REM ============================================================

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sunum-isit.ps1"
