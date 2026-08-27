<?xml version="1.0" encoding="UTF-8"?>
<!--
  ============================================================================
   Isı Haritası (Heatmap) stili — Ödev 9 / Madde 2
   Katman: staj:vw_point   ·   Stil adı: isi_haritasi

   NASIL ÇALIŞIYOR?
   Normalde bir SLD "her noktayı şu sembolle çiz" der. Burada araya bir
   RENDERING TRANSFORMATION giriyor: <Transformation> bloğu, çizimden ÖNCE
   vektör veriyi bir RASTER'a (ısı yüzeyine) dönüştürüyor. Yani noktalar
   tek tek çizilmiyor; GeoServer önce yoğunluk yüzeyini hesaplıyor,
   RasterSymbolizer da o yüzeyi renklendiriyor.

   Hesap SUNUCUDA yapılıyor: tarayıcıya sadece hazır PNG geliyor. Aynı işi
   istemcide yapmak tüm noktaları indirmeyi ve her karede yeniden hesaplamayı
   gerektirirdi.

   gs:Heatmap parametreleri:
     data          → dönüştürülecek veri (katmanın kendisi)
     radiusPixels  → her noktanın etki yarıçapı (piksel). Büyük değer daha
                     yumuşak/geniş lekeler üretir. env() ile WMS isteğinden
                     "radius" gönderilerek çalışma anında değiştirilebilir.
     pixelsPerCell → hesap ızgarasının çözünürlüğü. 10 = her 10 pikselde bir
                     hücre; küçültmek keskinleştirir ama yavaşlatır.
     outputBBOX / outputWidth / outputHeight
                   → çıktının hangi alana ve hangi boyutta üretileceği.
                     env(wms_bbox/wms_width/wms_height) ile o anki WMS
                     isteğinden okunuyor; sabit yazılsaydı harita her
                     kaydırıldığında yanlış yer hesaplanırdı.

   ÇIKTI ÖLÇEĞİ: gs:Heatmap yoğunluğu her zaman 0–1 aralığına normalleştirir.
   En yoğun hücre 1, boş alan 0 olur. Aşağıdaki ColorMap bu yüzden 0 ile 1
   arasında tanımlı — lejantta gösterilen değerler de bunlar.
  ============================================================================
-->
<StyledLayerDescriptor version="1.0.0"
    xsi:schemaLocation="http://www.opengis.net/sld StyledLayerDescriptor.xsd"
    xmlns="http://www.opengis.net/sld"
    xmlns:ogc="http://www.opengis.net/ogc"
    xmlns:xlink="http://www.w3.org/1999/xlink"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <NamedLayer>
    <Name>isi_haritasi</Name>
    <UserStyle>
      <Title>Isı Haritası</Title>
      <Abstract>Nokta yoğunluğuna göre üretilen dinamik ısı yüzeyi (gs:Heatmap)</Abstract>

      <FeatureTypeStyle>
        <Transformation>
          <ogc:Function name="gs:Heatmap">

            <ogc:Function name="parameter">
              <ogc:Literal>data</ogc:Literal>
            </ogc:Function>

            <!-- Etki yarıçapı. WMS isteğine &env=radius:40 eklenerek
                 değiştirilebilir; eklenmezse 35 kullanılır. -->
            <ogc:Function name="parameter">
              <ogc:Literal>radiusPixels</ogc:Literal>
              <ogc:Function name="env">
                <ogc:Literal>radius</ogc:Literal>
                <ogc:Literal>35</ogc:Literal>
              </ogc:Function>
            </ogc:Function>

            <ogc:Function name="parameter">
              <ogc:Literal>pixelsPerCell</ogc:Literal>
              <ogc:Literal>10</ogc:Literal>
            </ogc:Function>

            <ogc:Function name="parameter">
              <ogc:Literal>outputBBOX</ogc:Literal>
              <ogc:Function name="env">
                <ogc:Literal>wms_bbox</ogc:Literal>
              </ogc:Function>
            </ogc:Function>

            <ogc:Function name="parameter">
              <ogc:Literal>outputWidth</ogc:Literal>
              <ogc:Function name="env">
                <ogc:Literal>wms_width</ogc:Literal>
              </ogc:Function>
            </ogc:Function>

            <ogc:Function name="parameter">
              <ogc:Literal>outputHeight</ogc:Literal>
              <ogc:Function name="env">
                <ogc:Literal>wms_height</ogc:Literal>
              </ogc:Function>
            </ogc:Function>

          </ogc:Function>
        </Transformation>

        <Rule>
          <RasterSymbolizer>
            <!-- Dönüşümün ürettiği raster katmanının geometri alanı -->
            <Geometry><ogc:PropertyName>the_geom</ogc:PropertyName></Geometry>

            <!-- Altındaki temel harita tamamen kapanmasın -->
            <Opacity>0.75</Opacity>

            <!--
              type="ramp" → değerler arasında renk GEÇİŞİ yapılır (basamak değil).

              RAMPA: kehribar → mercan → erik. Gökkuşağı rampası (mavi-yeşil-
              sarı-kırmızı) bilerek TERK EDİLDİ: sıralı bir büyüklüğü ton
              değiştirerek göstermek hem algısal olarak yanıltıcı (yeşil ile
              sarı arasındaki fark, sarı ile turuncu arasındakinden daha büyük
              görünür) hem de arayüzün geri kalanıyla uyumsuzdu. Tek yönlü
              sıcaklık rampası hem "ısı" kavramına hem uygulamanın kehribar
              vurgu rengine oturuyor.

              En alttaki giriş TAM saydam: veri olmayan yer boyanmıyor.
              (Eskiden 0.01'di; sebebi GeoServer'ın lejantta tam saydam kutuyu
              kırmızı çarpıyla çizmesiydi. Lejant artık arayüzde CSS ile
              çiziliyor, o kısıt kalktı.)

              ⚠ Bu renkler frontend/src/wms.js → ISI_RAMPASI ile AYNI olmalı;
              lejant ve termometre çubuğu oradan besleniyor.

              Etiketlerde Türkçe karakter YOK: GeoServer lejant metnini
              çizerken bozuk gösteriyordu ("yoğun" -> "yoÄŸun").
            -->
            <ColorMap type="ramp">
              <ColorMapEntry color="#ffe3a8" quantity="0.00" opacity="0.00" label="0.00  veri yok"/>
              <ColorMapEntry color="#ffc46b" quantity="0.25" opacity="0.55" label="0.25  seyrek"/>
              <ColorMapEntry color="#f89551" quantity="0.50" opacity="0.72" label="0.50  orta"/>
              <ColorMapEntry color="#e05a45" quantity="0.75" opacity="0.85" label="0.75  yogun"/>
              <ColorMapEntry color="#9c2350" quantity="1.00" opacity="0.94" label="1.00  en yogun"/>
            </ColorMap>
          </RasterSymbolizer>
        </Rule>
      </FeatureTypeStyle>
    </UserStyle>
  </NamedLayer>
</StyledLayerDescriptor>
