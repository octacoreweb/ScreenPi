using Microsoft.Data.Sqlite;

namespace SharedData.Data;

public static class DatabaseInitializer
{
    private static readonly string DbDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenPi");

    public static string DbPath => Path.Combine(DbDirectory, "screenpi.db");

    public static string ConnectionString => $"Data Source={DbPath}";

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DbDirectory);

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        // Enable WAL mode for concurrent read/write and minimal I/O
        using (var walCmd = connection.CreateCommand())
        {
            walCmd.CommandText = "PRAGMA journal_mode=WAL;";
            walCmd.ExecuteNonQuery();
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS SessionLog (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                ProcessName   TEXT    NOT NULL,
                WindowTitle   TEXT    NOT NULL DEFAULT '',
                StartTime     TEXT    NOT NULL,
                EndTime       TEXT,
                DurationSeconds REAL  NOT NULL DEFAULT 0
            );";
        cmd.ExecuteNonQuery();

        // Index for date-range queries used by the dashboard
        using var idxCmd = connection.CreateCommand();
        idxCmd.CommandText = @"
            CREATE INDEX IF NOT EXISTS IX_SessionLog_StartTime
            ON SessionLog (StartTime);";
        idxCmd.ExecuteNonQuery();
    }
}
