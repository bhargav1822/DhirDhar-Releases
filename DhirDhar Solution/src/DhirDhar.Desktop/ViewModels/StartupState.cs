namespace DhirDhar.Desktop.ViewModels;

public enum StartupState
{
    Starting,
    LoadingConfiguration,
    InitializingServices,
    InitializingDatabase,
    CheckingDatabase,
    PreparingApplication,
    Ready,
    Failed
}

public sealed record StartupProgress(StartupState State, string Message, int Percentage);
