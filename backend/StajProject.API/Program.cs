using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StajProject.Business.Auth;
using StajProject.Business.Services;
using StajProject.DataAccess.Context;
using StajProject.DataAccess.Repositories;
using StajProject.Entities;

var builder = WebApplication.CreateBuilder(args);

// ---- Servis kayıtları (Dependency Injection) ----

// DbContext: PostgreSQL + PostGIS (NetTopologySuite)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.UseNetTopologySuite()));

// Katmanlar: Repository (DataAccess) ve Service (Business)
builder.Services.AddScoped<ILocationRepository, LocationRepository>();
builder.Services.AddScoped<ILocationService, LocationService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();

// ---- JWT Kimlik Doğrulama ----
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
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
            ClockSkew = TimeSpan.Zero           // varsayılan 5 dk toleransı kaldır: süre dolar dolmaz 401
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger'a "Authorize" düğmesi ekle (Bearer token ile korumalı uçları test edebilmek için)
builder.Services.AddSwaggerGen(options =>
{
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

// Uygulama açılışında bekleyen EF Core migration'larını uygula ve demo kullanıcıyı ekle
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Seed: hiç kullanıcı yoksa demo kullanıcı oluştur (admin / staj123)
    //
    // DİKKAT (Ödev 3 / Görev 1): Artık User üzerinde global query filter var.
    // Düz "db.Users.Any()" yazarsak EF buna otomatik "WHERE is_deleted = false" ekler.
    // Yani admin'i soft delete ile sildiysen, uygulama her açılışta "hiç kullanıcı yok"
    // sanıp yeni bir admin yaratır ve silme işlemin boşa gider.
    // Filtreyi bilinçli olarak devre dışı bırakıp TÜM satırlara bakıyoruz:
    if (!db.Users.IgnoreQueryFilters().Any())
    {
        var hasher = new PasswordHasher<User>();
        var admin = new User { Username = "admin" };
        admin.PasswordHash = hasher.HashPassword(admin, "staj123");
        db.Users.Add(admin);
        db.SaveChanges();
    }
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowFrontend");

app.UseAuthentication();   // önce kimlik doğrulama (token'ı çözer) hesap sorgulama
app.UseAuthorization();    // sonra yetkilendirme ([Authorize] kontrolü)

app.MapControllers();

app.Run();
