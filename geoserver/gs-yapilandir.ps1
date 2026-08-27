# ============================================================================
#  GeoServer'da workspace + PostGIS store + uc katmani olusturur.
#
#  Neden REST API? Ayni isi arayuzden tiklayarak da yapabilirsin (KURULUM.md
#  4-B). Ama tiklamalar depoda kayitli kalmaz: makine degistiginde ya da
#  GeoServer'i sifirladiginda "acaba hangi kutuyu isaretlemistim?" sorusuna
#  donersin. Betik yapilandirmayi BELGE haline getirir - projedeki
#  backend\db\setup.sql ile ayni mantik.
#
#  Betik idempotent'tir: var olani atlar, tekrar tekrar calistirilabilir.
#
#  Kullanim:
#    powershell -ExecutionPolicy Bypass -File geoserver\gs-yapilandir.ps1
#    powershell -ExecutionPolicy Bypass -File geoserver\gs-yapilandir.ps1 -KatmanlariKilitle
# ============================================================================

param(
    [string]$GeoServerUrl = "http://localhost:8080/geoserver",
    [string]$GsKullanici  = "admin",
    [string]$GsSifre      = "geoserver",

    [string]$Workspace    = "staj",
    [string]$NamespaceUri = "http://stajproject.local/staj",
    [string]$StoreAdi     = "staj_db",

    # PostGIS baglanti bilgileri - backend\db\setup.sql ile ayni olmali
    [string]$DbHost       = "localhost",
    [int]   $DbPort       = 5432,
    [string]$DbAdi        = "staj_db",
    [string]$DbSema       = "public",
    [string]$DbKullanici  = "stajyer",
    [string]$DbSifre      = "stajyer123",

    # Katmanlari anonim erisime kapatir (sadece yonetici rolu okuyabilir).
    # Varsayilan KAPALI: acik oldugunda GeoServer'in kendi Layer Preview
    # sayfasi da giris ister ve demo sirasinda kafa karistirabilir.
    [switch]$KatmanlariKilitle
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
#  Katmanlar - Odev 9 / Madde 1: "SQL ile Olustur" (SQL View)
#
#  Katman artik dogrudan tabloya degil, bir SQL SORGUSUNA bakiyor. Iki faydasi:
#    1) is_deleted = false kurali KATMANIN ICINDE. Silinmis kayit GeoServer'in
#       hicbir servisinden (WFS, WMS, lejant, isi haritasi) cikamaz. Onceden bu
#       kurali her istekte CQL ile biz gonderiyorduk; bir yerde unutulsa
#       silinmis veri sizardi.
#    2) Katman yalnizca gereken kolonlari yayinliyor.
#
#  keyColumn: id  -> GeoServer bunu birincil anahtar sayar; WFS cevabindaki
#                    "vw_point.5" feature id'si ve id kolonu bundan geliyor.
#                    Bir view'in birincil anahtari veritabanindan okunamadigi
#                    icin ELLE bildirmek zorunlu.
#  geometry       -> Ayni sebeple geometri kolonunun adi, tipi ve SRID'si de
#                    elle bildiriliyor.
#
#  Tablo adlari AppDbContext.ConfigureGeometryTable cagrilarindaki adlarla,
#  katman adlari ise backend'deki GeoServerKatmanlari.cs ile ayni olmali.
# ---------------------------------------------------------------------------
#
#  ODEV 13 / MADDE 1 ile dorduncu katman geldi: vw_poi.
#
#  Digerlerinden farki, SQL'ini kendisi tasimasi (Sql anahtari). Cizim
#  katmanlari tek tabloya bakiyor; POI katmani UC tabloyu birlestiriyor:
#    poi           -> noktanin kendisi
#    poi_category  -> kategori adi VE kokü (SLD stilleri buna gore suzuyor)
#    users         -> ekleyen kullanicinin adi (admin listesindeki sutun)
#
#  Kok kategori neden ozyinelemeli (WITH RECURSIVE) bulunuyor?
#  Kategori agacinin derinligi onceden belli degil ("Yeme-Icme > Restoran >
#  Kebapci" gibi ucuncu bir seviye acilabilir). Tek bir LEFT JOIN ile sadece
#  BIR seviye yukari cikilirdi ve ucuncu seviyedeki bir POI'nin "koku"
#  yanlislikla orta seviye olurdu -> SLD onu tanimaz, harita sessizce yanlis
#  simge cizerdi. Ozyinelemeli sorgu koke kadar tirmaniyor.
#
#  mesai_plani kolonu ::text ile donusturuluyor: kolon tipi jsonb ve
#  GeoServer'in JDBC surucusu jsonb'yi taniyamayip kolonu katmandan tamamen
#  disarida birakiyor. text'e cevirince duz bir metin kolonu oluyor ve
#  backend JSON'u kendisi cozuyor (GeoServerPoiRepository).
$Katmanlar = @(
    @{ Ad = "vw_point";   Tablo = "tbl_point";   Baslik = "Nokta cizimleri (SQL View)";   Tip = "Point" },
    @{ Ad = "vw_line";    Tablo = "tbl_line";    Baslik = "Cizgi cizimleri (SQL View)";   Tip = "LineString" },
    @{ Ad = "vw_polygon"; Tablo = "tbl_polygon"; Baslik = "Poligon cizimleri (SQL View)"; Tip = "Polygon" },
    @{ Ad = "vw_poi";     Tablo = "poi";         Baslik = "POI - ilgi noktalari (SQL View)"; Tip = "Point";
       Sql = "WITH RECURSIVE agac AS (SELECT id, ad, parent_id, ad AS kok FROM poi_category WHERE parent_id IS NULL AND is_deleted = false UNION ALL SELECT c.id, c.ad, c.parent_id, a.kok FROM poi_category c JOIN agac a ON c.parent_id = a.id WHERE c.is_deleted = false) SELECT p.id, p.isim, p.kategori_id, k.ad AS kategori_adi, k.kok AS kok_kategori, p.mesai_saatleri, p.mesai_plani::text AS mesai_plani, p.created_date, p.modified_date, p.user_id, u.username AS kullanici_adi, p.is_active, p.geom FROM poi p JOIN agac k ON k.id = p.kategori_id LEFT JOIN users u ON u.id = p.user_id AND u.is_deleted = false WHERE p.is_deleted = false" }
)

# Isi haritasi stilleri. SLD dosyalari depoda: geoserver\*.sld
#   isi_haritasi -> ekranda gorunen renkli yuzey (Odev 9)
#   isi_deger    -> AYNI hesabin gri tonlamali hali; arayuz pikselden
#                   yogunluk degerini okuyor (Odev 11 "termometre")
$Stiller = @(
    @{ Ad = "isi_haritasi"; Dosya = "isi-haritasi.sld" },
    @{ Ad = "isi_deger";    Dosya = "isi-deger.sld" }
)

# ---------------------------------------------------------------------------
#  POI STILLERI BU BETIKTE DEGIL - Odev 13 iyilestirmesi
#
#  Ilk uygulamada bes SLD dosyasi depoda elle yaziliydi ve bu betik onlari
#  yukluyordu. Iki sorunu vardi:
#
#    1. Odev "HER BIR POI kategorisi icin ayri Style" istiyor; kok basina
#       stil, Restoran ile Kafe'yi ayni simgede birlestiriyordu.
#    2. Daha onemlisi: stiller VERIYLE BAGLI DEGILDI. Yonetici panelden yeni
#       bir kategori actiginda hicbir stil onu tanimiyordu ve POI'ler yedek
#       stille ciziliyordu - hata vermeden yanlis calisan bir durum.
#
#  Artik stiller KATEGORI TABLOSUNDAN uretiliyor ve backend GeoServer'in
#  REST API'sine yaziyor:
#      Business/Poiler/PoiStilUretici.cs   -> SLD uretimi
#      Business/Services/PoiStyleService   -> yazma, baglama, artik temizleme
#
#  Ne zaman calisiyor?
#    * uygulama ilk acilista (DatabaseSeeder sonunda, sessizce)
#    * kategori eklendiginde / guncellendiginde / silindiginde
#    * yonetim panelindeki "Harita stillerini yenile" dugmesiyle
#
#  Bu betigin sorumlulugu artik yalnizca KATMANI olusturmak (vw_poi).
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
#  HTTP yardimcilari
#
#  Authorization basligini elle kuruyoruz. -Credential parametresi de calisir
#  ama o "once istegi gonder, 401 gelirse tekrarla" yolunu izler; bazi
#  GeoServer surumlerinde POST govdesi ikinci denemede kayboluyor.
# ---------------------------------------------------------------------------
$ikili   = "$GsKullanici`:$GsSifre"
$b64     = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($ikili))
$Basliklar = @{ Authorization = "Basic $b64" }

