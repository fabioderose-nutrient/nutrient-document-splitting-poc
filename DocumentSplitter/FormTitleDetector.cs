using System.Text.RegularExpressions;

namespace DocumentSplitter;

/// <summary>
/// Text rules that recognise the documents of a printed tax return package: form and
/// schedule titles, "Page X of Y" headers and the print-tracking footer that tax
/// preparation software adds at the bottom of every page.
/// </summary>
public static class FormTitleDetector
{
    // Print-tracking footer line: an 8-digit and a 6-digit number, a client code and a
    // version number, for example "10000001 200001 SAMPLE01 2025.01000".
    private static readonly Regex TrackingFooterPattern = new(
        @"\d{8}\s+\d{6}\s+\w+\s+\d{4}\.\d{5}", RegexOptions.Compiled);

    // Form template identifier printed alone on the last line: 6 digits and a date.
    private static readonly Regex TemplateIdPattern = new(
        @"^\d{6}\s+\d{2}-\d{2}-\d{2}$", RegexOptions.Compiled);

    // "Page X of Y" in form headers.
    private static readonly Regex PageOfPattern = new(
        @"[Pp]age\s+(\d+)\s+(?:of|/)\s+(\d+)", RegexOptions.Compiled);

    // Form and schedule titles. When several match, the one that appears first wins.
    private static readonly Regex StateSchedulePattern = new(@"(?i)\b(Schedule\s+[A-Z]{2}-\w+)", RegexOptions.Compiled);
    private static readonly Regex FederalSchedulePattern = new(@"(?i)\b(SCHEDULE\s+[A-Z0-9]\d?)\b", RegexOptions.Compiled);
    private static readonly Regex StateFormPattern = new(@"(?i)\b(Form\s+[A-Z]{2}-\w+(?:/\w+)?)", RegexOptions.Compiled);
    private static readonly Regex FederalFormPattern = new(@"(?i)\b(Form\s+\d{3,5}[-\w]*)", RegexOptions.Compiled);

    /// <summary>Removes the print-tracking footer lines from the bottom of the page text.</summary>
    public static string StripTrackingFooter(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var lines = text.Split('\n').ToList();

        for (int pass = 0; pass < 2; pass++) // up to 2 footer blocks
        {
            if (lines.Count < 2) break;

            int lastIdx = LastNonEmpty(lines);
            if (lastIdx < 0) break;

            if (TrackingFooterPattern.IsMatch(lines[lastIdx]))
            {
                lines.RemoveAt(lastIdx);
                // The page sequence number is printed on the line above.
                lastIdx--;
                if (lastIdx >= 0 && Regex.IsMatch(lines[lastIdx].Trim(), @"^\d{1,4}$"))
                    lines.RemoveAt(lastIdx);
            }

            lastIdx = LastNonEmpty(lines);
            if (lastIdx >= 0 && TemplateIdPattern.IsMatch(lines[lastIdx].Trim()))
                lines.RemoveAt(lastIdx);
        }

        return string.Join('\n', lines).Trim();
    }

    /// <summary>Returns the form or document title found in the page text, or an empty string.</summary>
    public static string DetectFormTitle(string cleanText)
    {
        if (string.IsNullOrWhiteSpace(cleanText)) return "";

        string header = cleanText.Length > 800 ? cleanText[..800] : cleanText;
        string firstFew = string.Join(" ", cleanText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Take(5).Select(l => l.Trim()));

        // Priority 1: document types recognised from the first lines.
        if (Regex.IsMatch(firstFew, @"(?i)\bclient.?s\s+copy\b"))
            return "Cover Page";
        if (Regex.IsMatch(firstFew, @"(?i)(e-?file|IRS).*\b(jurat|disclosure|signature\s+authorization)\b"))
            return "e-file Signature Authorization";
        if (Regex.IsMatch(firstFew, @"(?i)\bdirect\s+deposit.*\b(report|debit)\b"))
            return "Direct Deposit/Debit Report";
        if (Regex.IsMatch(firstFew, @"(?i)\bchecklist\b"))
            return "Checklist";

        // Priority 2: form and schedule numbers. The earliest match in the text wins.
        var candidates = new List<(int pos, string title)>();
        foreach (var pattern in new[] { StateSchedulePattern, FederalSchedulePattern, StateFormPattern, FederalFormPattern })
        {
            var m = pattern.Match(header);
            if (m.Success) candidates.Add((m.Index, Normalize(m.Groups[1].Value)));
        }

        if (candidates.Count > 0)
        {
            var best = candidates.OrderBy(c => c.pos).First();
            return SpecializeTitle(best.title, header);
        }

        // Priority 3: other document types.
        if (Regex.IsMatch(header, @"(?i)\bestimated\s+tax\s+(worksheet|payment)\b"))
            return "Estimated Tax Worksheet";
        if (Regex.IsMatch(header, @"(?i)\breceipt\b"))
            return "Receipt";
        if (Regex.IsMatch(header, @"(?i)\bstatement\b"))
            return "Statement";

        return "";
    }

    /// <summary>Reads "Page X of Y" from the page header.</summary>
    public static void DetectFormPageNumber(string cleanText, PageInfo info)
    {
        if (string.IsNullOrWhiteSpace(cleanText)) return;

        string header = cleanText.Length > 800 ? cleanText[..800] : cleanText;
        var m = PageOfPattern.Match(header);
        if (m.Success)
        {
            info.FormPageNum = int.Parse(m.Groups[1].Value);
            info.FormTotalPages = int.Parse(m.Groups[2].Value);
        }
    }

    // Distinguishes the payment voucher from the worksheet of an estimated tax form.
    private static string SpecializeTitle(string title, string header)
    {
        string upper = title.ToUpperInvariant();
        if (upper.Contains("1040-ES") || upper.Contains("1040ES"))
        {
            if (Regex.IsMatch(header, @"(?i)\b(voucher|coupon)\b")) return "Form 1040-ES Voucher";
            if (Regex.IsMatch(header, @"(?i)\bworksheet\b")) return "Estimated Tax Worksheet";
            return "Form 1040-ES";
        }
        return title;
    }

    private static int LastNonEmpty(List<string> lines)
    {
        int i = lines.Count - 1;
        while (i >= 0 && string.IsNullOrWhiteSpace(lines[i])) i--;
        return i;
    }

    private static string Normalize(string raw) => Regex.Replace(raw.Trim(), @"\s+", " ");
}
