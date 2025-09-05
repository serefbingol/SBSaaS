using System;

namespace SBSaaS.Domain.Common
{
    /// <summary>
    /// Bu arayüzü uygulayan entity'lerin bir kiracıya (tenant) ait olduğunu belirtir.
    /// Bu, DbContext seviyesinde otomatik veri izolasyonu (global query filters) için kullanılır.
    /// </summary>
    public interface ITenantScoped
    {
        public Guid TenantId { get; set; }
    }
}
