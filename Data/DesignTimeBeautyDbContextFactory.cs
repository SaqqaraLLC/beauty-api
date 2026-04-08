using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace Beauty.Api.Data;

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

        // ✅ Use ONE connection string name everywhere
        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Missing connection string 'DefaultConnection'.");

        // ✅ Pin MySQL version to avoid AutoDetect socket issues
        var serverVersion = new MySqlServerVersion(new Version(8, 0, 34));

        var options = new DbContextOptionsBuilder<BeautyDbContext>()
            .UseMySql(connectionString, serverVersion)
            .Options;

        return new BeautyDbContext(options);
    }
}
