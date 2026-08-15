using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StajProject.API.Middleware;
using StajProject.API.Services;
using StajProject.Business;
using StajProject.Business.Services;
using StajProject.Business.Auth;
using StajProject.DataAccess;

// ============================================================================
//  SUNUM KATMANI (Presentation)
//
//  Bu dosya artık SADECE sunum katmanının işleriyle ilgilenir:
//  kimlik doğrulama, Swagger, CORS, middleware boru hattı.
//
//  Veri erişimi ve iş mantığı kendi katmanlarında kayıtlıdır:
//    AddDataAccessLayer() → DbContext + repository'ler   (DataAccess)
//    AddBusinessLayer()   → servisler + JwtSettings      (Business)
//
//  Böylece yeni bir repository/servis eklendiğinde bu dosya değişmez.
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

// ---- Katman kayıtları ----
builder.Services.AddDataAccessLayer(builder.Configuration);
builder.Services.AddBusinessLayer(builder.Configuration);

// Giriş yapan kullanıcıyı iş katmanına taşıyan servis (Ödev 5).
// Arayüzü Business'ta, gerçeklemesi burada: HttpContext bir SUNUM ayrıntısıdır,
// iş katmanının ondan haberi olmamalı.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// ---- JWT kimlik doğrulama (sunum katmanının işi) ----
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,            // süresi dolan token reddedilir → 401
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.Zero           // varsayılan 5 dk toleransı kaldır
        };
    });
builder.Services.AddAuthorization();

// ---- Hız sınırı: kaba kuvvet (brute force) saldırısına karşı ----
//
// Login ucu sınırsız denenebiliyordu; saldırgan dakikada binlerce şifre
// deneyebilirdi. .NET 8'in yerleşik rate limiter'ı ile IP başına
// dakikada 5 deneme ile sınırlıyoruz.
const string GirisPolitikasi = "giris";

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(GirisPolitikasi, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Sayaç IP BAŞINA tutulur; bir kullanıcının denemeleri
            // diğerlerini kilitlemez.
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,   // sıraya alma, doğrudan reddet
            }));

    // Reddedilen isteğe, frontend'in beklediği { message } biçiminde cevap ver.
    options.OnRejected = async (context, iptal) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";

        // Kullanıcıya ne kadar bekleyeceğini söyle (hem başlıkta hem gövdede)
        var saniye = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var sure)
            ? (int)sure.TotalSeconds
            : 60;
        context.HttpContext.Response.Headers.RetryAfter = saniye.ToString();

        await context.HttpContext.Response.WriteAsync(
            JsonSerializer.Serialize(new
            {
                message = $"Çok fazla giriş denemesi yaptınız. {saniye} saniye sonra tekrar deneyin."
            }),
            iptal);
    };
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger'a "Authorize" düğmesi ekle
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "StajProject API",
        Version = "v1",
        Description =
            "Katmanlı mimari (API → Business → DataAccess → Entities), PostGIS geometri " +
            "tabloları ve WKT tabanlı veri transferi. Geometriler EPSG:4326 (WGS84) " +
            "olarak saklanır; harita EPSG:3857 kullandığı için dönüşüm istemcide yapılır.",
    });

    // Kodda yazdığımız /// <summary> açıklamalarını Swagger'a taşı.
    // İki dosya: uç açıklamaları API'den, DTO alan açıklamaları Business'tan.
    foreach (var xml in new[] { "StajProject.API.xml", "StajProject.Business.xml" })
    {
        var yol = Path.Combine(AppContext.BaseDirectory, xml);
        if (File.Exists(yol))
        {
            // includeControllerXmlComments: controller sınıfının kendi
            // <summary>'sini de grup açıklaması olarak göster
            options.IncludeXmlComments(yol, includeControllerXmlComments: true);
        }
    }

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Login endpoint'inden aldığın token'ı buraya yapıştır."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// React (Vite) geliştirme sunucusu için CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// ---- Açılış işleri: şema (DataAccess) + başlangıç verisi (Business) ----
app.Services.MigrateDatabase();
await app.Services.SeedDatabaseAsync();

// ---- Middleware boru hattı ----
// Hata yakalayıcı EN BAŞTA: altındaki tüm katmanların hatalarını sarabilmesi için.
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowFrontend");

// Hız sınırı, kimlik doğrulamadan ÖNCE: geçersiz istekler daha az iş yapsın diye
app.UseRateLimiter();

app.UseAuthentication();   // önce kimlik doğrulama (token'ı çözer)
app.UseAuthorization();    // sonra yetkilendirme ([Authorize] kontrolü)

app.MapControllers();

app.Run();
