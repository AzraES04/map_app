# Staj Projesi — .NET 8 Web API + PostgreSQL/PostGIS + React + JWT + OpenLayers

Katmanlı mimariye sahip bir Web API, PostGIS destekli PostgreSQL veritabanı ve React (Vite) frontend içerir.

**2. ödevle eklenenler:** JWT ile login, korumalı endpoint'ler, OpenLayers ile Türkiye'ye zoomlu harita,
token süresi dolunca otomatik çıkış, responsive tasarım.

**3. ödevle eklenenler:**
1. `users` tablosuna sistem durum takibi kolonları (`is_deleted`, `is_active`, `modified_date`)
2. OpenLayers `Draw` etkileşimleriyle **Nokta / Çizgi / Poligon** çizimi — her tip kendi tablosuna
3. Geometrilerin **WKT** formatında taşınması ve **EPSG:3857 ↔ EPSG:4326** projeksiyon dönüşümü

---

## Giriş Bilgileri (demo kullanıcı)

| Kullanıcı adı | Şifre     |
|---------------|-----------|
| `admin`       | `staj123` |

Uygulama ilk açılışta bu kullanıcıyı otomatik oluşturur (şifre hash'lenerek `users` tablosuna yazılır).

---

## 1) Sistem Durum Takibi (Soft Delete & Audit)

`users` tablosuna ve üç geometri tablosuna aynı desen uygulandı:

| Kolon | Tip | Varsayılan | Anlamı |
|---|---|---|---|
| `is_deleted` | `boolean` | `false` | Soft delete bayrağı. Kayıt fiziksel olarak silinmez |
| `is_active` | `boolean` | `true` | Kayıt kullanılabilir mi (pasif ≠ silinmiş) |
| `modified_date` | `timestamptz` | `NULL` | Son güncelleme damgası (UTC). Hiç güncellenmediyse `NULL` |

**Nasıl çalışıyor:**

- **Otomatik damga:** `AppDbContext.SaveChanges(...)` override edilerek, `ChangeTracker`'da
  `Modified` durumundaki her `IAuditableEntity` için `ModifiedDate = DateTime.UtcNow` atanır.
  Servis katmanında tek satır kod yok.
- **Global query filter:** `HasQueryFilter(e => !e.IsDeleted)` — silinmiş kayıtlar hiçbir sorguda
  görünmez. Görmek gerekirse `.IgnoreQueryFilters()` denmelidir (seed bloğunda böyle yapılıyor).
- **Partial unique index:** `IX_users_username` yalnızca `WHERE is_deleted = false` satırlarını
  kapsar; böylece silinen bir kullanıcı adı tekrar kullanılabilir.
- **Giriş engeli:** `AuthService.LoginAsync`, `user.IsDeleted || !user.IsActive` durumunda
  şifre doğru olsa bile `null` döner. Dışarıya ayrı bir mesaj verilmez (user enumeration önlemi).

> **Neden UTC?** Kolon tipi `timestamp with time zone` ve Npgsql, `Kind`'ı `Utc` olmayan değeri
> reddediyor. Ayrıca UTC saat dilimi/yaz saati değişimlerinden bağımsızdır.

---

## 2) Harita ve Çizim İşlemleri

Sağ paneldeki araç düğmeleriyle üç çizim tipi aktif edilir; çizim bittiği anda kayıt formu açılır
ve veri **kendi tablosuna** yazılır:

| Araç | OpenLayers tipi | Tablo | Endpoint |
|---|---|---|---|
| 📍 Nokta | `Point` | `tbl_point` | `/api/points` |
| 📏 Çizgi | `LineString` | `tbl_line` | `/api/lines` |
| ⬟ Poligon | `Polygon` | `tbl_polygon` | `/api/polygons` |

> OGC standardında çizgi tipinin adı `Line` değil **`LineString`**'tir; tablo adı ise `tbl_line`.

**Sağ panel bölümleri:** ① Çizim araçları (tek aktif araç, toggle) → ② Çizim bitince açılan kayıt
formu (ad, açıklama, salt okunur WKT önizlemesi) → ③ Katman aç/kapa → ④ Sekmeli kayıt listesi
(hover'da haritada vurgu, tıklamada o geometriye zoom, çöp kutusuyla soft delete).

**Klavye:** `Esc` çizimi iptal eder, `Backspace` son noktayı siler.
(Kullanıcı forma yazı yazarken bu kısayollar devre dışıdır.)

---

## 3) WKT ve Projeksiyon Yönetimi

### WKT (Well-Known Text)

OGC'nin tanımladığı, geometriyi insan okuyabilir metin olarak ifade eden standart:

```
POINT (32.8597 39.9334)
LINESTRING (32.85 39.93, 32.86 39.94, 32.87 39.92)
POLYGON ((32.85 39.93, 32.87 39.93, 32.87 39.95, 32.85 39.95, 32.85 39.93))
```

Kurallar:
- Koordinat sırası **`X Y` = boylam enlem** (Google Maps'in `enlem, boylam` sırasının tersi)
- Poligonda **çift parantez**: dış parantez halkalar listesi, ikinci bir iç parantez "delik" olur
- Poligon **kapalı** olmalı: ilk koordinat = son koordinat
- WKT'nin içinde **SRID bilgisi yoktur** (o EWKT'nin işi: `SRID=4326;POINT(...)`)

Akrabaları: **WKB** (aynı verinin ikili hâli), **EWKT** (SRID'li), **GeoJSON** (JSON tabanlı).

**Neden WKT?** Nokta iki sayıyla taşınabilir ama çizgi/poligon değişken sayıda koordinat içerir.
WKT üç tipi de tek bir `string` alanında standart biçimde taşır — DTO üç tip için de aynı kalır.

### Projeksiyon dönüşümü

| | EPSG:4326 (WGS84) | EPSG:3857 (Web Mercator) |
|---|---|---|
| Birim | Derece | Metre |
| Ankara | `32.8597, 39.9334` | `3657925, 4856269` |
| Kullanım | **Veritabanı, WKT** | **Harita (OSM altlığı)** |

Dönüşüm mantığının tamamı `frontend/src/geo.js` içinde toplanmıştır:

```js
// Kaydetme yönü: harita (3857) → veritabanı (4326)
export function geometryToWkt(geometry) {
  const clone = geometry.clone()        // ⚠️ transform() geometriyi YERİNDE değiştirir!
  clone.transform(MAP_PROJECTION, DATA_PROJECTION)
  return wktFormat.writeGeometry(clone, { decimals: 6 })
}

// Okuma yönü: veritabanı (4326) → harita (3857)
export function wktToFeature(wkt) {
  return wktFormat.readFeature(wkt, {
    dataProjection: DATA_PROJECTION,
    featureProjection: MAP_PROJECTION,
  })
}
```

**En kritik iki nokta:**

1. **`clone()` olmadan `transform()` çağırmak** haritadaki feature'ı da bozar ve çizim
   Gine Körfezi'ne (0, 0 civarına) sıçrar. Hata mesajı alınmaz.
2. **Backend'de `geometry.SRID = 4326` elle atanmalıdır** (`WktConverter.Read`). WKT metninde
   SRID olmadığı için `WKTReader` SRID'si 0 olan geometri üretir; PostGIS
   `geometry(Point, 4326)` kolonuna bunu kabul etmez.

`decimals: 6` → yaklaşık 11 cm hassasiyet.

---

## Proje Yapısı

```
StajProject/
├── backend/
│   ├── StajProject.API/          → Controllers, Program.cs, Swagger, CORS, DI
│   ├── StajProject.Business/     → Services, DTOs, Geo/WktConverter
│   ├── StajProject.DataAccess/   → DbContext, Repositories, Migrations
│   ├── StajProject.Entities/     → Entity tanımları (User, Location, geometri tipleri)
│   ├── StajProject.Tests/        → 29 birim testi (xUnit)
│   └── db/setup.sql              → Rol + veritabanı + PostGIS kurulum betiği
└── frontend/
    └── src/
        ├── geo.js                → Projeksiyon + WKT dönüşümleri (tek merkez)
        ├── api.js                → Geometri API çağrıları
        ├── auth.js               → Token yönetimi, otomatik çıkış
        └── pages/MapPage.jsx     → Harita + çizim araçları + sağ panel
```

Bağımlılık yönü: `API → Business → DataAccess → Entities`

**Generic mimari:** Üç geometri tipi için üç ayrı repository/service yazmak yerine
`GeometryRepository<TEntity>` ve `GeometryService<TEntity, TGeometry>` generic sınıfları
DI'da üç kez, farklı tip argümanlarıyla kaydedilir. Controller'lar
`GeometryControllerBase<TEntity>`'den türeyen 3 satırlık sınıflardır.

---

## Gereksinimler

- .NET 8 SDK
- PostgreSQL 17 + **PostGIS 3.5** eklentisi
- Node.js 18+

## Kurulum

### 1. Veritabanı

PostgreSQL kurulumunda **Stack Builder → Spatial Extensions → PostGIS Bundle** seçilmelidir.
Ardından:

```bash
psql -U postgres -f backend/db/setup.sql
```

Betik `stajyer` rolünü, `staj_db` veritabanını oluşturur ve PostGIS eklentisini etkinleştirir.
Bağlantı bilgisi `backend/StajProject.API/appsettings.json` içindedir.

> PostGIS eklentisi **veritabanı bazındadır**: sunucuya kurmak yetmez, her veritabanında
> ayrıca `CREATE EXTENSION postgis` çalıştırılmalıdır.

### 2. Backend

```bash
dotnet run --project backend/StajProject.API
```

API `http://localhost:5000` üzerinde çalışır. Swagger: `http://localhost:5000/swagger`

Şema **EF Core migration** ile yönetilir; uygulama açılışta `Database.Migrate()` ile bekleyen
migration'ları otomatik uygular. Yeni değişiklik için:

```bash
dotnet ef migrations add MigrationAdi -p StajProject.DataAccess -s StajProject.API
```

### 3. Frontend

```bash
npm install --prefix frontend && npm run dev --prefix frontend
```

Arayüz `http://localhost:5173` üzerinde açılır; `/api` istekleri Vite proxy ile backend'e yönlenir.

### Testler

```bash
dotnet test backend/StajProject.sln
```

29 test: durum kolonlarının davranışı (EF InMemory ile gerçek `DbContext` üzerinde),
WKT çözümleme/tip doğrulama/SRID yönetimi, geometri servisi ve `LocationService`.

---

## API Uçları

| Metot | Yol | Açıklama |
|--------|----------------------|---------------------|
| POST | `/api/auth/login` | JWT alır (10 dk geçerli) |
| GET | `/api/points` · `/api/lines` · `/api/polygons` | Kayıtları WKT olarak listeler |
| GET | `/api/points/{id}` | Tek kayıt |
| POST | `/api/points` | `{ name, description, wkt }` |
| PUT | `/api/points/{id}` | Ad/açıklama (+ opsiyonel geometri) günceller |
| DELETE | `/api/points/{id}` | Soft delete |
| GET/POST/DELETE | `/api/locations` | 2. ödevden kalan tablo (geriye dönük uyumluluk) |

Geometri uçlarının tamamı `[Authorize]` ile korunur — token yoksa veya süresi dolduysa **401**.
Yanlış geometri tipi gönderilirse **400** ve açıklayıcı mesaj döner.

## Veritabanı Doğrulama

```sql
SELECT f_table_name, f_geometry_column, srid, type FROM geometry_columns;

SELECT id, name, ST_SRID(geom), ST_AsText(geom) FROM tbl_point;
```

## Notlar

- Çizim tipi renkleri iki yerde tanımlıdır ve senkron tutulmalıdır:
  `frontend/src/geo.js` (`DRAW_TYPES[*].color`) ve `frontend/src/index.css` (`--nokta`, `--cizgi`, `--poligon`).
- `describeGeometry` içindeki uzunluk/alan değerleri Mercator düzleminde hesaplanır;
  Türkiye enlemlerinde gerçek değerden yaklaşık %30 sapar. Yalnızca bilgi amaçlıdır.
