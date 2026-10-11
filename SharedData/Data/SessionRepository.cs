using Microsoft.Data.Sqlite;
using SharedData.Models;

namespace SharedData.Data;

public class SessionRepository
{
    private readonly string _connectionString;
    private readonly object _lock = new();

    public SessionRepository(string? connectionString = null)
    {
        _connectionString = connectionString ?? DatabaseInitializer.ConnectionString;
    }

    /// <summary>
    /// Insert a completed session log entry.
    /// </summary>
    public void Insert(SessionLog session)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO SessionLog (ProcessName, WindowTitle, StartTime, EndTime, DurationSeconds)
                VALUES (@processName, @windowTitle, @startTime, @endTime, @duration);";
            cmd.Parameters.AddWithValue("@processName", session.ProcessName);
            cmd.Parameters.AddWithValue("@windowTitle", session.WindowTitle);
            cmd.Parameters.AddWithValue("@startTime", session.StartTime.ToString("o"));
            cmd.Parameters.AddWithValue("@endTime",
                session.EndTime.HasValue ? session.EndTime.Value.ToString("o") : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@duration", session.DurationSeconds);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Update an existing session (used when flushing buffered duration).
    /// </summary>
    public void Update(SessionLog session)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE SessionLog
                SET EndTime = @endTime, DurationSeconds = @duration
                WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@endTime",
                session.EndTime.HasValue ? session.EndTime.Value.ToString("o") : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@duration", session.DurationSeconds);
            cmd.Parameters.AddWithValue("@id", session.Id);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Insert and return the generated Id (for the tracker's active session).
    /// </summary>
    public long InsertAndGetId(SessionLog session)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO SessionLog (ProcessName, WindowTitle, StartTime, EndTime, DurationSeconds)
                VALUES (@processName, @windowTitle, @startTime, @endTime, @duration);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@processName", session.ProcessName);
            cmd.Parameters.AddWithValue("@windowTitle", session.WindowTitle);
            cmd.Parameters.AddWithValue("@startTime", session.StartTime.ToString("o"));
            cmd.Parameters.AddWithValue("@endTime",
                session.EndTime.HasValue ? session.EndTime.Value.ToString("o") : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@duration", session.DurationSeconds);
            var result = cmd.ExecuteScalar();
            return (long)result!;
        }
    }

    /// <summary>
    /// Get all sessions for a given date, grouped and summed by ProcessName, sorted by total duration descending.
    /// </summary>
    public List<AppUsageSummary> GetDailySummary(DateTime date)
    {
        lock (_lock)
        {
            var summaries = new List<AppUsageSummary>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ProcessName,
                       SUM(DurationSeconds) as TotalSeconds,
                       COUNT(*) as SessionCount
                FROM SessionLog
                WHERE date(StartTime) = date(@date)
                GROUP BY ProcessName
                ORDER BY TotalSeconds DESC;";
            cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                summaries.Add(new AppUsageSummary
                {
                    ProcessName = reader.GetString(0),
                    TotalSeconds = reader.GetDouble(1),
                    SessionCount = reader.GetInt32(2)
                });
            }
            return summaries;
        }
    }

    /// <summary>
    /// Get total screen time for a given date.
    /// </summary>
    public double GetTotalScreenTime(DateTime date)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(DurationSeconds), 0)
                FROM SessionLog
                WHERE date(StartTime) = date(@date);";
            cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
            var result = cmd.ExecuteScalar();
            return Convert.ToDouble(result);
        }
    }
}

public class AppUsageSummary
{
    public string ProcessName { get; set; } = string.Empty;
    public double TotalSeconds { get; set; }
    public int SessionCount { get; set; }

    public TimeSpan Duration => TimeSpan.FromSeconds(TotalSeconds);

    public string FormattedDuration
    {
        get
        {
            var ts = Duration;
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }
    }
}
