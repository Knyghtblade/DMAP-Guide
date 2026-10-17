using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using Microsoft.Maui;

namespace DmapAndroid;

[Activity(
    Label = "DMAP Guide",
    Theme = "@style/DmapTheme",
    MainLauncher = true,
    Exported = true,
    // The tome is a two-page spread: landscape, either way up.
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize |
        ConfigChanges.Orientation |
        ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize |
        ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        try
        {
            base.OnCreate(savedInstanceState);

            // A phone in landscape is short: give the tome the whole screen (swipe from an edge to see the bars).
            if (Window != null && AndroidX.Core.View.WindowCompat.GetInsetsController(Window, Window.DecorView) is { } bars)
            {
                bars.Hide(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
                bars.SystemBarsBehavior = AndroidX.Core.View.WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
            }
        }
        catch (Exception exception)
        {
            var details = new TextView(this)
            {
                Text = $"DMAP Guide could not start. Please send a screenshot of this message.\n\n{exception}",
                TextSize = 14
            };
            details.SetTextIsSelectable(true);
            details.SetPadding(32, 32, 32, 32);
            var scrollView = new Android.Widget.ScrollView(this);
            scrollView.AddView(details);
            SetContentView(scrollView);
        }
    }
}
