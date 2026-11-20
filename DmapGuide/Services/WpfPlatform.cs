using DmapShared.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DmapGuide.Services;

/// <summary>The desktop's dialogs, pickers and timers for the shared view model.</summary>
public class WpfPlatform : IPlatformServices
{
    public bool SupportsFolders => true;
    public bool SupportsServer => false;

    public string CacheFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DMAP Guide", "cache");

    /// <summary>From &lt;Version&gt; in DmapGuide.csproj, which "Publish Shared App" bumps on every build it ships.</summary>
    public string AppVersion { get; } = (typeof(WpfPlatform).Assembly.GetName().Version ?? new Version(0, 0, 0)).ToString(3);

    public Task AlertAsync(string title, string message)
    {
        TomeDialog.Show(title, message, "OK", null);
        return Task.CompletedTask;
    }

    public Task<bool> AskAsync(string title, string message, string yes, string no) =>
        Task.FromResult(TomeDialog.Show(title, message, yes, no));

    public Task<string?> PickImageAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a picture",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*"
        };
        return Task.FromResult(dlg.ShowDialog(Application.Current.MainWindow) == true ? dlg.FileName : null);
    }

    public Task<string?> PickPdfAsync(string title, string prompt) => Task.FromResult(PdfDropDialog.Show(title, prompt));

    public Task<string?> PickFolderAsync(string title, string? startIn)
    {
        var dlg = new OpenFolderDialog { Title = title, InitialDirectory = startIn ?? "" };
        return Task.FromResult(dlg.ShowDialog(Application.Current.MainWindow) == true ? dlg.FolderName : null);
    }

    public bool SupportsWebCopy => true;

    public Task<string?> PickSaveFileAsync(string title, string fileName)
    {
        var dlg = new SaveFileDialog
        {
            Title = title,
            FileName = fileName,
            Filter = "Web page|*.html",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        return Task.FromResult(dlg.ShowDialog(Application.Current.MainWindow) == true ? dlg.FileName : null);
    }

    /// <summary>The picture scaled down so its longer side is at most <paramref name="maxSize"/>, as a JPEG data: address.</summary>
    public string? PictureForWeb(string file, int maxSize)
    {
        try
        {
            var decoder = BitmapDecoder.Create(new Uri(file), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            double scale = Math.Min(1.0, (double)maxSize / Math.Max(frame.PixelWidth, frame.PixelHeight));
            BitmapSource small = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;

            // Photos as JPEG (small); pictures that really have see-through parts as PNG, which keeps them.
            bool seeThrough = HasSeeThroughParts(small);
            BitmapEncoder encoder = seeThrough ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 82 };
            encoder.Frames.Add(BitmapFrame.Create(small));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return $"data:image/{(seeThrough ? "png" : "jpeg")};base64," + Convert.ToBase64String(stream.ToArray());
        }
        catch
        {
            return null; // left out of the web copy, like a missing picture in the app
        }
    }

    // Many pictures are saved with room for see-through parts but don't use it.
    private static bool HasSeeThroughParts(BitmapSource picture)
    {
        var bgra = new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] < 250) return true;
        return false;
    }

    public void BeginInvoke(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    public void OpenLink(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

    public void StartTimer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => tick();
        timer.Start();
    }
}

