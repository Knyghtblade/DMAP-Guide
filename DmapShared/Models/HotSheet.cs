using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DmapShared.Models;

[JsonConverter(typeof(JsonStringEnumConverter<HotSheetBlockKind>))]
public enum HotSheetBlockKind { Text, Rule, Table }

[JsonConverter(typeof(JsonStringEnumConverter<HotSheetStyle>))]
public enum HotSheetStyle { Body, Bold, Heading, Title }

[JsonConverter(typeof(JsonStringEnumConverter<HotSheetAlign>))]
public enum HotSheetAlign { Left, Center, Right }

/// <summary>
/// One piece of an episode's Hot Sheets, rebuilt from the PDF: a paragraph (in one of four
/// styles), a dividing line, or a table. The words stay editable; the layout comes from the PDF.
/// </summary>
public partial class HotSheetBlock : ObservableObject
{
    public HotSheetBlockKind Kind { get; set; }

    /// <summary>Text: how it's set (the PDF's title, headings, bold lines and body text).</summary>
    public HotSheetStyle Style { get; set; }

    /// <summary>Text: the paragraph. Line breaks inside it are kept.</summary>
    [ObservableProperty] private string _text = "";

    /// <summary>Table: drawn with lines around the cells (false for side-by-side columns of text).</summary>
    public bool Bordered { get; set; } = true;

    /// <summary>Table: each column's share of the width, left to right.</summary>
    public List<double> ColumnWidths { get; set; } = new();

    public ObservableCollection<HotSheetRow> Rows { get; set; } = new();

    /// <summary>Starts a section (an episode's overview heading or its box), so the page offers "Remove section". Not saved.</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _startsSection;

    public static HotSheetBlock Paragraph(string text, HotSheetStyle style = HotSheetStyle.Body) =>
        new() { Kind = HotSheetBlockKind.Text, Style = style, Text = text };

    public static HotSheetBlock Rule() => new() { Kind = HotSheetBlockKind.Rule };

    /// <summary>The words only, for searching, the old plain-text Hot Sheets, and older apps.</summary>
    public static string PlainText(IEnumerable<HotSheetBlock> blocks)
    {
        var text = new StringBuilder();
        foreach (var block in blocks)
        {
            string part = block.Kind switch
            {
                HotSheetBlockKind.Text => block.Text.Trim(),
                HotSheetBlockKind.Rule => "",
                _ => string.Join("\n", block.Rows.Select(r =>
                         string.Join("   ", r.Cells.Select(c => c.Text.Replace("\n", " ").Trim()).Where(t => t.Length > 0))))
            };
            if (part.Length == 0) continue;
            if (text.Length > 0) text.Append("\n\n");
            text.Append(part);
        }
        return text.ToString();
    }
}

/// <summary>A row of a Hot Sheets table.</summary>
public class HotSheetRow
{
    /// <summary>Shaded in the PDF (e.g. every other line of the timecode log).</summary>
    public bool Shaded { get; set; }

    public ObservableCollection<HotSheetCell> Cells { get; set; } = new();
}

/// <summary>A cell of a Hot Sheets table; its words can be edited.</summary>
public partial class HotSheetCell : ObservableObject
{
    [ObservableProperty] private string _text = "";

    public bool Bold { get; set; }

    public HotSheetAlign Align { get; set; }

    /// <summary>How many of the table's columns it covers (a title row across the whole table covers them all).</summary>
    public int Span { get; set; } = 1;
}
