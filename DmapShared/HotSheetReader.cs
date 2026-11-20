using DmapShared.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DmapShared;

/// <summary>
/// Reads a Hot Sheets PDF back into editable paragraphs, dividing lines and tables, keeping the
/// layout: the title, bold lines, side-by-side columns, tables (from the lines drawn around their
/// cells, with shaded rows and cells that run across the table) and paragraphs or tables that carry
/// on over a page break. Typed text only; a scanned page has no words to read.
/// </summary>
public static class HotSheetReader
{
    public static List<HotSheetBlock> Read(string path)
    {
        using var pdf = PdfDocument.Open(path);
        var pieces = new List<Piece>();
        int pageIndex = 0;
        foreach (var page in pdf.GetPages())
            pieces.AddRange(ReadPage(page, pageIndex++));
        return Assemble(pieces);
    }

    // ============================================================
    // ONE PAGE: lines of text, dividing lines and tables, top to bottom
    // ============================================================
    private abstract record Piece(int Page, double Top);

    /// <summary>A line of text; more than one cell when it's set out in columns.</summary>
    private sealed record LinePiece(int Page, double Top, double Baseline, double Right, double TextRight,
        double FirstWordWidth, double Size, bool Bold, List<(string Text, double Left)> Cells) : Piece(Page, Top);

    private sealed record RulePiece(int Page, double Top) : Piece(Page, Top);

    private sealed record TablePiece(int Page, double Top, HotSheetBlock Table) : Piece(Page, Top);

    private record struct HLine(double Y, double X1, double X2);
    private record struct VLine(double X, double Y1, double Y2);

    private const double Tol = 2.5;

    private static List<Piece> ReadPage(Page page, int pageIndex)
    {
        var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        var (hLines, vLines, shades) = Drawing(page);

        var pieces = new List<Piece>();
        var used = new HashSet<Word>();

        // Tables: horizontal and vertical lines that meet.
        var (tables, loose) = Tables(hLines, vLines);
        foreach (var (hs, vs) in tables)
            pieces.Add(new TablePiece(pageIndex, hs.Max(h => h.Y), Table(hs, vs, words, shades, used)));

        // A long line on its own divides the page.
        foreach (var h in loose.Where(h => h.X2 - h.X1 > page.Width * 0.3))
            pieces.Add(new RulePiece(pageIndex, h.Y));

        // Everything else is lines of text.
        var lines = GroupLines(words.Where(w => !used.Contains(w)));
        double textRight = lines.Count == 0 ? page.Width : lines.Max(l => l[^1].BoundingBox.Right);
        foreach (var line in lines)
        {
            double size = Median(line.SelectMany(w => w.Letters).Select(l => l.PointSize));
            var cells = new List<(string, double)>();
            var current = new List<Word> { line[0] };
            for (int i = 1; i < line.Count; i++)
            {
                // A wide gap means the line is set out in columns.
                if (line[i].BoundingBox.Left - line[i - 1].BoundingBox.Right > Math.Max(24, size * 4))
                {
                    cells.Add((Join(current), current[0].BoundingBox.Left));
                    current = new List<Word>();
                }
                current.Add(line[i]);
            }
            cells.Add((Join(current), current[0].BoundingBox.Left));

            pieces.Add(new LinePiece(pageIndex, line.Max(w => w.BoundingBox.Top), line[0].BoundingBox.Bottom,
                line[^1].BoundingBox.Right, textRight, line[0].BoundingBox.Width, size, IsBold(line), cells));
        }

        return pieces.OrderByDescending(p => p.Top).ToList();
    }

