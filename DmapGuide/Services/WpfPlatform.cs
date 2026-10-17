using DmapShared.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DmapGuide.Services;

/// <summary>The desktop's dialogs, pickers and timers for the shared view model.</summary>
public class WpfPlatform : IPlatformServices
{
    public bool SupportsFolders => true;

    public string CacheFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DMAP Guide", "cache");

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

    public Task<string?> PickFolderAsync(string title, string? startIn)
    {
        var dlg = new OpenFolderDialog { Title = title, InitialDirectory = startIn ?? "" };
        return Task.FromResult(dlg.ShowDialog(Application.Current.MainWindow) == true ? dlg.FolderName : null);
    }

    public void BeginInvoke(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    public void StartTimer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => tick();
        timer.Start();
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
