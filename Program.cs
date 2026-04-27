using Beauty.Api.Authorization;
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



// Authorization — one policy per permission + tenant membership check
builder.Services.AddAuthorization(options =>
{
    // Register one policy per atomic permission
    foreach (var permission in Permissions.All)
        options.AddPolicy(permission, policy =>
            policy.RequireAuthenticatedUser()
                  .RequireClaim("permission", permission));

    // Tenant membership — user must carry a tenant_id claim
    options.AddPolicy("TenantMember", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("tenant_id"));
});

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
    // SignInManager needs the standard identity cookie schemes registered.
    .AddCookie(IdentityConstants.TwoFactorRememberMeScheme)
    .AddCookie(IdentityConstants.TwoFactorUserIdScheme)
    .AddCookie(IdentityConstants.ExternalScheme)
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

        // Allow apostrophes and all standard email characters in usernames
        options.User.AllowedUserNameCharacters =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+'";
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
        if (builder.Environment.IsDevelopment())
        {
            // Development: allow any origin so localhost:3000 works
            policy
                .SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
        else
        {
            // Production: restrict to known Saqqara domains only
            policy
                .WithOrigins(
                    "https://saqqarallc.com",
                    "https://www.saqqarallc.com",
                    "https://app.saqqarallc.com")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
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

    // Public API v1: 30 requests per minute per IP
    options.AddFixedWindowLimiter("public-api", limiterOptions =>
    {
        limiterOptions.PermitLimit = 30;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});






// Tenant context — resolves current EnterpriseAccountId from claims
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

// Audit logging
builder.Services.AddScoped<Beauty.Api.Services.AuditService>();

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

// Blob Storage
builder.Services.AddSingleton<Beauty.Api.Services.BlobStorageService>();

builder.Services.AddScoped<IEmailSender, GraphEmailSender>();
builder.Services.AddSingleton<ITemplateRenderer, FileTemplateRenderer>();
builder.Services.AddScoped<EmailTemplateService>();
builder.Services.AddScoped<ContractGeneratorService>();
builder.Services.AddScoped<InvoiceGeneratorService>();

// Worldpay
builder.Services.AddHttpClient();
builder.Services.AddScoped<IWorldpayService, WorldpayService>();

// Power Automate webhooks
builder.Services.Configure<PowerAutomateSettings>(
    builder.Configuration.GetSection("PowerAutomate"));
builder.Services.AddScoped<IWebhookService, WebhookService>();

// Controllers & Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

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
        Title = "Saqqara API",
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
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

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

    // Always seed admin — creates if missing, resets password if exists
    if (!string.IsNullOrWhiteSpace(seed.AdminEmail) && !string.IsNullOrWhiteSpace(seed.AdminPassword))
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
            Console.WriteLine("[STARTUP] Admin user created.");
        }
        else
        {
            // Ensure role is set but do NOT reset the password — preserves whatever was set manually
            await userManager.SetLockoutEndDateAsync(admin, null);
            await userManager.ResetAccessFailedCountAsync(admin);
            if (!await userManager.IsInRoleAsync(admin, "Admin"))
                await userManager.AddToRoleAsync(admin, "Admin");
            Console.WriteLine("[STARTUP] Admin account verified.");
        }
    }

    if (app.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(seed.StaffEmail))
    {
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

// ── 3. Seed enterprise roles + permissions ───────────────────────────────
try
{
    await Beauty.Api.Services.PermissionSeeder.SeedAsync(app.Services);
    Console.WriteLine("[STARTUP] Permission seeding succeeded.");
}
catch (Exception ex)
{
    Console.WriteLine($"[STARTUP] Permission seeding failed: {ex.Message}");
}

// ── 4. Seed promo codes ──────────────────────────────────────────────────
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<Beauty.Api.Data.BeautyDbContext>();

    if (!await db.PromoCodes.AnyAsync(p => p.Code == "SAQQARA"))
    {
        db.PromoCodes.Add(new Beauty.Api.Models.Catalog.PromoCode
        {
            Code                    = "SAQQARA",
            Description             = "Saqqara promotional rate — %PURE product kit at 60% markup (standard 80%)",
            ProductMarkupMultiplier = 1.6m,
            MaxUses                 = null,   // unlimited
            IsActive                = true,
            CreatedAt               = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        Console.WriteLine("[STARTUP] Promo code SAQQARA seeded.");
    }
    else
    {
        Console.WriteLine("[STARTUP] Promo code SAQQARA already exists.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[STARTUP] Promo code seeding failed: {ex.Message}");
}


// ── 5. Seed gift catalog ─────────────────────────────────────────────────────
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<Beauty.Api.Data.BeautyDbContext>();

    if (!await db.GiftCatalog.AnyAsync())
    {
        db.GiftCatalog.AddRange(
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Rose",   Emoji = "🌹", SlabCost =    1, SortOrder = 1 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Comb",   Emoji = "🪮", SlabCost =    5, SortOrder = 2 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Pin",    Emoji = "📌", SlabCost =   10, SortOrder = 3 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Brush",  Emoji = "🖌️", SlabCost =   25, SortOrder = 4 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Crown",  Emoji = "👑", SlabCost =   50, SortOrder = 5 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Fire",   Emoji = "🔥", SlabCost =  100, SortOrder = 6 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Ice",    Emoji = "🧊", SlabCost = 1000, SortOrder = 7 }
        );
        await db.SaveChangesAsync();
        Console.WriteLine("[STARTUP] Gift catalog seeded.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[STARTUP] Gift catalog seeding failed: {ex.Message}");
}

// =================================================
// 5. REQUEST PIPELINE (LOCKS LIVE HERE)
// =================================================
app.UseSwagger();
app.UseSwaggerUI();

// Must sit before UseCors so CORS headers survive unhandled 500s
app.Use(async (ctx, next) =>
{
    try
    {
        await next(ctx);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled exception");
        var origin = ctx.Request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin))
        {
            ctx.Response.Headers["Access-Control-Allow-Origin"] = origin;
            ctx.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            ctx.Response.Headers["Vary"] = "Origin";
        }
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsJsonAsync(new { error = "Internal server error" });
        }
    }
});

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
app.MapGet("/health", async (BeautyDbContext db) =>
{
    string dbStatus;
    string? dbError = null;
    try { await db.Database.CanConnectAsync(); dbStatus = "Connected"; }
    catch (Exception ex) { dbStatus = "Failed"; dbError = ex.Message; }
    return Results.Ok(new { status = "Healthy", db = dbStatus, dbError, timestamp = DateTime.UtcNow });
})
   .AllowAnonymous();

// Public endpoints

app.Run();