    /// <summary>The page's thin lines (table borders and dividers) and its shaded boxes.</summary>
    private static (List<HLine>, List<VLine>, List<UglyToad.PdfPig.Core.PdfRectangle>) Drawing(Page page)
    {
        var hLines = new List<HLine>();
        var vLines = new List<VLine>();
        var shades = new List<UglyToad.PdfPig.Core.PdfRectangle>();
        foreach (var path in page.Paths)
        {
            if (!path.IsStroked && !path.IsFilled) continue; // clipping outlines, not drawn
            if (path.GetBoundingRectangle() is not { } r) continue;
            double w = r.Width, h = r.Height;
            bool thin = Math.Min(w, h) <= 2.5, longEnough = Math.Max(w, h) >= 8;

            if (thin && longEnough)
            {
                if (w >= h) hLines.Add(new HLine((r.Bottom + r.Top) / 2, r.Left, r.Right));
                else vLines.Add(new VLine((r.Left + r.Right) / 2, r.Bottom, r.Top));
                continue;
            }
            if (w <= 4 || h <= 4) continue;

            // A shaded box (behind a row of a table, say).
            if (path.IsFilled && w < page.Width * 0.95 && !IsWhite(path.FillColor))
                shades.Add(r);

            // A box drawn round a cell: its four sides.
            if (path.IsStroked)
            {
                hLines.Add(new HLine(r.Top, r.Left, r.Right));
                hLines.Add(new HLine(r.Bottom, r.Left, r.Right));
                vLines.Add(new VLine(r.Left, r.Bottom, r.Top));
                vLines.Add(new VLine(r.Right, r.Bottom, r.Top));
            }
        }
        return (hLines, vLines, shades);
    }

    private static bool IsWhite(UglyToad.PdfPig.Graphics.Colors.IColor? color)
    {
        if (color == null) return true;
        var (r, g, b) = color.ToRGBValues();
        return r > 0.98 && g > 0.98 && b > 0.98;
    }

    /// <summary>Lines that meet make a table; horizontal lines that meet nothing are left over.</summary>
    private static (List<(List<HLine>, List<VLine>)>, List<HLine>) Tables(List<HLine> hLines, List<VLine> vLines)
    {
        // Join every horizontal line to the vertical lines it touches (union-find).
        int n = hLines.Count + vLines.Count;
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        for (int i = 0; i < hLines.Count; i++)
            for (int j = 0; j < vLines.Count; j++)
            {
                var h = hLines[i];
                var v = vLines[j];
                if (v.X >= h.X1 - Tol && v.X <= h.X2 + Tol && h.Y >= v.Y1 - Tol && h.Y <= v.Y2 + Tol)
                    parent[Find(i)] = Find(hLines.Count + j);
            }

        var tables = new List<(List<HLine>, List<VLine>)>();
        var loose = new List<HLine>();
        foreach (var group in Enumerable.Range(0, n).GroupBy(Find))
        {
            var hs = group.Where(i => i < hLines.Count).Select(i => hLines[i]).ToList();
            var vs = group.Where(i => i >= hLines.Count).Select(i => vLines[i - hLines.Count]).ToList();
            if (hs.Count >= 2 && vs.Count >= 2) tables.Add((hs, vs));
            else loose.AddRange(hs);
        }
        return (tables, loose);
    }

    /// <summary>A table's rows and cells, from the lines around them, filled with the words inside.</summary>
    private static HotSheetBlock Table(List<HLine> hs, List<VLine> vs, List<Word> words,
        List<UglyToad.PdfPig.Core.PdfRectangle> shades, HashSet<Word> used)
    {
        var ys = Distinct(hs.Select(h => h.Y)).OrderByDescending(y => y).ToList();
        var xs = Distinct(vs.Select(v => v.X)).OrderBy(x => x).ToList();
        double left = xs[0], right = xs[^1];

        var table = new HotSheetBlock
        {
            Kind = HotSheetBlockKind.Table,
            ColumnWidths = xs.Zip(xs.Skip(1), (a, b) => (b - a) / (right - left)).ToList()
        };

        for (int i = 0; i + 1 < ys.Count; i++)
        {
            double top = ys[i], bottom = ys[i + 1];
            if (top - bottom < 3) continue; // a double line
            double mid = (top + bottom) / 2;

            // This row's cell edges: the vertical lines that cross it.
            var edges = Distinct(vs.Where(v => v.Y1 - Tol <= mid && v.Y2 + Tol >= mid).Select(v => v.X)
                    .Append(left).Append(right))
                .OrderBy(x => x).ToList();

            var row = new HotSheetRow
            {
                Shaded = shades.Any(s => s.Bottom - Tol <= mid && s.Top + Tol >= mid && s.Right > left + Tol && s.Left < right - Tol)
            };
            for (int c = 0; c + 1 < edges.Count; c++)
            {
                double cl = edges[c], cr = edges[c + 1];
                var inside = words.Where(w => !used.Contains(w)
                        && w.BoundingBox.Centroid.X > cl && w.BoundingBox.Centroid.X < cr
                        && w.BoundingBox.Centroid.Y < top && w.BoundingBox.Centroid.Y > bottom)
                    .ToList();
                used.UnionWith(inside);
                row.Cells.Add(Cell(inside, cl, cr, Column(xs, cr) - Column(xs, cl)));
            }

            if (row.Cells.All(c => c.Text.Length == 0) && top - bottom < 6) continue;
            table.Rows.Add(row);
        }
        return table;
    }

