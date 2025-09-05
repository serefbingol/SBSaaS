namespace SBSaaS.API.Contracts.Files;

/// <summary>
/// Bir dosya yüklemek için güvenli bir URL (presigned URL) talep etmek için kullanılan model.
/// </summary>
/// <param name="FileName">Yüklenecek dosyanın orijinal adı.</param>
/// <param name="ContentType">Dosyanın MIME türü (örn: "image/jpeg").</param>
/// <param name="Size">Dosyanın bayt cinsinden boyutu.</param>
public record PresignRequest(string FileName, string ContentType, long Size);

/// <summary>
/// Başarılı bir presigned URL talebi sonrası dönen yanıt modeli.
/// </summary>
/// <param name="PresignedUrl">Dosyayı yüklemek için kullanılacak geçici, güvenli URL.</param>
/// <param name="ObjectName">Dosyanın depolama servisinde alacağı tam yol ve ad.</param>
public record PresignResponse(string PresignedUrl, string ObjectName);

