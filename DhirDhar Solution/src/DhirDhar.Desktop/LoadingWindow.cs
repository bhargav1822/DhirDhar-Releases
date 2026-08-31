using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DhirDhar.Desktop.ViewModels;
using DhirDhar.Desktop.Views.Loading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace DhirDhar.Desktop;

public sealed class LoadingWindow : Window
{
    private const int LoadingWindowWidth = 709;
    private const int LoadingWindowHeight = 473;
    private static readonly TimeSpan MinimumLoadingDisplayTime = TimeSpan.Zero;

    private readonly Frame _rootFrame;
    private bool _presentationApplied;

    public LoadingWindow()
    {
        Title = "DhirDhar Solution";

        _rootFrame = new Frame();
        Content = _rootFrame;

        ApplyLoadingPresentation();
    }

    public void EnsureLoadingPresentationApplied()
    {
        if (_presentationApplied)
        {
            return;
        }

        _presentationApplied = true;
        ApplyLoadingPresentation();
    }

    private void ApplyLoadingPresentation()
    {
        var appWindow = AppWindow;

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        CenterAndResize();
    }

    private void CenterAndResize()
    {
        try
        {
            var scale = GetWindowScale();
            var width = (int)Math.Round(LoadingWindowWidth * scale);
            var height = (int)Math.Round(LoadingWindowHeight * scale);

            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
            var workArea = displayArea.WorkArea;

            // Maintain aspect ratio (709:473) when clamping to screen
            if (width > workArea.Width || height > workArea.Height)
            {
                double aspectRatio = (double)LoadingWindowWidth / LoadingWindowHeight;
                if (width > workArea.Width)
                {
                    width = workArea.Width;
                    height = (int)Math.Round(width / aspectRatio);
                }
                if (height > workArea.Height)
                {
                    height = workArea.Height;
                    width = (int)Math.Round(height * aspectRatio);
                }
            }

            var x = workArea.X + (workArea.Width - width) / 2;
            var y = workArea.Y + (workArea.Height - height) / 2;

            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }
        catch
        {
        }
    }

    private double GetWindowScale()
    {
        try
        {
            var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(this));
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public void ShowLoading(IServiceProvider serviceProvider)
    {
        var loadingViewModel = serviceProvider.GetRequiredService<LoadingViewModel>();
        _rootFrame.Navigate(typeof(LoadingPage), loadingViewModel);
    }

    public async Task RunStartupAsync(IServiceProvider serviceProvider)
    {
        var loadingViewModel = _rootFrame.Content as LoadingPage;
        if (loadingViewModel?.DataContext is not LoadingViewModel vm)
        {
            vm = serviceProvider.GetRequiredService<LoadingViewModel>();
        }

        var startedAt = DateTime.UtcNow;
        await vm.StartAsync();

        if (vm.CurrentState != StartupState.Ready)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.StartupCompleted += () => completion.TrySetResult(true);
            await completion.Task;
        }

        var elapsed = DateTime.UtcNow - startedAt;
        var remaining = MinimumLoadingDisplayTime - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }
    }
}