    private static int Column(List<double> xs, double x)
    {
        int best = 0;
        for (int i = 1; i < xs.Count; i++)
            if (Math.Abs(xs[i] - x) < Math.Abs(xs[best] - x)) best = i;
        return best;
    }

    private static HotSheetCell Cell(List<Word> words, double left, double right, int span)
    {
        var cell = new HotSheetCell { Span = Math.Max(1, span) };
        if (words.Count == 0) return cell;

        var lines = GroupLines(words);
        double size = Median(words.SelectMany(w => w.Letters).Select(l => l.PointSize));
        cell.Text = JoinLines(lines, right - size * 0.3, size);
        cell.Bold = IsBold(words);

        // Set to the left, centre or right of the cell as in the PDF (only clear for a single line).
        if (lines.Count == 1)
        {
            double lgap = lines[0][0].BoundingBox.Left - left, rgap = right - lines[0][^1].BoundingBox.Right;
            if (lgap > 12 && Math.Abs(lgap - rgap) < Math.Max(6, 0.2 * (lgap + rgap))) cell.Align = HotSheetAlign.Center;
            else if (lgap > 12 && rgap < lgap * 0.4) cell.Align = HotSheetAlign.Right;
        }
        else if (lines.All(l => Math.Abs(((l[0].BoundingBox.Left - left) - (right - l[^1].BoundingBox.Right))) < Math.Max(6, size)))
        {
            cell.Align = HotSheetAlign.Center;
        }
        return cell;
    }

    // ============================================================
    // ALL PAGES: lines into paragraphs, columns and tables
    // ============================================================
    private static List<HotSheetBlock> Assemble(List<Piece> pieces)
    {
        double body = BodySize(pieces.OfType<LinePiece>());
        var blocks = new List<HotSheetBlock>();
        Piece? previous = null;
        HotSheetBlock? open = null; // the block the previous piece went into

        foreach (var piece in pieces)
        {
            switch (piece)
            {
                case RulePiece:
                    blocks.Add(open = HotSheetBlock.Rule());
                    break;

                case TablePiece t:
                    // A table carrying on from the bottom of the last page.
                    if (open is { Kind: HotSheetBlockKind.Table, Bordered: true } prior && previous is TablePiece
                        && previous.Page == t.Page - 1 && SameColumns(prior, t.Table))
                    {
                        foreach (var row in t.Table.Rows) prior.Rows.Add(row);
                    }
                    else blocks.Add(open = t.Table);
                    break;

                case LinePiece line when line.Cells.Count > 1:
                    // Side-by-side columns of text: a table without lines.
                    if (open is { Kind: HotSheetBlockKind.Table, Bordered: false } columns && previous is LinePiece above
                        && above.Page == line.Page && above.Baseline - line.Baseline < line.Size * 2.2)
                    {
                        columns.Rows.Add(ColumnsRow(line, columns.ColumnWidths.Count));
                    }
                    else
                    {
                        var lefts = line.Cells.Select(c => c.Left).Append(line.TextRight).ToList();
                        double width = lefts[^1] - lefts[0];
                        open = new HotSheetBlock
                        {
                            Kind = HotSheetBlockKind.Table,
                            Bordered = false,
                            ColumnWidths = lefts.Zip(lefts.Skip(1), (a, b) => width <= 0 ? 1.0 / line.Cells.Count : (b - a) / width).ToList()
                        };
                        open.Rows.Add(ColumnsRow(line, line.Cells.Count));
                        blocks.Add(open);
                    }
                    break;

                case LinePiece line:
                    var style = StyleOf(line, body);
                    string text = line.Cells[0].Text;
                    if (open is { Kind: HotSheetBlockKind.Text } para && para.Style == style && previous is LinePiece prev
                        && SameParagraph(prev, line))
                    {
                        para.Text += (Wrapped(prev, line) ? " " : "\n") + text;
                    }
                    else blocks.Add(open = HotSheetBlock.Paragraph(text, style));
                    break;
            }
            previous = piece;
        }
        return blocks;
    }

