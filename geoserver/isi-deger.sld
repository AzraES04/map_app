<?xml version="1.0" encoding="UTF-8"?>
<!--
  ============================================================================
   Isı haritası — DEĞER katmanı (Ödev 11)
   Katman: staj:vw_point   ·   Stil adı: isi_deger

   NE İŞE YARIYOR?
   isi-haritasi.sld ile AYNI hesabı yapar (gs:Heatmap), ama sonucu renk
   rampasıyla değil, DÜZ GRİ TONLAMAYLA boyar:

       yoğunluk 0.0  →  #000000  (gri 0)
       yoğunluk 1.0  →  #ffffff  (gri 255)

   Böylece arayüz, resmin herhangi bir pikselinden gri seviyesini okuyup
   değeri geri hesaplayabiliyor:  deger = gri / 255

   NEDEN BÖYLE BİR ŞEY GEREKTİ?
   Kullanıcı haritada KAYIT OLMAYAN bir noktaya tıklayınca da yoğunluk
   değerini görmek istiyor. Denenen ve olmayan yol: WMS GetFeatureInfo.
   gs:Heatmap gibi bir "rendering transformation" için GeoServer, değeri
   değil grid'in tanımını döndürüyor:

       grid = DisposableGridCoverage["Process Results", ...]

   Renkli resimden geri çözmek de mümkündü ama rampa ara renkleri
   interpolasyonla ürettiği için yaklaşık olurdu. Gri tonlama tek kanal ve
   doğrusal: okunan değer birebir doğru.

   KRİTİK: Bu stil, ekrandaki renkli katmanla AYNI BBOX ve AYNI BOYUTTA
   istenmek zorunda. gs:Heatmap yoğunluğu her istek için ayrı normalleştiriyor
   (en yoğun hücre = 1); farklı bir alan istenirse okunan değer ekranda
   görünen renkle uyuşmaz. Arayüz bu yüzden değer resmini, renkli resmin
   isteğinden türetiyor (bkz. frontend/src/wms.js).

   Aşağıdaki gs:Heatmap parametreleri isi-haritasi.sld ile BİREBİR AYNI
   olmalı — radiusPixels farklı olsaydı iki resim farklı yüzeyler üretirdi.
  ============================================================================
-->
<StyledLayerDescriptor version="1.0.0"
    xsi:schemaLocation="http://www.opengis.net/sld StyledLayerDescriptor.xsd"
    xmlns="http://www.opengis.net/sld"
    xmlns:ogc="http://www.opengis.net/ogc"
    xmlns:xlink="http://www.w3.org/1999/xlink"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <NamedLayer>
    <Name>isi_deger</Name>
    <UserStyle>
      <Title>Isı Haritası — değer okuma</Title>
      <Abstract>Yoğunluğu gri tonlamayla kodlar; arayüz pikselden değeri okur</Abstract>

      <FeatureTypeStyle>
        <Transformation>
          <ogc:Function name="gs:Heatmap">

            <ogc:Function name="parameter">
              <ogc:Literal>data</ogc:Literal>
            </ogc:Function>

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
            <Geometry><ogc:PropertyName>the_geom</ogc:PropertyName></Geometry>

            <!-- TAM OPAK: saydamlık gri değeri bozardı, okunan sayı yanlış olurdu. -->
            <Opacity>1.0</Opacity>

            <ColorMap type="ramp">
              <ColorMapEntry color="#000000" quantity="0.0" opacity="1" label="0"/>
              <ColorMapEntry color="#ffffff" quantity="1.0" opacity="1" label="1"/>
            </ColorMap>
          </RasterSymbolizer>
        </Rule>
      </FeatureTypeStyle>
    </UserStyle>
  </NamedLayer>
</StyledLayerDescriptor>
