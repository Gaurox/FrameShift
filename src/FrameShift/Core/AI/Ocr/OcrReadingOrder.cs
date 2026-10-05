namespace FrameShift.Core.AI.Ocr;

internal static class OcrReadingOrder
{
    // Split at whitespace gutters, then read each column from top to bottom.
    public static IReadOnlyList<OcrLine> Sort(IReadOnlyList<OcrLine> lines) => Sort(lines, 0);

    private static IReadOnlyList<OcrLine> Sort(IReadOnlyList<OcrLine> lines, int depth)
    {
        if (lines.Count < 2)
            return lines.ToArray();
        var typicalHeight = lines.Select(l => l.Bottom - l.Top).Order().ElementAt(lines.Count / 2);
        // Very tall screenshots/PDF pages can have thousands of isolated lines.
        // Bound recursive whitespace splitting and use stable row ordering beyond it.
        if (depth >= 32)
            return SortRows(lines, typicalHeight);
        var intervals = lines.OrderBy(l => l.Left).ToArray();
        var right = intervals[0].Right;
        double split = 0;
        double gap = 0;
        for (var i = 1; i < intervals.Length; i++)
        {
            if (intervals[i].Left - right > gap)
            {
                gap = intervals[i].Left - right;
                split = (right + intervals[i].Left) / 2;
            }

            right = Math.Max(right, intervals[i].Right);
        }

        // A printed column gutter can be narrower than one line height. Use the
        // text scale rather than a fixed pixel threshold (PDF bounds use points).
        if (gap > Math.Max(1, typicalHeight * .6))
        {
            var left = lines.Where(l => l.Right < split).ToArray();
            var other = lines.Where(l => l.Left > split).ToArray();
            if (left.Length > 0 && other.Length > 0)
                return Sort(left, depth + 1).Concat(Sort(other, depth + 1)).ToArray();
        }

        var rows = lines.OrderBy(l => l.Top).ToArray();
        var bottom = rows[0].Bottom;
        for (var i = 1; i < rows.Length; i++)
        {
            // Separate full-width headings/introductions/footers from a column
            // block even when their margins are smaller than a line height.
            if (rows[i].Top - bottom > Math.Max(1, typicalHeight * .75))
            {
                var cut = (bottom + rows[i].Top) / 2;
                return Sort(lines.Where(l => l.Bottom < cut).ToArray(), depth + 1).Concat(Sort(lines.Where(l => l.Top > cut).ToArray(), depth + 1)).ToArray();
            }

            bottom = Math.Max(bottom, rows[i].Bottom);
        }

        return SortRows(lines, typicalHeight);
    }

    private static IReadOnlyList<OcrLine> SortRows(IReadOnlyList<OcrLine> lines, double height) =>
        lines.OrderBy(l => Math.Round(l.Top / Math.Max(1, height * .5))).ThenBy(l => l.Left).ToArray();

    public static string Text(OcrPage page, bool paragraphs)
    {
        if (!paragraphs)
            return string.Join(Environment.NewLine, page.Lines.Select(l => l.Text));
        var text = new System.Text.StringBuilder();
        OcrLine? previous = null;
        foreach (var line in page.Lines)
        {
            if (previous is not null)
            {
                var sameBlock = line.Top - previous.Bottom < Math.Max(2, (previous.Bottom - previous.Top) * .7) && Math.Abs(line.Left - previous.Left) < (previous.Bottom - previous.Top) * 2 && line.Top >= previous.Top;
                text.Append(sameBlock ? ' ' : Environment.NewLine + Environment.NewLine);
            }

            text.Append(line.Text);
            previous = line;
        }

        return text.ToString();
    }
}
