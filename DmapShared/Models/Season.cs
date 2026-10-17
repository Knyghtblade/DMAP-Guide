using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace DmapShared.Models;

public partial class Season : TreeNode
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private int _number;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _title = "";

    [ObservableProperty] private string _year = "";
    [ObservableProperty] private string _description = "";

    public ObservableCollection<Campaign> Campaigns { get; set; } = new();

    [JsonIgnore]
    public override string DisplayName =>
        string.IsNullOrWhiteSpace(Title) ? $"Season {Number}" : $"Season {Number} · {Title}";

    [JsonIgnore] public override TreeNode? ParentNode => null;
}
