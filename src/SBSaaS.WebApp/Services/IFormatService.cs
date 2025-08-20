using System;

namespace SBSaaS.WebApp.Services;

public interface IFormatService
{
    string Date(DateTimeOffset utc, string? timeZoneId = null, string? culture = null);
    string Currency(decimal amount, string currencyCode = "TRY", string? culture = null);
    string Number(double value, string? culture = null);
}
