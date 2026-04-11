using Beauty.Api.Data;
using Beauty.Api.Domain.Approvals;
using Beauty.Api.Domain.Broadcasting;
using Beauty.Api.Domain.Streams;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Beauty.Api.Services;
using Beauty.Api.Services.Payments;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
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
    // SignInManager (AddIdentityCore) needs these three schemes registered manually —
    // AddIdentity registers them automatically but AddIdentityCore does not.
    // Missing them causes PasswordSignInAsync to throw 500 on every login attempt.
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
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<BeautyDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();


// Authentication & Authorization (LOCK DEFINITION)


var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});






// Booking workflow
builder.Services.AddScoped<IBookingApprovalService, BookingApprovalService>();
builder.Services.AddScoped<UserApprovalService>();

// Broadcasting
builder.Services.AddScoped<IBroadcastService, BroadcastService>();

// Payments (Worldpay)
builder.Services.AddHttpClient();
builder.Services.AddScoped<IWorldpayService, WorldpayService>();

// Streams (Artist profiles & dangerous content detection)
builder.Services.AddScoped<IStreamDangerDetectionService, StreamDangerDetectionService>();
builder.Services.AddScoped<IArtistStreamService, ArtistStreamService>();

//Add Admin Login
builder.Services.Configure<SeedSettings>(
    builder.Configuration.GetSection("Seed"));

// Email
builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection("Email"));

builder.Services.AddScoped<IEmailSender, GraphEmailSender>();
builder.Services.AddSingleton<ITemplateRenderer, FileTemplateRenderer>();
builder.Services.AddScoped<EmailTemplateService>();

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<BeautyDbContext>("database");

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    // Auth endpoints — strict (5 per minute per IP)
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit         = 5;
        o.Window              = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        o.QueueLimit          = 0;
    });

    // General API — relaxed (120 per minute per IP)
    options.AddFixedWindowLimiter("api", o =>
    {
        o.PermitLimit         = 120;
        o.Window              = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        o.QueueLimit          = 5;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

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


using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var seed = services.GetRequiredService<IOptions<SeedSettings>>().Value;

    // ✅ Ensure all roles exist (all environments)
    foreach (var role in new[] { "Admin", "Staff", "Artist", "Client", "Company", "Agent", "Location", "Director" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // ✅ Seed users ONLY in development
    if (app.Environment.IsDevelopment())
    {
        // Seed Admin
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

        // Seed Staff
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
}

Console.WriteLine("EF CONNECTION = " +
    builder.Configuration.GetConnectionString("DefaultConnection"));

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

// Trust Azure's reverse proxy — clears KnownNetworks/KnownProxies so Azure's
// load balancer IPs are accepted, then reads X-Forwarded-Proto/For headers.
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedOptions.KnownNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");

app.UseRateLimiter();

// NOTE: No UseHttpsRedirection — Azure's load balancer terminates HTTPS at the edge.
// Adding it here would create redirect loops since Azure forwards internally as HTTP.

app.UseDefaultFiles();
app.UseStaticFiles();



// 🔒 AUTHENTICATION & AUTHORIZATION
app.UseAuthentication();
app.UseAuthorization();

// Health check — public, no auth required
app.MapHealthChecks("/health").AllowAnonymous();

// 🔒 DEFAULT DENY — everything requires auth unless [AllowAnonymous]
app.MapControllers();

app.Run();

