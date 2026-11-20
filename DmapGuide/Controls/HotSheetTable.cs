using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DmapShared.Models;

namespace DmapGuide.Controls;

/// <summary>
/// A table from an episode's Hot Sheets, set out as in the PDF: its columns, cells that run across
/// several columns, shaded rows and (for a bordered table) the lines between cells. Each cell's
/// words can be edited like any other text in the tome.
/// </summary>
public sealed class HotSheetTable : ContentControl
{
    private static readonly Brush Line = Frozen(Color.FromArgb(0x99, 0x87, 0x6A, 0x45));
    private static readonly Brush Shade = Frozen(Color.FromArgb(0x26, 0x87, 0x6A, 0x45));

    public HotSheetTable()
    {
        Focusable = false;
        DataContextChanged += (_, _) => Build();
    }

    private void Build()
    {
        if (DataContext is not HotSheetBlock { Kind: HotSheetBlockKind.Table } table || table.ColumnWidths.Count == 0)
        {
            Content = null;
            return;
        }

        var grid = new Grid();
        foreach (double width in table.ColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(width, 0.02), GridUnitType.Star) });

        var cellStyle = TryFindResource("InkBoxMulti") as Style;
        for (int r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int column = 0;
            foreach (var cell in row.Cells)
            {
                if (column >= table.ColumnWidths.Count) break;
                int span = Math.Min(cell.Span, table.ColumnWidths.Count - column);

                var box = new TextBox
                {
                    Style = cellStyle,
                    FontSize = 14,
                    FontWeight = cell.Bold ? FontWeights.SemiBold : FontWeights.Normal,
                    TextAlignment = cell.Align switch
                    {
                        HotSheetAlign.Center => TextAlignment.Center,
                        HotSheetAlign.Right => TextAlignment.Right,
                        _ => TextAlignment.Left
                    },
                    MinHeight = 0,
                    VerticalAlignment = VerticalAlignment.Center
                };
                box.SetBinding(TextBox.TextProperty, new Binding(nameof(HotSheetCell.Text))
                {
                    Source = cell,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });

                var border = new Border
                {
                    Child = box,
                    Background = row.Shaded ? Shade : Brushes.Transparent,
                    BorderBrush = Line,
                    // Each cell draws its right and bottom lines; the table's outline adds the top and left.
                    BorderThickness = table.Bordered ? new Thickness(0, 0, 1, 1) : new Thickness(0),
                    Padding = table.Bordered ? new Thickness(4, 3, 4, 3) : new Thickness(0, 1, 12, 1)
                };
                Grid.SetRow(border, r);
                Grid.SetColumn(border, column);
                Grid.SetColumnSpan(border, Math.Max(1, span));
                grid.Children.Add(border);
                column += Math.Max(1, span);
            }
        }

        Content = new Border
        {
            Child = grid,
            BorderBrush = Line,
            BorderThickness = table.Bordered ? new Thickness(1, 1, 0, 0) : new Thickness(0),
            Margin = new Thickness(0, 6, 0, 10)
        };
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
