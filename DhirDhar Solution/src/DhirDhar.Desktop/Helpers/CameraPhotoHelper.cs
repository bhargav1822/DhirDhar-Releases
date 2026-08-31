using System;
using System.IO;
using System.Threading.Tasks;
using DhirDhar.Desktop.Views.Borrowers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DhirDhar.Desktop.Helpers;

public static class CameraPhotoHelper
{
    private static string GetPhotoStorageDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var photosDir = Path.Combine(localAppData, "DhirDhar", "Photos");
        Directory.CreateDirectory(photosDir);
        return photosDir;
    }

    public static async Task<string?> CaptureOrPickPhotoAsync(string prefix, nint windowHandle, XamlRoot? xamlRoot = null)
    {
        string photosDir = GetPhotoStorageDirectory();
        string fileName = $"{prefix}_{Guid.NewGuid():N}.jpg";

        // 1. Attempt Live Camera Capture via CameraCaptureDialog if XamlRoot is available
        if (xamlRoot != null)
        {
            try
            {
                var dialog = new CameraCaptureDialog(prefix)
                {
                    XamlRoot = xamlRoot
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.CapturedFilePath))
                {
                    return dialog.CapturedFilePath;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CameraCaptureDialog failed: {ex.Message}. Falling back to FilePicker.");
            }
        }

        // 2. Fallback to FileOpenPicker if camera dialog fails or xamlRoot unavailable
        try
        {
            var openPicker = new FileOpenPicker();
            if (windowHandle != nint.Zero)
            {
                InitializeWithWindow.Initialize(openPicker, windowHandle);
            }
            openPicker.ViewMode = PickerViewMode.Thumbnail;
            openPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            openPicker.FileTypeFilter.Add(".jpg");
            openPicker.FileTypeFilter.Add(".jpeg");
            openPicker.FileTypeFilter.Add(".png");

            var pickedFile = await openPicker.PickSingleFileAsync();
            if (pickedFile != null)
            {
                var destinationFile = Path.Combine(photosDir, fileName);
                File.Copy(pickedFile.Path, destinationFile, overwrite: true);
                return destinationFile;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FilePicker failed: {ex.Message}");
        }

        return null;
    }
}
