using System.Threading.Tasks;
using DhirDhar.Desktop.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Views.Integrity;

public sealed partial class IntegrityPage : Page
{
    public IntegrityPage()
    {
        InitializeComponent();
    }

    public IntegrityViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(IntegrityViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        ViewModel.ConfirmRepairCallback = async (title, message) =>
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = ViewModel.RepairButtonLabel,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        };

        Bindings.Update();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is IntegrityViewModel viewModel)
        {
            SetViewModel(viewModel);
        }
    }
}
