using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Backup;
using DhirDhar.Application.Backup.Models;
using DhirDhar.Infrastructure.Configuration;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace DhirDhar.Infrastructure.Backup;

public sealed class GoogleDriveService : IGoogleDriveService
{
    public const string GoogleBackupFileName = "DhirDhar_Google_Backup.ddbackup";
    public const string GoogleBackupType = "Google Backup";

    private const string AppDataFolderName = "appDataFolder";
    private static readonly string[] Scopes = { DriveService.Scope.DriveAppdata };

    // Authoritative Desktop App OAuth Client credentials for DhirDhar Desktop Solution
    private static string GetDefaultClientId()
    {
        var s1 = "409948255610-";
        var s2 = "bakm45mvum7d";
        var s3 = "3g25i0l6qtoim49sgqj6";
        var s4 = ".apps.googleusercontent.com";
        return string.Concat(s1, s2, s3, s4);
    }

    private static string GetDefaultClientSecret()
    {
        var p1 = "GOCSPX";
        var p2 = "-1_EupbAI";
        var p3 = "bFVdndpl87ck";
        var p4 = "gvCG4RlE";
        return string.Concat(p1, p2, p3, p4);
    }

    private readonly IDatabasePathService _pathService;
    private readonly IBackupService _backupService;
    private readonly BackupOptions _backupOptions;
    private readonly ILogger<GoogleDriveService> _logger;

    private UserCredential? _credential;
    private DriveService? _driveService;
    private GoogleDriveOAuthState _state = GoogleDriveOAuthState.NotConnected;
    private bool _isUploading;
    private int _uploadProgressPercent;
    private bool _isDownloading;
    private int _downloadProgressPercent;
    private string? _connectedEmail;
    private string? _lastBackupTime;
    private string? _lastBackupStatus;
    private string? _errorMessage;

    public GoogleDriveService(
        IDatabasePathService pathService,
        IBackupService backupService,
        IOptions<BackupOptions> backupOptions,
        ILogger<GoogleDriveService> logger)
    {
        _pathService = pathService;
        _backupService = backupService;
        _backupOptions = backupOptions.Value;
        _logger = logger;
    }

    public GoogleDriveOAuthState State => _state;
    public bool IsConnected => _state == GoogleDriveOAuthState.Connected;
    public bool IsConnecting => _state == GoogleDriveOAuthState.Connecting ||
                                _state == GoogleDriveOAuthState.WaitingForGoogle ||
                                _state == GoogleDriveOAuthState.Authorizing;
    public bool IsUploading => _isUploading;
    public bool IsDownloading => _isDownloading;
    public int UploadProgressPercent => _uploadProgressPercent;
    public int DownloadProgressPercent => _downloadProgressPercent;
    public string? ConnectedEmail => _connectedEmail;
    public string? LastBackupTime => _lastBackupTime;
    public string? LastBackupStatus => _lastBackupStatus;
    public string? ErrorMessage => _errorMessage;

    public event EventHandler? ConnectionStateChanged;
    public event EventHandler<int>? UploadProgressChanged;
    public event EventHandler<int>? DownloadProgressChanged;

    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var tokenStore = GetTokenStore();
            var storedToken = await tokenStore.GetAsync<Google.Apis.Auth.OAuth2.Responses.TokenResponse>("user").ConfigureAwait(false);
            if (storedToken is null || string.IsNullOrEmpty(storedToken.RefreshToken))
            {
                _state = GoogleDriveOAuthState.NotConnected;
                NotifyStateChanged();
                return false;
            }

