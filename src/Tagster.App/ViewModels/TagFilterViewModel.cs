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
    public int Count { get; } = count;

    [ObservableProperty]
    private TagFilterState _state;
}