    private static HotSheetRow ColumnsRow(LinePiece line, int columns)
    {
        var row = new HotSheetRow();
        foreach (var (text, _) in line.Cells.Take(columns))
            row.Cells.Add(new HotSheetCell { Text = text, Bold = line.Bold });
        // More pieces than columns: the extra words go in the last column.
        if (line.Cells.Count > columns)
            row.Cells[^1].Text += " " + string.Join(" ", line.Cells.Skip(columns).Select(c => c.Text));
        while (row.Cells.Count < columns) row.Cells.Add(new HotSheetCell { Bold = line.Bold });
        return row;
    }

    private static bool SameColumns(HotSheetBlock a, HotSheetBlock b) =>
        a.ColumnWidths.Count == b.ColumnWidths.Count
        && a.ColumnWidths.Zip(b.ColumnWidths).All(p => Math.Abs(p.First - p.Second) < 0.03);

    /// <summary>The next line belongs to the same paragraph: close below it, or carried over to the next page.</summary>
    private static bool SameParagraph(LinePiece prev, LinePiece line) =>
        prev.Page == line.Page
            ? prev.Baseline - line.Baseline < Math.Max(prev.Size, line.Size) * 1.5
            : line.Page == prev.Page + 1 && Wrapped(prev, line);

    /// <summary>The line ran on to the next because the next word didn't fit (rather than a line break).</summary>
    private static bool Wrapped(LinePiece prev, LinePiece line) =>
        prev.TextRight - prev.Right < line.FirstWordWidth + prev.Size * 0.6;

    private static HotSheetStyle StyleOf(LinePiece line, double body) =>
        line.Size >= body * 1.6 ? HotSheetStyle.Title
        : line.Size >= body * 1.2 ? HotSheetStyle.Heading
        : line.Bold ? HotSheetStyle.Bold
        : HotSheetStyle.Body;

    // ============================================================
    // HELPERS
    // ============================================================
    /// <summary>Words grouped into lines (same baseline), top to bottom, each left to right.</summary>
    private static List<List<Word>> GroupLines(IEnumerable<Word> words)
    {
        var lines = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom))
        {
            double size = word.Letters.Count > 0 ? word.Letters[0].PointSize : 10;
            var line = lines.FirstOrDefault(l => Math.Abs(l[0].BoundingBox.Bottom - word.BoundingBox.Bottom) < Math.Max(1.5, size * 0.4));
            if (line == null) lines.Add(line = new List<Word>());
            line.Add(word);
        }
        foreach (var line in lines) line.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        return lines;
    }

    /// <summary>A cell's lines as one text: lines that ran on are joined with a space, real line breaks kept.</summary>
    private static string JoinLines(List<List<Word>> lines, double right, double size)
    {
        var text = new System.Text.StringBuilder(Join(lines[0]));
        for (int i = 1; i < lines.Count; i++)
        {
            bool wrapped = right - lines[i - 1][^1].BoundingBox.Right < lines[i][0].BoundingBox.Width + size * 0.6;
            text.Append(wrapped ? ' ' : '\n').Append(Join(lines[i]));
        }
        return text.ToString();
    }

    private static string Join(List<Word> words) => string.Join(" ", words.Select(w => w.Text));

    private static bool IsBold(IEnumerable<Word> words)
    {
        var letters = words.SelectMany(w => w.Letters).Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
        return letters.Count > 0 && letters.Count(l => IsBoldFont(l.FontName)) >= letters.Count * 0.8;
    }

    private static bool IsBoldFont(string? font) =>
        font != null && new[] { "Bold", "Black", "Heavy", "Semibold", "SemiBold", "Demi" }
            .Any(k => font.Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>The usual text size (most letters are set in it).</summary>
    private static double BodySize(IEnumerable<LinePiece> lines)
    {
        var sizes = lines.GroupBy(l => Math.Round(l.Size)).OrderByDescending(g => g.Sum(l => l.Cells.Sum(c => c.Text.Length))).FirstOrDefault();
        return sizes?.Key ?? 11;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 10 : sorted[sorted.Count / 2];
    }

    /// <summary>Positions closer together than the tolerance count as one.</summary>
    private static List<double> Distinct(IEnumerable<double> values)
    {
        var result = new List<double>();
        foreach (var v in values.OrderBy(v => v))
            if (result.Count == 0 || v - result[^1] > Tol) result.Add(v);
        return result;
    }
}
