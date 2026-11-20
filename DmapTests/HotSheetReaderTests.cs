using DmapShared;
using DmapShared.Models;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DmapTests;

/// <summary>Reading a Hot Sheets PDF back into its layout.</summary>
public class HotSheetReaderTests
{
    /// <summary>A PDF set out like the real Hot Sheets: title, crew lines, a divider, an info box and a shaded timecode log over two pages.</summary>
    private static string MakeHotSheetsPdf()
    {
        var builder = new PdfDocumentBuilder();
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);
        var page = builder.AddPage(612, 792);

        page.AddText("Dungeon Masters 999", 30, new PdfPoint(150, 720), bold);
        page.AddText("Date: 1/2/2026", 11, new PdfPoint(72, 680), bold);
        page.AddText("Producers: Ann, Bo", 11, new PdfPoint(72, 665), bold);
        page.AddText("Director: Cy Dee", 11, new PdfPoint(72, 650), bold);
        page.AddText("Audio: Ed Fay", 11, new PdfPoint(430, 650), bold);
        page.DrawLine(new PdfPoint(75, 630), new PdfPoint(537, 630), 1.3);
        page.AddText("The party gathers in the arena of the city and the crowd roars for the champions to", 11, new PdfPoint(72, 610), regular);
        page.AddText("begin.", 11, new PdfPoint(72, 595), regular);

        // An info box: a title row across the table, then three columns.
        foreach (var y in new[] { 570.0, 550, 530 }) page.DrawLine(new PdfPoint(72, y), new PdfPoint(522, y), 1.3);
        page.DrawLine(new PdfPoint(72, 530), new PdfPoint(72, 570), 1.3);
        page.DrawLine(new PdfPoint(522, 530), new PdfPoint(522, 570), 1.3);
        page.DrawLine(new PdfPoint(222, 530), new PdfPoint(222, 550), 1.3);
        page.DrawLine(new PdfPoint(372, 530), new PdfPoint(372, 550), 1.3);
        page.AddText("Arena Day - #999", 12, new PdfPoint(250, 556), bold);
        page.AddText("LOCATION", 10, new PdfPoint(124, 536), regular);
        page.AddText("CAST", 10, new PdfPoint(285, 536), regular);
        page.AddText("NOTES", 10, new PdfPoint(430, 536), regular);

        // The timecode log: 10 rows here, 5 more on the next page, every other row shaded.
        Log(page, regular, 500, 0, 10);
        page = builder.AddPage(612, 792);
        Log(page, regular, 720, 10, 5);
        page.AddText("End of sheets", 11, new PdfPoint(72, 580), regular);

        string path = Path.Combine(TempFolder.Create("hotsheets"), "Ep 999 Hot Sheets.pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private static void Log(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font, double top, int first, int count)
    {
        double[] xs = { 72, 180, 225, 528 };
        for (int i = 0; i < count; i++)
        {
            double y = top - i * 20;
            if ((first + i) % 2 == 1)
            {
                page.SetTextAndFillColor(239, 239, 239);
                page.DrawRectangle(new PdfPoint(72, y - 20), 456, 20, 0, fill: true);
                page.SetTextAndFillColor(0, 0, 0);
            }
            page.AddText($"11:{first + i:00}:00:00", 10, new PdfPoint(76, y - 14), font);
            page.AddText("1", 10, new PdfPoint(216, y - 14), font);
            page.AddText($"moment {first + i}", 10, new PdfPoint(230, y - 14), font);
        }
        page.SetStrokeColor(204, 204, 204);
        for (int i = 0; i <= count; i++) page.DrawLine(new PdfPoint(72, top - i * 20), new PdfPoint(528, top - i * 20), 1.3);
        foreach (var x in xs) page.DrawLine(new PdfPoint(x, top - count * 20), new PdfPoint(x, top), 1.3);
        page.SetStrokeColor(0, 0, 0);
    }

    [Fact]
    public void Title_bold_lines_columns_dividers_and_tables_come_back_as_laid_out()
    {
        var blocks = HotSheetReader.Read(MakeHotSheetsPdf());

        Assert.Collection(blocks,
            title =>
            {
                Assert.Equal(HotSheetStyle.Title, title.Style);
                Assert.Equal("Dungeon Masters 999", title.Text);
            },
            crew =>
            {
                Assert.Equal(HotSheetStyle.Bold, crew.Style);
                Assert.Equal("Date: 1/2/2026\nProducers: Ann, Bo", crew.Text);
            },
            columns =>
            {
                Assert.Equal(HotSheetBlockKind.Table, columns.Kind);
                Assert.False(columns.Bordered);
                var row = Assert.Single(columns.Rows);
                Assert.Equal(new[] { "Director: Cy Dee", "Audio: Ed Fay" }, row.Cells.Select(c => c.Text));
                Assert.All(row.Cells, c => Assert.True(c.Bold));
            },
            rule => Assert.Equal(HotSheetBlockKind.Rule, rule.Kind),
            paragraph =>
            {
                Assert.Equal(HotSheetStyle.Body, paragraph.Style);
                Assert.Equal("The party gathers in the arena of the city and the crowd roars for the champions to begin.", paragraph.Text);
            },
            info =>
            {
                Assert.True(info.Bordered);
                Assert.Equal(3, info.ColumnWidths.Count);
                var heading = Assert.Single(info.Rows[0].Cells);
                Assert.Equal("Arena Day - #999", heading.Text);
                Assert.Equal(3, heading.Span);
                Assert.True(heading.Bold);
                Assert.Equal(new[] { "LOCATION", "CAST", "NOTES" }, info.Rows[1].Cells.Select(c => c.Text));
                Assert.All(info.Rows[1].Cells, c => Assert.Equal(HotSheetAlign.Center, c.Align));
            },
            log =>
            {
                // One table across both pages.
                Assert.Equal(15, log.Rows.Count);
                Assert.Equal("11:14:00:00", log.Rows[14].Cells[0].Text);
                Assert.Equal("moment 14", log.Rows[14].Cells[2].Text);
                Assert.Equal(Enumerable.Range(0, 15).Select(i => i % 2 == 1), log.Rows.Select(r => r.Shaded));
                Assert.All(log.Rows, r => Assert.Equal(HotSheetAlign.Right, r.Cells[1].Align));
            },
            end => Assert.Equal("End of sheets", end.Text));
    }

    [Fact]
    public void The_plain_words_keep_every_cell()
    {
        var blocks = HotSheetReader.Read(MakeHotSheetsPdf());
        string text = HotSheetBlock.PlainText(blocks);
        Assert.Contains("Director: Cy Dee   Audio: Ed Fay", text);
        Assert.Contains("11:14:00:00   1   moment 14", text);
    }
}
