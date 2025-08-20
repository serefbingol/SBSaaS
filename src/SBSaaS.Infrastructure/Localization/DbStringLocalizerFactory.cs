using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using SBSaaS.Infrastructure.Persistence;

namespace SBSaaS.Infrastructure.Localization;

public class DbStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly IDbContextFactory<SbsDbContext> _dbFactory;
    private readonly IMemoryCache _cache;

    public DbStringLocalizerFactory(IDbContextFactory<SbsDbContext> dbFactory, IMemoryCache cache)
    {
        _dbFactory = dbFactory;
        _cache = cache;
    }

    public IStringLocalizer Create(Type resourceSource) => new DbStringLocalizer(_dbFactory, _cache, resourceSource.FullName!);
    public IStringLocalizer Create(string baseName, string location) => new DbStringLocalizer(_dbFactory, _cache, baseName);
}
