using System.Threading.Tasks;
using DhirDhar.Desktop.ViewModels.Security;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Views.Security;

public sealed partial class SecurityPage : Page
{
    public SecurityPage()
    {
        InitializeComponent();
    }

    public SecurityViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(SecurityViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        Bindings.Update();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is SecurityViewModel viewModel)
        {
            SetViewModel(viewModel);
        }
    }

    public async Task LoadSecurityPageAsync()
    {
        await Task.CompletedTask;
    }
}
