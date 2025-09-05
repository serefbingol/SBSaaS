using System;
using System.ComponentModel.DataAnnotations;

namespace SBSaaS.Application.Features.DTOs
{
    /// <summary>
    /// Bir kiracıya özel bir özellik (feature) limiti atamak için kullanılan veri transfer nesnesi.
    /// </summary>
    public class SetFeatureOverrideRequest
    {
        [Required]
        public Guid TenantId { get; set; }

        [Required(AllowEmptyStrings = false)]
        [StringLength(100)]
        public required string FeatureKey { get; set; }

        [Required]
        [Range(0, long.MaxValue)]
        public long Limit { get; set; }
    }
}