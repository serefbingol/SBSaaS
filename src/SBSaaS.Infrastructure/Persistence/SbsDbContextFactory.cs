
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using SBSaaS.Application.Interfaces;
using System.IO;
using System.Reflection;

namespace SBSaaS.Infrastructure.Persistence;

public class DesignTimeTenantContext : ITenantContext
{
    public Guid TenantId => Guid.Empty;
    public bool IsTenantResolved => true;
}

public class DesignTimeCurrentUser : ICurrentUser
{
    public Guid? UserId => null; // Design-time operations don't have a user context.
}

public class SbsDbContextFactory : IDesignTimeDbContextFactory<SbsDbContext>
{
    public SbsDbContext CreateDbContext(string[] args)
    {
        // EF tools can be run from various directories (solution root, project folder).
        // This logic robustly finds the API project path where appsettings.json resides.
        var basePath = Directory.GetCurrentDirectory();
        var apiProjectPath = Path.GetFullPath(Path.Combine(basePath, "../SBSaaS.API")); // From Infrastructure
        if (!Directory.Exists(apiProjectPath))
        {
            apiProjectPath = Path.GetFullPath(Path.Combine(basePath, "src/SBSaaS.API")); // From solution root
        }

        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(apiProjectPath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.Development.json", optional: true);

        // Load user secrets from the startup project (SBSaaS.API) to avoid hardcoding the ID.
        configBuilder.AddUserSecrets(Assembly.Load("SBSaaS.API"), optional: true);

        IConfiguration config = configBuilder
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("Postgres");
        
        if (string.IsNullOrEmpty(connectionString))
        {
            // Updated error message to be more helpful.
            throw new InvalidOperationException("Could not find a connection string named 'Postgres'. " +
                                                "Check your appsettings.json, appsettings.Development.json, and user secrets.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<SbsDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new SbsDbContext(optionsBuilder.Options, new DesignTimeTenantContext(), new DesignTimeCurrentUser());
    }
}
