using System;
using System.ComponentModel;
using DhirDhar.Desktop.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DhirDhar.Desktop.Views.Loading;

public sealed partial class LoadingPage : Page
{
    public LoadingPage()
    {
        InitializeComponent();
    }

    public LoadingViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(LoadingViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        Bindings.Update();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is LoadingViewModel viewModel)
        {
            SetViewModel(viewModel);
        }

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateSpinnerState();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        SpinnerStoryboard.Stop();
    }

    private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // Viewbox handles all scaling automatically - no manual layout needed
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsConnectingDatabase))
        {
            UpdateSpinnerState();
        }
    }

    private void UpdateSpinnerState()
    {
        if (ViewModel.IsConnectingDatabase)
        {
            SpinnerStoryboard.Begin();
        }
        else
        {
            SpinnerStoryboard.Stop();
        }
    }
}