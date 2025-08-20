using System.Threading;
using System.Threading.Tasks;

namespace SBSaaS.Worker.Services;

public interface IBackgroundTaskQueue
{
    ValueTask QueueBackgroundWorkItemAsync(MinioScanRequest scanRequest);

    ValueTask<MinioScanRequest> DequeueBackgroundWorkItemAsync(
        CancellationToken cancellationToken);
}