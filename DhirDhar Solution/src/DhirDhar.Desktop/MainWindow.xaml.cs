using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DhirDhar.Desktop.Configuration;
using DhirDhar.Desktop.ViewModels;
using DhirDhar.Desktop.Views.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace DhirDhar.Desktop;

public sealed partial class MainWindow : Window
{
    private const int SW_MAXIMIZE = 3;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private readonly ILogger<MainWindow>? _logger;

    public MainWindow(AppOptions appOptions, ILogger<MainWindow> logger)
        : this(appOptions)
    {
        _logger = logger;
    }

    public MainWindow(AppOptions appOptions)
    {
        InitializeComponent();

        AppOptions = appOptions;

        App.MainWindowHandle = WindowNative.GetWindowHandle(this);

        Title = appOptions.Name;
        TrySetWindowIcon();
        MaximizeWindow();

        Activated += (s, e) => _logger?.LogInformation("[LIFECYCLE] MainWindow.Activated WindowActivationState={State}", e.WindowActivationState);
        Closed += (s, e) => _logger?.LogInformation("[LIFECYCLE] MainWindow.Closed triggered.");

        _logger?.LogInformation("Main window '{Name}' opened.", appOptions.Name);
    }

    public AppOptions AppOptions { get; }

    public void MaximizeWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, true);
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            presenter.Maximize();
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, SW_MAXIMIZE);
        }
    }

    public void NavigateToMainShell(IServiceProvider serviceProvider)
    {
        RestoreMainPresentation();

        var loc = serviceProvider.GetService<DhirDhar.Application.Localization.ILocalizationService>();
        if (loc != null)
        {
            loc.LanguageChanged += (s, e) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        if (Content is FrameworkElement root && !string.IsNullOrWhiteSpace(loc.CurrentLanguage))
                        {
                            root.Language = loc.CurrentLanguage;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Error setting root element language");
                    }
                });
            };
        }

        var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
        RootFrame.Navigate(typeof(MainPage), mainViewModel);
    }

    public void NavigateToActivation(IServiceProvider serviceProvider)
    {
        RestoreMainPresentation();
        var licenseViewModel = serviceProvider.GetRequiredService<DhirDhar.Desktop.ViewModels.License.LicenseViewModel>();
        RootFrame.Navigate(typeof(DhirDhar.Desktop.Views.License.LicenseActivationPage), licenseViewModel);
    }

    public bool IsDashboardReady()
    {
        return RootFrame.Content is MainPage page && page.IsDashboardDataLoaded();
    }

    public Task WhenDashboardReadyAsync()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Complete()
        {
            tcs.TrySetResult(true);
        }

        if (RootFrame.Content is MainPage page)
        {
            if (page.IsLoaded && page.IsDashboardDataLoaded())
            {
                Complete();
            }
            else
            {
                void OnLoaded(object s, RoutedEventArgs e)
                {
                    page.Loaded -= OnLoaded;
                    if (page.IsDashboardDataLoaded())
                    {
                        Complete();
                    }
                    else
                    {
                        page.DashboardDataLoaded += () => Complete();
                    }
                }
                page.Loaded += OnLoaded;
            }
        }
        else if (RootFrame.Content is FrameworkElement element)
        {
            if (element.IsLoaded)
            {
                Complete();
            }
            else
            {
                void OnLoaded(object s, RoutedEventArgs e)
                {
                    element.Loaded -= OnLoaded;
                    Complete();
                }
                element.Loaded += OnLoaded;
            }
        }
        else
        {
            Complete();
        }

        return tcs.Task;
    }

    private void RestoreMainPresentation()
    {
        MaximizeWindow();
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch
        {
        }
    }
}
