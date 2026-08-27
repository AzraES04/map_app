# GeoServer Kurulumu (Ödev 8 / Madde 1)

Bu belge sıfırdan çalışan bir GeoServer'a kadar olan **tüm** adımları içerir.
Her adımın sonunda bir *doğrulama* satırı var — oraya kadar gelmeden sonrakine geçme.

Toplam süre: ~15 dakika. İndirilecek: ~123 MB (GeoServer) + ~45 MB (Java).

---

## 0) Neyi neden kuruyoruz — beş kavram

Kurulum adımlarını körlemesine takip etmemek için önce şu beşini oturt.
(Sunumda ilk sorulacak şey bunlar.)

| Kavram | Nedir | Bizdeki karşılığı |
|---|---|---|
| **Workspace** (Çalışma alanı) | Katmanların isim alanı (namespace). Aynı adlı iki katman farklı workspace'lerde yan yana yaşayabilir. Katman adı her zaman `workspace:katman` biçiminde anılır. | `staj` |
| **Store** (Veri deposu) | GeoServer'ın veriyi *nereden* okuduğu — bir PostGIS veritabanı, bir Shapefile klasörü, bir GeoTIFF dosyası. Bağlantı bilgisi burada durur. | `staj_db` (PostGIS bağlantısı) |
| **Layer** (Katman) | Store içindeki tek bir tablonun — ya da tek bir SORGUNUN — yayınlanabilir hâli: SRS, başlık, sınır kutusu (bounding box) ve stil bilgisi eklenmiş hâli. | `staj:vw_point`, `staj:vw_line`, `staj:vw_polygon` |
| **WMS** (Web Map Service) | Katmanı **resim** olarak sunar. İstek: "şu sınırlar arasını 256×256 PNG olarak çiz". Cevap: PNG. Tarayıcı geometriyi hiç görmez, hazır boyanmış resim gelir. | Haritanın genel gösterimi |
| **WFS** (Web Feature Service) | Katmanı **veri** olarak sunar. İstek: "şu koşula uyan kayıtları ver". Cevap: GeoJSON / GML — koordinatların kendisi. | Tıklama, düzenleme, analiz |
| **SQL View** (Ödev 9) | Katmanın bir tabloya değil bir SORGUYA bağlanması. Arayüzde: *Configure new SQL view*. | `vw_point` = `tbl_point WHERE is_deleted = false` |
| **Style / SLD** (Ödev 9) | Katmanın nasıl çizileceğini anlatan XML. İçinde *rendering transformation* varsa veri, çizilmeden önce dönüştürülür. | `isi_haritasi` — noktalardan ısı yüzeyi |

**WMS mi WFS mi?** Kural basit: veriyle *iş yapacaksan* (tıklama, düzenleme,
ölçme) WFS; sadece *göstereceksen* WMS. WMS milyonlarca kaydı sunucuda boyayıp
tek resim gönderdiği için çok hızlıdır ama tarayıcı o resmin içindeki tek bir
noktayı seçemez.

Ödev 9 bu iş bölümünü açıkça istiyor ve proje de öyle çalışıyor: haritanın
**genel gösterimi WMS** katmanından geliyor (varsayılan açık), **tıklama,
düzenleme ve analiz WFS** vektör katmanları üzerinden yürüyor.

---

## 1) Java kurulumu

GeoServer bir Java uygulamasıdır. Bu makinede hiç Java yok, önce onu kuralım.
GeoServer 2.28 **Java 17 veya 21** ister; biz 21 kuruyoruz.

PowerShell'i aç (yönetici olmasına gerek yok) ve şunu çalıştır:

```bash
winget install --id EclipseAdoptium.Temurin.21.JRE -e --accept-package-agreements --accept-source-agreements
```

> `JRE` = Java Runtime Environment (sadece çalıştırmak için). GeoServer'ı
> derlemeyeceğimiz için JDK'ya gerek yok. Yine de JDK kurmak istersen
> yukarıdaki `.JRE` yerine `.JDK` yaz — ikisi de çalışır.

**Doğrulama** — PowerShell penceresini **kapatıp yeniden aç** (PATH değişikliği
ancak yeni pencerede görünür), sonra:

```bash
java -version
```

`openjdk version "21.0.x"` benzeri bir çıktı görmelisin. Göremiyorsan sorun
değil, 2. adımdaki başlatma betiği Java'yı diskte kendisi arıyor.

---

## 2) GeoServer'ı indir ve aç

