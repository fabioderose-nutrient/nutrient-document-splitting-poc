using System.Text.RegularExpressions;
using DocumentSplitter;
using GdPicture14;

// Splits one PDF that concatenates several documents (for example a printed tax return
// package) into one PDF per document.
//
// Usage: DocumentSplitter [input.pdf] [output-folder]
//   input.pdf      defaults to samples/tax-return-package.pdf
//   output-folder  defaults to output/ in the working directory

// An empty key starts GdPicture in evaluation mode.
new LicenseManager().RegisterKEY(Environment.GetEnvironmentVariable("GDPICTURE_LICENSE_KEY") ?? "");

string inputPath = args.Length > 0 ? args[0] : FindDefaultSample();
string outputDir = Path.GetFullPath(args.Length > 1 ? args[1] : "output");

if (!File.Exists(inputPath))
{
    Console.WriteLine($"ERROR: input PDF not found: {inputPath}");
    return 1;
}

Console.WriteLine($"Input PDF: {Path.GetFileName(inputPath)}");

if (Directory.Exists(outputDir))
{
    foreach (var old in Directory.GetFiles(outputDir, "*.pdf")) File.Delete(old);
}
Directory.CreateDirectory(outputDir);

using GdPicturePDF pdf = new GdPicturePDF();
var status = pdf.LoadFromFile(inputPath, true);
if (status != GdPictureStatus.OK)
{
    Console.WriteLine($"ERROR: failed to load PDF: {status}");
    return 1;
}

int pageCount = pdf.GetPageCount();
Console.WriteLine($"Total pages: {pageCount}");

if (pageCount <= 1)
{
    Console.WriteLine("Only 1 page, nothing to split.");
    return 0;
}

Console.WriteLine("\n=== Phase 1: Page analysis ===");
var pages = Splitter.AnalyzeAllPages(pdf, pageCount);

Console.WriteLine("\n=== Phase 2: OCR (image-only pages) ===");
Splitter.OcrImagePages(pdf, pages);

Console.WriteLine("\n=== Phase 3: Split point detection ===");
var splitPoints = Splitter.DetermineSplitPoints(pages);

Console.WriteLine("\n=== Phase 4: Splitting and saving ===");
Splitter.SplitAndSave(pdf, pages, splitPoints, outputDir);

Console.WriteLine($"\nDone. {splitPoints.Count} document(s) saved to: {outputDir}");
return 0;

static string FindDefaultSample()
{
    // Works from the repository root and from the project folder.
    foreach (var dir in new[] { Directory.GetCurrentDirectory(), Path.Combine(Directory.GetCurrentDirectory(), "..") })
    {
        var candidate = Path.Combine(dir, "samples", "tax-return-package.pdf");
        if (File.Exists(candidate)) return Path.GetFullPath(candidate);
    }
    return Path.Combine("samples", "tax-return-package.pdf");
}

static class Splitter
{
    // =========================================================================
    //  Phase 1: page analysis
    // =========================================================================
    public static List<PageInfo> AnalyzeAllPages(GdPicturePDF pdf, int pageCount)
    {
        var pages = new List<PageInfo>();
        pdf.SetMeasurementUnit(PdfMeasurementUnit.PdfMeasurementUnitPoint);

        for (int i = 1; i <= pageCount; i++)
        {
            pdf.SelectPage(i);
            var info = new PageInfo
            {
                PageNumber = i,
                WidthPt = pdf.GetPageWidth(),
                HeightPt = pdf.GetPageHeight(),
                ImageCount = pdf.GetPageImageCount()
            };

            string text = pdf.GetPageText() ?? "";
            info.ExtractedText = text.Trim();

            bool hasSubstantialText = info.ExtractedText.Length > 50;
            info.IsImageBased = info.ImageCount > 0 && !hasSubstantialText;
            info.IsBlank = info.ExtractedText.Length < 5 && info.ImageCount == 0;

            if (info.ImageCount > 0)
                ExtractPrimaryImageMetadata(pdf, info);

            string cleanText = FormTitleDetector.StripTrackingFooter(info.ExtractedText);
            info.FormTitle = FormTitleDetector.DetectFormTitle(cleanText);
            FormTitleDetector.DetectFormPageNumber(cleanText, info);

            Console.WriteLine(
                $"  Page {info.PageNumber,3}: {info.SizeKey,16} | imgs={info.ImageCount} | " +
                $"form=\"{Truncate(info.FormTitle, 25)}\" | " +
                $"formPg={info.FormPageNum?.ToString() ?? "-"}/{info.FormTotalPages?.ToString() ?? "-"}" +
                (info.IsBlank ? " | blank" : "") +
                (info.IsImageBased ? $" | imgFp={info.ImageFingerprint}" : ""));

            pages.Add(info);
        }

        return pages;
    }

