using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SBSaaS.Application.Features;
using SBSaaS.Application.Features.DTOs;
using SBSaaS.Domain.Entities.Billing;
using SBSaaS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SBSaaS.API.Controllers
{
    [ApiController]
    [Route("api/v1/admin/features")]
    [Authorize(Roles = "Admin")]
    [Produces("application/json")]
    public class AdminFeaturesController : ControllerBase
    {
        private readonly SbsDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly IFeatureService _featureService;

        public AdminFeaturesController(SbsDbContext context, IMemoryCache cache, IFeatureService featureService)
        {
            _context = context;
            _cache = cache;
            _featureService = featureService;
        }

        /// <summary>
        /// Bir kiracı için belirli bir özelliğin limitini tanımlar veya günceller.
        /// </summary>
        /// <remarks>
        /// Bu endpoint, `billing.feature_override` tablosunda bir kayıt oluşturur veya mevcut kaydı günceller.
        /// İşlem sonrası, performans için tutulan ilgili önbellek kaydı temizlenir.
        /// </remarks>
        /// <param name="request">Kiracı ID'si, özellik anahtarı ve yeni limiti içeren istek.</param>
        /// <returns>İşlem başarılı olursa 204 No Content döner.</returns>
        [HttpPut("override")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SetFeatureOverride([FromBody] SetFeatureOverrideRequest request)
        {
            var overrideEntity = await _context.FeatureOverrides
                .FirstOrDefaultAsync(o => o.TenantId == request.TenantId && o.FeatureKey == request.FeatureKey);

            if (overrideEntity != null)
            {
                overrideEntity.Limit = request.Limit;
            }
            else
            {
                overrideEntity = new FeatureOverride
                {
                    TenantId = request.TenantId,
                    FeatureKey = request.FeatureKey,
                    Limit = request.Limit
                };
                _context.FeatureOverrides.Add(overrideEntity);
            }

            await _context.SaveChangesAsync();

            // FeatureService'in kullandığı önbelleği temizle.
            // DÜZELTME: Önceki adımda yanlışlıkla tekil bir anahtar siliniyordu.
            // Doğrusu, o kiracıya ait tüm özellikleri içeren önbellek grubunu temizlemektir.
            var cacheKey = $"features_{request.TenantId}";
            _cache.Remove(cacheKey);

            return NoContent();
        }

        /// <summary>
        /// Belirtilen bir kiracının geçerli tüm özellik limitlerini listeler.
        /// </summary>
        /// <remarks>
        /// Bu endpoint, kiracının abonelik planından gelen limitler ile o kiracıya özel olarak atanmış
        /// limitleri (override) birleştirerek nihai listeyi döndürür.
        /// </remarks>
        /// <param name="tenantId">Limitleri listelenecek kiracının ID'si.</param>
        /// <returns>Özellik anahtarları ve limitlerini içeren bir sözlük.</returns>
        [HttpGet("limits/{tenantId:guid}")]
        [ProducesResponseType(typeof(Dictionary<string, long>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetTenantLimits(Guid tenantId)
        {
            var features = await _featureService.GetAllFeaturesForTenantAsync(tenantId);
            return Ok(features);
        }
    }
}