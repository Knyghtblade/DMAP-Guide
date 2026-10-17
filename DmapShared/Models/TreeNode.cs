using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace DmapShared.Models;

/// <summary>Anything shown in the table-of-contents tree.</summary>
public abstract partial class TreeNode : ObservableObject
{
    /// <summary>Permanent identity; also the record's file name in the shared guide folder and its id on the server.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Who last saved this page, and when (UTC).</summary>
    public string LastEditedBy { get; set; } = "";
    public DateTime? LastEdited { get; set; }

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isSelected;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isExpanded;

    [JsonIgnore]
    public abstract string DisplayName { get; }

    [JsonIgnore]
    public abstract TreeNode? ParentNode { get; }
}
