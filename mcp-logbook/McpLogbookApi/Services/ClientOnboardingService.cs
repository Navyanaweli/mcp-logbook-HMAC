using System.Security.Cryptography;
using McpLogbookApi.Models;
using Microsoft.AspNetCore.DataProtection;

namespace McpLogbookApi.Services;

// Result of onboarding a new external client. RawSecret is only ever
// available here, at creation time -- share it with the external client now;
// it cannot be retrieved again afterwards (only EncryptedSecret is persisted).
public record ClientOnboardingResult(Guid ClientId, string RawSecret);

// Creates external HMAC client credentials. The purpose string is shared with
// HmacAuthenticationMiddleware so the same IDataProtector key is used to
// encrypt here and decrypt there.
public class ClientOnboardingService
{
    public const string ProtectorPurpose = "McpLogbookApi.ExternalClients.SharedSecret.v1";

    private readonly ExternalClientRepository _repo;
    private readonly IDataProtector _protector;

    public ClientOnboardingService(ExternalClientRepository repo, IDataProtectionProvider dataProtectionProvider)
    {
        _repo = repo;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
    }

    // Onboards a new external client for the given primary company. expiresAt is set by
    // the admin per client (contract durations vary by external company) -- no default.
    public ClientOnboardingResult CreateClient(string clientName, int primaryCompanyId, DateTime? expiresAt)
    {
        var clientId = Guid.NewGuid();

        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var rawSecret = Convert.ToBase64String(secretBytes);
        var encryptedSecret = _protector.Protect(rawSecret);

        var now = DateTime.UtcNow;
        _repo.InsertClient(new Client(
            ClientId: clientId,
            CompanyId: primaryCompanyId,
            EncryptedSecret: encryptedSecret,
            ClientName: clientName,
            CreatedAt: now,
            ExpiresAt: expiresAt,
            RevokedAt: null,
            LastUsedAt: null));

        _repo.GrantCompanyAccess(clientId, primaryCompanyId, now);

        // Returned once -- the caller (admin) must securely hand this to the external client now
        return new ClientOnboardingResult(clientId, rawSecret);
    }
}
