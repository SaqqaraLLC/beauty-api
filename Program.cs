using Beauty.Api.Data;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MySqlConnector;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Beauty.Api.Services;

// ===================== BUILDER =====================
var builder = WebApplication.CreateBuilder(args);

Console.WriteLine($"[ENV] ASPNETCORE_ENVIRONMENT = {builder.Environment.EnvironmentName}");

// ===================== CONFIG =====================
var connectionString = builder.Configuration.GetConnectionString("BeautyDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'BeautyDb' is missing.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtKey = builder.Configuration["Jwt:SigningKey"];

if (string.IsNullOrWhiteSpace(jwtIssuer) ||
    string.IsNullOrWhiteSpace(jwtAudience) ||
    string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException("JWT configuration is missing.");
}

var signingKey = new SymmetricSecurityKey(
    Encoding.UTF8.GetBytes(jwtKey)
);


// ===================== JWT KEY =====================
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new()
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter: Bearer {your JWT token}"
    });

    c.AddSecurityRequirement(new()
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

// ===================== SERVICES =====================

// EF Core

builder.Services.AddDbContext<BeautyDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("BeautyDb"),
        new MySqlServerVersion(new Version(8, 0, 34))
    ));


// Identity
builder.Services
    .AddIdentityCore<ApplicationUser>()
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<BeautyDbContext>()
    .AddDefaultTokenProviders();

// Auth
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
builder.Services.AddScoped<BookingWorkflowService>();  

// Email
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<IEmailSender, GraphEmailSender>();
builder.Services.AddSingleton<ITemplateRenderer, FileTemplateRenderer>();
builder.Services.AddScoped<EmailTemplateService>();

// MVC & Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection("Email"));

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// ===================== APP =====================
var app = builder.Build();


// ===================== DB WARMUP =====================
var redacted = Regex.Replace(connectionString, @"Pwd=[^;]*", "Pwd=***", RegexOptions.IgnoreCase);
app.Logger.LogInformation("[DB-CONN] {Conn}", redacted);

await using (var warmup = new MySqlConnection(connectionString))
{
    await warmup.OpenAsync();
}
app.Logger.LogInformation("[DB-CONN] Warmup open succeeded");

// Migrations + seed

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<BeautyDbContext>();

    // ✅ Step 1: Ensure schema exists
    await db.Database.MigrateAsync();

    // ✅ Step 2: Seed Identity data
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);
}


// ===================== PIPELINE =====================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers().RequireAuthorization();
app.MapGet("/", () => Results.Ok("Saqqara API is running"));
app.MapGet("/healthz", () => Results.Ok("healthy"));

app.Run();

