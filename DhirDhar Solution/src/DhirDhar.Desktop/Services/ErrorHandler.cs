using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.Services;

public interface IErrorHandler
{
    void HandleError(Exception exception, string? context = null, bool isFatal = false);
    void HandleStartupError(Exception exception);
    void HandleUnexpectedError(Exception exception);
}

public sealed class ErrorHandler : IErrorHandler
{
    private readonly ILogger<ErrorHandler> _logger;

    public ErrorHandler(ILogger<ErrorHandler> logger)
    {
        _logger = logger;
    }

    public void HandleError(Exception exception, string? context = null, bool isFatal = false)
    {
        if (isFatal)
        {
            _logger.LogCritical(exception, "Fatal error in context '{Context}'.", context ?? "Unknown");
        }
        else
        {
            _logger.LogError(exception, "Error in context '{Context}'.", context ?? "Unknown");
        }
    }

    public void HandleStartupError(Exception exception)
    {
        HandleError(exception, "Startup", isFatal: true);
    }

    public void HandleUnexpectedError(Exception exception)
    {
        HandleError(exception, "Unexpected", isFatal: false);
    }
}
