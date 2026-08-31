using System;
using System.Threading.Tasks;
using DhirDhar.Desktop.Updates.Models;
using DhirDhar.Desktop.Views.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DhirDhar.Desktop.Updates.UI;

public sealed class UpdateDialogService : IUpdateDialogService
{
    private readonly IServiceProvider _serviceProvider;

    public UpdateDialogService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<bool?> ShowUpdateAvailableAsync(UpdateInfo updateInfo, string currentVersion)
    {
        try
        {
            var vm = _serviceProvider.GetRequiredService<UpdateNotificationViewModel>();
            vm.Populate(updateInfo, currentVersion);

            var dialog = new UpdateNotificationDialog
            {
                DataContext = vm,
                PrimaryButtonText = vm.UpdateNowLabel,
                CloseButtonText = vm.LaterLabel,
                XamlRoot = App.MainWindow?.Content?.XamlRoot
            };

            var result = await dialog.ShowAsync().AsTask().ConfigureAwait(true);
            return result == ContentDialogResult.Primary ? true : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UPDATER] Dialog show error: {ex.Message}");
            return null;
        }
    }
}
