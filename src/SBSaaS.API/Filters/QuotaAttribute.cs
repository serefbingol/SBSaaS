using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Entities.Billing;
using SBSaaS.Infrastructure.Persistence;
using System;
using System.Threading.Tasks;
using SBSaaS.Application.Features;

namespace SBSaaS.API.Filters
{
    /// <summary>
    /// Belirli bir özellik (feature) için kiracı bazlı kota kontrolü yapar.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
    public class QuotaAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _featureKey;
        private readonly int _defaultLimit;

        /// <summary>
        /// Kota kontrolü yapılacak özelliğin anahtarını belirtir.
        /// </summary>
        /// <param name="featureKey">Ölçülecek özelliğin anahtarı (örn: 'api_calls').</param>
        /// <param name="defaultLimit">Kiracı için özel bir limit tanımlanmamışsa kullanılacak varsayılan limit.</param>
        public QuotaAttribute(string featureKey, int defaultLimit)
        {
            _featureKey = !string.IsNullOrWhiteSpace(featureKey) ? featureKey : throw new ArgumentNullException(nameof(featureKey));
            _defaultLimit = defaultLimit > 0 ? defaultLimit : throw new ArgumentOutOfRangeException(nameof(defaultLimit));
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // Adım 4.6.2: Gerekli servisleri ve bilgileri al
            var tenantContext = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
            if (tenantContext.TenantId == Guid.Empty)
            {
                await next(); // Kiracı bilgisi yoksa kontrolü atla
                return;
            }

            var featureService = context.HttpContext.RequestServices.GetRequiredService<IFeatureService>();
            var dbContext = context.HttpContext.RequestServices.GetRequiredService<SbsDbContext>();

            // Projenizdeki IFeatureService ve QuotaUsage entity'sine göre düzeltildi.
            long limit = (await featureService.GetCurrentTenantFeatureLimitAsync($"quota.{_featureKey}")) ?? _defaultLimit;

            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);
            var usage = await dbContext.QuotaUsages
                .FirstOrDefaultAsync(q => q.TenantId == tenantContext.TenantId && q.FeatureKey == _featureKey && q.PeriodStart == today, context.HttpContext.RequestAborted);

            var currentUsage = usage?.Usage ?? 0;

            // Adım 4.6.3: Limiti aşan istekleri engelle
            if (currentUsage >= limit)
            {
                var problemDetails = new ProblemDetails
                {
                    Title = "Quota Exceeded",
                    Detail = $"Daily quota for '{_featureKey}' has been reached. Limit: {limit}.",
                    Status = StatusCodes.Status429TooManyRequests,
                    Instance = context.HttpContext.Request.Path
                };
                context.Result = new ObjectResult(problemDetails) { StatusCode = StatusCodes.Status429TooManyRequests };

                // İstemciyi bilgilendirmek için Retry-After başlığını ekle
                var retryAfterSeconds = (int)(tomorrow - DateTime.UtcNow).TotalSeconds; // 'tomorrow' artık tanımlı
                context.HttpContext.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
                return;
            }

            // Adım 4.6.4: Kullanım sayacını artır
            if (usage == null)
            {
                usage = new QuotaUsage
                {
                    TenantId = tenantContext.TenantId,
                    FeatureKey = _featureKey,
                    PeriodStart = today,
                    PeriodEnd = tomorrow,
                    Usage = 1
                };
                dbContext.QuotaUsages.Add(usage);
            }
            else
            {
                usage.Usage++;
            }
            await dbContext.SaveChangesAsync(context.HttpContext.RequestAborted);

            await next();
        }
    }
}