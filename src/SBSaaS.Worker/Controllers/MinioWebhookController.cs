using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using SBSaaS.Worker.Services;
using System.Threading.Tasks;
using System;

namespace SBSaaS.Worker.Controllers;

[ApiController]
[Route("api/webhooks")]
public class MinioWebhookController : ControllerBase
{
    private readonly ILogger<MinioWebhookController> _logger;
    private readonly IBackgroundTaskQueue _queue;

    public MinioWebhookController(ILogger<MinioWebhookController> logger, IBackgroundTaskQueue queue)
    {
        _logger = logger;
        _queue = queue;
    }

    [HttpPost("minio")]
    public async Task<IActionResult> HandleMinioWebhook([FromBody] JsonElement payload)
    {
        _logger.LogInformation("Received MinIO webhook notification.");

        try
        {
            // Parse the MinIO webhook payload to extract bucket and object name
            // MinIO webhook structure: { "Records": [ { "s3": { "bucket": { "name": "bucketName" }, "object": { "key": "objectName" } } } ] }
            var bucketName = payload.GetProperty("Records")[0].GetProperty("s3").GetProperty("bucket").GetProperty("name").GetString();
            var objectName = payload.GetProperty("Records")[0].GetProperty("s3").GetProperty("object").GetProperty("key").GetString();

            if (string.IsNullOrEmpty(bucketName) || string.IsNullOrEmpty(objectName))
            {
                _logger.LogWarning("MinIO webhook payload missing bucket or object name.");
                return BadRequest("Missing bucket or object name.");
            }

            _logger.LogInformation("Queuing scan for object: {BucketName}/{ObjectName}", bucketName, objectName);

            // Enqueue the MinioScanRequest
            await _queue.QueueBackgroundWorkItemAsync(new MinioScanRequest(bucketName, objectName));

            // Acknowledge receipt of the event to MinIO immediately.
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MinIO webhook payload.");
            return StatusCode(500, "Internal server error.");
        }
    }
}