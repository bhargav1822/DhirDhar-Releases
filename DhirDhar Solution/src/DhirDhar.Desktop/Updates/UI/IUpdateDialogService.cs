using System;
using System.Threading.Tasks;
using DhirDhar.Desktop.Updates.Models;

namespace DhirDhar.Desktop.Updates.UI;

/// <summary>
/// Opens the localized update notification dialog on the application's main thread.
/// </summary>
public interface IUpdateDialogService
{
    Task<bool?> ShowUpdateAvailableAsync(UpdateInfo updateInfo, string currentVersion);
}
