using System;
using System.Globalization;
using System.Linq;

namespace SBSaaS.WebApp.Services;

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
        var cultureInfo = new CultureInfo(culture ?? CultureInfo.CurrentCulture.Name);

        var regionInfo = new RegionInfo(cultureInfo.Name);
        if (regionInfo.ISOCurrencySymbol.Equals(currencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return amount.ToString("C", cultureInfo);
        }

        var nfi = (NumberFormatInfo)cultureInfo.NumberFormat.Clone();

        var cultureForCurrency = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .FirstOrDefault(c => {
                try { return new RegionInfo(c.Name).ISOCurrencySymbol.Equals(currencyCode, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });

        if (cultureForCurrency != null)
        {
            var currencyNfi = cultureForCurrency.NumberFormat;
            nfi.CurrencySymbol = currencyNfi.CurrencySymbol;
            nfi.CurrencyPositivePattern = currencyNfi.CurrencyPositivePattern;
            nfi.CurrencyNegativePattern = currencyNfi.CurrencyNegativePattern;
        }
        else { nfi.CurrencySymbol = currencyCode; }

        return amount.ToString("C", nfi);
    }

    public string Number(double value, string? culture = null)
    {
        var ci = new CultureInfo(culture ?? CultureInfo.CurrentCulture.Name);
        return value.ToString("N", ci);
    }
}
