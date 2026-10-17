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

        return builder.Build();
    }
}