using SBSaaS.Application.Interfaces;
using System;

namespace SBSaaS.Worker.Services
{
    public class WorkerTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }

        public void SetTenantId(Guid tenantId)
        {
            TenantId = tenantId;
        }
    }
}
