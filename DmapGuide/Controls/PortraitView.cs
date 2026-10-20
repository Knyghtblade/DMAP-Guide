using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DmapGuide.Controls;

/// <summary>
/// A picture filling a circle, like a cast portrait. The picture is scaled to cover the
/// circle and <see cref="OffsetX"/> / <see cref="OffsetY"/> (-1 to 1) say which part of it
/// shows. While <see cref="CanDrag"/> is on (edit mode) it can be dragged into place.
/// Draws nothing without a picture, so whatever sits behind it (the placeholder) shows.
/// </summary>
public class PortraitView : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(PortraitView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((PortraitView)d).UpdateCursor()));

    public static readonly DependencyProperty OffsetXProperty = DependencyProperty.Register(
        nameof(OffsetX), typeof(double), typeof(PortraitView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty OffsetYProperty = DependencyProperty.Register(
        nameof(OffsetY), typeof(double), typeof(PortraitView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CanDragProperty = DependencyProperty.Register(
        nameof(CanDrag), typeof(bool), typeof(PortraitView),
        new FrameworkPropertyMetadata(false, (d, _) => ((PortraitView)d).UpdateCursor()));

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>-1 shows the picture's right edge, 0 its middle, 1 its left edge (only matters when it's wider than tall).</summary>
    public double OffsetX
    {
        get => (double)GetValue(OffsetXProperty);
        set => SetValue(OffsetXProperty, value);
    }

    /// <summary>-1 shows the picture's bottom, 0 its middle, 1 its top (only matters when it's taller than wide).</summary>
    public double OffsetY
    {
        get => (double)GetValue(OffsetYProperty);
        set => SetValue(OffsetYProperty, value);
    }

    public bool CanDrag
    {
        get => (bool)GetValue(CanDragProperty);
        set => SetValue(CanDragProperty, value);
    }

    public PortraitView()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        ClipToBounds = true;
    }

    /// <summary>Where the picture is drawn, and how far it overflows the circle on each axis.</summary>
    private (Rect Image, double SlackX, double SlackY)? Layout()
    {
        if (Source is not ImageSource img || img.Width <= 0 || img.Height <= 0) return null;
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return null;

        double scale = Math.Max(w / img.Width, h / img.Height);
        double dw = img.Width * scale, dh = img.Height * scale;
        double slackX = dw - w, slackY = dh - h;
        double x = -slackX / 2 + Clamp(OffsetX) * slackX / 2;
        double y = -slackY / 2 + Clamp(OffsetY) * slackY / 2;
        return (new Rect(x, y, dw, dh), slackX, slackY);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Layout() is not { } layout) return;
        dc.PushClip(new EllipseGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        dc.DrawImage(Source, layout.Image);
        dc.Pop();
    }

    // ============================================================
    // DRAGGING THE PICTURE INTO PLACE
    // ============================================================
    private bool _dragging;
    private Point _dragStart;
    private double _startX, _startY;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!CanDrag || Layout() == null) return;

        _dragging = true;
        _dragStart = e.GetPosition(this);
        _startX = OffsetX;
        _startY = OffsetY;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || Layout() is not { } layout) return;

        // Moving the mouse down shows more of the top: the picture follows the pointer.
        var p = e.GetPosition(this);
        if (layout.SlackX > 0) OffsetX = Clamp(_startX + (p.X - _dragStart.X) / (layout.SlackX / 2));
        if (layout.SlackY > 0) OffsetY = Clamp(_startY + (p.Y - _dragStart.Y) / (layout.SlackY / 2));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
    }

    private void UpdateCursor()
    {
        bool draggable = CanDrag && Source != null;
        Cursor = draggable ? Cursors.SizeAll : null;
        ToolTip = draggable ? "Drag the picture to position it inside the circle" : null;
    }

    private static double Clamp(double v) => Math.Max(-1, Math.Min(1, v));
}
