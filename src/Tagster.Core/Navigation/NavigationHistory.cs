namespace Tagster.Core;

/// <summary>One visited location and the scroll position the user left it at.</summary>
public sealed class NavigationEntry(string path)
{
    public string Path { get; } = path;
    public double ScrollOffset { get; set; }
}

/// <summary>
/// Back/forward history that remembers each location's scroll offset, so pressing Back returns to
/// exactly where you were — not the top of the list (the Tag Explorer annoyance we're fixing).
/// </summary>
public sealed class NavigationHistory
{
    private readonly Stack<NavigationEntry> _back = new();
    private readonly Stack<NavigationEntry> _forward = new();

    public NavigationEntry? Current { get; private set; }
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    /// <summary>Navigate to a new location, clearing the forward stack.</summary>
    public NavigationEntry Navigate(string path)
    {
        if (Current is not null) _back.Push(Current);
        _forward.Clear();
        Current = new NavigationEntry(path);
        return Current;
    }

    public NavigationEntry? GoBack()
    {
        if (_back.Count == 0) return null;
        if (Current is not null) _forward.Push(Current);
        Current = _back.Pop();
        return Current;
    }

    public NavigationEntry? GoForward()
    {
        if (_forward.Count == 0) return null;
        if (Current is not null) _back.Push(Current);
        Current = _forward.Pop();
        return Current;
    }

    /// <summary>Record the scroll offset for the current location before leaving it.</summary>
    public void SaveScrollOffset(double offset)
    {
        if (Current is not null) Current.ScrollOffset = offset;
    }
}
