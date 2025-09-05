namespace SBSaaS.API.Contracts.Auth;

/// <summary>
/// Kullanıcı girişi için istek modeli.
/// </summary>
/// <param name="Email">Kullanıcının e-posta adresi.</param>
/// <param name="Password">Kullanıcının parolası.</param>
public record LoginRequest(string Email, string Password);

/// <summary>
/// Access token'ı yenilemek için kullanılan istek modeli.
/// </summary>
/// <param name="RefreshToken">Kullanıcıya ait geçerli refresh token.</param>
public record TokenRefreshRequest(string RefreshToken);

/// <summary>
/// Başarılı bir kimlik doğrulama sonrası dönen yanıt modeli.
/// </summary>
/// <param name="AccessToken">Erişim için kullanılacak JWT.</param>
/// <param name="RefreshToken">Erişim token'ını yenilemek için kullanılacak token.</param>
/// <param name="ExpiresAt">Erişim token'ının geçerlilik bitiş zamanı (UTC).</param>
public record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt);

/// <summary>
/// Bir kullanıcıyı sisteme davet etmek için kullanılan istek modeli.
/// </summary>
/// <param name="Email">Davet edilecek kullanıcının e-posta adresi.</param>
/// <param name="Role">Kullanıcıya atanacak varsayılan rol.</param>
public record InviteUserRequest(string Email, string Role);

/// <summary>
/// Bir daveti kabul etmek ve kullanıcı kaydını tamamlamak için kullanılan istek modeli.
/// </summary>
/// <param name="Token">Davet e-postasında gönderilen tek kullanımlık token.</param>
/// <param name="Password">Kullanıcının belirlediği yeni parola.</param>
public record AcceptInviteRequest(string Token, string Password);

