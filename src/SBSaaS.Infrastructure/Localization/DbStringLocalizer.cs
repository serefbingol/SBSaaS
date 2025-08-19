using Microsoft.Extensions.Localization;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using SBSaaS.Infrastructure.Persistence;

namespace SBSaaS.Infrastructure.Localization;

public class DbStringLocalizer : IStringLocalizer
{
    private readonly SbsDbContext _db;
    private readonly string _baseName;

    public DbStringLocalizer(SbsDbContext db, string baseName)
    { _db = db; _baseName = baseName; }

    public LocalizedString this[string name]
    {
        get
        {
            var culture = Thread.CurrentThread.CurrentUICulture.Name;
            var value = _db.Set<Translation>().AsNoTracking()
                .Where(x => x.Key == name && x.Culture == culture)
                .Select(x => x.Value)
                .FirstOrDefault();
            var notFound = value is null;
            value ??= name; // fallback
            return new LocalizedString(name, value, notFound);
        }
    }

    public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Enumerable.Empty<LocalizedString>();
    public IStringLocalizer WithCulture(CultureInfo culture) => this;
}