    private static void ExtractPrimaryImageMetadata(GdPicturePDF pdf, PageInfo info)
    {
        int bestIdx = 0;
        long bestArea = 0;

        for (int imgIdx = 0; imgIdx < info.ImageCount; imgIdx++)
        {
            int imgW = 0, imgH = 0;
            pdf.GetPageImageSize(imgIdx, ref imgW, ref imgH);
            long area = (long)imgW * imgH;
            if (area > bestArea) { bestArea = area; bestIdx = imgIdx; }
        }

        int w = 0, h = 0;
        pdf.GetPageImageSize(bestIdx, ref w, ref h);
        info.PrimaryImagePixelWidth = w;
        info.PrimaryImagePixelHeight = h;

        float dpiH = 0, dpiV = 0;
        pdf.GetPageImageResolution(bestIdx, ref dpiH, ref dpiV);
        info.PrimaryImageDpiH = dpiH;
        info.PrimaryImageDpiV = dpiV;

        info.PrimaryImageBitDepth = pdf.GetPageImageBitDepth(bestIdx);
        info.PrimaryImageColorSpace = pdf.GetPageImageColorSpace(bestIdx).ToString();

        try
        {
            var filters = pdf.GetPageImageFilters(bestIdx);
            info.PrimaryImageFilters = filters != null ? string.Join(",", filters) : "";
        }
        catch { info.PrimaryImageFilters = "unknown"; }
    }

    // =========================================================================
    //  Phase 2: OCR of image-only pages
    // =========================================================================
    public static void OcrImagePages(GdPicturePDF pdf, List<PageInfo> pages)
    {
        var needOcr = pages.Where(p => p.IsImageBased && string.IsNullOrEmpty(p.FormTitle) && !p.IsBlank).ToList();
        if (needOcr.Count == 0)
        {
            Console.WriteLine("  No image-only pages need OCR.");
            return;
        }

        string? resources = OcrResources.Resolve();
        if (resources == null)
        {
            Console.WriteLine("  OCR resources (eng.traineddata) not found, skipping OCR. Set OCR_RESOURCES_PATH.");
            return;
        }

        Console.WriteLine($"  OCR on {needOcr.Count} image-only page(s)...");

        using var ocr = new GdPictureOCR();
        using var imaging = new GdPictureImaging();
        ocr.ResourcesFolder = resources;
        ocr.AddLanguage(OCRLanguage.English);
        ocr.OCRMode = OCRMode.FavorSpeed;

        foreach (var page in needOcr)
        {
            pdf.SelectPage(page.PageNumber);
            int imageId = pdf.RenderPageToGdPictureImage(150, false);
            if (pdf.GetStat() != GdPictureStatus.OK) continue;

            try
            {
                ocr.SetImage(imageId);
                string? resultId = ocr.RunOCR();
                if (string.IsNullOrEmpty(resultId))
                {
                    Console.WriteLine($"    Page {page.PageNumber}: OCR failed ({ocr.GetStat()}).");
                    continue;
                }

                string ocrText = ocr.GetOCRResultText(resultId) ?? "";
                if (!string.IsNullOrWhiteSpace(ocrText))
                {
                    page.ExtractedText = ocrText.Trim();
                    page.TextFromOcr = true;
                    string clean = FormTitleDetector.StripTrackingFooter(page.ExtractedText);
                    page.FormTitle = FormTitleDetector.DetectFormTitle(clean);
                    FormTitleDetector.DetectFormPageNumber(clean, page);
                    Console.WriteLine($"    Page {page.PageNumber}: OCR -> form=\"{page.FormTitle}\", formPg={page.FormPageNum?.ToString() ?? "-"}/{page.FormTotalPages?.ToString() ?? "-"}");
                }
                ocr.ReleaseOCRResult(resultId);
            }
            finally
            {
                imaging.ReleaseGdPictureImage(imageId);
            }
        }
    }

