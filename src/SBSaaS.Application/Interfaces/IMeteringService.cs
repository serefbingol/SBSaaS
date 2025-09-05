using System;
using System.Threading.Tasks;

namespace SBSaaS.Application.Interfaces
{
    /// <summary>
    /// Sistemin ürettiği değeri (örn: depolama, API çağrısı) ölçülebilir olaylara dönüştürmek
    /// ve bu olayları toplamak için kullanılır.
    /// </summary>
    public interface IMeteringService
    {
        /// <summary>
        /// Bir kullanım olayını kaydeder.
        /// </summary>
        /// <param name="tenantId">Olayın ait olduğu kiracı.</param>
        /// <param name="key">Ölçümün anahtarı (örn: "storage_bytes", "api_calls").</param>
        /// <param name="quantity">Kullanım miktarı.</param>
        /// <param name="source">Olayın kaynağı (örn: "file_upload", "api_request").</param>
        /// <param name="idempotencyKey">Aynı olayın mükerrer işlenmesini önleyen benzersiz anahtar.</param>    
        Task RecordUsageAsync(Guid tenantId, string key, decimal quantity, string source, string idempotencyKey, DateTimeOffset? occurredAt = null, CancellationToken ct = default);
    }
}