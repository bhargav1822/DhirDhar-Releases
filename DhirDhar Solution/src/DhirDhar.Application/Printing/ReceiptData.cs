using System;
using System.Collections.Generic;

namespace DhirDhar.Application.Printing;

public enum ReceiptType
{
    BorrowerReceipt,
    ReceiveAmount,
    GiveAmount,
    Transaction,
    LoanSummary,
    InterestSummary,
    AccountStatement,
    PaymentHistory,
    BorrowerQrCode
}

public sealed record ReceiptItemRow(
    DateTime Date,
    string EventType,
    decimal? Debit,
    decimal? Credit,
    decimal? Interest,
    decimal Balance,
    string? Description);

public sealed class ReceiptData
{
    public ReceiptType Type { get; set; } = ReceiptType.BorrowerReceipt;
    public string BusinessName { get; set; } = "DhirDhar";
    public string? BusinessPrefix { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }

    // Borrower Profile
    public string? BorrowerName { get; set; }
    public string? BorrowerNumber { get; set; }
    public string? Contact { get; set; }
    public string? Address { get; set; }
    public string? Village { get; set; }
    public string? AccountStatus { get; set; } = "Active";

    // Loan / Account Metrics
    public DateTime? LoanDate { get; set; }
    public decimal? InitialPrincipal { get; set; }
    public decimal? InterestRate { get; set; }
    public string? LoanDuration { get; set; }
    public string? DisplayDuration { get; set; }
    public decimal? MonthlyInterest { get; set; }
    public string? OrnamentType { get; set; }
    public string? OrnamentWeight { get; set; }
    public decimal? CurrentPrincipal { get; set; }
    public decimal? TotalInterest { get; set; }
    public decimal? TotalOutstanding { get; set; }
    public decimal? TotalDeposits { get; set; }
    public decimal? TotalWithdrawals { get; set; }

    // Single Transaction Data
    public DateTime? TransactionDate { get; set; }
    public string? TransactionType { get; set; }
    public decimal? TransactionAmount { get; set; }
    public string? PaymentMode { get; set; } = "Cash";
    public string? Description { get; set; }

    // Interest Calculation Period
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    // Table / History Rows
    public List<ReceiptItemRow> Items { get; set; } = new();

    // QR Code
    public string? QrCodePayload { get; set; }
    public byte[]? QrCodePngBytes { get; set; }

    // Print Context & Localization
    public string PaperSize { get; set; } = "POS80";
    public double CustomPaperWidthMm { get; set; } = 80.0;
    public string LanguageCode { get; set; } = "gu-IN";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? FooterNote { get; set; }
    public bool AutoCut { get; set; } = true;
}