    // =========================================================================
    //  Phase 3: split points
    // =========================================================================
    // Each signal has a weight. A new document starts before a page when the weights
    // of the signals found between it and the previous page add up to 6 or more.
    private const int SplitThreshold = 6;

    public static List<int> DetermineSplitPoints(List<PageInfo> pages)
    {
        var splitPoints = new List<int> { 0 };

        for (int i = 1; i < pages.Count; i++)
        {
            var prev = pages[i - 1];
            var curr = pages[i];
            var reasons = new List<(string reason, int weight)>();

            if (curr.IsBlank) continue;

            // Primary signal: the form title changed.
            bool formTitleChanged = false;
            if (!string.IsNullOrEmpty(prev.FormTitle) && !string.IsNullOrEmpty(curr.FormTitle)
                && !string.Equals(prev.FormTitle, curr.FormTitle, StringComparison.OrdinalIgnoreCase))
            {
                formTitleChanged = true;
                reasons.Add(($"FORM_TITLE_CHANGE (\"{Truncate(prev.FormTitle, 20)}\" -> \"{Truncate(curr.FormTitle, 20)}\")", 10));
            }

            // "Page 1 of Y" on the current page: a new form starts.
            if (curr.FormPageNum == 1 && curr.FormTotalPages > 1)
            {
                if (prev.FormPageNum == null || prev.FormPageNum != curr.FormPageNum - 1)
                    reasons.Add(("FORM_PAGE_1_OF_Y", 10));
            }

            // The previous page was the last page of its form ("Page Y of Y").
            if (prev.FormPageNum.HasValue && prev.FormTotalPages.HasValue
                && prev.FormPageNum == prev.FormTotalPages)
            {
                reasons.Add(($"PREV_FORM_LAST_PAGE ({prev.FormPageNum}/{prev.FormTotalPages})", 10));
            }

            // Page numbers of the same form are not consecutive.
            if (!formTitleChanged
                && prev.FormPageNum.HasValue && curr.FormPageNum.HasValue
                && curr.FormPageNum != prev.FormPageNum + 1)
            {
                reasons.Add(($"FORM_PAGE_BREAK ({prev.FormPageNum} -> {curr.FormPageNum})", 8));
            }

            // Page size or orientation changed.
            if (Math.Abs(prev.WidthPt - curr.WidthPt) > 2 || Math.Abs(prev.HeightPt - curr.HeightPt) > 2)
            {
                reasons.Add(($"PAGE_SIZE_CHANGE ({prev.SizeKey} -> {curr.SizeKey})", 8));
            }

            // Consecutive scanned pages with a different scan fingerprint.
            if (prev.IsImageBased && curr.IsImageBased
                && !string.IsNullOrEmpty(prev.ImageFingerprint)
                && !string.IsNullOrEmpty(curr.ImageFingerprint)
                && prev.ImageFingerprint != curr.ImageFingerprint)
            {
                reasons.Add(("IMAGE_SOURCE_CHANGE", 6));
            }

            // A blank page separates two documents.
            if (prev.IsBlank && i >= 2 && !pages[i - 2].IsBlank)
            {
                reasons.Add(("BLANK_PAGE_SEPARATOR", 5));
            }

            // Text page followed by a scanned page, or the opposite.
            if (prev.IsImageBased != curr.IsImageBased && !prev.IsBlank)
            {
                reasons.Add(("CONTENT_TYPE_CHANGE", 2));
            }

            // A form title appears after pages without one.
            if (string.IsNullOrEmpty(prev.FormTitle) && !string.IsNullOrEmpty(curr.FormTitle))
            {
                reasons.Add(($"NEW_FORM_DETECTED (\"{Truncate(curr.FormTitle, 20)}\")", 6));
            }

            // The previous page had a form title, the current one has none.
            if (!string.IsNullOrEmpty(prev.FormTitle) && string.IsNullOrEmpty(curr.FormTitle))
            {
                reasons.Add(("FORM_TITLE_LOST", 4));
            }

            int totalScore = reasons.Sum(r => r.weight);

            if (totalScore >= SplitThreshold)
            {
                string reasonStr = string.Join(", ", reasons.Select(r => $"{r.reason}[{r.weight}]"));
                Console.WriteLine($"  SPLIT before page {curr.PageNumber} (score={totalScore}): {reasonStr}");
                splitPoints.Add(i);
            }
        }

        splitPoints = MergeFalsePositives(pages, splitPoints);

        Console.WriteLine($"\n  Total documents detected: {splitPoints.Count}");
        return splitPoints;
    }

