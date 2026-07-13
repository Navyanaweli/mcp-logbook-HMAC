using McpLogbookApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace McpLogbookApi.Controllers;

// Admin-only onboarding for external HMAC clients. Auth is enforced upstream by
// AdminHmacAuthenticationMiddleware -- no [Authorize]/filter needed here.
[ApiController]
[Route("api/admin/clients")]
public class ClientAdminController : ControllerBase
{
    private readonly ClientOnboardingService _onboarding;
    private readonly ExternalClientRepository _clientRepo;

    public ClientAdminController(ClientOnboardingService onboarding, ExternalClientRepository clientRepo)
    {
        _onboarding = onboarding;
        _clientRepo = clientRepo;
    }

    // Creates a new external client credential. The raw secret is returned exactly once here --
    // the admin must securely hand it to the external client now; it cannot be retrieved again.
    [HttpPost]
    public IActionResult CreateClient([FromBody] CreateClientRequest request)
    {
        var result = _onboarding.CreateClient(request.ClientName, request.PrimaryCompanyId, request.ExpiresAt);
        return Ok(new
        {
            clientId = result.ClientId,
            sharedSecret = result.RawSecret,
            warning = "This is the only time the shared secret will be shown. Store it securely now."
        });
    }

    // Immediately revokes a client's credential -- HmacAuthenticationMiddleware checks
    // RevokedAt on every request, so the very next request from this client gets 401.
    [HttpPatch("{clientId:guid}/revoke")]
    public IActionResult RevokeClient(Guid clientId)
    {
        var revoked = _clientRepo.RevokeClient(clientId, DateTime.UtcNow);
        if (!revoked)
            return NotFound(new { message = "Unknown client id." });

        return Ok(new { clientId, message = "Client credential revoked." });
    }
}

// ExpiresAt has no default -- the admin sets it per client at onboarding time,
// since different external companies may have different contract durations.
public record CreateClientRequest(string ClientName, int PrimaryCompanyId, DateTime? ExpiresAt);
