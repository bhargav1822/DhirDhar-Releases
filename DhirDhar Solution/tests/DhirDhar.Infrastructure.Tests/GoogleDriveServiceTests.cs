using System;
using System.IO;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Backup;
using DhirDhar.Infrastructure.Backup;
using DhirDhar.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class GoogleDriveServiceTests
{
    private readonly TestPathService _pathService;
    private readonly GoogleDriveService _service;

    public GoogleDriveServiceTests()
    {
        _pathService = new TestPathService();
        var backupOptions = Options.Create(new BackupOptions());

        _service = new GoogleDriveService(
            _pathService,
            null!,
            backupOptions,
            NullLogger<GoogleDriveService>.Instance);
    }

    [Fact]
    public void IsConnected_InitiallyFalse()
    {
        Assert.False(_service.IsConnected);
        Assert.False(_service.IsConnecting);
        Assert.False(_service.IsUploading);
        Assert.Null(_service.ConnectedEmail);
        Assert.Equal(GoogleDriveOAuthState.NotConnected, _service.State);
    }

    [Fact]
    public async Task InitializeAsync_WhenNoTokensExist_ReturnsFalse()
    {
        var result = await _service.InitializeAsync();
        Assert.False(result);
        Assert.False(_service.IsConnected);
        Assert.Equal(GoogleDriveOAuthState.NotConnected, _service.State);
    }

    [Fact]
    public async Task DisconnectAsync_ResetsProperties()
    {
        await _service.DisconnectAsync();
        Assert.False(_service.IsConnected);
        Assert.Null(_service.ConnectedEmail);
        Assert.Equal(GoogleDriveOAuthState.Disconnected, _service.State);
    }

    [Fact]
    public async Task ListCloudBackupsAsync_WhenDisconnected_ReturnsEmpty()
    {
        var backups = await _service.ListCloudBackupsAsync();
        Assert.Empty(backups);
    }

    [Fact]
    public async Task CleanupOldCloudBackupsAsync_WhenDisconnected_CompletesWithoutError()
    {
        var exception = await Record.ExceptionAsync(async () =>
        {
            await _service.CleanupOldCloudBackupsAsync(3);
        });
        Assert.Null(exception);
    }

    [Fact]
    public async Task EncryptedFileDataStore_StoreAndRetrieve_Succeeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dhirdhar-test-tokens-" + Guid.NewGuid().ToString("N"));
        var store = new EncryptedFileDataStore(tempDir);
        try
        {
            var testToken = new Google.Apis.Auth.OAuth2.Responses.TokenResponse
            {
                AccessToken = "test-access-token",
                RefreshToken = "test-refresh-token",
                TokenType = "Bearer",
                ExpiresInSeconds = 3600
            };

            await store.StoreAsync("user", testToken);
            var retrieved = await store.GetAsync<Google.Apis.Auth.OAuth2.Responses.TokenResponse>("user");

            Assert.NotNull(retrieved);
            Assert.Equal("test-access-token", retrieved.AccessToken);
            Assert.Equal("test-refresh-token", retrieved.RefreshToken);

            await store.DeleteAsync<Google.Apis.Auth.OAuth2.Responses.TokenResponse>("user");
            var deleted = await store.GetAsync<Google.Apis.Auth.OAuth2.Responses.TokenResponse>("user");
            Assert.Null(deleted);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private sealed class TestPathService : IDatabasePathService
    {
        public string AppDataDirectory => Path.GetTempPath();
        public string ApplicationDataDirectory => Path.GetTempPath();
        public string DatabaseDirectory => Path.GetTempPath();
        public string DatabasePath => Path.Combine(Path.GetTempPath(), "test.db");
        public string BackupDirectory => Path.Combine(Path.GetTempPath(), "dhirdhar-test-backups");
        public string LogDirectory => Path.Combine(Path.GetTempPath(), "dhirdhar-test-logs");
    }

    [Fact]
    public async Task GetClientSecrets_ResolvesValidClientIdAndSecret()
    {
        var backupOptions = Options.Create(new BackupOptions());
        var service = new GoogleDriveService(
            _pathService,
            null!,
            backupOptions,
            NullLogger<GoogleDriveService>.Instance);

        // Access internal/private GetClientSecretsAsync via reflection to verify client_id and client_secret are non-empty
        var method = typeof(GoogleDriveService).GetMethod("GetClientSecretsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var task = method.Invoke(service, new object[] { default(System.Threading.CancellationToken) }) as Task<Google.Apis.Auth.OAuth2.ClientSecrets?>;
        Assert.NotNull(task);

        var secrets = await task;
        Assert.NotNull(secrets);
        Assert.False(string.IsNullOrWhiteSpace(secrets.ClientId));
        Assert.False(string.IsNullOrWhiteSpace(secrets.ClientSecret));
        Assert.Contains("googleusercontent.com", secrets.ClientId);
        Assert.StartsWith("GOCSPX", secrets.ClientSecret);
    }

    [Fact]
    public async Task GoogleDriveService_StateChanges_FiresConnectionStateChanged()
    {
        bool eventFired = false;
        _service.ConnectionStateChanged += (s, e) => eventFired = true;

        // Disconnecting fires the state change event
        await _service.DisconnectAsync();
        Assert.True(eventFired);
        Assert.Equal(GoogleDriveOAuthState.Disconnected, _service.State);
    }

    [Fact]
    public void GoogleDriveService_DownloadProperties_InitialValuesCorrect()
    {
        Assert.False(_service.IsDownloading);
        Assert.Equal(0, _service.DownloadProgressPercent);
    }

    [Fact]
    public async Task DownloadBackupAsync_WhenDisconnected_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _service.DownloadBackupAsync("test-id", "test-dest.ddbackup");
        });
    }

    [Fact]
    public async Task RestoreFromCloudAsync_WhenDisconnected_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _service.RestoreFromCloudAsync("test-id");
        });
    }
}

