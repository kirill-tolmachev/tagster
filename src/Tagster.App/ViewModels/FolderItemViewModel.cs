using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Tagster.Core;

namespace Tagster.App;

/// <summary>A folder tile in the grid; its thumbnail is filled in asynchronously.</summary>
public sealed partial class FolderItemViewModel(FolderEntry entry) : ObservableObject
{
    public string Name => entry.Name;
    public string FullPath => entry.FullPath;
    public bool IsTagged => entry.IsTagged;
    public IReadOnlyList<string> Tags => entry.Tags;
    public string TagsTooltip => entry.Tags.Count > 0 ? string.Join(", ", entry.Tags) : entry.Name;

    [ObservableProperty]
    private ImageSource? _thumbnail;
}
