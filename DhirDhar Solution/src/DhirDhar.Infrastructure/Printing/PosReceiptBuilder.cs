using System;
using System.Collections.Generic;
using System.IO;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Printing;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;

namespace DhirDhar.Infrastructure.Printing;

/// <summary>
/// High-fidelity POS and thermal receipt PDF layout generator.
/// Supports 58 mm, 80 mm, 110 mm, Custom thermal widths, as well as A4, A5, and Letter paper sizes.
/// Dynamically reflows Gujarati and Indic Unicode text, localizes numbers, and calculates continuous receipt heights.
/// </summary>
public static class PosReceiptBuilder
{
    static PosReceiptBuilder()
    {
        try
        {
            if (GlobalFontSettings.FontResolver is not Reports.IndicFontResolver)
            {
                GlobalFontSettings.FontResolver = new Reports.IndicFontResolver();
            }
        }
        catch
        {
            // Ignore if font resolver already set
        }
    }

    public static string BuildReceiptPdf(ReceiptData receipt, string exportDirectory)
    {
        if (receipt == null) throw new ArgumentNullException(nameof(receipt));

        Directory.CreateDirectory(exportDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var fileName = $"Receipt_{receipt.Type}_{receipt.BorrowerNumber ?? "General"}_{timestamp}.pdf";
        var filePath = Path.Combine(exportDirectory, fileName);

        bool isThermal = PaperSizeHelper.IsThermalPosSize(receipt.PaperSize);
        var (pageWidthPt, defaultHeightPt, _) = PaperSizeHelper.GetDimensions(receipt.PaperSize, receipt.CustomPaperWidthMm);

        double margin = isThermal ? Math.Max(6.0, pageWidthPt * 0.04) : 36.0;
        double printableWidth = pageWidthPt - (margin * 2);

        // Pre-calculate content height for single continuous thermal roll
        double calculatedHeight = isThermal
            ? MeasureThermalHeight(receipt, printableWidth, margin)
            : defaultHeightPt;

        using var document = new PdfDocument();
        document.Info.Title = $"{receipt.BusinessName} - {receipt.Title}";
        document.Info.Author = receipt.BusinessName;

        var page = document.AddPage();
        page.Width = XUnit.FromPoint(pageWidthPt);
        page.Height = XUnit.FromPoint(Math.Max(120.0, calculatedHeight));

        using var gfx = XGraphics.FromPdfPage(page);
        DrawReceiptContent(gfx, receipt, margin, printableWidth, isThermal, page);

        document.Save(filePath);
        return filePath;
    }

    private static double MeasureThermalHeight(ReceiptData receipt, double printableWidth, double margin)
    {
        double h = margin * 2;

        // Header (Business Name, Subtitle, Title)
        h += 45;

        // Metadata Key-Values
        int keyValCount = 0;
        if (!string.IsNullOrWhiteSpace(receipt.BorrowerName)) keyValCount++;
        if (!string.IsNullOrWhiteSpace(receipt.BorrowerNumber)) keyValCount++;
        if (!string.IsNullOrWhiteSpace(receipt.Contact)) keyValCount++;
        if (!string.IsNullOrWhiteSpace(receipt.Village)) keyValCount++;
        if (!string.IsNullOrWhiteSpace(receipt.Address)) keyValCount++;
        if (receipt.LoanDate.HasValue) keyValCount++;
        if (receipt.InitialPrincipal.HasValue) keyValCount++;
        if (receipt.InterestRate.HasValue) keyValCount++;
        if (!string.IsNullOrWhiteSpace(receipt.DisplayDuration)) keyValCount++;
        if (receipt.MonthlyInterest.HasValue) keyValCount++;
        if (!string.IsNullOrWhiteSpace(receipt.OrnamentType)) keyValCount++;
        if (!string.IsNullOrWhiteSpace(receipt.OrnamentWeight)) keyValCount++;
        if (receipt.TransactionDate.HasValue) keyValCount++;
        if (receipt.TransactionAmount.HasValue) keyValCount++;
        if (!string.IsNullOrWhiteSpace(receipt.PaymentMode)) keyValCount++;
        if (receipt.CurrentPrincipal.HasValue) keyValCount++;
        if (receipt.TotalInterest.HasValue) keyValCount++;
        if (receipt.TotalOutstanding.HasValue) keyValCount++;
        if (receipt.TotalDeposits.HasValue) keyValCount++;
        if (receipt.TotalWithdrawals.HasValue) keyValCount++;

        h += (keyValCount * 14.0);

        // Multi-line description if present
        if (!string.IsNullOrWhiteSpace(receipt.Description))
        {
            var lines = WrapText(receipt.Description, printableWidth, 8);
            h += (lines.Count * 12.0) + 8;
        }

        // Table Items if present
        if (receipt.Items != null && receipt.Items.Count > 0)
        {
            h += 22; // Table header
            h += (receipt.Items.Count * 22.0); // Table rows
        }

        // QR Code if present
        if (receipt.QrCodePngBytes != null && receipt.QrCodePngBytes.Length > 0)
        {
            double qrSize = Math.Min(110.0, Math.Max(70.0, printableWidth * 0.55));
            h += qrSize + 20;
        }

        // Footer
        h += 45;

        return h;
    }

    private static void DrawReceiptContent(XGraphics gfx, ReceiptData r, double margin, double printableWidth, bool isThermal, PdfPage page)
    {
        var lang = r.LanguageCode ?? "gu-IN";
        var normLang = ScriptTranslator.NormalizeLanguageCode(lang);
        bool isEnglish = normLang == "en";

        // Fonts setup (resolved through IndicFontResolver)
        double scale = isThermal ? (printableWidth < 180 ? 0.85 : 1.0) : 1.1;
        var fontBiz = new XFont("Arial", 13 * scale, XFontStyle.Bold);
        var fontTitle = new XFont("Arial", 10 * scale, XFontStyle.Bold);
        var fontSub = new XFont("Arial", 7.5 * scale, XFontStyle.Regular);
        var fontBody = new XFont("Arial", 8 * scale, XFontStyle.Regular);
        var fontBold = new XFont("Arial", 8 * scale, XFontStyle.Bold);
        var fontSmall = new XFont("Arial", 7 * scale, XFontStyle.Regular);
        var fontAccent = new XFont("Arial", 9 * scale, XFontStyle.Bold);

        double y = margin;

        // 1. Business Header (Centered)
        var bizName = LocalizeText(r.BusinessName, lang);
        gfx.DrawString(bizName, fontBiz, XBrushes.Black, new XRect(margin, y, printableWidth, 18), XStringFormats.Center);
        y += 18;

        if (!string.IsNullOrWhiteSpace(r.Subtitle))
        {
            gfx.DrawString(LocalizeText(r.Subtitle, lang), fontSub, XBrushes.DarkGray, new XRect(margin, y, printableWidth, 12), XStringFormats.Center);
            y += 12;
        }

        // Document Title
        var titleText = !string.IsNullOrWhiteSpace(r.Title) ? LocalizeText(r.Title, lang) : GetDefaultTitle(r.Type, lang);
        gfx.DrawString(titleText, fontTitle, XBrushes.Black, new XRect(margin, y, printableWidth, 14), XStringFormats.Center);
        y += 16;

        // Divider Line
        DrawDashedLine(gfx, margin, y, margin + printableWidth, y);
        y += 8;

        // 2. Borrower Details
        if (!string.IsNullOrWhiteSpace(r.BorrowerName))
        {
            var bName = isEnglish ? r.BorrowerName : LocalizeText(r.BorrowerName, lang);
            y = DrawKeyValue(gfx, GetLabel("Borrower", lang), bName, fontBody, fontBold, margin, y, printableWidth);
        }
        if (!string.IsNullOrWhiteSpace(r.BorrowerNumber))
        {
            var formattedNo = isEnglish ? r.BorrowerNumber : ScriptTranslator.ConvertDigitsToIndic(r.BorrowerNumber, normLang);
            y = DrawKeyValue(gfx, GetLabel("AccountNo", lang), formattedNo, fontBody, fontBold, margin, y, printableWidth);
        }
        if (!string.IsNullOrWhiteSpace(r.Contact))
        {
            var contactDigits = isEnglish ? r.Contact : ScriptTranslator.ConvertDigitsToIndic(r.Contact, normLang);
            y = DrawKeyValue(gfx, GetLabel("Contact", lang), contactDigits, fontBody, fontBody, margin, y, printableWidth);
        }
        if (!string.IsNullOrWhiteSpace(r.Village))
        {
            var vName = isEnglish ? r.Village : LocalizeText(r.Village, lang);
            y = DrawKeyValue(gfx, GetLabel("Village", lang), vName, fontBody, fontBody, margin, y, printableWidth);
        }
        if (!string.IsNullOrWhiteSpace(r.Address))
        {
            var addr = isEnglish ? r.Address : LocalizeText(r.Address, lang);
            y = DrawKeyValue(gfx, GetLabel("Address", lang), addr, fontBody, fontBody, margin, y, printableWidth);
        }

        // 3. Single Transaction Specifics
        if (r.TransactionDate.HasValue)
        {
            var dtStr = FormatDate(r.TransactionDate.Value, lang);
            y = DrawKeyValue(gfx, GetLabel("Date", lang), dtStr, fontBody, fontBold, margin, y, printableWidth);
        }

        if (r.TransactionAmount.HasValue && r.TransactionAmount.Value > 0)
        {
            var amtStr = FormatCurrency(r.TransactionAmount.Value, lang);
            var isDeposit = r.TransactionType?.Equals("Deposit", StringComparison.OrdinalIgnoreCase) ?? false;
            var typeLabel = isDeposit ? GetLabel("ReceivedAmount", lang) : GetLabel("GivenAmount", lang);

            y = DrawKeyValue(gfx, typeLabel, amtStr, fontBody, fontAccent, margin, y, printableWidth);
        }

        if (!string.IsNullOrWhiteSpace(r.PaymentMode))
        {
            var modeStr = r.PaymentMode.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                ? (normLang == "gu" ? "રોકડ" : (normLang == "hi" ? "नकद" : "Cash"))
                : r.PaymentMode;
            y = DrawKeyValue(gfx, GetLabel("PaymentMode", lang), modeStr, fontBody, fontBody, margin, y, printableWidth);
        }

        // 4. Loan Summary Metrics
        if (r.InitialPrincipal.HasValue)
        {
            y = DrawKeyValue(gfx, GetLabel("InitialLoan", lang), FormatCurrency(r.InitialPrincipal.Value, lang), fontBody, fontBold, margin, y, printableWidth);
        }
        if (r.InterestRate.HasValue)
        {
            var rateStr = isEnglish
                ? $"{r.InterestRate.Value:F2}% / {GetLabel("PerMonth", lang)}"
                : $"{ScriptTranslator.ConvertDigitsToIndic(r.InterestRate.Value.ToString("F2"), normLang)}% / {GetLabel("PerMonth", lang)}";
            y = DrawKeyValue(gfx, GetLabel("InterestRate", lang), rateStr, fontBody, fontBody, margin, y, printableWidth);
        }
        if (!string.IsNullOrWhiteSpace(r.DisplayDuration))
        {
            y = DrawKeyValue(gfx, GetLabel("Duration", lang), r.DisplayDuration, fontBody, fontBody, margin, y, printableWidth);
        }
        if (r.MonthlyInterest.HasValue && r.MonthlyInterest.Value > 0)
        {
            y = DrawKeyValue(gfx, GetLabel("MonthlyInterest", lang), FormatCurrency(r.MonthlyInterest.Value, lang), fontBody, fontBody, margin, y, printableWidth);
        }
        if (!string.IsNullOrWhiteSpace(r.OrnamentType) && !r.OrnamentType.Equals("લાગુ નથી") && !r.OrnamentType.Equals("N/A") && !r.OrnamentType.Equals("लागू नहीं"))
        {
            var ornType = isEnglish ? r.OrnamentType : LocalizeText(r.OrnamentType, lang);
            y = DrawKeyValue(gfx, GetLabel("JewelleryType", lang), ornType, fontBody, fontBody, margin, y, printableWidth);
        }
        if (!string.IsNullOrWhiteSpace(r.OrnamentWeight) && !r.OrnamentWeight.Equals("લાગુ નથી") && !r.OrnamentWeight.Equals("N/A") && !r.OrnamentWeight.Equals("लागू नहीं"))
        {
            var ornWeight = isEnglish ? r.OrnamentWeight : ScriptTranslator.ConvertDigitsToIndic(r.OrnamentWeight, normLang);
            y = DrawKeyValue(gfx, GetLabel("Weight", lang), ornWeight, fontBody, fontBody, margin, y, printableWidth);
        }

        // 5. Total Balance Figures
        if (r.CurrentPrincipal.HasValue)
        {
            y = DrawKeyValue(gfx, GetLabel("CurrentPrincipal", lang), FormatCurrency(r.CurrentPrincipal.Value, lang), fontBody, fontBold, margin, y, printableWidth);
        }
        if (r.TotalInterest.HasValue)
        {
            y = DrawKeyValue(gfx, GetLabel("TotalInterest", lang), FormatCurrency(r.TotalInterest.Value, lang), fontBody, fontBody, margin, y, printableWidth);
        }
        if (r.TotalOutstanding.HasValue)
        {
            DrawDashedLine(gfx, margin, y + 2, margin + printableWidth, y + 2);
            y += 6;
            y = DrawKeyValue(gfx, GetLabel("TotalOutstanding", lang), FormatCurrency(r.TotalOutstanding.Value, lang), fontAccent, fontAccent, margin, y, printableWidth);
            DrawDashedLine(gfx, margin, y + 2, margin + printableWidth, y + 2);
            y += 6;
        }

        // Description
        if (!string.IsNullOrWhiteSpace(r.Description))
        {
            var descLabel = GetLabel("DescriptionColon", lang);
            gfx.DrawString(descLabel, fontSmall, XBrushes.DarkGray, new XPoint(margin, y + 8));
            y += 12;

            var rawDesc = isEnglish ? r.Description : LocalizeText(r.Description, lang);
            var wrappedLines = WrapText(rawDesc, printableWidth, 8);
            foreach (var line in wrappedLines)
            {
                gfx.DrawString(line, fontBody, XBrushes.Black, new XPoint(margin, y + 8));
                y += 11;
            }
        }

        // 6. Statement Table Items
        if (r.Items != null && r.Items.Count > 0)
        {
            DrawDashedLine(gfx, margin, y + 4, margin + printableWidth, y + 4);
            y += 8;

            // Compact Table Header
            gfx.DrawString(GetLabel("Date", lang), fontSmall, XBrushes.DarkSlateGray, new XPoint(margin, y + 8));
            gfx.DrawString(GetLabel("Type", lang), fontSmall, XBrushes.DarkSlateGray, new XPoint(margin + (printableWidth * 0.35), y + 8));
            gfx.DrawString(GetLabel("AmountBal", lang), fontSmall, XBrushes.DarkSlateGray, new XPoint(margin + (printableWidth * 0.70), y + 8));
            y += 14;

            foreach (var item in r.Items)
            {
                var dtStr = item.Date.ToString("dd/MM/yy");
                if (!isEnglish) dtStr = ScriptTranslator.ConvertDigitsToIndic(dtStr, normLang);

                var typeDisplay = isEnglish ? item.EventType : LocalizeText(item.EventType, lang);
                decimal amt = item.Credit ?? item.Debit ?? item.Interest ?? 0m;
                var amtDisplay = FormatCurrency(amt, lang);

                gfx.DrawString(dtStr, fontSmall, XBrushes.Black, new XPoint(margin, y + 8));
                gfx.DrawString(typeDisplay, fontSmall, XBrushes.Black, new XPoint(margin + (printableWidth * 0.35), y + 8));
                gfx.DrawString(amtDisplay, fontSmall, XBrushes.Black, new XPoint(margin + (printableWidth * 0.70), y + 8));
                y += 12;

                var balStr = $"{GetLabel("BalColon", lang)} {FormatCurrency(item.Balance, lang)}";
                gfx.DrawString(balStr, fontSmall, XBrushes.Gray, new XPoint(margin + (printableWidth * 0.35), y + 7));
                y += 10;
            }

            DrawDashedLine(gfx, margin, y + 2, margin + printableWidth, y + 2);
            y += 6;
        }

        // 7. QR Code Embed
        if (r.QrCodePngBytes != null && r.QrCodePngBytes.Length > 0)
        {
            y += 6;
            double qrSize = Math.Min(100.0, Math.Max(70.0, printableWidth * 0.55));
            double qrX = margin + ((printableWidth - qrSize) / 2.0);

            try
            {
                using var ms = new MemoryStream(r.QrCodePngBytes);
                var ximg = XImage.FromStream(() => new MemoryStream(r.QrCodePngBytes));
                gfx.DrawImage(ximg, qrX, y, qrSize, qrSize);
                y += qrSize + 6;

                var scanLabel = GetLabel("ScanAccountQr", lang);
                gfx.DrawString(scanLabel, fontSmall, XBrushes.DarkGray, new XRect(margin, y, printableWidth, 10), XStringFormats.Center);
                y += 12;
            }
            catch
            {
                // Fallback gracefully if image cannot be instantiated
            }
        }

        // 8. Footer Note & Timestamp
        y += 4;
        DrawDashedLine(gfx, margin, y, margin + printableWidth, y);
        y += 8;

        var footerMsg = !string.IsNullOrWhiteSpace(r.FooterNote)
            ? r.FooterNote
            : GetLabel("ThankYou", lang);
        gfx.DrawString(footerMsg, fontBold, XBrushes.Black, new XRect(margin, y, printableWidth, 12), XStringFormats.Center);
        y += 14;

        var createdStr = isEnglish
            ? $"{r.CreatedAt:dd-MM-yyyy hh:mm tt}"
            : $"{ScriptTranslator.ConvertDigitsToIndic(r.CreatedAt.ToString("dd-MM-yyyy hh:mm tt"), normLang)}";
        gfx.DrawString(createdStr, fontSmall, XBrushes.DarkGray, new XRect(margin, y, printableWidth, 10), XStringFormats.Center);
    }

    private static double DrawKeyValue(XGraphics gfx, string key, string value, XFont fKey, XFont fVal, double margin, double y, double width)
    {
        double halfWidth = width * 0.46;
        gfx.DrawString(key, fKey, XBrushes.DarkSlateGray, new XPoint(margin, y + 9));
        gfx.DrawString(value, fVal, XBrushes.Black, new XPoint(margin + halfWidth, y + 9));
        return y + 13.5;
    }

    private static void DrawDashedLine(XGraphics gfx, double x1, double y1, double x2, double y2)
    {
        var pen = new XPen(XColor.FromArgb(200, 100, 116, 139), 0.75)
        {
            DashStyle = XDashStyle.Dash
        };
        gfx.DrawLine(pen, x1, y1, x2, y2);
    }

    private static string GetLabel(string key, string lang)
    {
        var norm = ScriptTranslator.NormalizeLanguageCode(lang);
        if (norm == "en")
        {
            return key switch
            {
                "Borrower" => "Borrower",
                "AccountNo" => "Account No",
                "Contact" => "Contact",
                "Village" => "Village",
                "Address" => "Address",
                "Date" => "Date",
                "ReceivedAmount" => "Received Amount",
                "GivenAmount" => "Given Amount",
                "PaymentMode" => "Payment Mode",
                "InitialLoan" => "Initial Loan",
                "InterestRate" => "Interest Rate",
                "Duration" => "Duration",
                "MonthlyInterest" => "Monthly Interest",
                "JewelleryType" => "Jewellery Type",
                "Weight" => "Weight",
                "CurrentPrincipal" => "Current Principal",
                "TotalInterest" => "Total Interest",
                "TotalOutstanding" => "Total Outstanding",
                "DescriptionColon" => "Description:",
                "Type" => "Type",
                "AmountBal" => "Amount / Bal",
                "BalColon" => "Bal:",
                "ScanAccountQr" => "Scan Account QR",
                "ThankYou" => "Thank You For Your Business",
                "PerMonth" => "month",
                _ => key
            };
        }
        if (norm == "gu")
        {
            return key switch
            {
                "Borrower" => "ખાતેદાર",
                "AccountNo" => "ખાતા નંબર",
                "Contact" => "સંપર્ક",
                "Village" => "ગામ",
                "Address" => "સરનામું",
                "Date" => "તારીખ",
                "ReceivedAmount" => "જમા રકમ",
                "GivenAmount" => "ઉપાડ રકમ",
                "PaymentMode" => "ચુકવણી મોડ",
                "InitialLoan" => "શરૂઆત લોન રકમ",
                "InterestRate" => "વ્યાજ દર",
                "Duration" => "સમયગાળો",
                "MonthlyInterest" => "માસિક વ્યાજ",
                "JewelleryType" => "દાગીનાનો પ્રકાર",
                "Weight" => "વજન",
                "CurrentPrincipal" => "મૂળ બાકી",
                "TotalInterest" => "કુલ વ્યાજ",
                "TotalOutstanding" => "કુલ બાકી રકમ",
                "DescriptionColon" => "નોંધ:",
                "Type" => "પ્રકાર",
                "AmountBal" => "રકમ / બાકી",
                "BalColon" => "બાકી:",
                "ScanAccountQr" => "ખાતાનો QR સ્કેન કરો",
                "ThankYou" => "પધારજો / આભાર",
                "PerMonth" => "મહિનો",
                _ => ScriptTranslator.ToGujarati(key)
            };
        }
        if (norm == "hi")
        {
            return key switch
            {
                "Borrower" => "उधारकर्ता",
                "AccountNo" => "खाता संख्या",
                "Contact" => "संपर्क",
                "Village" => "गांव",
                "Address" => "पता",
                "Date" => "तारीख",
                "ReceivedAmount" => "जमा राशि",
                "GivenAmount" => "निकासी राशि",
                "PaymentMode" => "भुगतान मोड",
                "InitialLoan" => "प्रारंभिक ऋण",
                "InterestRate" => "ब्याज दर",
                "Duration" => "अवधि",
                "MonthlyInterest" => "मासिक ब्याज",
                "JewelleryType" => "आभूषण प्रकार",
                "Weight" => "वजन",
                "CurrentPrincipal" => "मूल बकाया",
                "TotalInterest" => "कुल ब्याज",
                "TotalOutstanding" => "कुल बकाया राशि",
                "DescriptionColon" => "विवरण:",
                "Type" => "प्रकार",
                "AmountBal" => "राशि / बकाया",
                "BalColon" => "बकाया:",
                "ScanAccountQr" => "खाता QR स्कैन करें",
                "ThankYou" => "धन्यवाद / फिर पधारें",
                "PerMonth" => "माह",
                _ => ScriptTranslator.ToHindi(key)
            };
        }
        return ScriptTranslator.Translate(key, lang);
    }

    private static string GetDefaultTitle(ReceiptType type, string lang)
    {
        var norm = ScriptTranslator.NormalizeLanguageCode(lang);
        if (norm == "gu")
        {
            return type switch
            {
                ReceiptType.ReceiveAmount => "જમા રસીદ",
                ReceiptType.GiveAmount => "ઉપાડ રસીદ",
                ReceiptType.Transaction => "વ્યવહાર રસીદ",
                ReceiptType.LoanSummary => "લોન સારાંશ",
                ReceiptType.InterestSummary => "વ્યાજ સારાંશ",
                ReceiptType.AccountStatement => "ખાતાવહી સ્ટેટમેન્ટ",
                ReceiptType.PaymentHistory => "ચુકવણી ઇતિહાસ",
                ReceiptType.BorrowerQrCode => "ખાતા QR કોડ",
                _ => "ખાતેદાર રસીદ"
            };
        }
        if (norm == "hi")
        {
            return type switch
            {
                ReceiptType.ReceiveAmount => "जमा रसीद",
                ReceiptType.GiveAmount => "निकासी रसीद",
                ReceiptType.Transaction => "लेन-देन रसीद",
                ReceiptType.LoanSummary => "ऋण सारांश",
                ReceiptType.InterestSummary => "ब्याज सारांश",
                ReceiptType.AccountStatement => "खाता विवरण",
                ReceiptType.PaymentHistory => "भुगतान इतिहास",
                ReceiptType.BorrowerQrCode => "खाता QR कोड",
                _ => "उधारकर्ता रसीद"
            };
        }
        return type switch
        {
            ReceiptType.ReceiveAmount => "Deposit Receipt",
            ReceiptType.GiveAmount => "Withdrawal Receipt",
            ReceiptType.Transaction => "Transaction Receipt",
            ReceiptType.LoanSummary => "Loan Summary",
            ReceiptType.InterestSummary => "Interest Summary",
            ReceiptType.AccountStatement => "Account Statement",
            ReceiptType.PaymentHistory => "Payment History",
            ReceiptType.BorrowerQrCode => "Account QR Code",
            _ => "Borrower Receipt"
        };
    }

    private static string FormatCurrency(decimal amount, string lang)
    {
        string formatted = $"₹ {amount:N2}";
        var norm = ScriptTranslator.NormalizeLanguageCode(lang);
        if (norm == "en") return formatted;
        return ScriptTranslator.ConvertDigitsToIndic(formatted, norm);
    }

    private static string FormatDate(DateTime date, string lang)
    {
        string formatted = date.ToString("dd-MM-yyyy");
        var norm = ScriptTranslator.NormalizeLanguageCode(lang);
        if (norm == "en") return formatted;
        return ScriptTranslator.ConvertDigitsToIndic(formatted, norm);
    }

    private static string LocalizeText(string text, string lang)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;
        var norm = ScriptTranslator.NormalizeLanguageCode(lang);
        if (norm == "en") return text;
        return ScriptTranslator.Translate(text, lang);
    }

    private static List<string> WrapText(string text, double maxWidth, double fontSize)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return lines;

        // Approximate char width ~ 0.55 * fontSize
        int maxCharsPerLine = Math.Max(10, (int)(maxWidth / (fontSize * 0.55)));
        var words = text.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        string currentLine = string.Empty;
        foreach (var word in words)
        {
            if (string.IsNullOrEmpty(currentLine))
            {
                currentLine = word;
            }
            else if (currentLine.Length + 1 + word.Length <= maxCharsPerLine)
            {
                currentLine += " " + word;
            }
            else
            {
                lines.Add(currentLine);
                currentLine = word;
            }
        }

        if (!string.IsNullOrEmpty(currentLine))
        {
            lines.Add(currentLine);
        }

        return lines;
    }
}
