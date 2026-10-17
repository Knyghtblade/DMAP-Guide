using Microsoft.Maui.Handlers;

namespace DmapAndroid;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.ConfigureFonts(fonts =>
        {
            fonts.AddFont("CinzelDecorative-Bold.ttf", "DmapDisplay");
            fonts.AddFont("Cinzel.ttf", "DmapHeading");
            fonts.AddFont("EBGaramond.ttf", "DmapBody");
            fonts.AddFont("EBGaramond-Italic.ttf", "DmapBodyItalic");
        });

#if ANDROID
        // Text on the page should read like writing, not a form: no Android underline.
        // (Edit mode is shown by the field's light background, as on the desktop.)
        static Android.Graphics.Drawables.Drawable Plain(IView view) => new Android.Graphics.Drawables.ColorDrawable(
            (view as VisualElement)?.BackgroundColor is Color color
                ? Android.Graphics.Color.Argb((int)(color.Alpha * 255), (int)(color.Red * 255), (int)(color.Green * 255), (int)(color.Blue * 255))
                : Android.Graphics.Color.Transparent);
        EntryHandler.Mapper.AppendToMapping(nameof(IView.Background), (handler, view) => handler.PlatformView.Background = Plain(view));
        EditorHandler.Mapper.AppendToMapping(nameof(IView.Background), (handler, view) => handler.PlatformView.Background = Plain(view));
#endif

        return builder.Build();
    }
}
