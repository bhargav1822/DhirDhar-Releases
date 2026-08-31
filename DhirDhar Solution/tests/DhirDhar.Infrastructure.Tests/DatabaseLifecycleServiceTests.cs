using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Caching;
using DhirDhar.Infrastructure.Caching;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class DatabaseLifecycleServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dataDir;
    private readonly string _dbPath;
    private readonly TestPathService _pathService;
    private readonly ICacheService _cacheService;
    private readonly DatabaseLifecycleService _lifecycleService;

    public DatabaseLifecycleServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dhirdhar-lifecycle-tests-" + Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_tempDir, "Data");
        Directory.CreateDirectory(_dataDir);

        _dbPath = Path.Combine(_dataDir, "DhirDhar.db");
        CreateSampleDatabase(_dbPath, "InitialUser");

        _pathService = new TestPathService(_tempDir, _dbPath, Path.Combine(_tempDir, "Backup"));
        _cacheService = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<MemoryCacheService>.Instance);
        _lifecycleService = new DatabaseLifecycleService(_pathService, _cacheService, NullLogger<DatabaseLifecycleService>.Instance);
    }

    private static void CreateSampleDatabase(string path, string borrowerName)
    {
        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadWriteCreate;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            CREATE TABLE IF NOT EXISTS Borrowers (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Phone TEXT NOT NULL,
                Status INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Transactions (
                Id TEXT PRIMARY KEY,
                Amount REAL NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Loans (
                Id TEXT PRIMARY KEY
            );
            CREATE TABLE IF NOT EXISTS ApplicationSettings (
                Id TEXT PRIMARY KEY,
                Value TEXT
            );
            DELETE FROM Borrowers;
            INSERT INTO Borrowers (Id, Name, Phone, Status) VALUES ('B1', '{borrowerName}', '9876543210', 0);
        ";
        cmd.ExecuteNonQuery();
        conn.Close();
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task EnterRestoreMode_PausesRegisteredWorkers_AndSetsRestoreModeActive()
    {
        var testWorker = new MockPausableWorker("Worker1");
        _lifecycleService.RegisterPausableWorker(testWorker);

        Assert.False(_lifecycleService.IsRestoreModeActive);
        Assert.False(testWorker.IsPaused);

        using (var session = await _lifecycleService.EnterRestoreModeAsync())
        {
            Assert.True(_lifecycleService.IsRestoreModeActive);
            Assert.True(testWorker.IsPaused);
        }

        Assert.False(_lifecycleService.IsRestoreModeActive);
        Assert.True(testWorker.IsResumed);
    }

    [Fact]
    public async Task ReplaceDatabaseAtomically_ReplacesDatabaseSuccessfully_AndRaisesRestoredEvent()
    {
        var stagedDbPath = Path.Combine(_tempDir, "Staged.db");
        CreateSampleDatabase(stagedDbPath, "RestoredBorrowerName");

        bool eventRaised = false;
        _lifecycleService.DatabaseRestored += (s, e) => eventRaised = true;

        using (var session = await _lifecycleService.EnterRestoreModeAsync())
        {
            await _lifecycleService.ReplaceDatabaseAtomicallyAsync(stagedDbPath);
        }

        Assert.True(eventRaised);

        // Verify data in active database
        using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Name FROM Borrowers WHERE Id = 'B1';";
        var name = cmd.ExecuteScalar()?.ToString();
        Assert.Equal("RestoredBorrowerName", name);
    }

    [Fact]
    public async Task ReplaceDatabaseAtomically_WhenStagedDatabaseIsCorrupted_RollsBackToOriginal()
    {
        var corruptedStagedDbPath = Path.Combine(_tempDir, "CorruptedStaged.db");
        await File.WriteAllBytesAsync(corruptedStagedDbPath, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 });

        using (var session = await _lifecycleService.EnterRestoreModeAsync())
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _lifecycleService.ReplaceDatabaseAtomicallyAsync(corruptedStagedDbPath));

            Assert.Contains("failed", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Verify active database is still intact with original data
        using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Name FROM Borrowers WHERE Id = 'B1';";
        var name = cmd.ExecuteScalar()?.ToString();
        Assert.Equal("InitialUser", name);
    }

    [Fact]
    public async Task ReplaceDatabaseAtomically_WhenStagedDatabaseIsMissingRequiredTables_RollsBackToOriginal()
    {
        var incompleteStagedDbPath = Path.Combine(_tempDir, "Incomplete.db");
        using (var setupConn = new SqliteConnection($"Data Source={incompleteStagedDbPath};Mode=ReadWriteCreate;Pooling=False"))
        {
            setupConn.Open();
            using var setupCmd = setupConn.CreateCommand();
            setupCmd.CommandText = "CREATE TABLE OtherTable (Id INT);";
            setupCmd.ExecuteNonQuery();
            setupConn.Close();
        }

        using (var session = await _lifecycleService.EnterRestoreModeAsync())
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _lifecycleService.ReplaceDatabaseAtomicallyAsync(incompleteStagedDbPath));

            Assert.Contains("missing essential schema tables", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Verify active database is still intact with original data
        using var verifyConn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly;Pooling=False");
        verifyConn.Open();
        using var verifyCmd = verifyConn.CreateCommand();
        verifyCmd.CommandText = "SELECT Name FROM Borrowers WHERE Id = 'B1';";
        var name = verifyCmd.ExecuteScalar()?.ToString();
        Assert.Equal("InitialUser", name);
    }

    [Fact]
    public async Task MutexProtection_PreventsSimultaneousRestoreAcrossSessions()
    {
        using var session1 = await _lifecycleService.EnterRestoreModeAsync();

        // Create second instance simulating another process or background worker thread
        var secondaryService = new DatabaseLifecycleService(_pathService, _cacheService, NullLogger<DatabaseLifecycleService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(async () =>
        {
            using var session2 = await secondaryService.EnterRestoreModeAsync();
        }));

        Assert.Contains("DhirDhar is already running", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    private sealed class MockPausableWorker : IPausableDatabaseWorker
    {
        public string WorkerName { get; }
        public bool IsPaused { get; private set; }
        public bool IsResumed { get; private set; }

        public MockPausableWorker(string name)
        {
            WorkerName = name;
        }

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            IsPaused = true;
            IsResumed = false;
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken = default)
        {
            IsPaused = false;
            IsResumed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TestPathService : IDatabasePathService
    {
        public TestPathService(string appDataDir, string dbPath, string backupDir)
        {
            ApplicationDataDirectory = appDataDir;
            DatabasePath = dbPath;
            BackupDirectory = backupDir;
            DatabaseDirectory = Path.GetDirectoryName(dbPath)!;
            LogDirectory = Path.Combine(appDataDir, "Logs");
        }

        public string ApplicationDataDirectory { get; }
        public string DatabaseDirectory { get; }
        public string DatabasePath { get; }
        public string BackupDirectory { get; }
        public string LogDirectory { get; }
    }
}
