namespace McpLogbookApi.Models;

// A grant of read-only access for one Client to one Company's data.
// No AccessLevel column -- all external access is read-only by design.
public record ClientCompanyAccess(int Id, Guid ClientId, int CompanyId, DateTime GrantedAt);
