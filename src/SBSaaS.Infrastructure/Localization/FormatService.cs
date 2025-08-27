using System.Globalization;

using SBSaaS.Application.Interfaces;

namespace SBSaaS.Infrastructure.Localization;

public class FormatService : IFormatService
{
    public string Date(DateTimeOffset utc, string? timeZoneId = null, string? culture = null)
    {
        var tz = !string.IsNullOrWhiteSpace(timeZoneId) ? TimeZoneInfo.FindSystemTimeZoneById(timeZoneId) : TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTime(utc, tz);
        var ci = new CultureInfo(culture ?? CultureInfo.CurrentCulture.Name);
        return local.ToString(ci);
    }

    public string Currency(decimal amount, string currencyCode = "TRY", string? culture = null)
    {
        var ci = new CultureInfo(culture ?? CultureInfo.CurrentCulture.Name);
        return amount.ToString("C", ci);
    }

    public string Number(double value, string? culture = null)
    {
        var ci = new CultureInfo(culture ?? CultureInfo.CurrentCulture.Name);
        return value.ToString("N", ci);
    }
}
