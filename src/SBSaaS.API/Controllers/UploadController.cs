using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Entities;
using SBSaaS.Domain.Enums;
using SBSaaS.Infrastructure.Persistence;
using System;
using System.IO;
using System.Threading.Tasks;
using SBSaaS.Application.Events;

namespace SBSaaS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UploadController : ControllerBase
    {
        private readonly IFileStorage _fileStorage;
        private readonly SbsDbContext _context;
        private readonly IMessagePublisher _messagePublisher;
        private readonly ITenantContext _tenantContext;
        private readonly string _bucketName;

        public UploadController(
            IFileStorage fileStorage,
            SbsDbContext context,
            IMessagePublisher messagePublisher,
            ITenantContext tenantContext,
            IConfiguration configuration)
        {
            _fileStorage = fileStorage;
            _context = context;
            _messagePublisher = messagePublisher;
            _tenantContext = tenantContext;
            _bucketName = configuration["Minio:Bucket"] ?? throw new ArgumentNullException("Minio:Bucket configuration is missing.");
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("File is empty.");
            }

            var tenantId = _tenantContext.TenantId;
            if (tenantId == Guid.Empty)
            {
                return Unauthorized("A valid tenant context is required.");
            }

            // TODO: Get user ID from a proper user context service
            var userId = User.Identity?.Name ?? "anonymous"; 

            var fileEntity = new SBSaaS.Domain.Entities.File
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UploadedByUserId = userId,
                OriginalFileName = file.FileName,
                ContentType = file.ContentType,
                Size = file.Length,
                BucketName = _bucketName,
                StorageObjectName = $"{tenantId}/{Guid.NewGuid()}_{file.FileName}",
                ScanStatus = ScanStatus.PendingScan,
                IsDeleted = false,
                CreatedUtc = DateTimeOffset.UtcNow
            };

            string checksum;
            try
            {
                using var stream = file.OpenReadStream();
                checksum = await _fileStorage.UploadAsync(
                    fileEntity.BucketName,
                    fileEntity.StorageObjectName,
                    stream,
                    fileEntity.ContentType,
                    HttpContext.RequestAborted
                );
            }
            catch (Exception ex)
            {
                // TODO: Log exception
                return StatusCode(500, $"An error occurred while uploading the file: {ex.Message}");
            }

            fileEntity.Checksum = checksum;

            _context.Files.Add(fileEntity);
            await _context.SaveChangesAsync();

            var scanEvent = new FileUploadedForScanEvent
            {
                // EventId is initialized in the constructor of FileUploadedForScanEvent
                // OccurredAt is initialized in the constructor of FileUploadedForScanEvent
                Module = "FileUploads",
                CorrelationId = Guid.NewGuid(), // TODO: Get from HttpContext trace identifier
                TenantId = fileEntity.TenantId,
                TriggeringUserId = fileEntity.UploadedByUserId,
                FileId = fileEntity.Id,
                BucketName = fileEntity.BucketName,
                StorageObjectName = fileEntity.StorageObjectName,
                OriginalFileName = fileEntity.OriginalFileName,
                ContentType = fileEntity.ContentType,
                FileSize = fileEntity.Size
            };

            await _messagePublisher.Publish(scanEvent);

            return Ok(new { FileId = fileEntity.Id, Status = "File uploaded successfully and queued for scanning." });
        }
    }
}