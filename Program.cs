using Beauty.Api.Data;
using Beauty.Api.Domain.Approvals;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MySqlConnector;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;

// =================================================
// 1. BUILDER
// =================================================
var builder = WebApplication.CreateBuilder(args);
Console.WriteLine($"[ENV] ASPNETCORE_ENVIRONMENT = {builder.Environment.EnvironmentName}");


// =================================================
// 2. CONFIGURATION
// =================================================

// Database
var connectionString = builder.Configuration.GetConnectionString("BeautyDb");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Connection string 'BeautyDb' is missing.");

// JWT
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtKey = builder.Configuration["Jwt:SigningKey"];

if (string.IsNullOrWhiteSpace(jwtIssuer) ||
    string.IsNullOrWhiteSpace(jwtAudience) ||
    string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException("JWT configuration is missing.");
}

SymmetricSecurityKey signingKey =
    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));


// =================================================
// 3. SERVICES
// =================================================

// EF Core
builder.Services.AddDbContext<BeautyDbContext>(options =>
    options.UseMySql(
        connectionString,
        new MySqlServerVersion(new Version(8, 0, 34))
    ));

// Identity
builder.Services
    .AddIdentityCore<ApplicationUser>()
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<BeautyDbContext>()
    .AddDefaultTokenProviders();

// Authentication & Authorization (LOCK DEFINITION)

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.SaveToken = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = signingKey,

            ClockSkew = TimeSpan.FromMinutes(2),

            // ✅ THIS IS THE IMPORTANT ROLE FIX
            RoleClaimType = ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization();



// Booking workflow
builder.Services.AddScoped<IBookingApprovalService, BookingApprovalService>();

// Email
builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection("Email"));

builder.Services.AddScoped<IEmailSender, GraphEmailSender>();
builder.Services.AddSingleton<ITemplateRenderer, FileTemplateRenderer>();
builder.Services.AddScoped<EmailTemplateService>();

// Controllers & Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.AddDebug();


builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "My API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {your JWT token}"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});



// =================================================
// 4. BUILD + STARTUP WORK
// =================================================
var app = builder.Build();

// DB warmup
var redacted = Regex.Replace(connectionString, @"Pwd=[^;]*", "Pwd=***");
app.Logger.LogInformation("[DB-CONN] {Conn}", redacted);

await using (var warmup = new MySqlConnection(connectionString))
{
 await warmup.OpenAsync();
}
app.Logger.LogInformation("[DB-CONN] Warmup open succeeded");

// Migrations & seeding
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BeautyDbContext>();
    await db.Database.MigrateAsync();

    // TEMPORARY: disable until ArtistId/LocationId exist
    // await IdentitySeeder.SeedAsync(scope.ServiceProvider);
}


// =================================================
// 5. REQUEST PIPELINE (LOCKS LIVE HERE)
// =================================================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

// 🔒 AUTHENTICATION & AUTHORIZATION
app.UseAuthentication();
app.UseAuthorization();

// 🔒 DEFAULT DENY — everything requires auth unless [AllowAnonymous]
app.MapControllers();
// Public endpoints

app.Run();

