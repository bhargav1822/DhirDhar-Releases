using System;
using System.Threading.Tasks;
using DhirDhar.Desktop.ViewModels.Backup;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Views.Backup;

public sealed partial class BackupRestorePage : Page
{
    public BackupRestorePage()
    {
        InitializeComponent();
    }

    public BackupRestoreViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(BackupRestoreViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        if (viewModel != null)
        {
            var loc = App.ServiceProvider?.GetService(typeof(DhirDhar.Application.Localization.ILocalizationService)) as DhirDhar.Application.Localization.ILocalizationService;
            string L(string key, string fallback) => loc?.GetString(key) ?? fallback;

            viewModel.ConfirmRestoreCallback = (message) =>
            {
                var tcs = new TaskCompletionSource<bool>();
                async void ShowDialog()
                {
                    try
                    {
                        var root = this.XamlRoot ?? App.MainWindow?.Content?.XamlRoot;
                        if (root == null)
                        {
                            tcs.TrySetResult(true);
                            return;
                        }

                        var dialog = new ContentDialog
                        {
                            Title = L("ConfirmRestoreTitle", "Confirm Data Restoration"),
                            Content = message,
                            PrimaryButtonText = L("RestoreData", "Restore Data"),
                            CloseButtonText = L("Cancel", "Cancel"),
                            DefaultButton = ContentDialogButton.Close,
                            XamlRoot = root
                        };

                        var result = await dialog.ShowAsync();
                        tcs.TrySetResult(result == ContentDialogResult.Primary);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error showing confirm dialog: {ex}");
                        tcs.TrySetResult(false);
                    }
                }

                if (this.DispatcherQueue != null && !this.DispatcherQueue.HasThreadAccess)
                {
                    this.DispatcherQueue.TryEnqueue(ShowDialog);
                }
                else
                {
                    ShowDialog();
                }

                return tcs.Task;
            };

            viewModel.PickBackupFileCallback = () =>
            {
                var tcs = new TaskCompletionSource<string?>();
                async void PickFile()
                {
                    try
                    {
                        var picker = new Windows.Storage.Pickers.FileOpenPicker();
                        var hwnd = App.MainWindowHandle;
                        if (hwnd != IntPtr.Zero)
                        {
                            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                        }
                        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
                        picker.FileTypeFilter.Add(".ddbackup");
                        picker.FileTypeFilter.Add(".zip");

                        var file = await picker.PickSingleFileAsync();
                        tcs.TrySetResult(file?.Path);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error picking file: {ex}");
                        tcs.TrySetResult(null);
                    }
                }

                if (this.DispatcherQueue != null && !this.DispatcherQueue.HasThreadAccess)
                {
                    this.DispatcherQueue.TryEnqueue(PickFile);
                }
                else
                {
                    PickFile();
                }

                return tcs.Task;
            };

            viewModel.PromptPasswordOrRecoveryKeyCallback = (promptMessage) =>
            {
                var tcs = new TaskCompletionSource<string?>();
                async void ShowPasswordDialog()
                {
                    try
                    {
                        var root = this.XamlRoot ?? App.MainWindow?.Content?.XamlRoot;
                        if (root == null)
                        {
                            tcs.TrySetResult(null);
                            return;
                        }

                        var stack = new StackPanel { Spacing = 12 };
                        var textPrompt = new TextBlock
                        {
                            Text = promptMessage ?? L("BackupDecryptionPrompt", "Please enter the password or disaster recovery key to decrypt this backup:"),
                            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                        };
                        var passBox = new PasswordBox
                        {
                            PlaceholderText = L("PasswordOrRecoveryKeyPlaceholder", "Password or Recovery Key (e.g. DDRK-...)")
                        };
                        stack.Children.Add(textPrompt);
                        stack.Children.Add(passBox);

                        var dialog = new ContentDialog
                        {
                            Title = L("BackupDecryptionTitle", "Backup Decryption Required"),
                            Content = stack,
                            PrimaryButtonText = L("DecryptAndRestore", "Decrypt & Restore"),
                            CloseButtonText = L("Cancel", "Cancel"),
                            DefaultButton = ContentDialogButton.Primary,
                            XamlRoot = root
                        };

                        var result = await dialog.ShowAsync();
                        if (result == ContentDialogResult.Primary)
                        {
                            tcs.TrySetResult(passBox.Password);
                        }
                        else
                        {
                            tcs.TrySetResult(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error showing password dialog: {ex}");
                        tcs.TrySetResult(null);
                    }
                }

                if (this.DispatcherQueue != null && !this.DispatcherQueue.HasThreadAccess)
                {
                    this.DispatcherQueue.TryEnqueue(ShowPasswordDialog);
                }
                else
                {
                    ShowPasswordDialog();
                }

                return tcs.Task;
            };
        }
        try
        {
            Bindings.Update();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating Backup & Restore page bindings: {ex}");
            viewModel?.SetPageError(ex);
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        try
        {
            if (e.Parameter is BackupRestoreViewModel viewModel)
            {
                SetViewModel(viewModel);
            }
            else if (ViewModel == null)
            {
                var vm = App.ServiceProvider?.GetService(typeof(BackupRestoreViewModel)) as BackupRestoreViewModel;
                if (vm != null)
                {
                    SetViewModel(vm);
                }
                else
                {
                    throw new InvalidOperationException("BackupRestoreViewModel could not be resolved for the Backup & Restore page.");
                }
            }

            _ = ViewModel?.InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing Backup & Restore page: {ex}");
            if (ViewModel != null)
            {
                ViewModel.SetPageError(ex);
            }
        }
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        try
        {
            ViewModel?.Dispose();
        }
        catch
        {
        }
    }

    public async Task LoadBackupPageAsync()
    {
        if (ViewModel != null)
        {
            await ViewModel.InitializeAsync();
        }
    }

    private async void OnRestoreItemClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DhirDhar.Application.Backup.Models.BackupHistoryEntry entry && ViewModel != null)
        {
            await ViewModel.RestoreSpecificBackupAsync(entry);
        }
    }
}
