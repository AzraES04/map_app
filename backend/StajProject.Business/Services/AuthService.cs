using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using StajProject.Business.Auth;
using StajProject.Business.DTOs;
using StajProject.Business.Validation;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

namespace StajProject.Business.Services;

public class AuthService :IAuthService       // BOŞLUK 1
{
    private readonly IUserRepository _userRepository;   // BOŞLUK 2
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly JwtSettings _jwtSettings;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IOptions<JwtSettings> jwtOptions)
    {
        _userRepository = userRepository;              // BOŞLUK 3
        _refreshTokenRepository = refreshTokenRepository;
        _jwtSettings = jwtOptions.Value;      // BOŞLUK 4
    }

public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto request)
    {
        // 1) Kullanıcıyı kullanıcı adına göre bul
        var user = await _userRepository.GetByUsernameAsync(request.Username);

        // 2) Kullanıcı bulunamadıysa giriş başarısız
        if (user is null)
        {
            return null;
        }
    
        // 2.5) Hesap durumu uygun mu? (Ödev 3 / Görev 1)
        // Not: Bilerek "hesabınız pasif" gibi ayrı bir mesaj DÖNMÜYORUZ. Öyle yapsaydık
        // saldırgana "bu kullanıcı adı sistemde var" bilgisini doğrulamış olurduk
        // (user enumeration). Dışarıya tek tip "kullanıcı adı veya şifre hatalı" gider.
        if (user.IsDeleted || !user.IsActive)
        {
            return null;
        }

        // 3) Şifreyi doğrula (hash karşılaştırması)
        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        // 4) Şifre yanlışsa giriş başarısız
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        // 4.5) Hesap yönetici onayından geçti mi? (Ödev 10)
        //
        // Bu kontrol ŞİFRE DOĞRULANDIKTAN SONRA yapılıyor — sırası önemli.
        // Önce yapsaydık, şifreyi bilmeyen biri de "onay bekliyor" cevabını
        // alır ve o kullanıcı adının sistemde var olduğunu öğrenirdi
        // (user enumeration). Şimdi bu bilgiyi yalnızca şifreyi zaten bilen,
        // yani hesabın gerçek sahibi görüyor — ve onun bilmeye hakkı var:
        // aksi hâlde "kayıt oldum ama giremiyorum" diye sebepsiz beklerdi.
        if (!user.IsApproved)
        {
            throw new IsKuraliException(
                "Hesabınız henüz yönetici onayından geçmedi. Onaylandığında giriş yapabilirsiniz.");
        }

        // 5) Erişim token'ı + yenileme anahtarı üret (Eksik 5)
        return await OturumUretAsync(user);
    }

    /// <summary>
    /// Yeni hesap açar (Ödev 10 — giriş ekranındaki "Kayıt Ol").
    ///
    /// NEDEN ONAY GEREKİYOR? Kayıt ucu herkese açık. Onay olmasaydı internetten
    /// gelen herkes anında giriş yapıp haritayı ve envanteri görürdü. Onayla
    /// birlikte akış şu oluyor: kullanıcı kaydolur → yönetici panelde "onay
    /// bekliyor" rozetini görür → onaylar ve rol atar → kullanıcı girebilir.
    ///
    /// Kayıt olan kullanıcıya ROL VERİLMİYOR: yetkisiz bir hesap giriş yapsa
    /// bile hiçbir şey ekleyip silemez. Yetki dağıtımı yöneticinin işi (Ödev 6).
    /// </summary>
    public async Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request)
    {
        var kullaniciAdi = request.Username.Trim();

        if (kullaniciAdi.Length == 0)
        {
            throw new IsKuraliException("Kullanıcı adı boş olamaz.");
        }

        // Kullanıcı adı dolu mu? Burada "bu ad alınmış" demek zorundayız —
        // aksi hâlde kullanıcı neden kaydolamadığını anlayamaz. Kayıt
        // ekranında bu bilgi kaçınılmaz; bu yüzden uç hız sınırına tabi.
        if (await _userRepository.GetByUsernameAsync(kullaniciAdi) is not null)
        {
            throw new IsKuraliException($"\"{kullaniciAdi}\" kullanıcı adı zaten alınmış.");
        }

        var yeni = new User
        {
            Username = kullaniciAdi,
            IsActive = true,
            IsApproved = false,   // asıl kural burada
        };
        yeni.PasswordHash = _passwordHasher.HashPassword(yeni, request.Password);

        await _userRepository.AddAsync(yeni);

        return new RegisterResponseDto
        {
            Username = kullaniciAdi,
            Message = "Kaydınız alındı. Yönetici onayından sonra giriş yapabilirsiniz.",
        };
    }

    // ======================================================================
    //  Eksik 5 — YENİLEME ANAHTARI (refresh token)
    // ======================================================================

    /// <summary>
    /// Oturumu yeniler: geçerli bir yenileme anahtarı karşılığında YENİ bir
    /// erişim token'ı ve YENİ bir yenileme anahtarı verir.
    ///
    /// ---- DÖNDÜRME (rotation) ----
    ///
    /// Eski anahtar burada iptal ediliyor. Aynı anahtarı yedi gün boyunca
    /// tekrar tekrar kullandırmak daha kolay olurdu ama o zaman anahtarı bir
    /// kez ele geçiren kişi, süresi dolana kadar sizinle birlikte oturumu
    /// paylaşırdı ve bunu kimse fark etmezdi.
    ///
    /// ---- YENİDEN KULLANIM TESPİTİ ----
    ///
    /// Döndürme sayesinde her anahtar TEK KULLANIMLIK. Bu yüzden iptal
    /// edilmiş bir anahtarın tekrar sunulması normal kullanımda ASLA olmaz;
    /// olduysa aynı değerin iki kopyası dolaşıyor demektir. O anda
    /// kullanıcının bütün açık oturumları kapatılıyor: hırsız da gerçek
    /// kullanıcı da dışarı düşer, gerçek kullanıcı yeniden giriş yapar,
    /// hırsızın elindeki her şey ölür.
    ///
    /// Bu, döndürmeyi "biraz daha iyi" olmaktan çıkarıp hırsızlığı GÖRÜNÜR
    /// kılan asıl mekanizma.
    ///
    /// ---- HESAP DURUMU BURADA DA KONTROL EDİLİYOR ----
    ///
    /// Yenileme, girişin sessiz tekrarıdır; giriş neyi soruyorsa burada da
    /// sorulmalı. Sormasaydık pasife alınan bir kullanıcı elindeki yenileme
    /// anahtarıyla yedi gün boyunca yeni token üretmeye devam ederdi —
    /// "hesabı kapattım" düğmesi hiçbir işe yaramazdı.
    /// </summary>
    /// <returns>Yeni oturum; anahtar geçersizse <c>null</c>.</returns>
    public async Task<LoginResponseDto?> RefreshAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var kayit = await _refreshTokenRepository.GetByHashAsync(Ozetle(refreshToken));

        // Böyle bir anahtar hiç yok (uydurulmuş ya da hesabı silinmiş birine ait).
        if (kayit is null)
        {
            return null;
        }

        // --- Yeniden kullanım: iptal edilmiş bir anahtar tekrar sunuldu ---
        if (kayit.RevokedAt is not null)
        {
            await _refreshTokenRepository.RevokeAllForUserAsync(kayit.UserId, YenidenKullanim);
            return null;
        }

        // Süresi dolmuş: oturumun mutlak sonu geldi, yeniden giriş gerekir.
        if (kayit.ExpiresAt <= DateTime.UtcNow)
        {
            return null;
        }

        var user = kayit.User;

        // Hesap artık uygun değilse anahtarı da kapat: elde geçerli bir
        // anahtarın kalması, kapatılmış bir hesabın açık kapısı demek.
        if (user is null || user.IsDeleted || !user.IsActive || !user.IsApproved)
        {
            await _refreshTokenRepository.RevokeAllForUserAsync(kayit.UserId, HesapUygunDegil);
            return null;
        }

        var yeniOturum = await OturumUretAsync(user);

        // Eski anahtarı ancak yenisi ÜRETİLDİKTEN sonra kapatıyoruz: araya bir
        // hata girerse kullanıcı elindeki geçerli anahtarla kalsın, ortada hiç
        // anahtar kalmayan bir boşluk oluşmasın.
        kayit.RevokedAt = DateTime.UtcNow;
        kayit.RevokedReason = Dondurme;
        kayit.ReplacedByHash = Ozetle(yeniOturum.RefreshToken);
        await _refreshTokenRepository.SaveAsync();

        return yeniOturum;
    }

    /// <summary>
    /// Çıkış: yenileme anahtarını iptal eder.
    ///
    /// Erişim token'ı İPTAL EDİLEMEZ (imzaya bakılıyor, veritabanına değil),
    /// yani çıkıştan sonra en fazla <see cref="JwtSettings.ExpiryMinutes"/>
    /// kadar geçerli kalmaya devam eder. Kapatılan şey oturumun kendisi: o
    /// pencere dolduğunda yenisi ÜRETİLEMEZ.
    ///
    /// Geçersiz anahtar için de sessizce başarılı dönüyor. "Böyle bir anahtar
    /// yok" demek, elinde rastgele değerler olan birine hangilerinin gerçek
    /// olduğunu söylemek olurdu; üstelik çıkış düğmesinin kullanıcıya
    /// verebileceği anlamlı bir hata da yok.
    /// </summary>
    public async Task LogoutAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var kayit = await _refreshTokenRepository.GetByHashAsync(Ozetle(refreshToken));
        if (kayit is null || kayit.RevokedAt is not null)
        {
            return;
        }

        kayit.RevokedAt = DateTime.UtcNow;
        kayit.RevokedReason = Cikis;
        await _refreshTokenRepository.SaveAsync();
    }

    // ---------------------------------------------------------------- iç işler

    /// <summary>İptal sebepleri — veritabanında ne yazdığı tek yerden okunsun.</summary>
    internal const string Cikis = "cikis";
    internal const string Dondurme = "dondurme";
    internal const string YenidenKullanim = "yeniden-kullanim";
    internal const string HesapUygunDegil = "hesap-uygun-degil";

    /// <summary>
    /// Kullanıcı için erişim token'ı + yenileme anahtarı üretir.
    ///
    /// Giriş ve yenileme AYNI metodu çağırıyor. İki ayrı kopya yazsaydık,
    /// birine eklenen bir claim ya da bir kontrol diğerinde unutulabilirdi;
    /// üstelik yenileme zaten "girişin sessiz tekrarı" — kod da bunu
    /// söylemeli.
    /// </summary>
    private async Task<LoginResponseDto> OturumUretAsync(User user)
    {
        var simdi = DateTime.UtcNow;
        var expiresAt = simdi.AddMinutes(_jwtSettings.ExpiryMinutes);

        // Token'ın içine yazılacak bilgiler (claim = iddia/bilgi).
        //
        // YETKİLER BİLEREK YAZILMIYOR: [YetkiGerekli] her istekte yetkiyi
        // veritabanından okuyor (Ödev 6). Token'a gömseydik, yöneticinin geri
        // aldığı bir yetki token süresi dolana kadar geçerli kalırdı.
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        // --- Yenileme anahtarı ---
        var refreshToken = AnahtarUret();
        var refreshExpiresAt = simdi.AddDays(_jwtSettings.RefreshExpiryDays);

        await _refreshTokenRepository.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Ozetle(refreshToken),   // düz metin ASLA saklanmıyor
            ExpiresAt = refreshExpiresAt,
            CreatedAt = simdi,
        });

        return new LoginResponseDto
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAt = expiresAt,
            Username = user.Username,
            RefreshToken = refreshToken,
            RefreshTokenExpiresAt = refreshExpiresAt,
        };
    }

    /// <summary>
    /// Kriptografik olarak rastgele bir yenileme anahtarı üretir.
    ///
    /// <c>RandomNumberGenerator</c> kullanılıyor, <c>Random</c> ya da
    /// <c>Guid.NewGuid()</c> DEĞİL. Random tahmin edilebilir bir diziden
    /// üretir (tohumu bilen sonrakini bilir); Guid ise benzersizlik için
    /// tasarlanmıştır, tahmin edilemezlik için değil. Burada gereken şey
    /// benzersizlik değil TAHMİN EDİLEMEZLİK: bu değeri bilen, kullanıcının
    /// yerine geçiyor.
    ///
    /// 32 bayt = 256 bit. Base64Url biçimi seçildi çünkü anahtar JSON
    /// gövdesinde ve HTTP başlıklarında taşınıyor; '+', '/', '=' karakterleri
    /// kaçış gerektirip sessiz bozulmalara yol açabiliyor.
    /// </summary>
    private static string AnahtarUret()
    {
        var bayt = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncoder.Encode(bayt);
    }

    /// <summary>
    /// Anahtarın SHA-256 özeti (64 karakter küçük harf hex).
    ///
    /// Şifrelerdeki PBKDF2 yerine düz SHA-256 yetiyor: bu değer insan seçimi
    /// değil, 256 bitlik rastgele veri. Yavaş hash'in amacı sözlük ve kaba
    /// kuvvet saldırısını pahalılaştırmaktır; burada denenecek bir sözlük yok.
    /// </summary>
    internal static string Ozetle(string anahtar)
    {
        var ozet = SHA256.HashData(Encoding.UTF8.GetBytes(anahtar));
        return Convert.ToHexString(ozet).ToLowerInvariant();
    }
}
