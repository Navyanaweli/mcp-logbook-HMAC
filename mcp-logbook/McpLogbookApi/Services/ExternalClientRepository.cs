using McpLogbookApi.Models;
using Microsoft.Data.Sqlite;

namespace McpLogbookApi.Services;

// Raw ADO.NET CRUD for external HMAC clients, mirroring LogbookRepository's style.
// Covers Clients, ClientCompanyAccess, and ExternalClientAuditLog.
public class ExternalClientRepository
{
    private readonly string _connectionString;

    public ExternalClientRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("LogbookDb")
            ?? throw new InvalidOperationException("ConnectionStrings:LogbookDb is not configured.");
    }

    // ── Companies ────────────────────────────────────────────────

    // Creates a new company and returns its generated CompanyId
    public int InsertCompany(string companyName)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Companies (CompanyName) VALUES (@companyName);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("@companyName", companyName);

        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    // Looks up an existing company by exact name -- used by demo/setup tooling that wants
    // to reuse a seeded company (e.g. "Ocean Star Shipping Co.") instead of always minting one
    public int? GetCompanyIdByName(string companyName)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CompanyId FROM Companies WHERE CompanyName = @companyName";
        command.Parameters.AddWithValue("@companyName", companyName);

        var result = command.ExecuteScalar();
        return result is null ? null : Convert.ToInt32((long)result);
    }

    // ── Clients ──────────────────────────────────────────────────

    public Client? GetClientById(Guid clientId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ClientId, CompanyId, EncryptedSecret, ClientName, CreatedAt, ExpiresAt, RevokedAt, LastUsedAt
            FROM Clients
            WHERE ClientId = @clientId
            """;
        command.Parameters.AddWithValue("@clientId", clientId.ToString());

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return ReadClient(reader);
    }

    public void InsertClient(Client client)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Clients (ClientId, CompanyId, EncryptedSecret, ClientName, CreatedAt, ExpiresAt, RevokedAt, LastUsedAt)
            VALUES (@clientId, @companyId, @encryptedSecret, @clientName, @createdAt, @expiresAt, @revokedAt, @lastUsedAt)
            """;
        command.Parameters.AddWithValue("@clientId", client.ClientId.ToString());
        command.Parameters.AddWithValue("@companyId", client.CompanyId);
        command.Parameters.AddWithValue("@encryptedSecret", client.EncryptedSecret);
        command.Parameters.AddWithValue("@clientName", client.ClientName);
        command.Parameters.AddWithValue("@createdAt", client.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("@expiresAt", (object?)client.ExpiresAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("@revokedAt", (object?)client.RevokedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("@lastUsedAt", (object?)client.LastUsedAt?.ToString("O") ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void UpdateLastUsed(Guid clientId, DateTime timestamp)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Clients SET LastUsedAt = @timestamp WHERE ClientId = @clientId";
        command.Parameters.AddWithValue("@timestamp", timestamp.ToString("O"));
        command.Parameters.AddWithValue("@clientId", clientId.ToString());
        command.ExecuteNonQuery();
    }

    // Returns false if no client with this id exists, so the caller can 404 instead of
    // silently succeeding. Idempotent -- revoking an already-revoked client just overwrites
    // RevokedAt with the new timestamp.
    public bool RevokeClient(Guid clientId, DateTime revokedAt)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Clients SET RevokedAt = @revokedAt WHERE ClientId = @clientId";
        command.Parameters.AddWithValue("@revokedAt", revokedAt.ToString("O"));
        command.Parameters.AddWithValue("@clientId", clientId.ToString());

        return command.ExecuteNonQuery() > 0;
    }

    // ── ClientCompanyAccess ──────────────────────────────────────

    public void GrantCompanyAccess(Guid clientId, int companyId, DateTime grantedAt)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ClientCompanyAccess (ClientId, CompanyId, GrantedAt)
            VALUES (@clientId, @companyId, @grantedAt)
            """;
        command.Parameters.AddWithValue("@clientId", clientId.ToString());
        command.Parameters.AddWithValue("@companyId", companyId);
        command.Parameters.AddWithValue("@grantedAt", grantedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    // Company IDs this client is allowed to read data for
    public IReadOnlyList<int> GetAllowedCompanyIds(Guid clientId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CompanyId FROM ClientCompanyAccess WHERE ClientId = @clientId";
        command.Parameters.AddWithValue("@clientId", clientId.ToString());

        var companyIds = new List<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            companyIds.Add(reader.GetInt32(0));

        return companyIds;
    }

    // ── ExternalClientAuditLog ───────────────────────────────────

    public void LogAttempt(Guid? clientId, int? requestedCompanyId, string toolName, bool allowed, string? denialReason)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ExternalClientAuditLog (ClientId, RequestedCompanyId, ToolName, Allowed, DenialReason, Timestamp)
            VALUES (@clientId, @requestedCompanyId, @toolName, @allowed, @denialReason, @timestamp)
            """;
        command.Parameters.AddWithValue("@clientId", (object?)clientId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("@requestedCompanyId", (object?)requestedCompanyId ?? DBNull.Value);
        command.Parameters.AddWithValue("@toolName", toolName);
        command.Parameters.AddWithValue("@allowed", allowed ? 1 : 0);
        command.Parameters.AddWithValue("@denialReason", (object?)denialReason ?? DBNull.Value);
        command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    // Admin-only query surface: optional filters by client, date range, and allow/deny status
    public IReadOnlyList<ExternalClientAuditEntry> GetAuditLogs(
        Guid? clientId = null, DateTime? from = null, DateTime? to = null, bool? allowed = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        var conditions = new List<string>();

        if (clientId is not null)
        {
            conditions.Add("ClientId = @clientId");
            command.Parameters.AddWithValue("@clientId", clientId.Value.ToString());
        }
        if (from is not null)
        {
            conditions.Add("Timestamp >= @from");
            command.Parameters.AddWithValue("@from", from.Value.ToString("O"));
        }
        if (to is not null)
        {
            conditions.Add("Timestamp <= @to");
            command.Parameters.AddWithValue("@to", to.Value.ToString("O"));
        }
        if (allowed is not null)
        {
            conditions.Add("Allowed = @allowed");
            command.Parameters.AddWithValue("@allowed", allowed.Value ? 1 : 0);
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        command.CommandText = $"""
            SELECT Id, ClientId, RequestedCompanyId, ToolName, Allowed, DenialReason, Timestamp
            FROM ExternalClientAuditLog
            {whereClause}
            ORDER BY Timestamp DESC
            """;

        var entries = new List<ExternalClientAuditEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new ExternalClientAuditEntry(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetString(3),
                reader.GetInt32(4) == 1,
                reader.IsDBNull(5) ? null : reader.GetString(5),
                DateTime.Parse(reader.GetString(6)).ToUniversalTime()));
        }

        return entries;
    }

    private static Client ReadClient(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetInt32(1),
        reader.GetString(2),
        reader.GetString(3),
        DateTime.Parse(reader.GetString(4)).ToUniversalTime(),
        reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)).ToUniversalTime(),
        reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)).ToUniversalTime(),
        reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)).ToUniversalTime());
}
