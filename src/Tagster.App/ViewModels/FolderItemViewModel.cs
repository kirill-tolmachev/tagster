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

    private IReadOnlyList<string> _tags;

    /// <summary>
    /// The folder's tags, always kept in display order — alphabetical and culture-aware, the same
    /// order as the folder list. Setting re-sorts before storing, so every surface shows them alike.
    /// </summary>
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        set
        {
            if (SetProperty(ref _tags, SortForDisplay(value)))
            {
                OnPropertyChanged(nameof(IsTagged));
                OnPropertyChanged(nameof(TagsTooltip));
            }
        }
    }

    /// <summary>True while this tile is on the clipboard as a cut — the grid dims it, like Explorer.</summary>
    [ObservableProperty] private bool _isCut;

    public FolderItemViewModel(string name, string fullPath, IReadOnlyList<string> tags, bool isFile = false)
    {
        Name = name;
        FullPath = fullPath;
        _tags = SortForDisplay(tags);
        IsFile = isFile;
    }

    public FolderItemViewModel(FolderEntry entry) : this(entry.Name, entry.FullPath, entry.Tags, entry.IsFile) { }

    public bool IsTagged => Tags.Count > 0;
    public string TagsTooltip => Tags.Count > 0 ? string.Join(", ", Tags) : Name;

    private static IReadOnlyList<string> SortForDisplay(IReadOnlyList<string> tags)
    {
        if (tags.Count <= 1) return tags;
        var sorted = tags.ToArray();
        Array.Sort(sorted, FolderNameComparer.Default);
        return sorted;
    }
}
