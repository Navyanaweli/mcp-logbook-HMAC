using Microsoft.Data.Sqlite;

namespace McpLogbookApi.Data;

// Bootstraps the SQLite file from schema.sql + seed_data.sql on first run
public static class DatabaseInitializer
{
    public static void EnsureCreated(string connectionString, string dataDirectory)
    {
        var dbPath = new SqliteConnectionStringBuilder(connectionString).DataSource;

        // Never re-seed an existing database — seed data has fixed primary keys
        if (File.Exists(dbPath))
            return;

        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var schema = File.ReadAllText(Path.Combine(dataDirectory, "schema.sql"));
        var seed   = File.ReadAllText(Path.Combine(dataDirectory, "seed_data.sql"));

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = schema + Environment.NewLine + seed;
        command.ExecuteNonQuery();
    }
}

