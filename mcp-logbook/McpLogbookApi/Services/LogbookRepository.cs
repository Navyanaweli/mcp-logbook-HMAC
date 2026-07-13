using McpLogbookApi.Models;
using Microsoft.Data.Sqlite;

namespace McpLogbookApi.Services;

// Read-only queries against ShipLogTable, scoped by Ships.CompanyId
public class LogbookRepository
{
    private readonly string _connectionString;

    public LogbookRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("LogbookDb")
            ?? throw new InvalidOperationException("ConnectionStrings:LogbookDb is not configured.");
    }

    // Ship IDs owned by the given set of companies, via Ships.CompanyId
    public IReadOnlyList<int> GetShipIdsForCompanies(IReadOnlyList<int> companyIds)
    {
        if (companyIds.Count == 0)
            return [];

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        var placeholders = string.Join(",", companyIds.Select((_, i) => $"@company{i}"));
        command.CommandText = $"""
            SELECT ShipId
            FROM Ships
            WHERE CompanyId IN ({placeholders})
            """;
        for (var i = 0; i < companyIds.Count; i++)
            command.Parameters.AddWithValue($"@company{i}", companyIds[i]);

        var shipIds = new List<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            shipIds.Add(reader.GetInt32(0));

        return shipIds;
    }

    // Logbook entries for the given set of ships
    public IReadOnlyList<ShipLogEntry> GetShipLogs(IReadOnlyList<int> shipIds)
    {
        if (shipIds.Count == 0)
            return [];

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        var placeholders = string.Join(",", shipIds.Select((_, i) => $"@ship{i}"));
        command.CommandText = $"""
            SELECT sl.ShipLogId, sl.ShipId, s.ShipName, sl.LogText, sl.LogDate
            FROM ShipLogTable sl
            JOIN Ships s ON s.ShipId = sl.ShipId
            WHERE sl.ShipId IN ({placeholders})
            ORDER BY sl.LogDate
            """;
        for (var i = 0; i < shipIds.Count; i++)
            command.Parameters.AddWithValue($"@ship{i}", shipIds[i]);

        var entries = new List<ShipLogEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            entries.Add(new ShipLogEntry(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4)));

        return entries;
    }

    // Single entry by id, with no access check — the controller decides authorization
    public ShipLogEntry? GetShipLogById(int shipLogId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sl.ShipLogId, sl.ShipId, s.ShipName, sl.LogText, sl.LogDate
            FROM ShipLogTable sl
            JOIN Ships s ON s.ShipId = sl.ShipId
            WHERE sl.ShipLogId = @id
            """;
        command.Parameters.AddWithValue("@id", shipLogId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new ShipLogEntry(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4));
    }
}
