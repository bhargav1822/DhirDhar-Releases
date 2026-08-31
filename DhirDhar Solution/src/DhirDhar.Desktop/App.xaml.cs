using System;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Desktop.Configuration;
using DhirDhar.Desktop.Services;
using DhirDhar.Desktop.Updates;
using DhirDhar.Desktop.Updates.UI;
using DhirDhar.Desktop.Views.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DhirDhar.Desktop;

public partial class App : Microsoft.UI.Xaml.Application
{
    private IServiceProvider? _serviceProvider;
    private Window? _window;

    public static IServiceProvider? ServiceProvider { get; set; }

    public static IntPtr MainWindowHandle { get; set; }

    public static Window? MainWindow { get; set; }

    public static DispatcherQueue? MainDispatcherQueue { get; set; }

    [System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    private static void RegisterCustomFonts()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            string[] fontPaths = new[]
            {
                System.IO.Path.Combine(baseDir, "Assets", "Fonts", "NotoSansGujarati.ttf"),
                System.IO.Path.Combine(baseDir, "Assets", "NotoSansGujarati.ttf")
            };

            foreach (var fontPath in fontPaths)
            {
                if (System.IO.File.Exists(fontPath))
                {
                    AddFontResourceEx(fontPath, 0x10 /* FR_PRIVATE */, IntPtr.Zero);
                }
            }
        }
        catch
        {
        }
    }

    public App()
    {
        RegisterCustomFonts();
        InitializeComponent();

        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            MainDispatcherQueue = DispatcherQueue.GetForCurrentThread();

            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(MainDispatcherQueue));

            var configuration = ConfigurationExtensions.BuildConfiguration();

            var services = new ServiceCollection();
            services.AddDesktopServices(configuration);
            _serviceProvider = services.BuildServiceProvider();
            ServiceProvider = _serviceProvider;

            // Initialize centralized typography so font follows CurrentLanguage before UI is created
            try { _serviceProvider.GetService<IAppTypographyService>()?.ApplyCurrentLanguageFont(); } catch { }

            var loadingWindow = new LoadingWindow();
            loadingWindow.EnsureLoadingPresentationApplied();
            _window = loadingWindow;
            loadingWindow.Activate();
            loadingWindow.EnsureLoadingPresentationApplied();

            _ = InitializeAndShowAsync(loadingWindow);
        }
        catch (Exception exception)
        {
            HandleFatalStartupFailure(exception);
        }
    }

    private async Task InitializeAndShowAsync(LoadingWindow loadingWindow)
    {
        try
        {
            // Step 1: Show loading UI and run all startup initialization (DB, services, etc.)
            loadingWindow.ShowLoading(_serviceProvider!);
            await loadingWindow.RunStartupAsync(_serviceProvider!).ConfigureAwait(false);

            var dispatcherQueue = loadingWindow.DispatcherQueue;
            dispatcherQueue.TryEnqueue(new DispatcherQueueHandler(() =>
            {
                try
                {
                    // Create and navigate main window on the UI thread (hidden - not activated yet).
                    // WinUI Window objects must be created on the UI thread.
                    var mainWindow = _serviceProvider!.GetRequiredService<MainWindow>();
                    _window = mainWindow;
                    MainWindow = mainWindow;
                    MainDispatcherQueue = mainWindow.DispatcherQueue;

                    var licenseManager = _serviceProvider!.GetService<DhirDhar.Application.Licensing.ILicenseManager>();
                    if (licenseManager != null && licenseManager.RequiresActivation)
                    {
                        mainWindow.NavigateToActivation(_serviceProvider!);
                        loadingWindow.Close();
                        mainWindow.Activate();
                    }
                    else
                    {
                        mainWindow.NavigateToMainShell(_serviceProvider!);

                        // Wait for dashboard to be fully loaded and rendered (data loaded)
                        WaitForDashboardDataAndCloseLoading(mainWindow, loadingWindow);
                    }
                }
                catch (Exception exception)
                {
                    LogDetailedException("MainWindow Navigation/Shell Initialization", exception);
                }
            }));
        }
        catch (Exception exception)
        {
            LogDetailedException("Application Startup & Background Tasks Initialization", exception);
        }
    }

    public void LogDetailedException(string componentOrLocation, Exception ex)
    {
        var logger = _serviceProvider?.GetService<ILogger<App>>();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[CRITICAL FAILURE IN {componentOrLocation}]");
        sb.AppendLine($"Type: {ex.GetType().FullName}");
        sb.AppendLine($"Message: {ex.Message}");
        sb.AppendLine($"HResult: 0x{ex.HResult:X8}");
        sb.AppendLine($"StackTrace:\n{ex.StackTrace}");
        var inner = ex.InnerException;
        int level = 1;
        while (inner != null)
        {
            sb.AppendLine($"--- Inner Exception Level {level} ---");
            sb.AppendLine($"Type: {inner.GetType().FullName}, Message: {inner.Message}, HResult: 0x{inner.HResult:X8}");
            sb.AppendLine($"StackTrace:\n{inner.StackTrace}");
            inner = inner.InnerException;
            level++;
        }
        logger?.LogCritical(ex, "{Details}", sb.ToString());
    }

    private static void WaitForDashboardDataAndCloseLoading(MainWindow mainWindow, LoadingWindow loadingWindow)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        var minDisplayTime = DateTime.UtcNow.AddSeconds(2);
        DispatcherTimer? timer = null;

        void TransitionToMainWindow(string reason)
        {
            try
            {
                timer?.Stop();
                mainWindow.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    try
                    {
                        var logger = ServiceProvider?.GetService<ILogger<App>>();
                        logger?.LogInformation("[LIFECYCLE] Transitioning from LoadingWindow to MainWindow. Reason: {Reason}", reason);

                        // CRITICAL WINUI 3 LIFETIME FIX:
                        // Activate MainWindow FIRST so WinUI registers it as active application window before closing LoadingWindow.
                         mainWindow.Activate();
                         mainWindow.MaximizeWindow();
                         logger?.LogInformation("[LIFECYCLE] MainWindow activated & maximized successfully. Queueing LoadingWindow.Close on low priority.");

                         StartBackgroundUpdateCheck();

                        mainWindow.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                        {
                            try
                            {
                                loadingWindow.AppWindow.Hide();
                                logger?.LogInformation("[LIFECYCLE] LoadingWindow hidden. MainWindow active.");
                            }
                            catch (Exception ex)
                            {
                                ServiceProvider?.GetService<ILogger<App>>()?.LogError(ex, "[LIFECYCLE] Error hiding LoadingWindow.");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        ServiceProvider?.GetService<ILogger<App>>()?.LogError(ex, "[LIFECYCLE] Error during LoadingWindow -> MainWindow transition.");
                    }
                });
            }
            catch (Exception ex)
            {
                ServiceProvider?.GetService<ILogger<App>>()?.LogError(ex, "[LIFECYCLE] Error queueing window transition.");
            }
        }

        void OnTick(object? sender, object e)
        {
            if (DateTime.UtcNow >= timeout)
            {
                TransitionToMainWindow("Timeout reached (10s)");
                return;
            }

            try
            {
                if (DateTime.UtcNow >= minDisplayTime && mainWindow.IsDashboardReady())
                {
                    TransitionToMainWindow("Dashboard ready");
                    return;
                }
            }
            catch (Exception ex)
            {
                ServiceProvider?.GetService<ILogger<App>>()?.LogError(ex, "[LIFECYCLE] Error checking Dashboard state.");
                TransitionToMainWindow("Exception in Dashboard check");
            }
        }

        timer = new DispatcherTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Tick += OnTick;
        timer.Start();
    }

    private void HandleFatalStartupFailure(Exception exception)
    {
        var logger = _serviceProvider?.GetService<ILogger<App>>();
        logger?.LogCritical(exception, "Application startup failed.");

        try
        {
            var configuration = ConfigurationExtensions.BuildConfiguration();
            var appOptions = configuration.LoadAppOptions();
            _window = new MainWindow(appOptions);
            _window.Activate();
        }
        catch
        {
        }
    }

    private static void StartBackgroundUpdateCheck()
    {
        try
        {
            var updateService = ServiceProvider?.GetService<IUpdateService>();
            if (updateService is null) return;

            updateService.UpdateAvailable += OnUpdateAvailable;

            // Automatically clean up any installed update packages from local updates directory
            _ = Task.Run(async () =>
            {
                try
                {
                    await updateService.CleanupInstalledPackagesAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ServiceProvider?.GetService<ILogger<App>>()?.LogWarning(ex, "[UPDATER] Error running startup package cleanup.");
                }
            });

            // Non-blocking, once-per-session check.
            _ = Task.Run(async () =>
            {
                try
                {
                    var settingsService = ServiceProvider?.GetService<DhirDhar.Application.Settings.ISettingsService>();
                    if (settingsService is not null)
                    {
                        var settings = await settingsService.GetSettingsAsync().ConfigureAwait(false);
                        if (!settings.UpdatesAutoCheckEnabled)
                        {
                            ServiceProvider?.GetService<ILogger<App>>()?.LogInformation("[UPDATER] Automatic update check disabled by settings preference.");
                            return;
                        }
                    }

                    await updateService.CheckForUpdatesAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ServiceProvider?.GetService<ILogger<App>>()?.LogError(ex, "[UPDATER] Background update check error.");
                }
            });
        }
        catch (Exception ex)
        {
            ServiceProvider?.GetService<ILogger<App>>()?.LogError(ex, "[UPDATER] Failed to start background update check.");
        }
    }

    private static void OnUpdateAvailable(object? sender, Updates.Models.UpdateInfo updateInfo)
    {
        try
        {
            var logger = ServiceProvider?.GetService<ILogger<App>>();
            logger?.LogInformation("[UPDATER] Update available: v{Version}. Download and installation are disabled in update check step.", updateInfo.Version);
        }
        catch (Exception ex)
        {
            ServiceProvider?.GetService<ILogger<App>>()?.LogError(ex, "[UPDATER] Error handling update available notification.");
        }
    }

    private static void WriteEmergencyLog(string text)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DhirDhar Solution", "Logs");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"app-{DateTime.Now:yyyyMMdd}.log");
            using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [EMERGENCY] {text}");
            writer.Flush();
        }
        catch
        {
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var logger = _serviceProvider?.GetService<ILogger<App>>();
        if (e.Exception != null)
        {
            var ex = e.Exception;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[UNHANDLED WINUI EXCEPTION] Type: {ex.GetType().FullName}, Message: {ex.Message}, HResult: 0x{ex.HResult:X8}");
            sb.AppendLine($"StackTrace:\n{ex.StackTrace}");
            var inner = ex.InnerException;
            int level = 1;
            while (inner != null)
            {
                sb.AppendLine($"--- Inner Exception Level {level} ---");
                sb.AppendLine($"Type: {inner.GetType().FullName}, Message: {inner.Message}, HResult: 0x{inner.HResult:X8}");
                sb.AppendLine($"StackTrace:\n{inner.StackTrace}");
                inner = inner.InnerException;
                level++;
            }
            logger?.LogCritical(ex, "{Details}", sb.ToString());
            WriteEmergencyLog(sb.ToString());
        }
        else
        {
            var msg = $"Unhandled application exception with null Exception object. Message: {e.Message}";
            logger?.LogCritical("{Message}", msg);
            WriteEmergencyLog(msg);
        }

        e.Handled = true;
    }

    private void OnCurrentDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var logger = _serviceProvider?.GetService<ILogger<App>>();
        if (e.ExceptionObject is Exception exception)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[UNHANDLED DOMAIN EXCEPTION] Type: {exception.GetType().FullName}, Message: {exception.Message}, HResult: 0x{exception.HResult:X8}");
            sb.AppendLine($"StackTrace:\n{exception.StackTrace}");
            var inner = exception.InnerException;
            int level = 1;
            while (inner != null)
            {
                sb.AppendLine($"--- Inner Exception Level {level} ---");
                sb.AppendLine($"Type: {inner.GetType().FullName}, Message: {inner.Message}, HResult: 0x{inner.HResult:X8}");
                sb.AppendLine($"StackTrace:\n{inner.StackTrace}");
                inner = inner.InnerException;
                level++;
            }
            logger?.LogCritical(exception, "{Details}", sb.ToString());
            WriteEmergencyLog(sb.ToString());
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var logger = _serviceProvider?.GetService<ILogger<App>>();
        if (e.Exception != null)
        {
            var ex = e.Exception;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[UNOBSERVED TASK EXCEPTION] Type: {ex.GetType().FullName}, Message: {ex.Message}, HResult: 0x{ex.HResult:X8}");
            sb.AppendLine($"StackTrace:\n{ex.StackTrace}");
            var inner = ex.InnerException;
            int level = 1;
            while (inner != null)
            {
                sb.AppendLine($"--- Inner Exception Level {level} ---");
                sb.AppendLine($"Type: {inner.GetType().FullName}, Message: {inner.Message}, HResult: 0x{inner.HResult:X8}");
                sb.AppendLine($"StackTrace:\n{inner.StackTrace}");
                inner = inner.InnerException;
                level++;
            }
            logger?.LogError(ex, "{Details}", sb.ToString());
            WriteEmergencyLog(sb.ToString());
        }

        e.SetObserved();
    }
}