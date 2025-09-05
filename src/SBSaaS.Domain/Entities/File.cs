
using SBSaaS.Domain.Enums;
using System;
using System.Collections.Generic;

namespace SBSaaS.Domain.Entities
{
    public class File
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string? UploadedByUserId { get; set; }
        public string? OriginalFileName { get; set; }
        public string? StorageObjectName { get; set; }
        public string? BucketName { get; set; }
        public string? FileUrl { get; set; }
        public string? ContentType { get; set; }
        public long Size { get; set; }
        public string? Checksum { get; set; }
        public ScanStatus ScanStatus { get; set; }
        public DateTimeOffset? ScannedAt { get; set; }
        public string? ScanResultDetails { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public string? UpdatedByUserId { get; set; }
        public DateTimeOffset? UpdatedUtc { get; set; }
        public string? DeletedByUserId { get; set; }
        public DateTimeOffset? DeletedUtc { get; set; }
    }
}
