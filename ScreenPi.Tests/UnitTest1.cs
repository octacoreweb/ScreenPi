using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharedData.Data;
using SharedData.Models;
using TrackerService;

namespace ScreenPi.Tests;

[TestClass]
public class SharedDataAndTrackerTests
{
    private string _testDbPath = string.Empty;
    private string _connectionString = string.Empty;
    private SessionRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"screenpi_test_{Guid.NewGuid()}.db");
        _connectionString = $"Data Source={_testDbPath}";

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            // Enable WAL mode
            using var walCmd = connection.CreateCommand();
            walCmd.CommandText = "PRAGMA journal_mode=WAL;";
            walCmd.ExecuteNonQuery();

            // Create schema
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
        }

        _repository = new SessionRepository(_connectionString);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
            var walFile = _testDbPath + "-wal";
            if (File.Exists(walFile)) File.Delete(walFile);
            var shmFile = _testDbPath + "-shm";
            if (File.Exists(shmFile)) File.Delete(shmFile);
        }
        catch { }
    }

    [TestMethod]
    public void Sqlite_WalMode_IsEnabled()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var mode = cmd.ExecuteScalar()?.ToString();

        Assert.AreEqual("wal", mode?.ToLowerInvariant());
    }

    [TestMethod]
    public void SessionRepository_InsertAndGetId_GeneratesValidId()
    {
        var log = new SessionLog
        {
            ProcessName = "code",
            WindowTitle = "Visual Studio Code",
            StartTime = DateTime.UtcNow,
            DurationSeconds = 120
        };

        var id = _repository.InsertAndGetId(log);
        Assert.IsTrue(id > 0);

        // Update it
        log.Id = id;
        log.DurationSeconds = 300;
        log.EndTime = DateTime.UtcNow;
        _repository.Update(log);

        var total = _repository.GetTotalScreenTime(DateTime.Today);
        Assert.AreEqual(300, total, 1.0);
    }

    [TestMethod]
    public void SessionRepository_DailySummary_GroupsAndSortsByDuration()
    {
        var today = DateTime.Today;

        _repository.Insert(new SessionLog
        {
            ProcessName = "chrome",
            WindowTitle = "Google Chrome",
            StartTime = today.AddHours(9),
            EndTime = today.AddHours(10),
            DurationSeconds = 3600
        });

        _repository.Insert(new SessionLog
        {
            ProcessName = "code",
            WindowTitle = "VS Code",
            StartTime = today.AddHours(10),
            EndTime = today.AddHours(12),
            DurationSeconds = 7200
        });

        _repository.Insert(new SessionLog
        {
            ProcessName = "chrome",
            WindowTitle = "Another Chrome Tab",
            StartTime = today.AddHours(13),
            EndTime = today.AddHours(14),
            DurationSeconds = 1800
        });

        var summaries = _repository.GetDailySummary(today);

        Assert.AreEqual(2, summaries.Count);
        // "code" should be first with 7200s
        Assert.AreEqual("code", summaries[0].ProcessName);
        Assert.AreEqual(7200, summaries[0].TotalSeconds);
        Assert.AreEqual(1, summaries[0].SessionCount);

        // "chrome" should be second with 3600 + 1800 = 5400s
        Assert.AreEqual("chrome", summaries[1].ProcessName);
        Assert.AreEqual(5400, summaries[1].TotalSeconds);
        Assert.AreEqual(2, summaries[1].SessionCount);

        var total = _repository.GetTotalScreenTime(today);
        Assert.AreEqual(12600, total);
    }

    [TestMethod]
    public void NativeMethods_GetTickCount_ReturnsNonZero()
    {
        uint tick = NativeMethods.GetTickCount();
        Assert.IsTrue(tick > 0, "GetTickCount should return a non-zero system tick count");
    }

    [TestMethod]
    public void NativeMethods_GetLastInputInfo_Succeeds()
    {
        var lii = new NativeMethods.LASTINPUTINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LASTINPUTINFO>()
        };

        bool success = NativeMethods.GetLastInputInfo(ref lii);
        Assert.IsTrue(success, "GetLastInputInfo should succeed on Windows");
        Assert.IsTrue(lii.dwTime > 0, "Last input time tick should be greater than 0");
    }

    [TestMethod]
    public void AppUsageSummary_FormattedDuration_FormatsCorrectly()
    {
        var summary1 = new AppUsageSummary { TotalSeconds = 45 };
        Assert.AreEqual("45s", summary1.FormattedDuration);

        var summary2 = new AppUsageSummary { TotalSeconds = 125 };
        Assert.AreEqual("2m 5s", summary2.FormattedDuration);

        var summary3 = new AppUsageSummary { TotalSeconds = 3665 };
        Assert.AreEqual("1h 1m", summary3.FormattedDuration);
    }
}