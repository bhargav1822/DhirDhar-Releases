using System;

namespace DhirDhar.Application.Printing;

public enum PaperSizeKind
{
    A4,
    A5,
    Letter,
    Pos58,
    Pos80,
    Pos110,
    PosCustom
}

public static class PaperSizeHelper
{
    public const double PointsPerMillimeter = 72.0 / 25.4; // ~2.83464567 pt/mm

    public static double MillimetersToPoints(double mm) => mm * PointsPerMillimeter;
    public static double PointsToMillimeters(double points) => points / PointsPerMillimeter;

    public static (double WidthPt, double HeightPt, bool IsContinuousRoll) GetDimensions(
        string paperSizeCode,
        double customWidthMm = 80.0,
        double defaultContinuousHeightPt = 500.0)
    {
        var kind = ParsePaperSizeKind(paperSizeCode);
        return kind switch
        {
            PaperSizeKind.A4 => (MillimetersToPoints(210.0), MillimetersToPoints(297.0), false),
            PaperSizeKind.A5 => (MillimetersToPoints(148.0), MillimetersToPoints(210.0), false),
            PaperSizeKind.Letter => (MillimetersToPoints(215.9), MillimetersToPoints(279.4), false),
            PaperSizeKind.Pos58 => (MillimetersToPoints(58.0), defaultContinuousHeightPt, true),
            PaperSizeKind.Pos80 => (MillimetersToPoints(80.0), defaultContinuousHeightPt, true),
            PaperSizeKind.Pos110 => (MillimetersToPoints(110.0), defaultContinuousHeightPt, true),
            PaperSizeKind.PosCustom => (MillimetersToPoints(Math.Clamp(customWidthMm, 30.0, 300.0)), defaultContinuousHeightPt, true),
            _ => (MillimetersToPoints(210.0), MillimetersToPoints(297.0), false)
        };
    }

    public static PaperSizeKind ParsePaperSizeKind(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return PaperSizeKind.A4;
        var clean = code.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");

        if (clean.Contains("58")) return PaperSizeKind.Pos58;
        if (clean.Contains("80")) return PaperSizeKind.Pos80;
        if (clean.Contains("110")) return PaperSizeKind.Pos110;
        if (clean.Contains("CUSTOM")) return PaperSizeKind.PosCustom;
        if (clean == "A5") return PaperSizeKind.A5;
        if (clean == "LETTER") return PaperSizeKind.Letter;
        return PaperSizeKind.A4;
    }

    public static bool IsThermalPosSize(string? paperSizeCode)
    {
        var kind = ParsePaperSizeKind(paperSizeCode);
        return kind is PaperSizeKind.Pos58 or PaperSizeKind.Pos80 or PaperSizeKind.Pos110 or PaperSizeKind.PosCustom;
    }
}
