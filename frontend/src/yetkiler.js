// ============================================================================
//  Yetki adları — backend'deki Business/Auth/Yetkiler.cs ile BİREBİR aynı olmalı.
//
//  Neden burada da tanımlı? Arayüzün "bu düğmeyi göstereyim mi?" sorusuna cevap
//  verebilmesi için yetkinin adını bilmesi gerekiyor. Metni her kullanım yerine
//  elle yazmak yerine tek dosyada topluyoruz: yazım hatası olursa tek yerde olur.
//
//  DİKKAT: Bu dosya yalnızca GÖRÜNÜRLÜK içindir. Asıl kontrol sunucuda,
//  [YetkiGerekli] / [EklemeYetkisiGerekli] özniteliklerinde yapılır. Buradaki
//  bir düğmeyi tarayıcıdan zorla açmak işe yaramaz; sunucu yine 403 döner.
// ============================================================================

export const YETKILER = {
  noktaEkleme: 'Point Ekleme',
  cizgiEkleme: 'Line Ekleme',
  poligonEkleme: 'Polygon Ekleme',
  kayitGuncelleme: 'Kayıt Güncelleme',
  kayitSilme: 'Kayıt Silme',
  analizCalistirma: 'Analiz Çalıştırma',
  kullaniciYonetimi: 'Kullanıcı Yönetimi',
  rolYonetimi: 'Rol Yönetimi',
  cografiYetkiTanimlama: 'Coğrafi Yetki Tanımlama',
}

/**
 * Çizim tipi → o tipi eklemek için gereken yetki.
 * Backend'de aynı eşleme PointsController/LinesController/PolygonsController
 * içindeki EklemeYetkisi özelliğinde duruyor.
 */
export const EKLEME_YETKISI = {
  Point: YETKILER.noktaEkleme,
  LineString: YETKILER.cizgiEkleme,
  Polygon: YETKILER.poligonEkleme,
}
