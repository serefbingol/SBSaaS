namespace SBSaaS.Application.Interfaces;

/// <summary>
/// Geçerli kullanıcı bağlamı hakkında bilgi sağlayan arayüz.
/// Bu, mevcut kullanıcının kimliğini ve durumunu soyutlar.
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// Geçerli kullanıcının kimliği (ID).
    /// </summary>
    string? UserId { get; }
}