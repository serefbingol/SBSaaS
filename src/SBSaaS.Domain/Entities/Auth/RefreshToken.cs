using SBSaaS.Domain.Entities;

namespace SBSaaS.Domain.Entities.Auth;

public class RefreshToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public string Token { get; set; } = default!; // DB'de hash'lenerek saklanmalı
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
    public string? ReplacedByToken { get; set; }
    public bool IsActive => RevokedUtc == null && DateTimeOffset.UtcNow < ExpiresUtc;
}