/// <summary>A window to drop a PDF onto (or choose one), then press Add to page.</summary>
internal static class PdfDropDialog
{
    /// <returns>The chosen PDF, or null if cancelled.</returns>
    public static string? Show(string title, string prompt)
    {
        var app = Application.Current;
        var window = new Window
        {
            Title = title,
            Owner = app.MainWindow is { IsLoaded: true } main ? main : null,
            WindowStartupLocation = app.MainWindow is { IsLoaded: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            SizeToContent = SizeToContent.Height,
            Width = 520,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow,
            Background = (Brush)app.Resources["ParchmentBrush"]
        };

        string? chosen = null;
        var ink = (Brush)app.Resources["InkSoftBrush"];

        var dropText = new TextBlock
        {
            Text = prompt,
            Style = (Style)app.Resources["Body"],
            FontSize = 16,
            FontStyle = FontStyles.Italic,
            Foreground = ink,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var dropZone = new Border
        {
            AllowDrop = true,
            Height = 170,
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,   // so drops land anywhere inside, not just on the words
            Child = dropText
        };
        // A dashed edge, so it reads as "drop here".
        dropZone.BorderBrush = new VisualBrush
        {
            Visual = new System.Windows.Shapes.Rectangle
            {
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Stroke = ink,
                StrokeThickness = 2,
                Width = 480,
                Height = 170
            }
        };

        var addButton = new Button
        {
            Content = "Add to page",
            Style = (Style)app.Resources["TomeButton"],
            MinWidth = 110,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true,
            IsEnabled = false
        };

        void Choose(string path)
        {
            chosen = path;
            dropText.Text = $"📄  {Path.GetFileName(path)}\n\nPress Add to page, or drop a different PDF.";
            dropText.FontStyle = FontStyles.Normal;
            addButton.IsEnabled = true;
        }

        static string? PdfIn(DragEventArgs e) =>
            e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files &&
            string.Equals(Path.GetExtension(files[0]), ".pdf", StringComparison.OrdinalIgnoreCase) ? files[0] : null;

        dropZone.DragOver += (_, e) =>
        {
            e.Effects = PdfIn(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        dropZone.Drop += (_, e) =>
        {
            if (PdfIn(e) is string path) Choose(path);
            else TomeDialog.Show(title, "That isn't a PDF. Drop the Hot Sheets as a .pdf file.", "OK", null);
        };

        var browse = new Button
        {
            Content = "Choose PDF…",
            Style = (Style)app.Resources["TomeButton"],
            MinWidth = 110,
            Margin = new Thickness(0)
        };
        browse.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Title = title, Filter = "PDF files|*.pdf" };
            if (dlg.ShowDialog(window) == true) Choose(dlg.FileName);
        };

        var cancel = new Button
        {
            Content = "Cancel",
            Style = (Style)app.Resources["TomeButton"],
            MinWidth = 96,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true
        };
        // Closing with the window's ✕ counts as Cancel.
        bool added = false;
        cancel.Click += (_, _) => window.Close();
        addButton.Click += (_, _) => { added = true; window.Close(); };

        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        DockPanel.SetDock(browse, Dock.Left);
        DockPanel.SetDock(addButton, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(browse);
        buttons.Children.Add(addButton);
        buttons.Children.Add(cancel);

        window.Content = new StackPanel
        {
            Margin = new Thickness(20, 18, 20, 18),
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontFamily = (FontFamily)app.Resources["HeadingFont"],
                    FontWeight = FontWeights.Bold,
                    FontSize = 18,
                    Foreground = (Brush)app.Resources["RubricBrush"],
                    Margin = new Thickness(0, 0, 0, 12)
                },
                dropZone,
                buttons
            }
        };

        window.ShowDialog();
        return added ? chosen : null;
    }
}

/// <summary>A message on parchment with the tome's buttons, in place of a plain Windows message box.</summary>
internal static class TomeDialog
{
    /// <returns>True if <paramref name="yes"/> was pressed.</returns>
    public static bool Show(string title, string message, string yes, string? no)
    {
        var app = Application.Current;
        var window = new Window
        {
            Title = title,
            Owner = app.MainWindow is { IsLoaded: true } main ? main : null,
            WindowStartupLocation = app.MainWindow is { IsLoaded: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow,
            Background = (Brush)app.Resources["ParchmentBrush"]
        };

        bool answer = false;
        Button MakeButton(string text, bool result)
        {
            var button = new Button
            {
                Content = text,
                Style = (Style)app.Resources["TomeButton"],
                MinWidth = 96,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = result,
                IsCancel = !result || no == null
            };
            button.Click += (_, _) =>
            {
                answer = result;
                window.Close();
            };
            return button;
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        if (no != null) buttons.Children.Add(MakeButton(no, false));
        buttons.Children.Add(MakeButton(yes, true));

        window.Content = new StackPanel
        {
            Margin = new Thickness(26, 20, 26, 18),
            MaxWidth = 520,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontFamily = (FontFamily)app.Resources["HeadingFont"],
                    FontWeight = FontWeights.Bold,
                    FontSize = 18,
                    Foreground = (Brush)app.Resources["RubricBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                },
                new TextBlock { Text = message, Style = (Style)app.Resources["Body"], FontSize = 16 },
                buttons
            }
        };

        window.ShowDialog();
        return answer;
    }
}
