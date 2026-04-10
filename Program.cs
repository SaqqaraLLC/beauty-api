using Beauty.Api.Data;
using Beauty.Api.Domain.Approvals;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MySqlConnector;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;

// =================================================
// 1. BUILDER
// =================================================
var builder = WebApplication.CreateBuilder(args);
Console.WriteLine($"[ENV] ASPNETCORE_ENVIRONMENT = {builder.Environment.EnvironmentName}");


// =================================================
// 2. CONFIGURATION
// =================================================



// Authorization must come AFTER authentication
builder.Services.AddAuthorization();

// Database
var connectionString = builder.Configuration.GetConnectionString("BeautyDb");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Connection string 'BeautyDb' is missing.");

// JWT

builder.Services
    .AddAuthentication(options =>
    {
        // ✅ DEFAULT = COOKIE (for frontend)
        options.DefaultAuthenticateScheme =
            IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme =
            IdentityConstants.ApplicationScheme;
    })
    .AddCookie(IdentityConstants.ApplicationScheme)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        var issuer = builder.Configuration["Jwt:Issuer"];
        var audience = builder.Configuration["Jwt:Audience"];
        var key = builder.Configuration["Jwt:SigningKey"];

        if (string.IsNullOrWhiteSpace(issuer) ||
            string.IsNullOrWhiteSpace(audience) ||
            string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("JWT configuration is missing.");
        }

        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),

            RoleClaimType = ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization();




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
    .AddIdentityCore<ApplicationUser>(options =>
    {
        // --------------------
        // Password policy
        // --------------------
        options.Password.RequiredLength = 8;

        // --------------------
        // Lockout policy
        // --------------------
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        // --------------------
        // Sign-in / MFA
        // --------------------
        options.SignIn.RequireConfirmedAccount = false;
        options.Tokens.AuthenticatorTokenProvider =
            TokenOptions.DefaultAuthenticatorProvider;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<BeautyDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();


// Authentication & Authorization (LOCK DEFINITION)


builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(
                "https://saqqarallc.com",
                "https://www.saqqarallc.com",
                "https://saqqarallc.net",
                "https://www.saqqarallc.net",
                "https://purple-tree-05ce20e0f.1.azurestaticapps.net",
                "http://localhost:3000"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    // General API: 60 requests per minute per IP
    options.AddFixedWindowLimiter("general", limiterOptions =>
    {
        limiterOptions.PermitLimit = 60;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 5;
    });

    // Auth endpoints: 10 requests per minute per IP
    options.AddFixedWindowLimiter("auth", limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 2;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});






// Booking workflow
builder.Services.AddScoped<IBookingApprovalService, BookingApprovalService>();
builder.Services.AddScoped<UserApprovalService>();
builder.Services.AddScoped<Beauty.Api.Services.AcsStreamingService>();

//Add Admin Login
builder.Services.Configure<SeedSettings>(
    builder.Configuration.GetSection("Seed"));

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


builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

    // ✅ CRITICAL FOR SPAs
    options.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});



// =================================================
// 4. BUILD + STARTUP WORK
// =================================================
var app = builder.Build();

// ── 1. Migrate first ─────────────────────────────────────────────────────
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BeautyDbContext>();
    await db.Database.MigrateAsync();
    Console.WriteLine("[STARTUP] Migration succeeded.");
}
catch (Exception ex)
{
    Console.WriteLine($"[STARTUP] Migration failed: {ex.Message}");
    // Continue — don't crash the process; health endpoint will still respond
}

// ── 2. Seed roles ────────────────────────────────────────────────────────
try
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var seed = services.GetRequiredService<IOptions<SeedSettings>>().Value;

    var allRoles = new[] { "Admin", "Staff", "Artist", "Agent", "Company", "Client" };
    foreach (var role in allRoles)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    if (app.Environment.IsDevelopment())
    {
        var admin = await userManager.FindByEmailAsync(seed.AdminEmail);
        if (admin == null)
        {
            admin = new ApplicationUser
            {
                UserName = seed.AdminEmail,
                Email = seed.AdminEmail,
                EmailConfirmed = true,
                Status = "Approved"
            };
            await userManager.CreateAsync(admin, seed.AdminPassword);
            await userManager.AddToRoleAsync(admin, "Admin");
        }

        var staff = await userManager.FindByEmailAsync(seed.StaffEmail);
        if (staff == null)
        {
            staff = new ApplicationUser
            {
                UserName = seed.StaffEmail,
                Email = seed.StaffEmail,
                EmailConfirmed = true,
                Status = "Approved"
            };
            await userManager.CreateAsync(staff, seed.StaffPassword);
            await userManager.AddToRoleAsync(staff, "Staff");
        }
    }
    Console.WriteLine("[STARTUP] Role seeding succeeded.");
}
catch (Exception ex)
{
    Console.WriteLine($"[STARTUP] Role seeding failed: {ex.Message}");
}


// =================================================
// 5. REQUEST PIPELINE (LOCKS LIVE HERE)
// =================================================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors("Frontend");

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();



// 🔒 AUTHENTICATION & AUTHORIZATION
app.UseAuthentication();
app.UseAuthorization();

// Rate limiting
app.UseRateLimiter();

// 🔒 DEFAULT DENY — everything requires auth unless [AllowAnonymous]
app.MapControllers();
// Public endpoints

app.Run();

