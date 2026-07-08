using McpLogbookApi.Models;
using Microsoft.Data.Sqlite;

namespace McpLogbookApi.Services;

// Read-only queries against ShipLogTable, scoped by UserShipRelationship
public class LogbookRepository
{
    private readonly string _connectionString;

    public LogbookRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("LogbookDb")
            ?? throw new InvalidOperationException("ConnectionStrings:LogbookDb is not configured.");
    }

    // All seeded users — for the demo login dropdown only (see DemoController)
    public IReadOnlyList<DemoUser> GetAllUsers()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Email, UserName FROM Users ORDER BY UserName";

        var users = new List<DemoUser>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            users.Add(new DemoUser(reader.GetString(0), reader.GetString(1)));

        return users;
    }

    // Ship IDs the given user (by email) is assigned to
    public IReadOnlyList<int> GetAssignedShipIds(string email)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT usr.ShipId
            FROM UserShipRelationship usr
            JOIN Users u ON u.UserId = usr.UserId
            WHERE u.Email = @email
            """;
        command.Parameters.AddWithValue("@email", email);

        var shipIds = new List<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            shipIds.Add(reader.GetInt32(0));

        return shipIds;
    }

    // Ships the given user (by email) is assigned to, with names — used for display
    // (e.g. the dashboard's "authorized ships" list), including ships with zero log entries
    public IReadOnlyList<AssignedShip> GetAssignedShips(string email)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.ShipId, s.ShipName
            FROM UserShipRelationship usr
            JOIN Users u ON u.UserId = usr.UserId
            JOIN Ships s ON s.ShipId = usr.ShipId
            WHERE u.Email = @email
            ORDER BY s.ShipName
            """;
        command.Parameters.AddWithValue("@email", email);

        var ships = new List<AssignedShip>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            ships.Add(new AssignedShip(reader.GetInt32(0), reader.GetString(1)));

        return ships;
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