1. Şu adresi tarayıcıda aç:
   <https://sourceforge.net/projects/geoserver/files/GeoServer/2.28.5/geoserver-2.28.5-bin.zip/download>
   İndirme kendiliğinden başlar (`geoserver-2.28.5-bin.zip`, ~123 MB).

2. ZIP'i **`C:\geoserver`** klasörüne çıkar.

   Dikkat: WinRAR/7-Zip "buraya çıkar" derken çoğu zaman fazladan bir klasör
   oluşturur. Doğru sonuç şu olmalı:

   ```
   C:\geoserver\bin\startup.bat        ← bu dosya buradaysa doğru
   C:\geoserver\data_dir\
   C:\geoserver\webapps\
   ```

   Eğer `C:\geoserver\geoserver-2.28.5\bin\...` gibi çıktıysa, içerideki
   klasörün içindekileri bir üste taşı.

**Doğrulama:**

```bash
dir C:\geoserver\bin\startup.bat
```

---

## 2.5) WPS eklentisi (ısı haritası için — Ödev 9)

Isı haritası SLD'sindeki `gs:Heatmap` dönüşümü GeoServer'ın çekirdeğinde
**kayıtlı değil**; WPS eklentisi kurulmadan stil yüklenirken şu hata alınır:

```
Unable to find function gs:Heatmap
```

1. İndir (~1.8 MB):
   <https://sourceforge.net/projects/geoserver/files/GeoServer/2.28.5/extensions/geoserver-2.28.5-wps-plugin.zip/download>
2. ZIP'in içindeki **jar dosyalarını** şu klasöre kopyala:
   `C:\geoserver\webapps\geoserver\WEB-INF\lib`
3. GeoServer'ı yeniden başlat.

**Doğrulama** — bu adres bir hata raporu değil, gerçek bir yetenek listesi
döndürmeli ve içinde `gs:Heatmap` geçmeli:

```bash
curl -u admin:geoserver "http://localhost:8080/geoserver/ows?service=WPS&version=1.0.0&request=GetCapabilities"
```

---

## 3) GeoServer'ı başlat

Proje kökündeki hazır betiği kullan — Java'yı bulur, `JAVA_HOME`/`GEOSERVER_HOME`
değişkenlerini ayarlar ve sunucuyu ayrı bir pencerede başlatır:

```bash
powershell -ExecutionPolicy Bypass -File geoserver\gs-baslat.ps1
```

Açılan pencerede loglar akar. Son satırlarda şunu gördüğünde hazırdır:

```
INFO [geoserver.GeoServer] - GeoServer configuration lock is enabled
... Started ServerConnector@... {HTTP/1.1}{0.0.0.0:8080}
```

