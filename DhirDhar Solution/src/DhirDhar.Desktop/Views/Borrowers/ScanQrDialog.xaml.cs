using System;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Application.QrCode;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DhirDhar.Desktop.Views.Borrowers;

public sealed partial class ScanQrDialog : ContentDialog
{
    private readonly IBorrowerService _borrowerService;
    private readonly IQrCodeService _qrCodeService;
    private readonly ILocalizationService _localizationService;

    public BorrowerSummary? ScannedBorrower { get; private set; }

    public ScanQrDialog(IBorrowerService borrowerService, IQrCodeService qrCodeService, ILocalizationService localizationService)
    {
        _borrowerService = borrowerService;
        _qrCodeService = qrCodeService;
        _localizationService = localizationService;

        InitializeComponent();

        Title = _localizationService.GetString("ScanQrTitle") ?? "Scan Account QR";
        PrimaryButtonText = _localizationService.GetString("SearchAccount") ?? "Search Account";
        CloseButtonText = _localizationService.GetString("Cancel") ?? "Cancel";

        InstructionTitleTextBlock.Text = _localizationService.GetString("ScanQrInstructionTitle") ?? "Scan Account QR Code";
        InstructionSubtitleTextBlock.Text = _localizationService.GetString("ScanQrInstructionSubtitle") ?? "Scan the borrower's account QR code with a USB scanner or enter the code below.";
        QrInputTextBox.PlaceholderText = _localizationService.GetString("ScanQrPlaceholder") ?? "DHIRDHAR|ACCOUNT|DJ102";
    }

    private void ContentDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        QrInputTextBox.Focus(FocusState.Programmatic);
    }

    private void QrInputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
    }

    private async void QrInputTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await ProcessScanAsync();
        }
    }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        await ProcessScanAsync();
    }

    private void ContentDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ScannedBorrower = null;
    }

    private async Task ProcessScanAsync()
    {
        var rawText = QrInputTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            ShowError(_localizationService.GetString("InvalidQrCode") ?? "Invalid DhirDhar QR Code.");
            return;
        }

        // 1. Validate DhirDhar QR format
        if (!_qrCodeService.TryParsePayload(rawText, out var borrowerNumber) || string.IsNullOrWhiteSpace(borrowerNumber))
        {
            ShowError(_localizationService.GetString("InvalidQrCode") ?? "Invalid DhirDhar QR Code.");
            return;
        }

        // 2. Search existing database for exact account
        SearchingProgressRing.Visibility = Visibility.Visible;
        SearchingProgressRing.IsActive = true;
        IsPrimaryButtonEnabled = false;

        try
        {
            var borrower = await _borrowerService.GetByBorrowerNumberAsync(borrowerNumber);
            if (borrower == null)
            {
                ShowError(_localizationService.GetString("BorrowerAccountNotFound") ?? "Borrower account not found.");
                return;
            }

            // Success: exact account located
            ScannedBorrower = borrower;
            Hide();
        }
        catch (Exception)
        {
            ShowError(_localizationService.GetString("BorrowerAccountNotFound") ?? "Borrower account not found.");
        }
        finally
        {
            SearchingProgressRing.IsActive = false;
            SearchingProgressRing.Visibility = Visibility.Collapsed;
            IsPrimaryButtonEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorMessageTextBlock.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
        QrInputTextBox.SelectAll();
        QrInputTextBox.Focus(FocusState.Programmatic);
    }
}
