using Beauty.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Beauty.Api.Data;

/// <summary>
/// Used only by `dotnet ef` CLI — supplies a no-op TenantContext
/// so the DbContext can be instantiated without an HTTP request.
/// </summary>
public sealed class DesignTimeBeautyDbContextFactory
    : IDesignTimeDbContextFactory<BeautyDbContext>
{
    public BeautyDbContext CreateDbContext(string[] args)
    {
        var env =
            System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{env}.json", optional: true)
            .AddUserSecrets<DesignTimeBeautyDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing connection string 'DefaultConnection'.");

        var options = new DbContextOptionsBuilder<BeautyDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 34)))
            .Options;

        return new BeautyDbContext(options, new DesignTimeTenantContext());
    }

    /// <summary>No-op tenant context for EF CLI — bypasses all filters during migration scaffolding.</summary>
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? CurrentTenantId  => null;
        public bool  IsPlatformUser   => true; // bypass all tenant filters
    }
}
