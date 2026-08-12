# Staj Projesi — .NET 8 Web API + PostgreSQL/PostGIS + React + JWT + OpenLayers

Katmanlı mimariye sahip bir Web API, PostGIS destekli PostgreSQL veritabanı ve React (Vite) frontend içerir.
İkinci ödevle eklenenler: **JWT ile login**, korumalı endpoint'ler, **OpenLayers** ile Türkiye'ye zoomlu harita ekranı,
token süresi dolunca otomatik çıkış ve responsive tasarım.

## Giriş Bilgileri (demo kullanıcı)

| Kullanıcı adı | Şifre     |
|---------------|-----------|
| `admin`       | `staj123` |

Uygulama ilk açılışta bu kullanıcıyı otomatik oluşturur (şifre hash'lenerek `users` tablosuna yazılır).

## Kimlik Doğrulama Akışı

1. `POST /api/auth/login` → kullanıcı adı + şifre gönderilir.
2. Doğruysa **10 dakika geçerli** bir JWT + son geçerlilik zamanı döner.
3. Frontend token'ı saklar, korumalı isteklere `Authorization: Bearer <token>` başlığı ekler.
4. `LocationsController` `[Authorize]` ile korunur — token yoksa veya süresi dolduysa **401** döner.
5. Süre dolduğu an frontend otomatik çıkış yapar ve login ekranına yönlendirir
   (hem zamanlayıcıyla hem de 401 cevabı yakalanarak — çifte güvence).

## Harita Ekranı

- OpenLayers + OpenStreetMap altlığı, açılışta Türkiye'ye zoomlu (`center: 35.24, 39.0 — zoom 6.4`).
- Veritabanındaki kayıtlı konumlar haritada nokta olarak gösterilir.
- Üst barda kalan oturum süresi sayacı ve çıkış düğmesi vardır; tasarım responsive'dir.

## Proje Yapısı

```
StajProject/
├── backend/
│   ├── StajProject.sln
│   ├── StajProject.API/          → Sunum katmanı (Controllers, Program.cs, Swagger, CORS)
│   ├── StajProject.Business/     → İş katmanı (Services, DTOs)
│   ├── StajProject.DataAccess/   → Veri erişim katmanı (DbContext, Repositories, Migrations)
│   ├── StajProject.Entities/     → Entity tanımları (Location, PostGIS Point)
│   └── StajProject.Tests/        → Birim testleri (xUnit, sahte repository ile)
└── frontend/                     → React (Vite) — konum ekleme/listeleme arayüzü
```

Bağımlılık yönü: `API → Business → DataAccess → Entities`

## Gereksinimler

- .NET 8 SDK
- PostgreSQL 14+ ve PostGIS eklentisi
- Node.js 18+

## Kurulum

### 1. Veritabanı

```sql
CREATE USER stajyer WITH PASSWORD 'stajyer123' SUPERUSER;
CREATE DATABASE staj_db OWNER stajyer;
\c staj_db
CREATE EXTENSION IF NOT EXISTS postgis;
```

Bağlantı bilgisi `backend/StajProject.API/appsettings.json` içindedir; kendi ortamına göre düzenle.

### 2. Backend

```bash
cd backend
dotnet restore
dotnet run --project StajProject.API
```

API `http://localhost:5000` üzerinde çalışır. Swagger: `http://localhost:5000/swagger`

> Veritabanı şeması **EF Core migration** ile yönetilir (`StajProject.DataAccess/Migrations/`).
> Uygulama açılışta `Database.Migrate()` çağırarak bekleyen migration'ları otomatik uygular —
> yani tabloyu elle oluşturmana gerek yok. Yeni bir şema değişikliği için:
> `dotnet ef migrations add MigrationAdi -p StajProject.DataAccess -s StajProject.API`

### Testler

```bash
cd backend
dotnet test
```

`StajProject.Tests` projesi, Business katmanını (LocationService) veritabanına ihtiyaç duymadan
bellekte çalışan sahte bir repository ile test eder (5 birim testi: koordinat→Point dönüşümü,
listeleme, olmayan id, silme senaryoları).

### 3. Frontend

```bash
cd frontend
npm install
npm run dev
```

Arayüz `http://localhost:5173` üzerinde açılır; `/api` istekleri Vite proxy ile backend'e yönlenir.

## POST Testi

```bash
curl -X POST http://localhost:5000/api/locations \
  -H "Content-Type: application/json" \
  -d '{"name":"Anıtkabir","description":"Ankara","longitude":32.8597,"latitude":39.9334}'
```

Veritabanında kontrol:

```sql
SELECT id, name, ST_AsText(geom) FROM locations;
```

## API Uçları

| Metot  | Yol                  | Açıklama            |
|--------|----------------------|---------------------|
| GET    | /api/locations       | Tüm konumlar        |
| GET    | /api/locations/{id}  | Tek konum           |
| POST   | /api/locations       | Yeni konum ekle     |
| DELETE | /api/locations/{id}  | Konum sil           |