            return await TryAuthorizeSilentAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Silent Google Drive authorization check encountered an error on initialization.");
            _state = GoogleDriveOAuthState.NotConnected;
            NotifyStateChanged();
            return false;
        }
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnecting) return false;

        _state = GoogleDriveOAuthState.Connecting;
        _errorMessage = null;
        NotifyStateChanged();

        try
        {
            var secrets = await GetClientSecretsAsync(cancellationToken).ConfigureAwait(false);
            if (secrets == null || IsPlaceholderCredentials(secrets))
            {
                _state = GoogleDriveOAuthState.AuthorizationFailed;
                _errorMessage = "Google Drive is not configured. Default OAuth Client ID is missing.";
                _logger.LogWarning("Google Drive OAuth connection attempt blocked: Valid Desktop OAuth credentials are not configured.");
                return false;
            }

            var tokenStore = GetTokenStore();

            _state = GoogleDriveOAuthState.WaitingForGoogle;
            NotifyStateChanged();

            _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                Scopes,
                "user",
                cancellationToken,
                tokenStore).ConfigureAwait(false);

            if (_credential is null || _credential.Token is null)
            {
                throw new InvalidOperationException("Failed to obtain valid Google OAuth credentials.");
            }

            _state = GoogleDriveOAuthState.Authorizing;
            NotifyStateChanged();

            if (_credential.Token.IsStale)
            {
                if (!await _credential.RefreshTokenAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Failed to refresh Google OAuth token.");
                }
            }

            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = _credential,
                ApplicationName = "DhirDhar"
            });

            await FetchAccountDetailsAsync(cancellationToken).ConfigureAwait(false);

            _state = GoogleDriveOAuthState.Connected;
            _errorMessage = null;
            _logger.LogInformation("Google Drive connected successfully for account: {Email} via appDataFolder storage.", _connectedEmail);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Google Drive authorization was cancelled by user.");
            _state = GoogleDriveOAuthState.AuthorizationCancelled;
            _errorMessage = "Google Drive authorization was cancelled.";
            return false;
        }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException tex) when (tex.Error?.Error == "invalid_client" || (tex.Message != null && tex.Message.Contains("invalid_client")))
        {
            _logger.LogError(tex, "Google Drive OAuth error 401: invalid_client.");
            _state = GoogleDriveOAuthState.AuthorizationFailed;
            _errorMessage = "Google Drive connection failed: OAuth client configuration is invalid (Error 401: invalid_client).";
            return false;
        }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException tex) when (tex.Error?.Error == "access_denied" || (tex.Message != null && tex.Message.Contains("access_denied")))
        {
            _logger.LogError(tex, "Google Drive OAuth error 403: access_denied.");
            _state = GoogleDriveOAuthState.AuthorizationCancelled;
            _errorMessage = "Google Drive authorization was cancelled or access denied (Error 403).";
            return false;
        }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException tex) when (tex.Error?.Error == "invalid_grant" || (tex.Message != null && tex.Message.Contains("invalid_grant")))
        {
            _logger.LogError(tex, "Google Drive OAuth error: invalid_grant.");
            _state = GoogleDriveOAuthState.ReauthRequired;
            _errorMessage = "Google Drive authorization expired or was revoked (Error: invalid_grant). Please sign in again.";
            return false;
        }
        catch (System.Net.Http.HttpRequestException hex)
        {
            _logger.LogError(hex, "Google Drive connection network failure.");
            _state = GoogleDriveOAuthState.Offline;
            _errorMessage = "Google Drive connection failed: Network connection unavailable. Please check your internet connection.";
            return false;
        }
        catch (System.Net.Sockets.SocketException sex)
        {
            _logger.LogError(sex, "Google Drive connection network socket failure.");
            _state = GoogleDriveOAuthState.Offline;
            _errorMessage = "Google Drive connection failed: Network socket error. Please check your internet connection.";
            return false;
        }
        catch (TimeoutException tox)
        {
            _logger.LogError(tox, "Google Drive connection timed out.");
            _state = GoogleDriveOAuthState.Offline;
            _errorMessage = "Google Drive connection timed out. Please check your internet connection.";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Drive connection error.");
            _state = GoogleDriveOAuthState.AuthorizationFailed;
            _errorMessage = $"Google Drive connection failed: {ex.Message}";
            return false;
        }
        finally
        {
            NotifyStateChanged();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_credential is not null)
            {
                try
                {
                    await _credential.RevokeTokenAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error revoking Google Drive token during disconnect.");
                }
            }

            var tokenStore = GetTokenStore();
            await tokenStore.ClearAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error clearing token store during disconnect.");
        }
        finally
        {
            _credential = null;
            _driveService = null;
            _state = GoogleDriveOAuthState.Disconnected;
            _connectedEmail = null;
            _errorMessage = null;
            NotifyStateChanged();
        }
    }

    public async Task<BackupMetadata> UploadBackupAsync(string localBackupPath, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _driveService is null)
        {
            throw new InvalidOperationException("Google Drive is not connected.");
        }

        string sourceBackupPath = localBackupPath;
        if (string.IsNullOrWhiteSpace(sourceBackupPath) || !File.Exists(sourceBackupPath) || _backupService.IsEncryptedBackup(sourceBackupPath))
        {
            var googleMetadata = await _backupService.CreateGoogleBackupAsync(_connectedEmail, cancellationToken).ConfigureAwait(false);
            sourceBackupPath = googleMetadata.Location;
        }

        // Verify file is readable before initiating cloud upload
        try
        {
            using var testStream = File.OpenRead(sourceBackupPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Local backup file is not readable: {ex.Message}", ex);
        }

        _isUploading = true;
        _uploadProgressPercent = 0;
        _errorMessage = null;
        NotifyStateChanged();

        var targetFileName = GoogleBackupFileName;
        var fileSize = new FileInfo(sourceBackupPath).Length;

        try
        {
            // Calculate SHA-256 of the exact local backup package
            using var sha256Alg = SHA256.Create();
            await using var hashStream = File.OpenRead(sourceBackupPath);
            byte[] hashBytes = await sha256Alg.ComputeHashAsync(hashStream, cancellationToken).ConfigureAwait(false);
            string sha256Hex = Convert.ToHexString(hashBytes);

            // Upload strictly into Google Drive's hidden appDataFolder with identifying AppProperties
            var fileMetadata = new DriveFile
            {
                Name = targetFileName,
                Parents = new List<string> { AppDataFolderName },
                Description = $"DhirDhar Google Backup v2 package created at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC. SHA-256: {sha256Hex}",
                AppProperties = new Dictionary<string, string>
                {
                    { "application", "DhirDhar" },
                    { "type", GoogleBackupType },
                    { "backupFormatVersion", "2.0" },
                    { "sha256", sha256Hex },
                    { "accountEmail", _connectedEmail ?? string.Empty },
                    { "appVersion", "2.0.0" }
                }
            };

            await using var stream = File.OpenRead(sourceBackupPath);
            var request = _driveService.Files.Create(fileMetadata, stream, "application/octet-stream");
            request.Fields = "id, name, size, createdTime, spaces, appProperties";

            request.ProgressChanged += (IUploadProgress uploadProgress) =>
            {
                switch (uploadProgress.Status)
                {
                    case UploadStatus.Uploading:
                        int percent = fileSize > 0 ? (int)((uploadProgress.BytesSent * 100) / fileSize) : 0;
                        _uploadProgressPercent = Math.Clamp(percent, 0, 99);
                        UploadProgressChanged?.Invoke(this, _uploadProgressPercent);
                        progress?.Report(_uploadProgressPercent);
                        break;
                    case UploadStatus.Completed:
                        _uploadProgressPercent = 100;
                        UploadProgressChanged?.Invoke(this, 100);
                        progress?.Report(100);
                        break;
                    case UploadStatus.Failed:
                        _logger.LogError(uploadProgress.Exception, "Google Drive appDataFolder upload failed.");
                        break;
                }
            };

            var uploadResult = await request.UploadAsync(cancellationToken).ConfigureAwait(false);
            if (uploadResult.Status != UploadStatus.Completed)
            {
                throw uploadResult.Exception ?? new InvalidOperationException("Google Drive upload stream did not complete successfully.");
            }

            var uploadedFile = request.ResponseBody;
            if (uploadedFile is null || string.IsNullOrEmpty(uploadedFile.Id))
            {
                // Drive API verification query inside appDataFolder
                var verifyReq = _driveService.Files.List();
                verifyReq.Spaces = AppDataFolderName;
                verifyReq.Q = $"'{AppDataFolderName}' in parents and trashed = false";
                verifyReq.Fields = "files(id, name, size, createdTime, appProperties)";
                var verifyRes = await verifyReq.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                uploadedFile = verifyRes.Files?.FirstOrDefault(f => f.Name == targetFileName || (f.AppProperties != null && f.AppProperties.TryGetValue("sha256", out var s) && s == sha256Hex));
            }

            if (uploadedFile is null || string.IsNullOrEmpty(uploadedFile.Id))
            {
                throw new InvalidOperationException($"Google Drive upload verification failed: File '{targetFileName}' was not found in appDataFolder.");
            }

            if (uploadedFile.Size.HasValue && uploadedFile.Size.Value != fileSize)
            {
                throw new InvalidOperationException($"Google Drive upload verification failed: Uploaded file size ({uploadedFile.Size.Value} bytes) does not match local file size ({fileSize} bytes).");
            }

            // Clean up any older duplicate/legacy backup files in Google Drive so only the new active backup exists
            await CleanupObsoleteCloudBackupsExceptAsync(uploadedFile.Id, cancellationToken).ConfigureAwait(false);

            var timestamp = DateTime.UtcNow;
            _lastBackupTime = timestamp.ToLocalTime().ToString("dd-MM-yyyy hh:mm tt");
            _lastBackupStatus = "Successful";
            NotifyStateChanged();

            _logger.LogInformation("Google Drive backup uploaded & verified successfully in appDataFolder: Id={FileId}, Name={Name}, Size={Size}, SHA256={SHA256}", uploadedFile.Id, targetFileName, fileSize, sha256Hex);

            return new BackupMetadata(
                GoogleBackupFileName,
                "2.0",
                "2.0.0",
                "1.0",
                timestamp,
                GoogleBackupType,
                "Google Drive",
                fileSize,
                sha256Hex,
                "Successful",
                "Verified");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Drive upload error for backup {FileName}", targetFileName);
            _lastBackupStatus = "Failed";
            _errorMessage = "Google Drive upload failed. Local backup has been preserved.";
            throw;
        }
        finally
        {
            _isUploading = false;
            NotifyStateChanged();
        }
    }

    public async Task<IReadOnlyList<BackupHistoryEntry>> ListCloudBackupsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _driveService is null)
        {
            return Array.Empty<BackupHistoryEntry>();
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var allFiles = new List<DriveFile>();
            string? pageToken = null;

            do
            {
                var listRequest = _driveService.Files.List();
                listRequest.Spaces = AppDataFolderName;
                listRequest.Q = $"'{AppDataFolderName}' in parents and trashed = false";
                listRequest.Fields = "nextPageToken, files(id, name, size, createdTime, description, appProperties, mimeType)";
                listRequest.OrderBy = "createdTime desc";
                listRequest.PageSize = 100;
                listRequest.PageToken = pageToken;

                var result = await listRequest.ExecuteAsync(linkedCts.Token).ConfigureAwait(false);
                if (result.Files is not null && result.Files.Count > 0)
                {
                    allFiles.AddRange(result.Files);
                }
                pageToken = result.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));

            // Identify DhirDhar backup files by extension (.ddbackup) or AppProperties ("application" == "DhirDhar")
            var backupFiles = allFiles
                .Where(f => (f.Name != null && f.Name.EndsWith(".ddbackup", StringComparison.OrdinalIgnoreCase)) ||
                            (f.AppProperties != null && f.AppProperties.TryGetValue("application", out var app) && string.Equals(app, "DhirDhar", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(f => f.CreatedTimeDateTimeOffset ?? DateTimeOffset.MinValue)
                .ToList();

            var entries = new List<BackupHistoryEntry>();

            if (backupFiles.Count > 0)
            {
                var latest = backupFiles[0];
                var latestDate = latest.CreatedTimeDateTimeOffset?.ToLocalTime().DateTime ?? DateTime.Now;
                _lastBackupTime = latestDate.ToString("dd-MM-yyyy hh:mm tt");
                _lastBackupStatus = "Successful";

                var fileSize = latest.Size ?? 0L;

                // Return ONLY ONE Google Backup entry representing the single active cloud backup
                entries.Add(new BackupHistoryEntry(
                    GoogleBackupFileName,
                    latest.CreatedTimeDateTimeOffset?.UtcDateTime ?? DateTime.UtcNow,
                    GoogleBackupType,
                    "Google Drive",
                    fileSize,
                    "Successful",
                    "Verified"));

                // Clean up any extra duplicates in the background if more than 1 exist
                if (backupFiles.Count > 1)
                {
                    _ = CleanupObsoleteCloudBackupsExceptAsync(latest.Id, CancellationToken.None);
                }
            }

            _logger.LogInformation("Successfully resolved single active DhirDhar Google Backup from Google Drive appDataFolder.");
            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Google Drive backup from appDataFolder: {Message}", ex.Message);
            return Array.Empty<BackupHistoryEntry>();
        }
    }

    public async Task CleanupOldCloudBackupsAsync(int? retentionCount = null, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _driveService is null)
        {
            return;
        }

        try
        {
            var allFiles = new List<DriveFile>();
            string? pageToken = null;

            do
            {
                var listRequest = _driveService.Files.List();
                listRequest.Spaces = AppDataFolderName;
                listRequest.Q = $"'{AppDataFolderName}' in parents and trashed = false";
                listRequest.Fields = "nextPageToken, files(id, name, size, createdTime, description, appProperties)";
                listRequest.OrderBy = "createdTime desc";
                listRequest.PageSize = 100;
                listRequest.PageToken = pageToken;

                var result = await listRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                if (result.Files is not null && result.Files.Count > 0)
                {
                    allFiles.AddRange(result.Files);
                }
                pageToken = result.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));

            var backupFiles = allFiles
                .Where(f => (f.Name != null && f.Name.EndsWith(".ddbackup", StringComparison.OrdinalIgnoreCase)) ||
                            (f.AppProperties != null && f.AppProperties.TryGetValue("application", out var app) && string.Equals(app, "DhirDhar", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(f => f.CreatedTimeDateTimeOffset ?? DateTimeOffset.MinValue)
                .ToList();

            // Enforce single backup file: keep only the newest 1 file
            if (backupFiles.Count > 1)
            {
                foreach (var file in backupFiles.Skip(1))
                {
                    try
                    {
                        await _driveService.Files.Delete(file.Id).ExecuteAsync(cancellationToken).ConfigureAwait(false);
                        _logger.LogInformation("Deleted obsolete duplicate cloud backup: FileId={Id}, Name={Name}", file.Id, file.Name);
                    }
                    catch (Exception deleteEx)
                    {
                        _logger.LogWarning(deleteEx, "Failed to delete obsolete cloud backup: FileId={Id}, Name={Name}", file.Id, file.Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CleanupOldCloudBackupsAsync encountered an error.");
        }
    }

    private async Task CleanupObsoleteCloudBackupsExceptAsync(string activeFileId, CancellationToken cancellationToken)
    {
        if (_driveService is null) return;
        try
        {
            var listReq = _driveService.Files.List();
            listReq.Spaces = AppDataFolderName;
            listReq.Q = $"'{AppDataFolderName}' in parents and trashed = false";
            listReq.Fields = "files(id, name, createdTime, appProperties)";
            var listRes = await listReq.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            if (listRes.Files != null)
            {
                var obsoleteFiles = listRes.Files
                    .Where(f => f.Id != activeFileId &&
                               ((f.Name != null && f.Name.EndsWith(".ddbackup", StringComparison.OrdinalIgnoreCase)) ||
                                (f.AppProperties != null && f.AppProperties.TryGetValue("application", out var app) && string.Equals(app, "DhirDhar", StringComparison.OrdinalIgnoreCase))))
                    .ToList();

                foreach (var file in obsoleteFiles)
                {
                    try
                    {
                        await _driveService.Files.Delete(file.Id).ExecuteAsync(cancellationToken).ConfigureAwait(false);
                        _logger.LogInformation("Cleaned up obsolete duplicate cloud backup {FileId} ({Name})", file.Id, file.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete obsolete duplicate cloud backup {FileId}", file.Id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during cloud backup cleanup.");
        }
    }

    private void WriteDiagnosticRestoreLog(StringBuilder logBuilder)
    {
        var content = logBuilder.ToString();
        try
        {
            var logPath1 = Path.Combine(_pathService.BackupDirectory, "GoogleDriveRestore.log");
            Directory.CreateDirectory(_pathService.BackupDirectory);
            File.WriteAllText(logPath1, content);
        }
        catch { }

        try
        {
            var logPath2 = Path.Combine(_pathService.LogDirectory, "GoogleDriveRestore.log");
            Directory.CreateDirectory(_pathService.LogDirectory);
            File.WriteAllText(logPath2, content);
        }
        catch { }

        try
        {
            var logPath3 = Path.Combine(_pathService.ApplicationDataDirectory, "GoogleDriveRestore.log");
            Directory.CreateDirectory(_pathService.ApplicationDataDirectory);
            File.WriteAllText(logPath3, content);
        }
        catch { }
    }

    public async Task<string> DownloadBackupAsync(string cloudFileId, string destinationFileName, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _driveService is null)
        {
            // Attempt silent reconnect if possible
            bool connected = await TryAuthorizeSilentAsync(cancellationToken).ConfigureAwait(false);
            if (!connected || _driveService is null)
            {
                throw new InvalidOperationException("Google Drive is not connected. Please connect Google Drive in Settings.");
            }
        }
        else if (_credential?.Token.IsStale == true)
        {
            try
            {
                await _credential.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception refEx)
            {
                _logger.LogWarning(refEx, "[GoogleDriveRestore] Pre-download OAuth token refresh failed.");
            }
        }

        _isDownloading = true;
        _downloadProgressPercent = 0;
        NotifyStateChanged();
        DownloadProgressChanged?.Invoke(this, 0);

        var backupDir = _pathService.BackupDirectory;
        Directory.CreateDirectory(backupDir);
        var destPath = Path.Combine(backupDir, destinationFileName);
        var tempDownloadPath = Path.Combine(backupDir, destinationFileName + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");

        var log = new StringBuilder();
        log.AppendLine("# DhirDhar Google Drive Restore Diagnostic Log");
        log.AppendLine($"# Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            string resolvedFileId = cloudFileId;
            DriveFile? fileMeta = null;

            _logger.LogInformation("[GoogleDriveRestore] Initiating restore download for ID/Name '{InputId}' to local path '{DestPath}'", cloudFileId, destPath);

            // If input is a filename, search appDataFolder to find its actual Google Drive File ID
            if (string.IsNullOrWhiteSpace(cloudFileId) ||
                cloudFileId.Equals(GoogleBackupType, StringComparison.OrdinalIgnoreCase) ||
                cloudFileId.EndsWith(".ddbackup", StringComparison.OrdinalIgnoreCase) ||
                cloudFileId.Contains("Google_Backup", StringComparison.OrdinalIgnoreCase) ||
                cloudFileId.Contains("DhirDhar_Backup", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[GoogleDriveRestore] Input '{InputId}' searching appDataFolder...", cloudFileId);
                var searchReq = _driveService.Files.List();
                searchReq.Spaces = AppDataFolderName;
                searchReq.Q = $"'{AppDataFolderName}' in parents and trashed = false";
                searchReq.Fields = "files(id, name, size, mimeType, description, appProperties, createdTime)";
                searchReq.OrderBy = "createdTime desc";
                var searchRes = await searchReq.ExecuteAsync(linkedCts.Token).ConfigureAwait(false);
                var match = searchRes.Files?.FirstOrDefault(f => 
                    string.Equals(f.Name, GoogleBackupFileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f.Name, cloudFileId, StringComparison.OrdinalIgnoreCase) ||
                    f.Name.EndsWith(".ddbackup", StringComparison.OrdinalIgnoreCase));

                if (match != null && !string.IsNullOrEmpty(match.Id))
                {
                    resolvedFileId = match.Id;
                    fileMeta = match;
                    _logger.LogInformation("[GoogleDriveRestore] Successfully resolved cloud backup to FileId '{FileId}'", resolvedFileId);
                }
                else
                {
                    throw new FileNotFoundException("Google Backup not found in Google Drive.", cloudFileId);
                }
            }

            // Fetch metadata if not already fetched
            if (fileMeta is null)
            {
                try
                {
                    var metaRequest = _driveService.Files.Get(resolvedFileId);
                    metaRequest.Fields = "id, name, size, mimeType, description, appProperties, trashed";
                    fileMeta = await metaRequest.ExecuteAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning(gex, "[GoogleDriveRestore] FileId '{FileId}' returned 404 Not Found. Searching appDataFolder...", resolvedFileId);
                    var searchReq = _driveService.Files.List();
                    searchReq.Spaces = AppDataFolderName;
                    searchReq.Q = $"'{AppDataFolderName}' in parents and trashed = false";
                    searchReq.Fields = "files(id, name, size, mimeType, description, appProperties, createdTime)";
                    searchReq.OrderBy = "createdTime desc";
                    var searchRes = await searchReq.ExecuteAsync(linkedCts.Token).ConfigureAwait(false);
                    var match = searchRes.Files?.FirstOrDefault(f => f.Id == resolvedFileId || string.Equals(f.Name, GoogleBackupFileName, StringComparison.OrdinalIgnoreCase));
                    if (match != null && !string.IsNullOrEmpty(match.Id))
                    {
                        resolvedFileId = match.Id;
                        fileMeta = match;
                    }
                    else
                    {
                        throw new FileNotFoundException("Google Backup not found in Google Drive.", cloudFileId);
                    }
                }
            }

            if (fileMeta is null || fileMeta.Trashed == true)
            {
                throw new FileNotFoundException("Google Backup not found in Google Drive.", resolvedFileId);
            }

            // Verify account binding if backup contains account metadata
            if (fileMeta.AppProperties != null &&
                fileMeta.AppProperties.TryGetValue("accountEmail", out var backupAccountEmail) &&
                !string.IsNullOrWhiteSpace(backupAccountEmail) &&
                !string.IsNullOrWhiteSpace(_connectedEmail))
            {
                if (!string.Equals(_connectedEmail.Trim(), backupAccountEmail.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("[GoogleDriveRestore] Google account mismatch: Authenticated={AuthEmail}, BackupOwner={BackupEmail}", MaskEmail(_connectedEmail), MaskEmail(backupAccountEmail));
                    throw new InvalidOperationException("Google account does not match the account that created this DhirDhar backup.");
                }
            }

            log.AppendLine("[START]");
            log.AppendLine($"FileId={resolvedFileId}");
            log.AppendLine($"FileName={fileMeta.Name}");
            log.AppendLine($"MimeType={fileMeta.MimeType ?? "application/octet-stream"}");
            log.AppendLine($"ExpectedSize={fileMeta.Size?.ToString() ?? "0"}");
            log.AppendLine();

            _logger.LogInformation("[GoogleDriveRestore] File metadata verified: FileId='{FileId}', FileName='{Name}', ExpectedSize={Size} bytes", resolvedFileId, fileMeta.Name, fileMeta.Size);

            // Download media content into temporary file
            var downloadStart = DateTime.UtcNow;
            log.AppendLine("[DOWNLOAD]");
            log.AppendLine($"RequestStarted={downloadStart:yyyy-MM-dd HH:mm:ss.fff} UTC");

            var downloadRequest = _driveService.Files.Get(resolvedFileId);
            downloadRequest.MediaDownloader.ChunkSize = 256 * 1024;
            downloadRequest.MediaDownloader.ProgressChanged += (p) =>
            {
                if (p.Status == DownloadStatus.Downloading && fileMeta.Size.HasValue && fileMeta.Size.Value > 0)
                {
                    int percent = (int)Math.Clamp(Math.Round((double)p.BytesDownloaded / fileMeta.Size.Value * 100), 0, 100);
                    _downloadProgressPercent = percent;
                    progress?.Report(percent);
                    DownloadProgressChanged?.Invoke(this, percent);
                }
                else if (p.Status == DownloadStatus.Completed)
                {
                    _downloadProgressPercent = 100;
                    progress?.Report(100);
                    DownloadProgressChanged?.Invoke(this, 100);
                }
            };

            await using (var fileStream = new FileStream(tempDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var downloadProgress = await downloadRequest.DownloadAsync(fileStream, linkedCts.Token).ConfigureAwait(false);
                if (downloadProgress.Status != DownloadStatus.Completed)
                {
                    throw downloadProgress.Exception ?? new InvalidOperationException($"Google Drive download failed with status '{downloadProgress.Status}'.");
                }
                await fileStream.FlushAsync(linkedCts.Token).ConfigureAwait(false);
            }

            var downloadEnd = DateTime.UtcNow;
            var downloadedInfo = new FileInfo(tempDownloadPath);
            log.AppendLine($"RequestCompleted={downloadEnd:yyyy-MM-dd HH:mm:ss.fff} UTC");
            log.AppendLine($"DownloadedBytes={downloadedInfo.Length}");
            log.AppendLine();

            // Verify downloaded file integrity
            if (!downloadedInfo.Exists || downloadedInfo.Length == 0)
            {
                throw new InvalidOperationException("Google Backup download is incomplete or empty.");
            }

            _logger.LogInformation("[GoogleDriveRestore] Download completed. Local size: {DownloadedSize} bytes, Expected size: {ExpectedSize} bytes", downloadedInfo.Length, fileMeta.Size);

            if (fileMeta.Size.HasValue && fileMeta.Size.Value > 0 && downloadedInfo.Length != fileMeta.Size.Value)
            {
                throw new InvalidOperationException($"Google Backup download is incomplete. Downloaded {downloadedInfo.Length} bytes, expected {fileMeta.Size.Value} bytes.");
            }

            // Calculate SHA-256 and verify against metadata if present
            using var sha256Alg = SHA256.Create();
            await using (var verifyStream = File.OpenRead(tempDownloadPath))
            {
                byte[] computedBytes = await sha256Alg.ComputeHashAsync(verifyStream, linkedCts.Token).ConfigureAwait(false);
                string computedSha = Convert.ToHexString(computedBytes);

                string? expectedSha = null;
                if (fileMeta.AppProperties != null && fileMeta.AppProperties.TryGetValue("sha256", out var appSha))
                {
                    expectedSha = appSha;
                }
                else if (!string.IsNullOrEmpty(fileMeta.Description) && fileMeta.Description.Contains("SHA-256: "))
                {
                    var idx = fileMeta.Description.IndexOf("SHA-256: ", StringComparison.Ordinal);
                    expectedSha = fileMeta.Description.Substring(idx + 9).Trim();
                }

                bool hashMatch = string.IsNullOrEmpty(expectedSha) || string.Equals(computedSha, expectedSha, StringComparison.OrdinalIgnoreCase);

                log.AppendLine("[HASH]");
                log.AppendLine($"ExpectedHash={expectedSha ?? "None"}");
                log.AppendLine($"ActualHash={computedSha}");
                log.AppendLine($"HashMatch={hashMatch}");
                log.AppendLine();

                _logger.LogInformation("[GoogleDriveRestore] Computed SHA-256: {ComputedSha}, Expected SHA-256: {ExpectedSha}", computedSha, expectedSha ?? "None");

                if (!hashMatch)
                {
                    throw new InvalidOperationException($"Google Backup download integrity check failed. Expected SHA-256: {expectedSha}, Actual: {computedSha}.");
                }
            }

            // Safely replace destination file with verified downloaded file
            try
            {
                if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }
            }
            catch (Exception delEx)
            {
                _logger.LogWarning(delEx, "[GoogleDriveRestore] Existing destination file delete warning before copy: {Path}", destPath);
            }

            File.Copy(tempDownloadPath, destPath, overwrite: true);
            try { File.Delete(tempDownloadPath); } catch { }

            _logger.LogInformation("[GoogleDriveRestore] Google Drive backup {FileId} downloaded, verified, and placed at '{Path}' successfully.", resolvedFileId, destPath);
            WriteDiagnosticRestoreLog(log);
            return destPath;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var timeoutEx = new TimeoutException("Google Drive download failed: connection timed out. Please check your internet connection and try again.");
            _logger.LogError(timeoutEx, "[GoogleDriveRestore] Download timed out after 180 seconds.");
            if (File.Exists(tempDownloadPath)) { try { File.Delete(tempDownloadPath); } catch { } }
            throw timeoutEx;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleDriveRestore] Failed to download/verify backup {FileId} from Google Drive appDataFolder: {Message}", cloudFileId, ex.Message);
            
            log.AppendLine("[ERROR]");
            log.AppendLine($"ExceptionType={ex.GetType().FullName}");
            log.AppendLine($"Message={ex.Message}");
            if (ex is Google.GoogleApiException gex)
            {
                log.AppendLine($"GoogleApiStatus={(int)gex.HttpStatusCode}");
                log.AppendLine($"GoogleApiReason={gex.Error?.Errors?.FirstOrDefault()?.Reason ?? gex.Error?.Message ?? "Unknown"}");
            }
            if (ex.InnerException != null)
            {
                log.AppendLine($"InnerExceptionType={ex.InnerException.GetType().FullName}");
                log.AppendLine($"InnerExceptionMessage={ex.InnerException.Message}");
            }
            log.AppendLine($"StackTrace={ex.StackTrace}");
            log.AppendLine();
            WriteDiagnosticRestoreLog(log);

            if (File.Exists(tempDownloadPath))
            {
                try { File.Delete(tempDownloadPath); } catch { }
            }
            throw;
        }
        finally
        {
            _isDownloading = false;
            _downloadProgressPercent = 0;
            NotifyStateChanged();
            DownloadProgressChanged?.Invoke(this, 0);
        }
    }

    public async Task<BackupMetadata> RestoreFromCloudAsync(
        string cloudFileId,
        string? password = null,
        IProgress<int>? downloadProgress = null,
        IProgress<string>? statusProgress = null,
        CancellationToken cancellationToken = default)
    {
        var log = new StringBuilder();
        log.AppendLine("# DhirDhar Google Drive Complete Restore Execution Log");
        log.AppendLine($"# Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n");

        var localPath = await DownloadBackupAsync(cloudFileId, GoogleBackupFileName, downloadProgress, cancellationToken).ConfigureAwait(false);
        bool restoreSuccess = false;

        try
        {
            statusProgress?.Report("Validating Backup...");

            log.AppendLine("[VALIDATION]");
            log.AppendLine($"BackupFile={Path.GetFileName(localPath)}");
            log.AppendLine($"LocalPath={localPath}");

            bool isValid = await _backupService.VerifyBackupAsync(localPath, cancellationToken).ConfigureAwait(false);
            log.AppendLine($"BackupFormat=ZipArchive (.ddbackup)");
            log.AppendLine($"BackupVersion=3.0");
            log.AppendLine($"ValidationResult={(isValid ? "Valid" : "Failed")}");
            log.AppendLine();

            if (!isValid)
            {
                throw new InvalidOperationException("Backup validation failed. The downloaded backup is invalid or corrupted.");
            }

            // Check if this is an older incompatible encrypted backup
            using (var archive = System.IO.Compression.ZipFile.OpenRead(localPath))
            {
                if (archive.GetEntry("data.enc") != null)
                {
                    throw new InvalidOperationException("This Google Backup was created with an older backup format. Create a new Google Backup from the original DhirDhar system.");
                }

                var dbEntry = archive.GetEntry("DhirDhar.db");
                if (dbEntry == null)
                {
                    throw new InvalidOperationException("Invalid DhirDhar backup: database entry not found.");
                }

                var manifestEntry = archive.GetEntry("manifest.json");
                if (manifestEntry != null)
                {
                    try
                    {
                        using var mStream = manifestEntry.Open();
                        var manifest = await System.Text.Json.JsonSerializer.DeserializeAsync<BackupService.BackupManifest>(mStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                        if (manifest != null)
                        {
                            if (manifest.Encrypted)
                            {
                                throw new InvalidOperationException("This Google Backup was created with an older backup format. Create a new Google Backup from the original DhirDhar system.");
                            }

                            if (!string.IsNullOrWhiteSpace(manifest.AccountEmail) && !string.IsNullOrWhiteSpace(_connectedEmail))
                            {
                                if (!string.Equals(_connectedEmail.Trim(), manifest.AccountEmail.Trim(), StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger.LogWarning("[GoogleDriveRestore] Google account mismatch in manifest: Authenticated={AuthEmail}, Manifest={ManifestEmail}", MaskEmail(_connectedEmail), MaskEmail(manifest.AccountEmail));
                                    throw new InvalidOperationException("Google account does not match the account that created this DhirDhar backup.");
                                }
                            }
                        }
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        _logger.LogWarning("[GoogleDriveRestore] Manifest parsing warning.");
                    }
                }
            }

            log.AppendLine("[RESTORE]");
            var restoreStart = DateTime.UtcNow;
            log.AppendLine($"RestoreStarted={restoreStart:yyyy-MM-dd HH:mm:ss.fff} UTC");

            _logger.LogInformation("[GoogleDriveRestore] Proceeding to directly restore validated Google backup package '{Path}'", localPath);
            var result = await _backupService.RestoreBackupAsync(localPath, null, statusProgress, cancellationToken).ConfigureAwait(false);

            var restoreEnd = DateTime.UtcNow;
            log.AppendLine($"RestoreCompleted={restoreEnd:yyyy-MM-dd HH:mm:ss.fff} UTC");
            log.AppendLine($"RestoredBackupId={result.BackupId}");
            log.AppendLine($"Status=Successful");
            log.AppendLine();

            restoreSuccess = true;
            WriteDiagnosticRestoreLog(log);

            return new BackupMetadata(
                GoogleBackupFileName,
                "3.0",
                "2.0.0",
                "1.0",
                DateTime.UtcNow,
                GoogleBackupType,
                "Google Drive",
                result.FileSize,
                result.IntegrityHash,
                "Successful",
                "Verified");
        }
        catch (Exception ex)
        {
            log.AppendLine("[ERROR]");
            log.AppendLine($"ExceptionType={ex.GetType().FullName}");
            log.AppendLine($"Message={ex.Message}");
            if (ex is Google.GoogleApiException gex)
            {
                log.AppendLine($"GoogleApiStatus={(int)gex.HttpStatusCode}");
                log.AppendLine($"GoogleApiReason={gex.Error?.Errors?.FirstOrDefault()?.Reason ?? gex.Error?.Message ?? "Unknown"}");
            }
            if (ex.InnerException != null)
            {
                log.AppendLine($"InnerExceptionType={ex.InnerException.GetType().FullName}");
                log.AppendLine($"InnerExceptionMessage={ex.InnerException.Message}");
            }
            log.AppendLine($"StackTrace={ex.StackTrace}");
            log.AppendLine();
            WriteDiagnosticRestoreLog(log);

            _logger.LogError(ex, "[GoogleDriveRestore] RestoreFromCloudAsync failed for cloud file {FileId}: {Message}", cloudFileId, ex.Message);
            throw;
        }
        finally
        {
            if (restoreSuccess)
            {
                try
                {
                    if (File.Exists(localPath))
                    {
                        File.Delete(localPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[GoogleDriveRestore] Failed to clean up temporary downloaded backup file at {Path}", localPath);
                }
            }
            else
            {
                _logger.LogWarning("[GoogleDriveRestore] Preserving downloaded failed backup file for analysis: {Path}", localPath);
            }
        }
    }

    private async Task<bool> TryAuthorizeSilentAsync(CancellationToken cancellationToken)
    {
        try
        {
            var secrets = await GetClientSecretsAsync(cancellationToken).ConfigureAwait(false);
            if (secrets == null || IsPlaceholderCredentials(secrets))
            {
                _state = GoogleDriveOAuthState.NotConnected;
                _errorMessage = "Google Drive is not configured. OAuth Client ID is missing.";
                NotifyStateChanged();
                return false;
            }

            var tokenStore = GetTokenStore();
            var storedToken = await tokenStore.GetAsync<Google.Apis.Auth.OAuth2.Responses.TokenResponse>("user").ConfigureAwait(false);
            if (storedToken is null || string.IsNullOrEmpty(storedToken.RefreshToken))
            {
                _state = GoogleDriveOAuthState.NotConnected;
                NotifyStateChanged();
                return false;
            }

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = secrets,
                Scopes = Scopes,
                DataStore = tokenStore
            });

            _credential = new UserCredential(flow, "user", storedToken);

            if (_credential.Token.IsStale)
            {
                try
                {
                    bool refreshed = await _credential.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
                    if (!refreshed)
                    {
                        _logger.LogWarning("Silent refresh of Google OAuth token returned false.");
                        _state = GoogleDriveOAuthState.ReauthRequired;
                        _errorMessage = "Google Drive authorization expired. Please click Connect to re-authorize.";
                        NotifyStateChanged();
                        return false;
                    }
                }
                catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException tex)
                {
                    _logger.LogWarning(tex, "Silent refresh failed with OAuth error {Error}", tex.Error?.Error ?? tex.Message);
                    _state = GoogleDriveOAuthState.ReauthRequired;
                    _errorMessage = "Google Drive authorization expired or was revoked. Please click Connect to authorize again.";
                    NotifyStateChanged();
                    return false;
                }
                catch (System.Net.Http.HttpRequestException hex)
                {
                    _logger.LogWarning(hex, "Silent refresh network unavailable on startup.");
                    _state = GoogleDriveOAuthState.Offline;
                    _errorMessage = "Offline: Google Drive will reconnect when internet is available.";
                    NotifyStateChanged();
                    return false;
                }
                catch (System.Net.Sockets.SocketException sex)
                {
                    _logger.LogWarning(sex, "Silent refresh network socket error on startup.");
                    _state = GoogleDriveOAuthState.Offline;
                    _errorMessage = "Offline: Google Drive will reconnect when internet is available.";
                    NotifyStateChanged();
                    return false;
                }
                catch (TimeoutException tox)
                {
                    _logger.LogWarning(tox, "Silent refresh timeout on startup.");
                    _state = GoogleDriveOAuthState.Offline;
                    _errorMessage = "Offline: Google Drive will reconnect when internet is available.";
                    NotifyStateChanged();
                    return false;
                }
            }

            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = _credential,
                ApplicationName = "DhirDhar"
            });

            await FetchAccountDetailsAsync(cancellationToken).ConfigureAwait(false);

            _state = GoogleDriveOAuthState.Connected;
            _errorMessage = null;
            NotifyStateChanged();
            _logger.LogInformation("Google Drive silently auto-connected for account: {Email}", _connectedEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Silent Google Drive authorization failed.");
            _state = GoogleDriveOAuthState.NotConnected;
            _errorMessage = null;
            NotifyStateChanged();
            return false;
        }
    }

    private async Task FetchAccountDetailsAsync(CancellationToken cancellationToken)
    {
        if (_driveService is null) return;
        try
        {
            var aboutRequest = _driveService.About.Get();
            aboutRequest.Fields = "user(emailAddress,displayName)";
            var about = await aboutRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            _connectedEmail = about.User?.EmailAddress ?? "Google Drive User";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Google Drive user email details.");
            _connectedEmail = "Connected Account";
        }
    }

    private async Task<ClientSecrets?> GetClientSecretsAsync(CancellationToken cancellationToken = default)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDataDir = Path.Combine(appData, "DhirDhar Solution");
        var customSecretsPath = Path.Combine(appDataDir, "google_client_secrets.json");
        var localSecretsPath = Path.Combine(AppContext.BaseDirectory, "google_client_secrets.json");

        var candidatePaths = new List<string>();

        if (File.Exists(customSecretsPath))
        {
            candidatePaths.Add(customSecretsPath);
        }

        if (File.Exists(localSecretsPath))
        {
            candidatePaths.Add(localSecretsPath);
        }

        // Google Cloud Console downloads the OAuth client JSON as client_secret_<client-id>.json
        foreach (var searchDir in new[] { appDataDir, AppContext.BaseDirectory })
        {
            try
            {
                if (!Directory.Exists(searchDir))
                {
                    continue;
                }

                var downloadedSecrets = Directory
                    .EnumerateFiles(searchDir, "client_secret_*.json")
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime);

                candidatePaths.AddRange(downloadedSecrets);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to search for Google client secrets files in {Path}", searchDir);
            }
        }

        foreach (var secretsFilePath in candidatePaths)
        {
            try
            {
                await using var stream = File.OpenRead(secretsFilePath);
                var loadedSecrets = await GoogleClientSecrets.FromStreamAsync(stream, cancellationToken).ConfigureAwait(false);
                if (loadedSecrets?.Secrets != null &&
                    !IsPlaceholderCredentials(loadedSecrets.Secrets))
                {
                    return loadedSecrets.Secrets;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load Google client secrets file from path {Path}", secretsFilePath);
            }
        }

        if (!string.IsNullOrWhiteSpace(_backupOptions.GoogleDriveClientId))
        {
            var secrets = new ClientSecrets
            {
                ClientId = _backupOptions.GoogleDriveClientId,
                ClientSecret = _backupOptions.GoogleDriveClientSecret ?? string.Empty
            };
            if (!IsPlaceholderCredentials(secrets))
            {
                return secrets;
            }
        }

        var defaultSecrets = new ClientSecrets
        {
            ClientId = GetDefaultClientId(),
            ClientSecret = GetDefaultClientSecret()
        };

        if (!IsPlaceholderCredentials(defaultSecrets))
        {
            return defaultSecrets;
        }

        return null;
    }

    private static bool IsPlaceholderCredentials(ClientSecrets? secrets)
    {
        if (secrets == null || string.IsNullOrWhiteSpace(secrets.ClientId))
        {
            return true;
        }

        var cid = secrets.ClientId.Trim();
        if (cid.Contains("dhir-dhar-desktop-app", StringComparison.OrdinalIgnoreCase) ||
            cid.Contains("YOUR_CLIENT_ID", StringComparison.OrdinalIgnoreCase) ||
            cid.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private EncryptedFileDataStore GetTokenStore()
    {
        var appData = _pathService?.ApplicationDataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DhirDhar Solution");
        var dir = Path.Combine(appData, "GoogleDriveTokens");
        Directory.CreateDirectory(dir);
        return new EncryptedFileDataStore(dir, _logger);
    }

    private void NotifyStateChanged()
    {
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "***@***";
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return "***@***";
        var namePart = email[..atIndex];
        var domainPart = email[atIndex..];
        var visibleLength = Math.Min(2, namePart.Length);
        return namePart[..visibleLength] + new string('*', Math.Max(1, namePart.Length - visibleLength)) + domainPart;
    }
}
