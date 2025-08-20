using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using SBSaaS.Infrastructure.Persistence;
using System.Globalization;

namespace SBSaaS.Infrastructure.Localization;

public class DbStringLocalizer : IStringLocalizer
{
    private readonly IDbContextFactory<SbsDbContext> _dbFactory;
    private readonly IMemoryCache _cache;
    private readonly string _baseName;

    public DbStringLocalizer(IDbContextFactory<SbsDbContext> dbFactory, IMemoryCache cache, string baseName)
    {
        _dbFactory = dbFactory;
        _cache = cache;
        _baseName = baseName;
    }

    public LocalizedString this[string name]
    {
        get
        {
            var culture = Thread.CurrentThread.CurrentUICulture.Name;
            var cacheKey = $"loc_{culture}_{name}";

            var value = _cache.GetOrCreate(cacheKey, entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromMinutes(60);
                using var db = _dbFactory.CreateDbContext();
                return db.Set<Translation>().AsNoTracking()
                    .Where(x => x.Key == name && x.Culture == culture)
                    .Select(x => x.Value)
                    .FirstOrDefault();
            });

            return new LocalizedString(name, value ?? name, value is null);
        }
    }

    public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Enumerable.Empty<LocalizedString>();
    public IStringLocalizer WithCulture(CultureInfo culture) => this;
}
