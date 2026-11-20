using System.ComponentModel;
using DmapShared.Models;

namespace DmapAndroid.Controls;

/// <summary>
/// An episode's Hot Sheets set out as in the PDF (the desktop's Hot Sheets page): the title, bold
/// lines, dividers and tables with their columns, shaded rows and cells across the table. Every
/// paragraph and cell can be edited. Its BindingContext is the Hot Sheets page.
/// </summary>
public sealed class HotSheetLayout : ContentView
{
    private static readonly Color Line = Color.FromRgba(0x87, 0x6A, 0x45, 0x99);
    private static readonly Color Shade = Color.FromRgba(0x87, 0x6A, 0x45, 0x26);

    private Episode? _episode;

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (_episode != null) _episode.PropertyChanged -= OnEpisodeChanged;
        _episode = (BindingContext as HotSheetsPage)?.Episode;
        if (_episode != null) _episode.PropertyChanged += OnEpisodeChanged;
        Build();
    }

    private void OnEpisodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Episode.HotSheetBlocks)) Build();
    }

    private void Build()
    {
        var stack = new VerticalStackLayout { Spacing = 4 };
        if (_episode != null)
            foreach (var block in _episode.HotSheetBlocks)
                stack.Children.Add(block.Kind switch
                {
                    HotSheetBlockKind.Rule => new BoxView { HeightRequest = 1, Color = Tome.InkFaded, Margin = new Thickness(0, 8, 0, 10) },
                    HotSheetBlockKind.Table => Table(block),
                    _ => Paragraph(block)
                });
        Content = stack;
    }

    private static View Paragraph(HotSheetBlock block)
    {
        var editor = new InkEditor { FontFamily = "DmapBody", FontSize = 15, TextColor = Tome.Ink };
        editor.SetBinding(Editor.TextProperty, new Binding(nameof(HotSheetBlock.Text), BindingMode.TwoWay, source: block));
        switch (block.Style)
        {
            case HotSheetStyle.Title:
                editor.FontFamily = "DmapHeading";
                editor.FontSize = 24;
                editor.FontAttributes = FontAttributes.Bold;
                editor.HorizontalTextAlignment = TextAlignment.Center;
                editor.Margin = new Thickness(0, 4, 0, 10);
                break;
            case HotSheetStyle.Heading:
                editor.FontFamily = "DmapHeading";
                editor.FontSize = 17;
                editor.FontAttributes = FontAttributes.Bold;
                break;
            case HotSheetStyle.Bold:
                editor.FontAttributes = FontAttributes.Bold;
                break;
        }
        return editor;
    }

    private static View Table(HotSheetBlock table)
    {
        var grid = new Grid { ColumnSpacing = 0, RowSpacing = 0 };
        foreach (double width in table.ColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(width, 0.02), GridUnitType.Star)));
        if (grid.ColumnDefinitions.Count == 0) return grid;

        for (int r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            int column = 0;
            foreach (var cell in row.Cells)
            {
                if (column >= table.ColumnWidths.Count) break;
                int span = Math.Max(1, Math.Min(cell.Span, table.ColumnWidths.Count - column));

                var editor = new InkEditor
                {
                    FontFamily = "DmapBody",
                    FontSize = 13,
                    TextColor = Tome.Ink,
                    MinimumHeightRequest = 0,
                    FontAttributes = cell.Bold ? FontAttributes.Bold : FontAttributes.None,
                    HorizontalTextAlignment = cell.Align switch
                    {
                        HotSheetAlign.Center => TextAlignment.Center,
                        HotSheetAlign.Right => TextAlignment.End,
                        _ => TextAlignment.Start
                    }
                };
                editor.SetBinding(Editor.TextProperty, new Binding(nameof(HotSheetCell.Text), BindingMode.TwoWay, source: cell));

                // Each cell has its own thin outline (MAUI borders can't draw just some sides).
                var box = new Border
                {
                    Content = editor,
                    BackgroundColor = row.Shaded ? Shade : Colors.Transparent,
                    Stroke = table.Bordered ? new SolidColorBrush(Line) : null,
                    StrokeThickness = table.Bordered ? 0.75 : 0,
                    Padding = table.Bordered ? new Thickness(3, 1) : new Thickness(0, 0, 10, 0)
                };
                grid.Add(box, column, r);
                Grid.SetColumnSpan(box, span);
                column += span;
            }
        }
        return new Border
        {
            Content = grid,
            Stroke = table.Bordered ? new SolidColorBrush(Line) : null,
            StrokeThickness = table.Bordered ? 0.75 : 0,
            Margin = new Thickness(0, 4, 0, 8)
        };
    }
}
