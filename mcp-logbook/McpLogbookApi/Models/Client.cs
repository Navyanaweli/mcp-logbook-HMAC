namespace McpLogbookApi.Models;

// An external HMAC-authenticated client credential.
// EncryptedSecret is the base64 shared secret encrypted via IDataProtector --
// reversible by design, since HMAC verification needs the raw secret (see
// ClientOnboardingService and HmacAuthenticationMiddleware).
public record Client(
    Guid ClientId,
    int CompanyId,
    string EncryptedSecret,
    string ClientName,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    DateTime? LastUsedAt
);
