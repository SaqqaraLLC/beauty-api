using Beauty.Api.Authorization;
using Beauty.Api.Data;
using Microsoft.AspNetCore.DataProtection;
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

// Data Protection — persist keys to MySQL so sessions survive restarts/deployments
builder.Services.AddDataProtection()
    .SetApplicationName("SaqqaraApi")
    .PersistKeysToDbContext<Beauty.Api.Data.BeautyDbContext>();

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






// HTTP client factory — required by WebhookService
builder.Services.AddHttpClient();

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

// Real-time gift broadcast (SSE)
builder.Services.AddSingleton<Beauty.Api.Services.GiftBroadcastService>();

// Battle matchmaking + auto-resolve
builder.Services.AddScoped<Beauty.Api.Services.BattleMatchmakingService>();
builder.Services.AddHostedService<Beauty.Api.Services.BattleAutoResolveService>();

builder.Services.AddScoped<IEmailSender, GraphEmailSender>();
builder.Services.AddSingleton<ITemplateRenderer, FileTemplateRenderer>();
builder.Services.AddScoped<EmailTemplateService>();
builder.Services.AddScoped<ContractGeneratorService>();
builder.Services.AddScoped<InvoiceGeneratorService>();

// Stripe
builder.Services.AddScoped<IStripeService, StripeService>();

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
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Rose",         Emoji = "🌹", SlabCost =     1, SortOrder = 1 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Comb",         Emoji = "🪮", SlabCost =     5, SortOrder = 2 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Pin",          Emoji = "📌", SlabCost =    10, SortOrder = 3 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Brush",        Emoji = "🖌️", SlabCost =    25, SortOrder = 4 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Crown",        Emoji = "👑", SlabCost =    50, SortOrder = 5 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Fire",         Emoji = "🔥", SlabCost =   100, SortOrder = 6 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Ice",          Emoji = "🧊", SlabCost =  1000, SortOrder = 7 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Diamond Ring", Emoji = "💍", SlabCost = 10000, SortOrder = 8 }
        );
        await db.SaveChangesAsync();
        Console.WriteLine("[STARTUP] Gift catalog seeded.");
    }
    else if (!await db.GiftCatalog.AnyAsync(g => g.SlabCost == 10000))
    {
        db.GiftCatalog.Add(
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Diamond Ring", Emoji = "💍", SlabCost = 10000, SortOrder = 8 }
        );
        await db.SaveChangesAsync();
        Console.WriteLine("[STARTUP] Diamond Ring gift added to catalog.");
    }
    else if (!await db.GiftCatalog.AnyAsync(g => g.Name == "Thunder Wolf"))
    {
        db.GiftCatalog.AddRange(
            // Animals
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Thunder Wolf",        Emoji = "🐺",  SlabCost =    50, SortOrder =  10 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Inferno Lion",         Emoji = "🦁",  SlabCost =   100, SortOrder =  11 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Shadow Panther",       Emoji = "🐆",  SlabCost =    75, SortOrder =  12 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Steel Rhino Charge",   Emoji = "🦏",  SlabCost =    50, SortOrder =  13 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Sky Eagle Strike",     Emoji = "🦅",  SlabCost =    30, SortOrder =  14 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Venom Cobra",          Emoji = "🐍",  SlabCost =    25, SortOrder =  15 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "War Gorilla",          Emoji = "🦍",  SlabCost =    75, SortOrder =  16 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Ice Bear Slam",        Emoji = "🐻‍❄️", SlabCost =   100, SortOrder =  17 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Fire Phoenix Rebirth", Emoji = "🦜",  SlabCost =   150, SortOrder =  18 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Hydra Awakening",      Emoji = "🐉",  SlabCost =   200, SortOrder =  19 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Shark Frenzy",         Emoji = "🦈",  SlabCost =    50, SortOrder =  20 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Battle Ram Impact",    Emoji = "🐏",  SlabCost =    30, SortOrder =  21 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Dragon Spiral",        Emoji = "🐲",  SlabCost =   500, SortOrder =  22 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Tiger Dash Combo",     Emoji = "🐯",  SlabCost =    75, SortOrder =  23 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Elephant War Stomp",   Emoji = "🐘",  SlabCost =   100, SortOrder =  24 },
            // Love
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Heart Pulse",          Emoji = "❤️",  SlabCost =     5, SortOrder =  30 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Rose Bloom Deluxe",    Emoji = "🌹",  SlabCost =    10, SortOrder =  31 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Cupid Strike",         Emoji = "💘",  SlabCost =    25, SortOrder =  32 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Love Rain",            Emoji = "💕",  SlabCost =    50, SortOrder =  33 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Golden Heart Lock",    Emoji = "💛",  SlabCost =   100, SortOrder =  34 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Kiss Explosion",       Emoji = "💋",  SlabCost =    15, SortOrder =  35 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Infinity Heart Loop",  Emoji = "♾️",  SlabCost =    75, SortOrder =  36 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Proposal Ring Drop",   Emoji = "💍",  SlabCost =   500, SortOrder =  37 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Love Fireworks",       Emoji = "🎆",  SlabCost =    50, SortOrder =  38 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Galaxy Love Spiral",   Emoji = "🌌",  SlabCost =   200, SortOrder =  39 },
            // Royalty
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Crown Ascension",      Emoji = "👑",  SlabCost =   100, SortOrder =  40 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Royal Throne Reveal",  Emoji = "🪑",  SlabCost =   150, SortOrder =  41 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Diamond Rain",         Emoji = "💎",  SlabCost =   500, SortOrder =  42 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "King Entrance Walk",   Emoji = "🤴",  SlabCost =   200, SortOrder =  43 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Golden Scepter Power", Emoji = "🔱",  SlabCost =   250, SortOrder =  44 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Royal Carpet Roll",    Emoji = "🟥",  SlabCost =    75, SortOrder =  45 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Treasure Chest Burst", Emoji = "💰",  SlabCost =   300, SortOrder =  46 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Crown Explosion",      Emoji = "👑",  SlabCost =   500, SortOrder =  47 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Royal Guard Formation",Emoji = "⚔️",  SlabCost =   150, SortOrder =  48 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Palace Materialize",   Emoji = "🏰",  SlabCost =  1000, SortOrder =  49 },
            // Elements
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Lightning Crash",      Emoji = "⚡",  SlabCost =    50, SortOrder =  50 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Fire Tornado",         Emoji = "🌪️", SlabCost =   100, SortOrder =  51 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Ice Freeze Wave",      Emoji = "❄️",  SlabCost =    75, SortOrder =  52 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Wind Cyclone",         Emoji = "🌀",  SlabCost =    25, SortOrder =  53 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Earthquake Break",     Emoji = "💥",  SlabCost =   100, SortOrder =  54 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Energy Orb Charge",    Emoji = "🔮",  SlabCost =   150, SortOrder =  55 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Black Hole Pull",      Emoji = "🌑",  SlabCost =   500, SortOrder =  56 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Plasma Beam Shot",     Emoji = "🔵",  SlabCost =   200, SortOrder =  57 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Light Explosion",      Emoji = "💥",  SlabCost =   150, SortOrder =  58 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Dark Aura Pulse",      Emoji = "🌑",  SlabCost =   300, SortOrder =  59 },
            // Premium
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Cosmic Portal",        Emoji = "🌀",  SlabCost =  1000, SortOrder =  60 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Meteor Strike",        Emoji = "☄️",  SlabCost =  2000, SortOrder =  61 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Time Warp",            Emoji = "⏰",  SlabCost =   500, SortOrder =  62 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "Neon Hacker Glitch",   Emoji = "💻",  SlabCost =   750, SortOrder =  63 },
            new Beauty.Api.Models.Gifts.GiftCatalogItem { Name = "God Mode Activation",  Emoji = "🌟",  SlabCost =  5000, SortOrder =  64 }
        );
        await db.SaveChangesAsync();
        Console.WriteLine("[STARTUP] 50 gift animations added to catalog.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[STARTUP] Gift catalog seeding failed: {ex.Message}");
}

