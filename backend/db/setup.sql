-- ============================================================
--  StajProject — veritabanı ilk kurulum betiği
--
--  Çalıştırma (postgres süper kullanıcısıyla):
--    psql -U postgres -f backend/db/setup.sql
--
--  Bu betik yalnızca ROL + VERİTABANI + POSTGIS eklentisini hazırlar.
--  Tabloları OLUŞTURMAZ — onları EF Core migration'ları oluşturur
--  (uygulama açılışında Database.Migrate() otomatik çalışır).
-- ============================================================

-- 1) Uygulamanın bağlanacağı kullanıcı.
--    appsettings.Development.json'daki ConnectionString ile birebir aynı
--    olmalı: Username=stajyer / Password=<aşağıda seçtiğiniz şifre>
--
--    ⚠ AŞAĞIDAKİ ŞİFRE YALNIZCA YEREL GELİŞTİRME İÇİNDİR.
--    Veritabanı yalnızca localhost'tan erişilebiliyor ve bu proje bir
--    ödev/portföy çalışması; şifre bilerek betiğin içinde duruyor ki
--    kurulum tek komutla çalışsın. Projeyi kendi makinenizde kurarken
--    kendi şifrenizi yazın ve appsettings.Development.json'a da onu
--    girin. GERÇEK bir sunucuya kurulacaksa bu şifre MUTLAKA
--    değiştirilmeli ve SUPERUSER yetkisi kaldırılmalıdır.
--
--    SUPERUSER yetkisi geliştirme kolaylığı içindir; PostGIS eklentisini
--    kurabilmesi ve migration'ları uygulayabilmesi gerekiyor.
CREATE ROLE stajyer WITH LOGIN PASSWORD 'stajyer123' SUPERUSER;

-- 2) Veritabanı. Sahibi stajyer olsun ki tabloları o oluşturabilsin.
CREATE DATABASE staj_db OWNER stajyer;

-- 3) Yeni veritabanına geç (\c = \connect, psql komutu)
\c staj_db

-- 4) PostGIS eklentisini bu veritabanına kur.
--    Eklenti VERİTABANI BAZINDADIR: sunucuya kurmuş olmak yetmez,
--    her veritabanında ayrıca etkinleştirilmesi gerekir.
--    Bu komut geometry tipini, ST_* fonksiyonlarını ve GIST operatör
--    sınıflarını bu veritabanına ekler.
CREATE EXTENSION IF NOT EXISTS postgis;

-- 5) Doğrulama
SELECT postgis_full_version();
