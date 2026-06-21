using CommunityToolkit.Mvvm.ComponentModel;

namespace Tagster.App;

/// <summary>Whether a tag is being used to include, exclude, or neither in the current search.</summary>
public enum TagFilterState
{
    None,
    Include,
    Exclude,
}

/// <summary>A tag in the filter panel, with its usage count and current include/exclude state.</summary>
public sealed partial class TagFilterViewModel(string name, int count) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>Archive-wide folder count — the baseline shown when no filter is active.</summary>
    public int GlobalCount { get; } = count;

    /// <summary>
    /// Folders carrying this tag in the current view: the archive-wide total normally, or — while a
    /// filter is active — how many of the current results carry it (the faceted "live" count).
    /// </summary>
    [ObservableProperty]
    private int _count = count;

    [ObservableProperty]
    private TagFilterState _state;
}
