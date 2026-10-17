using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using Microsoft.Maui;

namespace DmapAndroid;

[Activity(
    Label = "DMAP Guide",
    Theme = "@style/Maui.MainTheme",
    MainLauncher = true,
    Exported = true,
    ScreenOrientation = ScreenOrientation.Landscape,
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