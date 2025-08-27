using System;

namespace SBSaaS.Application.Events
{
    /// <summary>
    /// Bir dosya yüklendiğinde ve tarama için sıraya alındığında yayınlanan olay.
    /// </summary>
    public class FileUploadedForScanEvent
    {
        // --- Event Metadata (Sizin önerilerinizle zenginleştirildi) ---

        /// <summary>
        /// Bu olayın kendisine ait benzersiz kimliği.
        /// </summary>
        public Guid EventId { get; } = Guid.NewGuid();

        /// <summary>
        /// Olayın meydana geldiği UTC zaman damgası.
        /// </summary>
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Olayın ilgili olduğu iş modülü (örn: "FileUploads", "Billing").
        /// </summary>
        public string Module { get; set; }


        // --- Correlation & Context (İzlenebilirlik ve Güvenlik) ---

        /// <summary>
        /// Bu olayı tetikleyen API isteğinin CorrelationId'si. Uçtan uca izleme için kritik.
        /// </summary>
        public Guid CorrelationId { get; set; }

        /// <summary>
        /// İşlemin ait olduğu kiracının kimliği.
        /// </summary>
        public Guid TenantId { get; set; }

        /// <summary>
        /// Dosyayı yükleyerek bu olayı tetikleyen kullanıcının kimliği.
        /// </summary>
        public string TriggeringUserId { get; set; }


        // --- File Specific Data (Dosyaya Özel Bilgiler) ---

        /// <summary>
        /// Veritabanındaki 'Files' tablosunda yer alan kaydın birincil anahtarı.
        /// </summary>
        public Guid FileId { get; set; }

        /// <summary>
        /// Dosyanın MinIO'da bulunduğu bucket'ın adı.
        /// </summary>
        public string BucketName { get; set; }

        /// <summary>
        /// Dosyanın MinIO'daki tam yolu ve adı (Sizin "StorageObjectKey" önerinizi karşılar).
        /// </summary>
        public string StorageObjectName { get; set; }

        /// <summary>
        /// Dosyanın kullanıcı tarafından yüklenen orijinal adı.
        /// </summary>
        public string OriginalFileName { get; set; }

        /// <summary>
        /// Dosyanın MIME türü (örn: "image/png").
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// Dosyanın byte cinsinden boyutu.
        /// </summary>
        public long FileSize { get; set; }
    }
}
