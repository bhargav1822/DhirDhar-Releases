using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DhirDhar.Updater;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DhirDhar Solution", "Logs");
        Directory.CreateDirectory(logDir);
        string logFilePath = Path.Combine(logDir, "update.log");

        Log(logFilePath, $"[UPDATER START] Arguments: {string.Join(" ", args)}");

        int pid = GetArgInt(args, "--pid");
        string packagePath = GetArgString(args, "--package");
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            packagePath = GetArgString(args, "--zip");
        }
        string targetDir = GetArgString(args, "--target");
        string exePath = GetArgString(args, "--exe");

        if (string.IsNullOrWhiteSpace(packagePath) || string.IsNullOrWhiteSpace(targetDir) || string.IsNullOrWhiteSpace(exePath))
        {
            Log(logFilePath, "[UPDATER ERROR] Missing required arguments (--package/--zip, --target, --exe). Exiting.");
            return 1;
        }

        // Ensure updater is not executing from target directory (which locks target directory DLLs)
        string currentExe = Environment.ProcessPath ?? "";
        string currentDir = Path.GetDirectoryName(currentExe) ?? "";
        if (!string.IsNullOrEmpty(currentDir) && !string.IsNullOrEmpty(targetDir) &&
            string.Equals(Path.GetFullPath(currentDir).TrimEnd('\\', '/'), Path.GetFullPath(targetDir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            string tempUpdaterDir = Path.Combine(Path.GetTempPath(), "DhirDharUpdater_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempUpdaterDir);
            foreach (var f in Directory.GetFiles(currentDir))
            {
                var fname = Path.GetFileName(f);
                if (fname.StartsWith("DhirDharUpdater", StringComparison.OrdinalIgnoreCase) ||
                    fname.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    fname.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Copy(f, Path.Combine(tempUpdaterDir, fname), true); } catch { }
                }
            }

            string relocatedExe = Path.Combine(tempUpdaterDir, Path.GetFileName(currentExe));
            if (File.Exists(relocatedExe))
            {
                Log(logFilePath, $"[UPDATER] Relocating updater to '{tempUpdaterDir}' to unlock target directory...");
                string escapedArgs = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
                var startInfo = new ProcessStartInfo
                {
                    FileName = relocatedExe,
                    Arguments = escapedArgs,
                    UseShellExecute = true,
                    WorkingDirectory = tempUpdaterDir
                };
                Process.Start(startInfo);
                return 0;
            }
        }

        if (!File.Exists(packagePath))
        {
            Log(logFilePath, $"[UPDATER ERROR] Update package file does not exist at '{packagePath}'. Exiting.");
            return 1;
        }

        // 1. Wait for DhirDhar main process to exit
        if (pid > 0)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                Log(logFilePath, $"[UPDATER] Waiting for main application process (PID {pid}) to exit...");
                if (!process.HasExited)
                {
                    bool exited = process.WaitForExit(30000);
                    if (!exited)
                    {
                        Log(logFilePath, $"[UPDATER WARNING] Process PID {pid} did not exit in 30s. Terminating process.");
                        process.Kill();
                    }
                }
            }
            catch (ArgumentException)
            {
                Log(logFilePath, $"[UPDATER] Process PID {pid} has already exited.");
            }
            catch (Exception ex)
            {
                Log(logFilePath, $"[UPDATER WARNING] Error waiting for process exit: {ex.Message}");
            }
        }

        // Additional delay to ensure file handles are completely released
        await Task.Delay(1500).ConfigureAwait(false);

        // 2. Determine package type: Executable Installer vs ZIP archive
        bool isInstallerExe = packagePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        if (isInstallerExe)
        {
            if (!IsValidPeExecutable(packagePath))
            {
                Log(logFilePath, $"[UPDATER ERROR] Package file '{packagePath}' failed PE executable validation. Aborting.");
                return 1;
            }

            Log(logFilePath, $"[UPDATER] Executing installer package '{packagePath}' silently...");
            try
            {
                var installerInfo = new ProcessStartInfo
                {
                    FileName = packagePath,
                    Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /DIR=\"{targetDir}\"",
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(packagePath) ?? targetDir
                };

                using var installerProcess = Process.Start(installerInfo);
                if (installerProcess != null)
                {
                    installerProcess.WaitForExit();
                    Log(logFilePath, $"[UPDATER] Installer process completed with exit code: {installerProcess.ExitCode}");

                    if (installerProcess.ExitCode == 0)
                    {
                        TryDeletePackageWithRetry(packagePath, logFilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                Log(logFilePath, $"[UPDATER ERROR] Failed to run installer executable: {ex.Message}");
                return 1;
            }
        }
        else
        {
            // Stage ZIP Extraction
            string updatesDir = Path.GetDirectoryName(packagePath) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DhirDhar Solution", "Updates");
            string stagingDir = Path.Combine(updatesDir, "Staging");
            if (Directory.Exists(stagingDir))
            {
                try { Directory.Delete(stagingDir, true); } catch { }
            }
            Directory.CreateDirectory(stagingDir);

            Log(logFilePath, $"[UPDATER] Extracting ZIP package '{packagePath}' to staging directory '{stagingDir}'...");
            try
            {
                ZipFile.ExtractToDirectory(packagePath, stagingDir);
            }
            catch (Exception ex)
            {
                Log(logFilePath, $"[UPDATER ERROR] Failed to extract update package: {ex.Message}");
                return 1;
            }

            // Locate content root inside staging directory (handle single root folder inside ZIP)
            string contentRoot = stagingDir;
            var subDirs = Directory.GetDirectories(stagingDir);
            var subFiles = Directory.GetFiles(stagingDir);
            if (subDirs.Length == 1 && subFiles.Length == 0)
            {
                contentRoot = subDirs[0];
            }

            // Backup current application files
            string backupDir = Path.Combine(updatesDir, "Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(backupDir);

            Log(logFilePath, $"[UPDATER] Replacing application files in '{targetDir}' (Backup: '{backupDir}')...");
            try
            {
                CopyDirectoryContent(contentRoot, targetDir, backupDir, logFilePath);
                Log(logFilePath, "[UPDATER SUCCESS] Application files updated successfully.");
            }
            catch (Exception ex)
            {
                Log(logFilePath, $"[UPDATER ERROR] File replacement failed: {ex.Message}. Rolling back from backup...");
                try
                {
                    RestoreBackupContent(backupDir, targetDir, logFilePath);
                    Log(logFilePath, "[UPDATER ROLLBACK] Previous version successfully restored.");
                }
                catch (Exception rollbackEx)
                {
                    Log(logFilePath, $"[UPDATER CRITICAL] Rollback failed: {rollbackEx.Message}");
                }
                return 1;
            }

            // Cleanup temporary files
            try
            {
                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                TryDeletePackageWithRetry(packagePath, logFilePath);
                if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
            }
            catch (Exception ex)
            {
                Log(logFilePath, $"[UPDATER WARNING] Cleanup error: {ex.Message}");
            }
        }

        // 3. Restart updated application
        string finalExeToLaunch = File.Exists(exePath) ? exePath : Path.Combine(targetDir, "DhirDhar.Desktop.exe");
        if (File.Exists(finalExeToLaunch) && IsValidPeExecutable(finalExeToLaunch))
        {
            Log(logFilePath, $"[UPDATER] Launching updated application '{finalExeToLaunch}'...");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = finalExeToLaunch,
                    UseShellExecute = true,
                    WorkingDirectory = targetDir
                });
                Log(logFilePath, "[UPDATER COMPLETE] Restarted DhirDhar successfully.");
            }
            catch (Exception ex)
            {
                Log(logFilePath, $"[UPDATER ERROR] Failed to start updated executable '{finalExeToLaunch}': {ex.Message}");
                return 1;
            }
        }
        else
        {
            Log(logFilePath, $"[UPDATER ERROR] Executable '{finalExeToLaunch}' not found or failed PE validation after update.");
            return 1;
        }

        return 0;
    }

    private static bool IsValidPeExecutable(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length < 1024) return false;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);

            if (fs.Length < 64) return false;
            ushort mz = reader.ReadUInt16();
            if (mz != 0x5A4D) return false; // 'MZ'

            fs.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = reader.ReadInt32();
            if (peOffset <= 0 || peOffset + 4 > fs.Length) return false;

            fs.Seek(peOffset, SeekOrigin.Begin);
            uint peSignature = reader.ReadUInt32();
            return peSignature == 0x00004550; // 'PE\0\0'
        }
        catch
        {
            return false;
        }
    }

    private static void CopyDirectoryContent(string sourceDir, string targetDir, string backupDir, string logFilePath)
    {
        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDir, file);
            string destFile = Path.Combine(targetDir, relativePath);
            string backupFile = Path.Combine(backupDir, relativePath);

            // Skip user data files if present
            string fileName = Path.GetFileName(file);
            if (fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("update.log", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Create directories if needed
            string? destSubDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(destSubDir)) Directory.CreateDirectory(destSubDir);

            string? backupSubDir = Path.GetDirectoryName(backupFile);
            if (!string.IsNullOrEmpty(backupSubDir)) Directory.CreateDirectory(backupSubDir);

            // Backup existing file before overwrite with retry
            int retries = 5;
            while (retries > 0)
            {
                try
                {
                    if (File.Exists(destFile))
                    {
                        File.Copy(destFile, backupFile, true);
                    }
                    File.Copy(file, destFile, true);
                    break;
                }
                catch (IOException) when (retries > 1)
                {
                    retries--;
                    Thread.Sleep(500);
                }
            }
        }
    }

    private static void RestoreBackupContent(string backupDir, string targetDir, string logFilePath)
    {
        foreach (string file in Directory.GetFiles(backupDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(backupDir, file);
            string destFile = Path.Combine(targetDir, relativePath);

            string? destSubDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(destSubDir)) Directory.CreateDirectory(destSubDir);

            File.Copy(file, destFile, true);
        }
    }

    private static string GetArgString(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1].Trim('"');
            }
        }
        return string.Empty;
    }

    private static int GetArgInt(string[] args, string flag)
    {
        string val = GetArgString(args, flag);
        return int.TryParse(val, out int result) ? result : 0;
    }

    private static void TryDeletePackageWithRetry(string packagePath, string logFilePath)
    {
        if (!File.Exists(packagePath)) return;
        int retries = 5;
        while (retries > 0)
        {
            try
            {
                var attributes = File.GetAttributes(packagePath);
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(packagePath, attributes & ~FileAttributes.ReadOnly);
                }
                File.Delete(packagePath);
                Log(logFilePath, $"[UPDATER] Successfully deleted update package '{packagePath}'.");
                break;
            }
            catch (Exception) when (retries > 1)
            {
                retries--;
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                Log(logFilePath, $"[UPDATER] Package locked during restart: {ex.Message}. Main app will clean it up on startup.");
                break;
            }
        }
    }

    private static void Log(string logFilePath, string message)
    {
        try
        {
            string formatted = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(logFilePath, formatted);
        }
        catch { }
    }
}
