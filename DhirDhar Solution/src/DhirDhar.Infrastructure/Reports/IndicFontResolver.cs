using System;
using System.Collections.Concurrent;
using System.IO;
using PdfSharpCore.Fonts;

namespace DhirDhar.Infrastructure.Reports;

/// <summary>
/// Custom font resolver for PdfSharpCore that embeds Unicode-capable Indic TrueType fonts
/// (Gujarati, Devanagari/Hindi/Marathi, Bengali, Punjabi, Tamil, Telugu, Kannada, Malayalam, Odia, Assamese)
/// into generated PDF documents.
/// </summary>
public sealed class IndicFontResolver : IFontResolver
{
    private static readonly ConcurrentDictionary<string, byte[]> FontCache = new(StringComparer.OrdinalIgnoreCase);

    public string DefaultFontName => "IndicFont";

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        string faceName = (isBold, isItalic) switch
        {
            (true, true) => "IndicFont-BoldItalic",
            (true, false) => "IndicFont-Bold",
            (false, true) => "IndicFont-Italic",
            _ => "IndicFont-Regular"
        };

        return new FontResolverInfo(faceName);
    }

    public byte[]? GetFont(string faceName)
    {
        if (FontCache.TryGetValue(faceName, out var cachedData))
        {
            return cachedData;
        }

        var bytes = LoadFontBytes(faceName);
        if (bytes != null && bytes.Length > 0)
        {
            FontCache[faceName] = bytes;
            return bytes;
        }

        return null;
    }

    private static byte[]? LoadFontBytes(string faceName)
    {
        var baseDir = AppContext.BaseDirectory;
        bool isBold = faceName.Contains("Bold", StringComparison.OrdinalIgnoreCase);

        if (isBold)
        {
            string[] boldCandidates = new[]
            {
                Path.Combine(baseDir, "Assets", "Fonts", "NotoSansGujarati.ttf"),
                Path.Combine(baseDir, "Assets", "Fonts", "NotoSansDevanagari.ttf"),
                Path.Combine(baseDir, "Assets", "Fonts", "GoogleSans.ttf"),
                @"C:\Windows\Fonts\shrutib.ttf",
                @"C:\Windows\Fonts\arialbd.ttf",
                @"C:\Windows\Fonts\segoeuib.ttf",
                @"C:\Windows\Fonts\Nirmala.ttc"
            };

            foreach (var path in boldCandidates)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var data = File.ReadAllBytes(path);
                        if (data.Length > 0) return data;
                    }
                    catch
                    {
                        // Try next candidate
                    }
                }
            }
        }

        string[] candidatePaths = new[]
        {
            Path.Combine(baseDir, "Assets", "Fonts", "NotoSansGujarati.ttf"),
            Path.Combine(baseDir, "Assets", "Fonts", "NotoSansDevanagari.ttf"),
            Path.Combine(baseDir, "Assets", "Fonts", "GoogleSans.ttf"),
            Path.Combine(baseDir, "Assets", "NotoSansGujarati.ttf"),
            Path.Combine(baseDir, "NotoSansGujarati-Variable.ttf"),
            @"C:\Windows\Fonts\shruti.ttf",
            @"C:\Windows\Fonts\arialuni.ttf",
            @"C:\Windows\Fonts\Nirmala.ttc",
            @"C:\Windows\Fonts\arial.ttf",
            @"C:\Windows\Fonts\segoeui.ttf"
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    var data = File.ReadAllBytes(path);
                    if (data.Length > 0) return data;
                }
                catch
                {
                    // Try next candidate
                }
            }
        }

        return null;
    }
}
