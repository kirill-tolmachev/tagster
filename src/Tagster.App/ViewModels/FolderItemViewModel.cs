using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Tagster.Core;

namespace Tagster.App;

/// <summary>A folder tile in the grid; its thumbnail and tags update as the user works.</summary>
public sealed partial class FolderItemViewModel : ObservableObject
{
    public string Name { get; }
    public string FullPath { get; }

    /// <summary>True for a file tile, false for a folder tile — drives double-click and the inspector.</summary>
    public bool IsFile { get; }
    public bool IsFolder => !IsFile;

    [ObservableProperty] private ImageSource? _thumbnail;
    [ObservableProperty] private IReadOnlyList<string> _tags;

    public FolderItemViewModel(string name, string fullPath, IReadOnlyList<string> tags, bool isFile = false)
    {
        Name = name;
        FullPath = fullPath;
        _tags = tags;
        IsFile = isFile;
    }

    public FolderItemViewModel(FolderEntry entry) : this(entry.Name, entry.FullPath, entry.Tags, entry.IsFile) { }

    public bool IsTagged => Tags.Count > 0;
    public string TagsTooltip => Tags.Count > 0 ? string.Join(", ", Tags) : Name;

    partial void OnTagsChanged(IReadOnlyList<string> value)
    {
        OnPropertyChanged(nameof(IsTagged));
        OnPropertyChanged(nameof(TagsTooltip));
    }
}
