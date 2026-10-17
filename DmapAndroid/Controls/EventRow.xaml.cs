using DmapShared.ViewModels;

namespace DmapAndroid.Controls;

public partial class EventRow : LinkRow
{
    public EventRow() => InitializeComponent();

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is not ScheduleEvent e) return;

        if (e.IsFilming)
        {
            Badge.BackgroundColor = Tome.Arcane;
            BadgeText.Text = "✦ FILMING";
        }
        Opacity = e.IsPast ? 0.55 : 1;
        Row.BackgroundColor = e.IsToday ? Color.FromArgb("#33C9A24A") : Colors.Transparent;
    }
}