    // A split before a page that says "Page X of Y" with X > 1 is undone when the
    // previous document has the same form title: that page is a continuation.
    private static List<int> MergeFalsePositives(List<PageInfo> pages, List<int> splitPoints)
    {
        var result = new List<int> { splitPoints[0] };

        for (int i = 1; i < splitPoints.Count; i++)
        {
            int pageIdx = splitPoints[i];
            var page = pages[pageIdx];

            if (page.FormPageNum > 1 && page.FormTotalPages.HasValue)
            {
                var prevPage = pages[result.Last()];
                if (!string.IsNullOrEmpty(prevPage.FormTitle)
                    && string.Equals(prevPage.FormTitle, page.FormTitle, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  MERGE: page {page.PageNumber} is a continuation ({page.FormTitle} page {page.FormPageNum}/{page.FormTotalPages})");
                    continue;
                }
            }

            result.Add(pageIdx);
        }

        return result;
    }

    // =========================================================================
    //  Phase 4: copy each page range into its own PDF
    // =========================================================================
    public static void SplitAndSave(GdPicturePDF sourcePdf, List<PageInfo> pages, List<int> splitPoints, string outputDir)
    {
        int totalPages = pages.Count;

        for (int docIdx = 0; docIdx < splitPoints.Count; docIdx++)
        {
            int startIdx = splitPoints[docIdx];
            int endIdx = docIdx + 1 < splitPoints.Count ? splitPoints[docIdx + 1] - 1 : totalPages - 1;
            int start = startIdx + 1, end = endIdx + 1;
            string pageRange = start == end ? $"{start}" : $"{start}-{end}";

            // Name the file after the title of the first page that has one.
            string label = pages.Skip(startIdx).Take(end - start + 1)
                .Select(p => p.FormTitle).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "";
            string fileName = $"document{docIdx + 1:00}" + (label.Length > 0 ? "-" + Slug(label) : "") + ".pdf";
            string outputPath = Path.Combine(outputDir, fileName);

            using var destPdf = new GdPicturePDF();
            destPdf.NewPDF();
            var status = destPdf.ClonePages(sourcePdf, pageRange);
            if (status != GdPictureStatus.OK)
            {
                Console.WriteLine($"  ERROR cloning pages {pageRange}: {status}");
                continue;
            }

            status = destPdf.SaveToFile(outputPath);
            if (status != GdPictureStatus.OK)
            {
                Console.WriteLine($"  ERROR saving {outputPath}: {status}");
                continue;
            }

            int count = end - start + 1;
            Console.WriteLine($"  {fileName} - pages {pageRange} ({count} page{(count > 1 ? "s" : "")})");
        }
    }

    private static string Slug(string s) =>
        Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}
