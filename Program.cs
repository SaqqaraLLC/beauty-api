using Beauty.Api.Data;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing.Patterns;

const string DEMO_DB =
    "Server=saqqara-mysql-prod.mysql.database.azure.com;Port=3306;Database=beauty;Uid=app_user;Pwd=Admin$2891aa;SslMode=Required;";
const string DEMO_JWT_ISSUER = "saqqara.api";
const string DEMO_JWT_AUDIENCE = "saqqara-api";
const string DEMO_JWT_KEY_BASE64 = "kV7r9G0jvU7Hq3Ckq3bEwJHq9Gk7FQ+0s4r4kdmkq0M=";

var builder = WebApplication.CreateBuilder(args);
Console.WriteLine($"[ENV] ASPNETCORE_ENVIRONMENT = {builder.Environment.EnvironmentName}");

// ---- configuration lookups with inline fallbacks
string connectionString = builder.Configuration.GetConnectionString("BeautyDb") ?? DEMO_DB;
string jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? DEMO_JWT_ISSUER;
string jwtAudience = builder.Configuration["Jwt:Audience"] ?? DEMO_JWT_AUDIENCE;
string rawKey = builder.Configuration["Jwt:Key"] ?? DEMO_JWT_KEY_BASE64;

// Try Base64 first, then fall back to raw; ensure 32 bytes for HS256
byte[] keyBytes;
try { keyBytes = Convert.FromBase64String(rawKey); }
catch (FormatException)
{
    keyBytes = Encoding.UTF8.GetBytes(rawKey);
    if (keyBytes.Length < 32)
        keyBytes = SHA256.HashData(keyBytes);
}
var signingKey = new SymmetricSecurityKey(keyBytes);

// ---- MySQL EF Core (pin version; avoid AutoDetect at startup)
var serverVersion = new MySqlServerVersion(new Version(8, 0, 34));
builder.Services.AddDbContext<BeautyDbContext>(opts =>
    opts.UseMySql(connectionString, serverVersion, mySql =>
        mySql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null)));

// ---- Identity
builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(opts =>
    {
        opts.User.RequireUniqueEmail = true;
        opts.Password.RequireDigit = true;
        opts.Password.RequiredLength = 8;
        opts.Password.RequireUppercase = true;
        opts.Password.RequireLowercase = true;
        opts.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<BeautyDbContext>()
    .AddDefaultTokenProviders();

// ---- JWT
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.SaveToken = true;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddAuthorization();

// ---- Email options + services
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.PostConfigure<EmailOptions>(opt =>
{
    opt.Provider ??= "Graph";
    opt.From ??= "no-reply@saqqaraLLC.com";
    opt.TenantId ??= "d69d5ad3-8e6b-4bf2-bd26-8a6faf75be6c";
    opt.ClientId ??= "cb0b5afb-09f2-41cc-a2b2-b3e7e7f7fb2f";
    opt.ClientSecret ??= "980e0e8e-2de6-415b-ae99-5422f47740bc";
});
builder.Services.AddSingleton<IEmailSender, GraphEmailSender>();
builder.Services.AddSingleton<ITemplateRenderer, FileTemplateRenderer>();
builder.Services.AddScoped<EmailTemplateService>();

// ---- Swagger / Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opt =>
{
    opt.SwaggerDoc("v1", new() { Title = "Beauty API", Version = "v1" });
    opt.AddSecurityDefinition("Bearer", new()
    {
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter 'Bearer {token}'",
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey
    });
    opt.AddSecurityRequirement(new()
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ---- Logging
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// ========================= BUILD =========================
var app = builder.Build();

// --- safe to log with 'app' now
var redacted = Regex.Replace(connectionString, @"Pwd=[^;]*", "Pwd=***", RegexOptions.IgnoreCase);
app.Logger.LogInformation("[DB-CONN] {Conn}", redacted);

// --- warm-up (fail fast on auth/SSL), then migrations/seed
try
{
    await using var warmup = new MySqlConnector.MySqlConnection(connectionString);
    await warmup.OpenAsync();
    await warmup.CloseAsync();
    app.Logger.LogInformation("[DB-CONN] Warmup open succeeded");
}
catch (MySqlConnector.MySqlException mex) when (mex.Number == 1045)
{
    app.Logger.LogError(mex, "[DB-CONN] 1045 Access denied. Check user/password for app_user.");
    throw;
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BeautyDbContext>();
    await db.Database.MigrateAsync();
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);
}

// ======================= PIPELINE ========================
app.UseSwagger();
app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "Beauty API v1"); c.RoutePrefix = "swagger"; });

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/_endpoints/details", (EndpointDataSource ds) =>
{
    var data = ds.Endpoints
        .OfType<RouteEndpoint>()
        .Select(e => new
        {
            DisplayName = e.DisplayName,
            Route = e.RoutePattern.RawText,
            Methods = string.Join(",",
                e.Metadata.OfType<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>()
                          .SelectMany(m => m.HttpMethods))
        })
        .OrderBy(x => x.Route)
        .ToArray();
    return Results.Json(data);
});

app.Run();