// ── 6. Seed Pravada products + service-product mappings ──────────────────
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<Beauty.Api.Data.BeautyDbContext>();

    // ── Products ──────────────────────────────────────────────────────────
    var pravadaProducts = new[]
    {
        // Hair Care
        new { Name = "Daily Ritual Hydrating Shampoo",        Category = "Hair Care",  WholesaleCents = 500 },
        new { Name = "Daily Ritual Hydrating Conditioner",    Category = "Hair Care",  WholesaleCents = 500 },
        new { Name = "Pro-Restore Biotin Shampoo",            Category = "Hair Care",  WholesaleCents = 550 },
        new { Name = "Pro-Hydration Shampoo",                 Category = "Hair Care",  WholesaleCents = 500 },
        new { Name = "Pro-Hydration Conditioner",             Category = "Hair Care",  WholesaleCents = 500 },
        new { Name = "Weekly Ritual Hydrating Hair Mask",     Category = "Hair Care",  WholesaleCents = 700 },
        new { Name = "Pre-Wash Hair & Scalp Oil",             Category = "Hair Care",  WholesaleCents = 800 },
        new { Name = "Argan Smoothing Serum",                 Category = "Hair Care",  WholesaleCents = 900 },
        new { Name = "Smooth & Seal Blow Dry Spray",          Category = "Hair Care",  WholesaleCents = 750 },
        // Skin Care
        new { Name = "Kale Protein Facial Cleanser",          Category = "Skin Care",  WholesaleCents = 600 },
        new { Name = "Vitamin C Antioxidant Facial Cleanser", Category = "Skin Care",  WholesaleCents = 650 },
        new { Name = "Rose Water Hydra-Mist Toner",           Category = "Skin Care",  WholesaleCents = 600 },
        new { Name = "Niacinamide & HA Water Cream",          Category = "Skin Care",  WholesaleCents = 800 },
        new { Name = "Vitamin C Serum",                       Category = "Skin Care",  WholesaleCents = 950 },
        new { Name = "Multi-Molecular Hyaluronic Acid Serum", Category = "Skin Care",  WholesaleCents = 900 },
        new { Name = "Advanced Firming Eye Cream",            Category = "Skin Care",  WholesaleCents = 1000 },
        // Bath & Body / Nails
        new { Name = "Shea Butter Sugar Scrub",               Category = "Bath & Body", WholesaleCents = 550 },
        new { Name = "Argan & Aloe Body Lotion",              Category = "Bath & Body", WholesaleCents = 500 },
        new { Name = "Cuticle Oil",                           Category = "Bath & Body", WholesaleCents = 350 },
        new { Name = "Shea Butter Hand Cream",                Category = "Bath & Body", WholesaleCents = 450 },
    };

    foreach (var p in pravadaProducts)
    {
        var existing = await db.Products.FirstOrDefaultAsync(x => x.Name == p.Name && x.VendorName == "Pravada");
        if (existing == null)
        {
            db.Products.Add(new Beauty.Api.Models.Catalog.Product
            {
                Name                = p.Name,
                Brand               = "Saqqara",
                VendorName          = "Pravada",
                Category            = p.Category,
                WholesalePriceCents = p.WholesaleCents,
                Status              = Beauty.Api.Models.Catalog.ProductStatus.Approved,
                IsActive            = true,
                SubmittedAt         = DateTime.UtcNow,
                ApprovedAt          = DateTime.UtcNow,
            });
        }
        else if (existing.Brand != "Saqqara")
        {
            existing.Brand = "Saqqara";
        }
    }
    await db.SaveChangesAsync();

    // ── Service Categories ────────────────────────────────────────────────
    var categoryKeys = new[] { ("hair", "Hair"), ("skin", "Skin"), ("nails", "Nails") };
    foreach (var (key, display) in categoryKeys)
    {
        if (!await db.ServiceCategories.AnyAsync(x => x.Key == key))
            db.ServiceCategories.Add(new Beauty.Api.Models.ServiceCategory { Key = key, DisplayName = display, IsActive = true });
    }
    await db.SaveChangesAsync();

    // ── Services ──────────────────────────────────────────────────────────
    var hairCatId  = (await db.ServiceCategories.FirstAsync(x => x.Key == "hair")).Id;
    var skinCatId  = (await db.ServiceCategories.FirstAsync(x => x.Key == "skin")).Id;
    var nailCatId  = (await db.ServiceCategories.FirstAsync(x => x.Key == "nails")).Id;

    var services = new[]
    {
        new { Name = "Haircut / Shampoo", CategoryId = hairCatId },
        new { Name = "Color Service",     CategoryId = hairCatId },
        new { Name = "Blowout / Styling", CategoryId = hairCatId },
        new { Name = "Basic Facial",      CategoryId = skinCatId },
        new { Name = "Advanced Facial",   CategoryId = skinCatId },
        new { Name = "Manicure",          CategoryId = nailCatId },
        new { Name = "Pedicure",          CategoryId = nailCatId },
    };
    foreach (var s in services)
    {
        if (!await db.Services.AnyAsync(x => x.Name == s.Name))
            db.Services.Add(new Beauty.Api.Models.Service { Name = s.Name, CategoryId = s.CategoryId, Price = 0, DurationMinutes = 60, Active = true });
    }
    await db.SaveChangesAsync();

    // ── Service → Product Mappings ────────────────────────────────────────
    async Task<long> Svc(string name) => (await db.Services.FirstAsync(x => x.Name == name)).Id;
    async Task<int>  Prd(string name) => (await db.Products.FirstAsync(x => x.Name == name && x.VendorName == "Pravada")).ProductId;

    var mappings = new[]
    {
        // Haircut / Shampoo
        new { SvcName = "Haircut / Shampoo", PrdName = "Daily Ritual Hydrating Shampoo",        Usage = Beauty.Api.Models.Services.ProductUsageType.Required,  Sort = 1 },
        new { SvcName = "Haircut / Shampoo", PrdName = "Daily Ritual Hydrating Conditioner",    Usage = Beauty.Api.Models.Services.ProductUsageType.Required,  Sort = 2 },
        new { SvcName = "Haircut / Shampoo", PrdName = "Pre-Wash Hair & Scalp Oil",             Usage = Beauty.Api.Models.Services.ProductUsageType.Optional,  Sort = 3 },
        new { SvcName = "Haircut / Shampoo", PrdName = "Weekly Ritual Hydrating Hair Mask",     Usage = Beauty.Api.Models.Services.ProductUsageType.Aftercare, Sort = 4 },

        // Color Service
        new { SvcName = "Color Service",     PrdName = "Pro-Restore Biotin Shampoo",            Usage = Beauty.Api.Models.Services.ProductUsageType.Required,  Sort = 1 },
        new { SvcName = "Color Service",     PrdName = "Weekly Ritual Hydrating Hair Mask",     Usage = Beauty.Api.Models.Services.ProductUsageType.Required,  Sort = 2 },
        new { SvcName = "Color Service",     PrdName = "Argan Smoothing Serum",                 Usage = Beauty.Api.Models.Services.ProductUsageType.Optional,  Sort = 3 },
        new { SvcName = "Color Service",     PrdName = "Pre-Wash Hair & Scalp Oil",             Usage = Beauty.Api.Models.Services.ProductUsageType.Aftercare, Sort = 4 },

        // Blowout / Styling
        new { SvcName = "Blowout / Styling", PrdName = "Pro-Hydration Shampoo",                 Usage = Beauty.Api.Models.Services.ProductUsageType.Required,  Sort = 1 },
        new { SvcName = "Blowout / Styling", PrdName = "Pro-Hydration Conditioner",             Usage = Beauty.Api.Models.Services.ProductUsageType.Required,  Sort = 2 },
        new { SvcName = "Blowout / Styling", PrdName = "Smooth & Seal Blow Dry Spray",          Usage = Beauty.Api.Models.Services.ProductUsageType.Optional,  Sort = 3 },
        new { SvcName = "Blowout / Styling", PrdName = "Argan Smoothing Serum",                 Usage = Beauty.Api.Models.Services.ProductUsageType.Aftercare, Sort = 4 },

        // Basic Facial
        new { SvcName = "Basic Facial",      PrdName = "Kale Protein Facial Cleanser",          Usage = Beauty.Api.Models.Services.ProductUsageType.Required,  Sort = 1 },
        new { SvcName = "Basic Facial",      PrdName = "Rose Water Hydra-Mist Toner",           Usage = Beauty.Api.Models.Services.ProductUsageType.Required,  Sort = 2 },
        new { SvcName = "Basic Facial",      PrdName = "Niacinamide & HA Water Cream",          Usage = Beauty.Api.Models.Services.ProductUsageType.Required,  Sort = 3 },
        new { SvcName = "Basic Facial",      PrdName = "Vitamin C Serum",                       Usage = Beauty.Api.Models.Services.ProductUsageType.Optional,  Sort = 4 },
        new { SvcName = "Basic Facial",      PrdName = "Multi-Molecular Hyaluronic Acid Serum", Usage = Beauty.Api.Models.Services.ProductUsageType.Aftercare, Sort = 5 },

        // Advanced Facial
        new { SvcName = "Advanced Facial",   PrdName = "Vitamin C Antioxidant Facial Cleanser", Usage = Beauty.Api.Models.Services.ProductUsageType.Required,  Sort = 1 },
        new { SvcName = "Advanced Facial",   PrdName = "Multi-Molecular Hyaluronic Acid Serum", Usage = Beauty.Api.Models.Services.ProductUsageType.Required,  Sort = 2 },
        new { SvcName = "Advanced Facial",   PrdName = "Advanced Firming Eye Cream",            Usage = Beauty.Api.Models.Services.ProductUsageType.Optional,  Sort = 3 },
        new { SvcName = "Advanced Facial",   PrdName = "Niacinamide & HA Water Cream",          Usage = Beauty.Api.Models.Services.ProductUsageType.Aftercare, Sort = 4 },

        // Manicure
        new { SvcName = "Manicure",          PrdName = "Cuticle Oil",                           Usage = Beauty.Api.Models.Services.ProductUsageType.Required,  Sort = 1 },
        new { SvcName = "Manicure",          PrdName = "Shea Butter Hand Cream",                Usage = Beauty.Api.Models.Services.ProductUsageType.Optional,  Sort = 2 },
        new { SvcName = "Manicure",          PrdName = "Argan & Aloe Body Lotion",              Usage = Beauty.Api.Models.Services.ProductUsageType.Aftercare, Sort = 3 },

        // Pedicure
        new { SvcName = "Pedicure",          PrdName = "Shea Butter Sugar Scrub",               Usage = Beauty.Api.Models.Services.ProductUsageType.Required,  Sort = 1 },
        new { SvcName = "Pedicure",          PrdName = "Argan & Aloe Body Lotion",              Usage = Beauty.Api.Models.Services.ProductUsageType.Required,  Sort = 2 },
        new { SvcName = "Pedicure",          PrdName = "Cuticle Oil",                           Usage = Beauty.Api.Models.Services.ProductUsageType.Optional,  Sort = 3 },
        new { SvcName = "Pedicure",          PrdName = "Shea Butter Hand Cream",                Usage = Beauty.Api.Models.Services.ProductUsageType.Aftercare, Sort = 4 },
    };

    foreach (var m in mappings)
    {
        var svcId = await Svc(m.SvcName);
        var prdId = await Prd(m.PrdName);
        if (!await db.Set<Beauty.Api.Models.Services.ServiceRequiredProduct>()
                     .AnyAsync(x => x.ServiceId == svcId && x.ProductId == prdId))
        {
            db.Set<Beauty.Api.Models.Services.ServiceRequiredProduct>().Add(
                new Beauty.Api.Models.Services.ServiceRequiredProduct
                {
                    ServiceId = svcId,
                    ProductId = prdId,
                    UsageType = m.Usage,
                    SortOrder = m.Sort,
                    IsActive  = true,
                });
        }
    }
    await db.SaveChangesAsync();
    Console.WriteLine("[STARTUP] Pravada product catalog seeded.");
}
catch (Exception ex)
{
    Console.WriteLine($"[STARTUP] Pravada seeding failed: {ex.Message}");
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

