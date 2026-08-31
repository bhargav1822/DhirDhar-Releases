using System;

namespace DhirDhar.Application.Printing;

/// <summary>
/// Represents a paper size supported by a specific Windows printer, with physical dimensions.
/// </summary>
public sealed record PrinterPaperSizeInfo(
    string Name,
    string DisplayLabel,
    int RawKind,
    double WidthMm,
    double HeightMm,
    bool IsContinuousRoll);
