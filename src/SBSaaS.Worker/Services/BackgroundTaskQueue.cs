using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SBSaaS.Worker.Services;

public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<MinioScanRequest> _queue;

    public BackgroundTaskQueue(int capacity = 100)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<MinioScanRequest>(options);
    }

    public async ValueTask QueueBackgroundWorkItemAsync(MinioScanRequest scanRequest)
    {
        if (scanRequest == null)
        {
            throw new ArgumentNullException(nameof(scanRequest));
        }

        await _queue.Writer.WriteAsync(scanRequest);
    }

    public async ValueTask<MinioScanRequest> DequeueBackgroundWorkItemAsync(
        CancellationToken cancellationToken)
    {
        var scanRequest = await _queue.Reader.ReadAsync(cancellationToken);
        return scanRequest;
    }
}