function Yaz($isaret, $mesaj) { Write-Host "$isaret $mesaj" }

<#
  Bir REST kaynagi var mi? 200 -> var, 404 -> yok.
  Baska bir hata (401, 500) gercek bir sorundur; yukari firlatiyoruz.
#>
function Kaynak-VarMi([string]$yol) {
    try {
        Invoke-RestMethod -Uri "$GeoServerUrl/rest/$yol" -Headers $Basliklar `
                          -Method Get -TimeoutSec 20 | Out-Null
        return $true
    } catch {
        $kod = $null
        if ($_.Exception.Response) { $kod = [int]$_.Exception.Response.StatusCode }

        if ($kod -eq 404) { return $false }

        if ($kod -eq 401) {
            throw "GeoServer girisi reddedildi (401). Kullanici/sifre dogru mu? ($GsKullanici)"
        }
        throw
    }
}

function Rest-Gonder([string]$yol, [string]$govde, [string]$yontem = "Post") {
    Invoke-RestMethod -Uri "$GeoServerUrl/rest/$yol" -Headers $Basliklar `
                      -Method $yontem -ContentType "application/json" `
                      -Body $govde -TimeoutSec 60 | Out-Null
}

# ---------------------------------------------------------------------------
#  0) GeoServer ayakta mi?
# ---------------------------------------------------------------------------
Write-Host ""
Yaz "[i]" "GeoServer: $GeoServerUrl"

try {
    Invoke-RestMethod -Uri "$GeoServerUrl/rest/about/version.json" -Headers $Basliklar `
                      -Method Get -TimeoutSec 10 | Out-Null
} catch {
    Yaz "[X]" "GeoServer'a ulasilamiyor."
    Write-Host ""
    Write-Host "  Once baslat: powershell -ExecutionPolicy Bypass -File geoserver\gs-baslat.ps1"
    Write-Host "  Hata: $($_.Exception.Message)"
    exit 1
}

# ---------------------------------------------------------------------------
#  1) Workspace
#
#  /rest/namespaces ucunu kullaniyoruz cunku hem workspace'i olusturuyor hem
#  de namespace URI'sini belirlememize izin veriyor. /rest/workspaces ucu
#  URI'yi otomatik uretirdi (http://staj gibi) - WFS cevaplarinda gorunen
#  isim alani bu, duzgun olmasi iyi olur.
# ---------------------------------------------------------------------------
if (Kaynak-VarMi "workspaces/$Workspace.json") {
    Yaz "[=]" "Workspace zaten var: $Workspace"
} else {
    Rest-Gonder "namespaces" "{""namespace"":{""prefix"":""$Workspace"",""uri"":""$NamespaceUri""}}"
    Yaz "[+]" "Workspace olusturuldu: $Workspace"
}

# ---------------------------------------------------------------------------
#  2) PostGIS store
#
#  "Expose primary keys" = true KRITIK: bu olmadan GeoServer id kolonunu
#  gizler, WFS cevabinda sadece "tbl_point.5" seklinde bir feature id kalir.
#  Backend kaydin id'sini oradan da cikarabiliyor ama kolonun kendisi
#  gelirse is cok daha saglam olur.
#
#  "Loose bbox" = true: bbox sorgularinda PostGIS'in && operatoru (sadece
#  sinir kutusu karsilastirmasi) kullanilir; kesin geometri testi yapilmaz.
#  Harita gezinirken cok daha hizli, gorsel sonuc ayni.
# ---------------------------------------------------------------------------
if (Kaynak-VarMi "workspaces/$Workspace/datastores/$StoreAdi.json") {
    Yaz "[=]" "Store zaten var: $StoreAdi"
} else {
    $storeJson = @"
{
  "dataStore": {
    "name": "$StoreAdi",
    "description": "StajProject PostGIS veritabani",
    "connectionParameters": {
      "entry": [
        { "@key": "dbtype",              "\$": "postgis" },
        { "@key": "host",                "\$": "$DbHost" },
        { "@key": "port",                "\$": "$DbPort" },
        { "@key": "database",            "\$": "$DbAdi" },
        { "@key": "schema",              "\$": "$DbSema" },
        { "@key": "user",                "\$": "$DbKullanici" },
        { "@key": "passwd",              "\$": "$DbSifre" },
        { "@key": "Expose primary keys", "\$": "true" },
        { "@key": "validate connections","\$": "true" },
        { "@key": "Loose bbox",          "\$": "true" },
        { "@key": "min connections",     "\$": "1" },
        { "@key": "max connections",     "\$": "10" }
      ]
    }
  }
}
"@

    try {
        Rest-Gonder "workspaces/$Workspace/datastores" $storeJson
        Yaz "[+]" "PostGIS store olusturuldu: $StoreAdi"
    } catch {
        Yaz "[X]" "Store olusturulamadi."
        Write-Host "  PostgreSQL calisiyor mu? Kullanici/sifre: $DbKullanici / $DbSifre"
        Write-Host "  Hata: $($_.Exception.Message)"
        exit 1
    }
}

# ---------------------------------------------------------------------------
#  3) Katmanlar
#
#  srs / projectionPolicy: veritabanindaki geometri kolonlari zaten
#  EPSG:4326 tanimli (geometry(Point,4326)). "FORCE_DECLARED" ile GeoServer'a
#  "kendi tahminini kullanma, 4326 de" diyoruz - kaynak SRID bir sekilde 0
#  gelirse katman bozulmasin.
# ---------------------------------------------------------------------------
# View'in yayinlayacagi kolonlar. is_deleted BILEREK YOK: bu view zaten
# yalnizca silinmemis satirlari donduruyor, kolonu tasimanin anlami kalmiyor.
$Kolonlar = "id, name, description, image_url, color, inserted_date, modified_date, inserted_user_id, is_active, geom"

foreach ($k in $Katmanlar) {
    $ad     = $k.Ad
    $tablo  = $k.Tablo
    $baslik = $k.Baslik
    $tip    = $k.Tip

    if (Kaynak-VarMi "workspaces/$Workspace/datastores/$StoreAdi/featuretypes/$ad.json") {
        Yaz "[=]" "SQL View katmani zaten var: ${Workspace}:$ad"
        continue
    }

    # Katman kendi SQL'ini tasiyorsa onu kullan (vw_poi), yoksa standart
    # "tablodan silinmemisleri sec" gorunumunu uret.
    if ($k.ContainsKey("Sql")) {
        $sql = $k.Sql
    } else {
        $sql = "SELECT $Kolonlar FROM $tablo WHERE is_deleted = false"
    }

    $ftJson = @"
{
  "featureType": {
    "name": "$ad",
    "nativeName": "$ad",
    "title": "$baslik",
    "srs": "EPSG:4326",
    "projectionPolicy": "FORCE_DECLARED",
    "enabled": true,
    "metadata": {
      "entry": [
        {
          "@key": "JDBC_VIRTUAL_TABLE",
          "virtualTable": {
            "name": "$ad",
            "sql": "$sql",
            "escapeSql": false,
            "keyColumn": "id",
            "geometry": { "name": "geom", "type": "$tip", "srid": 4326 }
          }
        }
      ]
    }
  }
}
"@

    try {
        # recalculate: sinir kutusunu (bounding box) veriden hesapla.
        # Bu yapilmazsa katmanin sinirlari bos kalir ve WMS bos resim doner.
        Rest-Gonder "workspaces/$Workspace/datastores/$StoreAdi/featuretypes?recalculate=nativebbox,latlonbbox" $ftJson
        Yaz "[+]" "SQL View katmani olusturuldu: ${Workspace}:$ad  ($tablo WHERE is_deleted = false)"
    } catch {
        Yaz "[X]" "Katman olusturulamadi: $ad"
        Write-Host "  Tablo veritabaninda var mi? Backend'i bir kez calistirinca"
        Write-Host "  EF Core migration'lari tablolari olusturur."
        Write-Host "  Hata: $($_.Exception.Message)"
        exit 1
    }
}

# ---------------------------------------------------------------------------
#  3.5) Isi haritasi stili (Odev 9 / Madde 2)
#
#  SLD depoda duruyor; burada GeoServer'a yukluyoruz. Govdeyi UTF-8 BAYT
#  dizisi olarak gonderiyoruz: Windows PowerShell metin govdeyi varsayilan
#  olarak ISO-8859-1 ile kodluyor ve lejanttaki Turkce etiketler bozuluyor.
# ---------------------------------------------------------------------------
$yuklenenStiller = @()

foreach ($stil in $Stiller) {
    $stilAdi = $stil.Ad
    $sldYolu = Join-Path $PSScriptRoot $stil.Dosya

    if (-not (Test-Path $sldYolu)) {
        Yaz "[!]" "SLD bulunamadi, atlaniyor: $sldYolu"
        continue
    }

    # -Encoding UTF8 SART. Windows PowerShell 5.1'in Get-Content varsayilani
    # ANSI'dir (Windows-1254); BOM'suz bir UTF-8 dosyasi okundugunda Turkce
    # harfler daha OKUMA sirasinda bozuluyor ve asagidaki UTF8.GetBytes bozuk
    # metni yeniden kodluyor.
    #
    # Bu, ODEV 13'te GERCEKTEN YASANDI: stiller sorunsuz yuklendi, hicbir hata
    # cikmadi, ama SLD icindeki <ogc:Literal>Saglik</ogc:Literal> filtresi
    # "SaÄŸlÄ±k" olarak gittigi icin hicbir satirla eslesmedi. Sonuc: butun
    # POI'ler "Diger" stiliyle (mor yildiz) cizildi. Sessiz calisan, sebebi
    # gorunmeyen hata turu.
    $sldGovde = [Text.Encoding]::UTF8.GetBytes((Get-Content $sldYolu -Raw -Encoding UTF8))

    try {
        if (Kaynak-VarMi "workspaces/$Workspace/styles/$stilAdi.json") {
            Invoke-RestMethod -Uri "$GeoServerUrl/rest/workspaces/$Workspace/styles/$stilAdi" `
                -Headers $Basliklar -Method Put -ContentType "application/vnd.ogc.sld+xml" `
                -Body $sldGovde -TimeoutSec 60 | Out-Null
            Yaz "[=]" "Stil guncellendi: ${Workspace}:$stilAdi"
        } else {
            Invoke-RestMethod -Uri "$GeoServerUrl/rest/workspaces/$Workspace/styles?name=$stilAdi" `
                -Headers $Basliklar -Method Post -ContentType "application/vnd.ogc.sld+xml" `
                -Body $sldGovde -TimeoutSec 60 | Out-Null
            Yaz "[+]" "Stil olusturuldu: ${Workspace}:$stilAdi"
        }
        $yuklenenStiller += $stilAdi
    } catch {
        Yaz "[X]" "Stil yuklenemedi ($stilAdi): $($_.Exception.Message)"
        exit 1
    }
}

# Stilleri nokta katmanina EK stil olarak bagla. Varsayilan stil
# degistirilmiyor: normal gosterimde noktalar yine nokta olarak cizilsin,
# isi haritasi yalnizca STYLES parametresiyle istendiginde devreye girsin.
if ($yuklenenStiller.Count -gt 0) {
    try {
        $liste = ($yuklenenStiller | ForEach-Object { "{""name"":""${Workspace}:$_""}" }) -join ","
        $bagJson = "{""layer"":{""styles"":{""style"":[$liste]}}}"
        Rest-Gonder "layers/${Workspace}:vw_point" $bagJson "Put"
        Yaz "[+]" "Stiller katmana baglandi: ${Workspace}:vw_point -> $($yuklenenStiller -join ', ')"
    } catch {
        Yaz "[!]" "Stil katmana baglanamadi (kritik degil): $($_.Exception.Message)"
    }
}

# ---------------------------------------------------------------------------
#  4) (istege bagli) Katmanlari anonim erisime kapat
#
#  Varsayilan GeoServer kurulumunda katmanlar herkese aciktir: tarayiciya
#  localhost:8080/.../wfs?... yazan biri TUM kullanicilarin cizimlerini
#  gorebilir. Uygulama icindeki sahiplik suzgeci backend'de calisiyor,
#  GeoServer'in kendisinde degil.
#
#  Bu kural "staj workspace'indeki her katmani (staj.*) OKUMAK (.r) icin
#  ROLE_ADMINISTRATOR gerekir" der. Backend zaten admin hesabiyla
#  baglandigi icin uygulama etkilenmez.
# ---------------------------------------------------------------------------
if ($KatmanlariKilitle) {
    try {
        Rest-Gonder "security/acl/layers" "{""$Workspace.*.r"":""ROLE_ADMINISTRATOR""}"
        Yaz "[+]" "Katmanlar anonim erisime kapatildi ($Workspace.*.r = ROLE_ADMINISTRATOR)"
    } catch {
        Yaz "[!]" "Guvenlik kurali eklenemedi (kritik degil): $($_.Exception.Message)"
    }
}

# ---------------------------------------------------------------------------
#  5) Dogrulama - katmanlar gercekten veri donuyor mu?
# ---------------------------------------------------------------------------
Write-Host ""
Yaz "[i]" "Dogrulama (WFS GetFeature):"

foreach ($k in $Katmanlar) {
    $ad = $k.Ad
    # Backend ile ayni surum ve parametre adlari (bkz. GeoServerClient):
    # WFS 1.0.0, tekil "typeName".
    $url = "$GeoServerUrl/$Workspace/wfs?service=WFS&version=1.0.0&request=GetFeature" +
           "&typeName=${Workspace}:$ad&outputFormat=application/json&maxFeatures=1"

    try {
        $cevap = Invoke-RestMethod -Uri $url -Headers $Basliklar -Method Get -TimeoutSec 30

        $adet = $cevap.totalFeatures
        if ($null -eq $adet) { $adet = "?" }

        Yaz "    [OK]" "${Workspace}:$ad  -> $adet kayit (silinmisler haric)"
    } catch {
        Yaz "    [X]" "${Workspace}:$ad  -> okunamadi: $($_.Exception.Message)"
    }
}

Write-Host ""
Yaz "[OK]" "GeoServer yapilandirmasi tamam."
Write-Host ""
Write-Host "  Katman onizleme : $GeoServerUrl/web/  ->  Data > Layer Preview"
Write-Host "  Sirada          : baslat.bat  (backend + frontend)"
Write-Host ""
