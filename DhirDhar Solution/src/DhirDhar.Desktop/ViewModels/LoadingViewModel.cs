using System;
using System.Threading.Tasks;
using DhirDhar.Application.Localization;
using DhirDhar.Desktop.Configuration;
using DhirDhar.Desktop.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace DhirDhar.Desktop.ViewModels;

public sealed class LoadingViewModel : ViewModelBase
{
    private readonly IApplicationStartupService _startupService;
    private readonly ILogger<LoadingViewModel> _logger;

    private StartupState _currentState = StartupState.Starting;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _hasFailed;
    private bool _canRetry = true;
    private bool _isRetrying;
    private int _progressPercentage;
    private bool _isConnectingDatabase = true;

    public LoadingViewModel(
        AppOptions appOptions,
        IApplicationStartupService startupService,
        ILocalizationService localizationService,
        ILogger<LoadingViewModel> logger)
    {
        _startupService = startupService;
        _logger = logger;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Note: The splash/loading screen must ALWAYS remain in English,
        // regardless of the user's selected application language.
        // Therefore, we intentionally do NOT attach runtime localization here.

        ApplicationName = appOptions.Name;
        ApplicationVersion = appOptions.Version;

        StartupCommand = new RelayCommand(async () => await ExecuteStartupAsync());
        RetryCommand = new RelayCommand(async () => await ExecuteRetryAsync(), () => CanRetry);
        ExitCommand = new RelayCommand(ExecuteExit);
    }

    private readonly DispatcherQueue _dispatcherQueue;

    public string ApplicationName { get; }
    public string ApplicationVersion { get; }

    public string FormattedApplicationVersion => $"Version {ApplicationVersion}";

    public string LoadingAppLabel => "Loading DhirDhar...";
    public string ConnectingDatabaseLabel => "Connecting to database...";
    public string CopyrightLabel => "© 2026 DhirDhar. All rights reserved.";

    public StartupState CurrentState
    {
        get => _currentState;
        private set => SetProperty(ref _currentState, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool HasFailed
    {
        get => _hasFailed;
        private set => SetProperty(ref _hasFailed, value);
    }

    public bool CanRetry
    {
        get => _canRetry;
        private set
        {
            if (SetProperty(ref _canRetry, value))
            {
                ((RelayCommand)RetryCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsRetrying
    {
        get => _isRetrying;
        private set => SetProperty(ref _isRetrying, value);
    }

    public int ProgressPercentage
    {
        get => _progressPercentage;
        private set
        {
            if (SetProperty(ref _progressPercentage, value))
            {
                OnPropertyChanged(nameof(FormattedProgressPercentage));
            }
        }
    }

    public string FormattedProgressPercentage => $"{ProgressPercentage}%";

    public bool IsConnectingDatabase
    {
        get => _isConnectingDatabase;
        private set => SetProperty(ref _isConnectingDatabase, value);
    }

    public RelayCommand StartupCommand { get; }
    public RelayCommand RetryCommand { get; }
    public RelayCommand ExitCommand { get; }

    public event Action? StartupCompleted;

    public async Task StartAsync()
    {
        await ExecuteStartupAsync();
    }

    private async Task ExecuteStartupAsync()
    {
        HasFailed = false;
        ErrorMessage = string.Empty;
        IsConnectingDatabase = true;

        try
        {
            var progress = new Progress<StartupProgress>(HandleProgress);
            await Task.Run(() => _startupService.InitializeAsync(progress)).ConfigureAwait(false);
            _logger.LogInformation("Application startup completed successfully.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Application startup failed.");
            RunOnUiThread(() =>
            {
                SetProgress(StartupState.Failed, "Startup failed.");
                if (exception.Message.Contains("integrity", StringComparison.OrdinalIgnoreCase))
                {
                    ErrorMessage = exception.Message;
                }
                else
                {
                    ErrorMessage = "The application could not start. Please try again or contact support.";
                }
                HasFailed = true;
                IsConnectingDatabase = false;
            });
        }
    }

    private void HandleProgress(StartupProgress progress)
    {
        RunOnUiThread(() =>
        {
            SetProgress(progress.State, GetEnglishStartupMessage(progress.State, progress.Message));
            ProgressPercentage = progress.Percentage;
        });
    }

    private async Task ExecuteRetryAsync()
    {
        IsRetrying = true;
        CanRetry = false;
        HasFailed = false;
        ErrorMessage = string.Empty;

        await ExecuteStartupAsync();

        IsRetrying = false;
        CanRetry = true;
    }

    private void SetProgress(StartupState state, string message)
    {
        CurrentState = state;
        StatusMessage = message;

        switch (state)
        {
            case StartupState.InitializingDatabase:
            case StartupState.CheckingDatabase:
                IsConnectingDatabase = true;
                break;
            case StartupState.Ready:
                IsConnectingDatabase = false;
                StartupCompleted?.Invoke();
                break;
        }
    }

    private string GetEnglishStartupMessage(StartupState state, string fallback)
    {
        return state switch
        {
            StartupState.Starting => $"Starting {ApplicationName}...",
            StartupState.LoadingConfiguration => "Loading configuration...",
            StartupState.InitializingServices => "Initializing services...",
            StartupState.InitializingDatabase => "Initializing database...",
            StartupState.CheckingDatabase => "Checking database...",
            StartupState.PreparingApplication => "Preparing application...",
            StartupState.Ready => "Ready",
            StartupState.Failed => "Startup failed.",
            _ => fallback
        };
    }

    private static void ExecuteExit()
    {
        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
