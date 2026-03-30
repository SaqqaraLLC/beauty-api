using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Beauty.Api.Data;

public sealed class DesignTimeBeautyDbContextFactory
    : IDesignTimeDbContextFactory<BeautyDbContext>
{
    public BeautyDbContext CreateDbContext(string[] args)
    {
        var env = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var cfg = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{env}.json", optional: true)
            .AddEnvironmentVariables() // ConnectionStrings__BeautyDb can override here
            .Build();

        var cs = cfg.GetConnectionString("BeautyDb");

        // ✅ Pin the server version so EF doesn't try to "AutoDetect" (which opens a socket).
        // Use your actual MySQL major/minor; 8.0.34 is a safe default for MySQL 8 dev.
        var serverVersion = new MySqlServerVersion(new Version(8, 0, 34));

        // If the connection string is missing at design time, provide a local placeholder
        // so scaffolding still works (database **update** will still require the real one).
        if (string.IsNullOrWhiteSpace(cs))
        {
            cs = "Server=127.0.0.1;Port=3306;Database=beauty;Uid=app_user;Pwd=ADMIN$2891aa;SslMode=None;";
        }

        var opts = new DbContextOptionsBuilder<BeautyDbContext>()
            .UseMySql(cs, serverVersion)
            .Options;

        return new BeautyDbContext(opts);
    }
}
