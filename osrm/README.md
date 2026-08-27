# OSRM — otomatik rota üretimi (Ödev 17 / Madde 1)

Ödev metni: *"Rota hesaplamaları için OSRM (Open Source Routing Machine) kullanın
(Docker üzerinde localde çalıştırıp HTTP istekleri atmanız tercih edilmektedir)."*

## Neden gerekli?

Ödev 16'da hattın çizgisi durakları **düz çizgilerle** birleştiriyordu. O çizgi
binaların içinden ve nehrin üstünden geçiyordu — "hat" değil "kuş uçuşu"ydu.

OSRM, OpenStreetMap yol ağını kullanarak duraklar arasındaki **gerçek sürüş
güzergahını** hesaplıyor: yollara oturan, tek yönleri ve dönüş kısıtlarını bilen
bir çizgi.

## Kurulum

```bash
powershell -ExecutionPolicy Bypass -File osrm-kur.ps1
```

Sonra:

```bash
docker compose up -d
```

Betik dört adım yapıyor: indir → (isteğe bağlı kırp) → `osrm-extract` →
`osrm-partition` + `osrm-customize`.

### Kaynak ihtiyacı — önce buna bakın

Türkiye'nin tamamı için:

| | Yaklaşık |
|---|---|
| İndirme | 500 MB |
| Geçici + kalıcı disk | ~6 GB |
| `osrm-extract` belleği | 6–8 GB |
| Süre | 5–20 dakika |

`osrm-extract` bellek yetmediğinde `bad_alloc` ile ölür. O durumda haritayı bir
dikdörtgene kırpın:

```bash
powershell -ExecutionPolicy Bypass -File osrm-kur.ps1 -Kutu "31.5,39.0,39.5,42.2"
```

Kutu `batı,güney,doğu,kuzey`. **Bedeli:** kutunun dışındaki duraklar için rota
hesaplanamaz — kutuyu seçerken hatlarınızın tamamını içine aldığından emin olun.

### Neden MLD, neden sürüm sabitlendi?

- **MLD** (`osrm-partition` + `osrm-customize`), alternatif olan CH'ye
  (`osrm-contract`) göre çok daha hızlı hazırlanıyor ve çok daha az bellek
  istiyor. Sorgu hızındaki fark bu ölçekte hissedilmiyor.
  Hazırlıktaki algoritma ile `docker-compose.yml`'deki `--algorithm mld`
  **aynı olmak zorunda**; ayrışırsa `osrm-routed` veriyi reddeder.
- İmaj etiketi `:latest` değil **`v5.25.0`**. OSRM'in veri biçimi sürümler
  arasında değişiyor: yeni bir imaj eski hazırlanmış veriyi
  *"incompatible file version"* diyerek reddeder. Sabit etiket, projenin bir
  sabah kendi kendine bozulmasını engelliyor.
- Port **5001** (OSRM'in varsayılanı 5000): bu projede 5000 backend'in portu.

## Doğrulama

```bash
curl "http://localhost:5001/route/v1/driving/32.8541,39.9208;32.852,39.942?overview=false"
```

`"code":"Ok"` görmelisiniz.

## `veri/` neden git'te yok?

Birkaç GB tutuyor ve **türetilmiş** bir çıktı: kaynağı OpenStreetMap, üretim
tarifi `osrm-kur.ps1`. Depoya koymak her klonda gigabaytlarca veri taşımak ve
OSM güncellendikçe eskiyen bir kopyayı sürümlemek olurdu.

## OSRM kapalıyken ne oluyor?

**Uygulama eksiksiz çalışıyor.** Bu bilinçli bir tasarım kararı:

- Rota hesabı, kullanıcının asıl yaptığı işin (durak ekleme, sıralama)
  **yan ürünü**. OSRM kapalı diye sıralama kaydetmeyi reddetmek, dış bir
  servisin arızasını kullanıcının işini engellemeye çevirmek olurdu.
- Rotası olmayan hat, Ödev 16'daki **düz çizgiyle** çiziliyor — hat yine
  görünüyor, sadece yollara oturmuyor.
- `appsettings.json` → `Osrm:Enabled: false` yapılırsa hiç denenmiyor. Projeyi
  yalnızca haritayı görmek için açan biri bu kurulumu yapmak zorunda değil.

### Peki eskiyen rota?

Durak eklendiğinde, taşındığında, silindiğinde ve **sırası değiştiğinde** rota
kendiliğinden yeniden hesaplanıyor. Ama OSRM o an kapalıysa hesaplanamıyor ve
veritabanında **eski** rota kalıyor.

Bu yüzden `guzergah.rota_imza` kolonu, rotanın hangi durak dizilimi için
hesaplandığını yazıyor. İmza tutmuyorsa arayüz *"rota güncel değil"* diyor ve
çizgiyi solgun gösteriyor. **Ayrışma gizlenmiyor, görünür kılınıyor** — sessizce
yanlış bir hat çizmektense.
