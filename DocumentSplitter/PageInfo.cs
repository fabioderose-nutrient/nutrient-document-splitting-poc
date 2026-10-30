namespace DocumentSplitter;

/// <summary>
/// Everything the splitter knows about one page of the input PDF.
/// </summary>
public sealed class PageInfo
{
    public int PageNumber { get; set; }
    public float WidthPt { get; set; }
    public float HeightPt { get; set; }
    public bool IsImageBased { get; set; }
    public int ImageCount { get; set; }

    // Metadata of the largest image on the page (the scan, for image-only pages).
    public float PrimaryImageDpiH { get; set; }
    public float PrimaryImageDpiV { get; set; }
    public int PrimaryImageBitDepth { get; set; }
    public string PrimaryImageColorSpace { get; set; } = "";
    public string PrimaryImageFilters { get; set; } = "";
    public int PrimaryImagePixelWidth { get; set; }
    public int PrimaryImagePixelHeight { get; set; }

    // Text content
    public string ExtractedText { get; set; } = "";
    public bool TextFromOcr { get; set; }
    public string FormTitle { get; set; } = "";   // Detected form or document title
    public int? FormPageNum { get; set; }          // "Page X of Y" -> X
    public int? FormTotalPages { get; set; }       // "Page X of Y" -> Y
    public bool IsBlank { get; set; }

    public string SizeKey => $"{Math.Round(WidthPt, 1)}x{Math.Round(HeightPt, 1)}";

    /// <summary>
    /// Resolution, bit depth, colour space and compression of the scan. Two consecutive
    /// scanned pages with different fingerprints most likely come from different scanners
    /// or scan sessions.
    /// </summary>
    public string ImageFingerprint => IsImageBased
        ? $"{Math.Round(PrimaryImageDpiH)}x{Math.Round(PrimaryImageDpiV)}_{PrimaryImageBitDepth}_{PrimaryImageColorSpace}_{PrimaryImageFilters}"
        : "";
}