İlk açılış 30–60 saniye sürebilir. **Bu pencereyi kapatma** — kapatırsan
GeoServer durur (aynı `baslat.bat`'taki backend/frontend pencereleri gibi).

**Doğrulama:** Tarayıcıda <http://localhost:8080/geoserver> açılmalı.
Giriş: **admin / geoserver** (varsayılan).

---

## 4) Workspace + Store + Layer'ları oluştur

İki yol var. **A yolu önerilir** — tekrarlanabilir ve depoda belgeli.

### A) Otomatik (REST API ile)

GeoServer'ın kendi REST API'sini kullanarak workspace, PostGIS store, **dört**
katmanı ve **iki ısı haritası stilini** tek komutta oluşturur. Betik *idempotent*'tir: zaten
varsa atlar, tekrar tekrar çalıştırılabilir.

> **ÖN KOŞUL — önce backend'i bir kez çalıştırın.** Katmanlar veritabanındaki
> tablolara bakıyor; tablolar da EF Core migration'larıyla oluşuyor. Özellikle
> Ödev 13'ten sonra `vw_poi` görünümü `poi.mesai_plani` kolonunu istiyor. Kolon
> yokken katman oluşturulamaz ve betik "Katman olusturulamadi: vw_poi" diyerek
> durur.

```bash
powershell -ExecutionPolicy Bypass -File geoserver\gs-yapilandir.ps1
```

Beklenen çıktı:

```
[+] Workspace olusturuldu: staj
[+] PostGIS store olusturuldu: staj_db
[+] SQL View katmani olusturuldu: staj:vw_point  (tbl_point WHERE is_deleted = false)
[+] SQL View katmani olusturuldu: staj:vw_line   (tbl_line WHERE is_deleted = false)
[+] SQL View katmani olusturuldu: staj:vw_polygon (tbl_polygon WHERE is_deleted = false)
[+] SQL View katmani olusturuldu: staj:vw_poi     (poi + kategori agaci + users)
[+] Stil olusturuldu: staj:isi_haritasi
[+] Stil olusturuldu: staj:isi_deger
[+] Stiller katmana baglandi: staj:vw_point -> isi_haritasi, isi_deger
[OK] GeoServer yapilandirmasi tamam.
```

**Ödev 13 ile gelen katman ve stiller ne işe yarıyor?**

`staj:vw_poi` katmanı `poi` tablosunu kategori ağacıyla ve `users` tablosuyla
birleştiren bir SQL View'dır. Diğer üç katmanın aksine tek tabloya bakmıyor,
çünkü SLD stillerinin "bu nokta hangi kategoride?" sorusunu sorabilmesi için
kategori **adının** katmanda bir kolon olarak bulunması gerekiyor — yabancı
anahtar yetmez, SLD JOIN yapamaz. Kök kategori (`kok_kategori`) özyinelemeli bir
sorguyla bulunuyor ki üç seviyeli bir ağaçta da doğru kök çıksın.

**POI stilleri bu betikte DEĞİL.** Kategori tablosundan üretilip backend
tarafından GeoServer'a yazılıyorlar — kategori başına bir SLD (`poi_kat_13`) ve
bir de yedek (`poi_diger`). Uygulama ilk açıldığında kendiliğinden oluşuyorlar;
yönetim panelindeki **"Harita stillerini yenile"** düğmesi de aynı işi yapıyor.

Betiği çalıştırdıktan sonra backend'i bir kez açın; stiller o sırada yazılır.
Uygulama katmanı stil sayısı kadar isteyip her kopyaya bir stil vererek hepsini
tek resimde birleştiriyor:

```
LAYERS=staj:vw_poi,staj:vw_poi,...&STYLES=poi_kat_1,poi_kat_2,...
```

### B) Elle (arayüzden) — A yolu çalışmazsa

<http://localhost:8080/geoserver> → admin/geoserver ile giriş.

**4.1 Workspace**
- Sol menü → **Data → Workspaces** → *Add new workspace*
- Name: `staj`
- Namespace URI: `http://stajproject.local/staj`
- **Default Workspace** kutusunu işaretle → *Save*

**4.2 Store (PostGIS bağlantısı)**
- Sol menü → **Data → Stores** → *Add new Store* → **PostGIS - PostGIS Database**
- Workspace: `staj`
- Data Source Name: `staj_db`
- host: `localhost` · port: `5432` · database: `staj_db`
- schema: `public` · user: `stajyer` · passwd: `stajyer123`
- **Expose primary keys** kutusunu işaretle *(önemli — bu olmazsa `id`
  kolonu WFS cevabında görünmez, backend kaydı tanıyamaz)*
- *Save*

**4.3 Layer'lar — "SQL ile Oluştur" (Ödev 9 / Madde 1)**
- **Data → Layers → Add a new layer** → store: `staj:staj_db`
- Listenin üstündeki **"Configure new SQL view..."** bağlantısına tıkla
  *(tabloyu doğrudan yayınlama, ödev SQL View istiyor)*
- View Name: `vw_point`
- SQL statement:

  ```sql
  SELECT id, name, description, image_url, color,
         inserted_date, modified_date, inserted_user_id, is_active, geom
  FROM   tbl_point
  WHERE  is_deleted = false
  ```

- **Attributes** bölümünde *Refresh* → `id` satırında **Identifier** kutusunu işaretle
  *(bu, keyColumn'dur; olmazsa kayıtların kimliği kaybolur)*
- **Geometry** satırında: Type `Point`, SRID `4326`
- *Save* → açılan katman sayfasında:
  - Declared SRS: `EPSG:4326`, SRS handling: `Force declared`
  - **Bounding Boxes**: *Compute from data* → *Compute from native bounds*
    *(atlanırsa katmanın sınırları boş kalır ve WMS boş resim döner)*
  - *Save*
- Aynısını `vw_line` (`tbl_line`, `LineString`) ve `vw_polygon`
  (`tbl_polygon`, `Polygon`) için tekrarla.

**4.4 Isı haritası stili**
- **Data → Styles** → *Add a new style*
- Name: `isi_haritasi`, Workspace: `staj`, Format: `SLD`
- *Browse...* ile `geoserver/isi-haritasi.sld` dosyasını yükle → *Validate* → *Apply*
- **Data → Layers → staj:vw_point → Publishing** sekmesi → *Additional Styles*
  listesinden `isi_haritasi`'nı seç → *Save*

---

## 5) Doğrulama — gerçekten çalışıyor mu?

Hepsini yap; her biri farklı bir şeyi kanıtlar. (POI katmanı ve stilleri için
5.5 ve 5.6'ya bak — Ödev 13.)

**5.1 Katman listesi (WFS ayakta mı?)**

```bash
curl -u admin:geoserver "http://localhost:8080/geoserver/staj/wfs?service=WFS&version=1.0.0&request=GetCapabilities" -o -
```

Çıktının içinde `staj:tbl_point`, `staj:tbl_line`, `staj:tbl_polygon` geçmeli.

**5.2 Gerçek veri geliyor mu? (WFS GetFeature)**

Tarayıcıda aç:

```
http://localhost:8080/geoserver/staj/wfs?service=WFS&version=1.0.0&request=GetFeature&typeName=staj:tbl_point&outputFormat=application/json&maxFeatures=3
```

GeoJSON dönmeli ve `"features"` dizisinde `name`, `color`, `inserted_user_id`,
`geometry` alanları görünmeli. **Boş dönerse** `tbl_point` tablosunda kayıt
yoktur — uygulamaya girip birkaç nokta çiz, sonra tekrar dene.

> **Koordinat sırası:** `coordinates` dizisi Türkiye için `[32.xx, 39.xx]`
> olmalı — yani **önce boylam, sonra enlem**. GeoJSON standardı (RFC 7946) bunu
> şart koştuğu için çıktı tarafında sorun çıkmaz.
>
> Ama **girdi tarafı farklı davranıyor** — bkz. aşağıdaki 5.4.
>
> `id` alanının `properties` içinde göründüğünü de doğrula. Yoksa store
> ayarlarında **Expose primary keys** işaretlenmemiştir.

**5.3 Resim geliyor mu? (WMS)**

GeoServer arayüzünde **Data → Layer Preview** → `staj:tbl_point` satırında
**OpenLayers** bağlantısına tıkla. Noktaların haritada göründüğünü doğrula.

**5.4 Filtredeki eksen sırası — kurulumda çıkan gerçek tuzak**

WFS 2.0.0 (ve 1.1.0), **CQL filtresine yazılan** geometriyi EPSG:4326'nın
*resmî* eksen sırasıyla, yani **(enlem, boylam)** olarak okuyor. Bizim
WKT'lerimiz ise her yerde (boylam, enlem) sırasında. Sonuç: sorgu **hata
vermiyor**, sessizce başka bir yeri sorup "0 sonuç" dönüyor.

Kendin gör — aynı kutu, aynı koordinatlar, iki farklı WFS sürümü:

```bash
curl -s -u admin:geoserver -G "http://localhost:8080/geoserver/staj/wfs" --data-urlencode "service=WFS" --data-urlencode "version=2.0.0" --data-urlencode "request=GetFeature" --data-urlencode "typeNames=staj:tbl_point" --data-urlencode "outputFormat=application/json" --data-urlencode "count=0" --data-urlencode "CQL_FILTER=INTERSECTS(geom, POLYGON ((32.6 39.8, 33.1 39.8, 33.1 40.1, 32.6 40.1, 32.6 39.8)))"
```

`numberMatched: 0` döner. Sürümü **1.0.0** yapınca (parametre adı da tekilleşir:
`typeNames` → `typeName`) doğru sonuç gelir:

```bash
curl -s -u admin:geoserver -G "http://localhost:8080/geoserver/staj/wfs" --data-urlencode "service=WFS" --data-urlencode "version=1.0.0" --data-urlencode "request=GetFeature" --data-urlencode "typeName=staj:tbl_point" --data-urlencode "outputFormat=application/json" --data-urlencode "CQL_FILTER=INTERSECTS(geom, POLYGON ((32.6 39.8, 33.1 39.8, 33.1 40.1, 32.6 40.1, 32.6 39.8)))"
```

Backend bu yüzden WFS **1.0.0** kullanıyor (`GeoServerClient.OzellikGetirAsync`).

> Ara bir çözüm olarak 2.0.0'da kalıp geometriyi EWKT yazımıyla
> (`SRID=4326;POLYGON(...)`) göndermeyi de denedik — çalışmıyor: CQL_FILTER'da
> **noktalı virgül "filtre listesi" ayıracıdır**, önek filtreyi ikiye bölüyor ve
> üç koşullu bir süzgeçte GeoServer *"Could not parse CQL filter list"* diyor.

---

**5.5 POI katmanı kategori kolonlarını yayınlıyor mu? (Ödev 13)**

```bash
curl -u admin:geoserver "http://localhost:8080/geoserver/staj/wfs?service=WFS&version=1.0.0&request=GetFeature&typeName=staj:vw_poi&outputFormat=application/json&maxFeatures=1"
```

`properties` içinde şunlar görünmeli:

| Kolon | Ne için |
|---|---|
| `isim` | SLD'deki etiket (`TextSymbolizer`) bunu yazıyor |
| `kategori_adi` | bilgi amaçlı — "Restoran" |
| `kok_kategori` | **kritik** — bütün SLD süzgeçleri buna bakıyor |
| `mesai_saatleri` | mesainin okunur özeti |
| `mesai_plani` | gün gün plan, JSON metni olarak |
| `kullanici_adi` | admin listesindeki "ekleyen" sütunu |

`kok_kategori` yoksa hiçbir stil eşleşmez ve **harita boş görünür** — hata
mesajı da çıkmaz, en sinsi durum budur.

**5.6 Stiller yüklendi mi ve doğru çiziyor mu? (Ödev 13)**

Yüklü stillerin listesi:

```bash
curl -u admin:geoserver "http://localhost:8080/geoserver/rest/workspaces/staj/styles.json"
```

Kategori başına bir `poi_kat_*` ve bir `poi_diger` görünmeli. **Görünmüyorsa
backend'i bir kez açın:** stiller uygulama açılışında yazılıyor, bu betikte değil.

Uygulamanın hangi stilleri istediğini görmek için:

```bash
curl http://localhost:5000/api/poi/stiller -H "Authorization: Bearer TOKEN"
```

Tek bir stili tarayıcıda önizle — yalnızca o kategorideki POI'ler çıkmalı:

```
http://localhost:8080/geoserver/staj/wms?service=WMS&version=1.1.1&request=GetMap&layers=staj:vw_poi&styles=poi_kat_10&bbox=32.6,39.8,33.1,40.1&width=768&height=460&srs=EPSG:4326&format=image/png&transparent=true
```

**Zoom'a bağlı etiket** için aynı adreste `bbox`'ı daralt: geniş kutuda yalnızca
simgeler, dar kutuda (ör. `32.83,39.89,32.88,39.93`) simgelerin üstünde POI
adları görünmeli. Eşik `<MaxScaleDenominator>75000</MaxScaleDenominator>`, yani
z ≈ 13 ve sonrası.

> **Bu bir zamanlar gerçek bir tuzaktı.** İlk uygulamada SLD süzgeçleri metin
> karşılaştırmasıydı (`<ogc:Literal>Sağlık</ogc:Literal>`) ve dosya UTF-8 olarak
> yüklenmezse filtre hiçbir satırla eşleşmiyordu: stil "yüklendi" diyor, hata
> çıkmıyor, ama bütün POI'ler yedek stille çiziliyordu. Üretilen stiller
> `kategori_id = 13` diye **sayıyla** süzüyor; sayının kodlaması olmadığı için
> tuzak ortadan kalktı. (Isı haritası SLD'leri hâlâ elle yazılı; betik onları
> UTF-8 bayt dizisi olarak gönderiyor.)

---

## 6) Sorun giderme

| Belirti | Sebep / Çözüm |
|---|---|
| `gs-baslat.ps1` "Java bulunamadi" diyor | 1. adımı yap; sonra PowerShell'i kapatıp yeniden aç. |
| Tarayıcı `localhost:8080`'e bağlanamıyor | GeoServer penceresi kapanmış olabilir. Logda `Address already in use` yazıyorsa 8080'i başka bir program tutuyor demektir. |
| Store kaydederken "Connection refused" | PostgreSQL servisi kapalı. `Get-Service postgresql*` ile kontrol et. |
| Store kaydederken "password authentication failed" | Kullanıcı/şifre `stajyer` / `stajyer123` olmalı (bkz. `backend/db/setup.sql`). |
| Layer listesi boş geliyor | Tablolar henüz oluşmamış. Backend'i bir kez çalıştır — EF Core migration'ları tabloları kurar. |
| WFS cevabında `id` alanı yok | Store ayarlarında **Expose primary keys** işaretlenmemiş. Store'u düzenle, işaretle, kaydet. |
| Backend `503 GeoServer'a ulaşılamıyor` diyor | GeoServer penceresi kapalı. `gs-baslat.ps1` ile tekrar başlat. |

---

## 7) Bundan sonra

GeoServer ayaktayken uygulamayı normal şekilde başlat:

```bash
baslat.bat
```

`baslat.bat` artık GeoServer'ın ayakta olup olmadığını da kontrol ediyor;
kapalıysa uyarı basıyor.

Mimarinin nasıl değiştiği (backend artık veriyi nereden çekiyor)
`README.md` → **"GeoServer Entegrasyonu"** bölümünde anlatılıyor.
