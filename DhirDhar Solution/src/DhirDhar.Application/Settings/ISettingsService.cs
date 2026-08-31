using System;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Localization;

namespace DhirDhar.Application.Settings;

public interface ISettingsService
{
    ApplicationLanguageSettings LanguageSettings { get; }
    Task<AppSettingsModel> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(AppSettingsModel settings, CancellationToken cancellationToken = default);
    Task ResetSettingsAsync(CancellationToken cancellationToken = default);
    event EventHandler<AppSettingsModel>? SettingsChanged;
    Task ApplySettingsOnStartupAsync(CancellationToken cancellationToken = default);
}

