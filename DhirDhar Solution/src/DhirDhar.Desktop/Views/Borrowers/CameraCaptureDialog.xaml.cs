using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Storage;
using DhirDhar.Application.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Views.Borrowers;

public sealed partial class CameraCaptureDialog : ContentDialog
{
    private readonly string _photoPrefix;
    private readonly ILocalizationService? _localizationService;
    private MediaCapture? _mediaCapture;

    public CameraCaptureDialog(string photoPrefix = "borrower")
    {
        InitializeComponent();
        _photoPrefix = photoPrefix;
        _localizationService = App.ServiceProvider?.GetService<ILocalizationService>();
        Title = L("CameraCapture");
        PrimaryButtonText = L("CapturePhoto");
        CloseButtonText = L("Cancel");
        CameraLabelTextBlock.Text = L("CameraColon");
        StatusTextBlock.Text = L("InitializingCameraPreview");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private string L(string key) => _localizationService?.GetString(key) ?? key;

    public string? CapturedFilePath { get; private set; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await LoadCameraDevicesAsync();
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        await CleanupCameraAsync();
    }

    private async Task LoadCameraDevicesAsync()
    {
        try
        {
            StatusTextBlock.Text = L("DetectingCameras");
            StatusStackPanel.Visibility = Visibility.Visible;
            IsPrimaryButtonEnabled = false;

            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            if (devices == null || devices.Count == 0)
            {
                StatusTextBlock.Text = L("NoCameraDetected");
                StatusIcon.Glyph = "\uE711"; // Error icon
                CameraSelectionGrid.Visibility = Visibility.Collapsed;
                return;
            }

            if (devices.Count == 1)
            {
                CameraSelectionGrid.Visibility = Visibility.Collapsed;
                await StartCameraPreviewAsync(devices[0]);
            }
            else
            {
                CameraSelectionGrid.Visibility = Visibility.Visible;
                CameraDeviceComboBox.ItemsSource = devices;
                CameraDeviceComboBox.SelectedIndex = 0;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Camera access error: {ex.Message} (0x{ex.HResult:X8})");
            StatusTextBlock.Text = L("CameraAccessDisabled");
            StatusIcon.Glyph = "\uE711";
            IsPrimaryButtonEnabled = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Camera enumeration error: {ex.Message} (0x{ex.HResult:X8})");
            StatusTextBlock.Text = string.Format(L("CameraInitializeFailed"), ex.Message);
            StatusIcon.Glyph = "\uE711";
            IsPrimaryButtonEnabled = false;
        }
    }

    private async void CameraDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CameraDeviceComboBox.SelectedItem is DeviceInformation selectedDevice)
        {
            await StartCameraPreviewAsync(selectedDevice);
        }
    }

    private async Task StartCameraPreviewAsync(DeviceInformation device)
    {
        await CleanupCameraAsync();

        try
        {
            StatusTextBlock.Text = string.Format(L("ConnectingToCamera"), device.Name);
            StatusStackPanel.Visibility = Visibility.Visible;
            IsPrimaryButtonEnabled = false;

            _mediaCapture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = device.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl
            };

            await _mediaCapture.InitializeAsync(settings);

            var frameSource = _mediaCapture.FrameSources.Values
                .FirstOrDefault(fs => fs.Info.MediaStreamType == MediaStreamType.VideoPreview)
                ?? _mediaCapture.FrameSources.Values
                .FirstOrDefault(fs => fs.Info.MediaStreamType == MediaStreamType.VideoRecord)
                ?? _mediaCapture.FrameSources.Values.FirstOrDefault();

            if (frameSource != null)
            {
                CameraMediaPlayerElement.Source = MediaSource.CreateFromMediaFrameSource(frameSource);
                StatusStackPanel.Visibility = Visibility.Collapsed;
                IsPrimaryButtonEnabled = true;
                ErrorBannerTextBlock.Visibility = Visibility.Collapsed;
            }
            else
            {
                StatusTextBlock.Text = string.Format(L("CameraStreamNotFound"), device.Name);
                StatusIcon.Glyph = "\uE711";
                IsPrimaryButtonEnabled = false;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Camera preview permission denied: {ex.Message} (0x{ex.HResult:X8})");
            StatusTextBlock.Text = L("CameraAccessDisabled");
            StatusIcon.Glyph = "\uE711";
            IsPrimaryButtonEnabled = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Camera preview error on {device.Name}: {ex.Message} (0x{ex.HResult:X8})\n{ex}");
            StatusTextBlock.Text = string.Format(L("CameraPreviewStartFailed"), device.Name, ex.Message);
            StatusIcon.Glyph = "\uE711";
            IsPrimaryButtonEnabled = false;
        }
    }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (_mediaCapture != null)
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var photosDir = Path.Combine(localAppData, "DhirDhar", "Photos");
                Directory.CreateDirectory(photosDir);

                var fileName = $"{_photoPrefix}_{Guid.NewGuid():N}.jpg";
                var localFolder = await StorageFolder.GetFolderFromPathAsync(photosDir);
                var destinationFile = await localFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);

                await _mediaCapture.CapturePhotoToStorageFileAsync(ImageEncodingProperties.CreateJpeg(), destinationFile);
                CapturedFilePath = destinationFile.Path;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Photo capture error: {ex.Message} (0x{ex.HResult:X8})");
            args.Cancel = true;
            ErrorBannerTextBlock.Text = string.Format(L("CaptureFailed"), ex.Message);
            ErrorBannerTextBlock.Visibility = Visibility.Visible;
        }
        finally
        {
            await CleanupCameraAsync();
            deferral.Complete();
        }
    }

    private async void ContentDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        CapturedFilePath = null;
        await CleanupCameraAsync();
    }

    private async Task CleanupCameraAsync()
    {
        try
        {
            CameraMediaPlayerElement.Source = null;
            if (_mediaCapture != null)
            {
                _mediaCapture.Dispose();
                _mediaCapture = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Camera cleanup error: {ex.Message}");
            _mediaCapture = null;
        }
        await Task.CompletedTask;
    }
}
