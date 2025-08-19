using Microsoft.Extensions.Localization;
using System.Globalization;
using SBSaaS.Infrastructure.Persistence;
using SBSaaS.Infrastructure.Localization;

namespace SBSaaS.Infrastructure.Localization;

public class DbStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly SbsDbContext _db;
    public DbStringLocalizerFactory(SbsDbContext db) => _db = db;
    public IStringLocalizer Create(Type resourceSource) => new DbStringLocalizer(_db, resourceSource.FullName!);
    public IStringLocalizer Create(string baseName, string location) => new DbStringLocalizer(_db, baseName);
}
