using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Tagster.Core;
using Tagster.Shell;

namespace Tagster.App;

/// <summary>
/// Drives the main window: folder browsing, tag-based search, per-folder tag editing, and tag
/// management — all over the same archive root.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const int ThumbnailSize = 128;
    private const int MaxConcurrentThumbnails = 6;

    private readonly IFolderBrowser _browser;
    private readonly IThumbnailService _thumbnails;
    private readonly IFolderIndex _index;
    private readonly TaggingService _tagging;
    private readonly ArchiveScanner _scanner;
    private readonly ITagManager _tagManager;
    private readonly IFolderCoverService _covers;
    private readonly ILogger<MainViewModel> _log;
    private readonly NavigationHistory _history = new();
    private readonly SynchronizationContext _uiContext;

    private CancellationTokenSource? _thumbnailCts;

    /// <summary>
    /// Monotonic guard shared by the two view-producing async ops (<see cref="LoadCurrentAsync"/>
    /// and <see cref="ApplyFiltersAsync"/>). Each increments it before its await; any operation that
    /// finds the counter has moved on by the time it resumes is stale and bails out, so a slower
    /// earlier call can't overwrite the grid for a path/filter that's no longer active. UI-thread
    /// only, so a plain int is safe.
    /// </summary>
    private int _viewGeneration;

    /// <summary>
    /// Normalized tags present in the current search results. Null when no filter is active (the
    /// panel shows every tag); otherwise it narrows the tag list to those that co-occur with the
    /// current selection, so you only ever see tags that can still combine.
    /// </summary>
    private HashSet<string>? _availableTagNorms;

    public MainViewModel(
        IFolderBrowser browser,
        IThumbnailService thumbnails,
        IFolderIndex index,
        TaggingService tagging,
        ArchiveScanner scanner,
        ITagManager tagManager,
        IFolderCoverService covers,
        ILogger<MainViewModel> logger)
    {
        _browser = browser;
        _thumbnails = thumbnails;
        _index = index;
        _tagging = tagging;
        _scanner = scanner;
        _tagManager = tagManager;
        _covers = covers;
        _log = logger;
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public ObservableCollection<FolderItemViewModel> Items { get; } = [];
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];
    public ObservableCollection<TagFilterViewModel> Tags { get; } = [];
    public ObservableCollection<TagFilterViewModel> VisibleTags { get; } = [];
    public ObservableCollection<TagFilterViewModel> ActiveFilters { get; } = [];

    /// <summary>
    /// Add-tag autocomplete pool: archive tags not already on the selected folder, best-first. The
    /// suggestion box filters this as the user types so they reuse an existing tag instead of
    /// risking a mistyped duplicate. Empty unless a folder is selected.
    /// </summary>
    public ObservableCollection<string> AddTagSuggestions { get; } = [];

    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private string? _rootPath;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSearchMode;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private string _tagFilterText = "";
    [ObservableProperty] private string _newTagText = "";
    [ObservableProperty] private FolderItemViewModel? _selectedItem;

    public bool CanGoBack => _history.CanGoBack;
    public bool CanGoForward => _history.CanGoForward;
    public bool CanGoUp => !string.IsNullOrEmpty(CurrentPath) && Directory.GetParent(CurrentPath) is not null;
    public bool HasRoot => RootPath is not null;
    public bool HasSelection => SelectedItem is not null;
    public bool HasNoSelection => SelectedItem is null;
    public bool SelectedIsFolder => SelectedItem is { IsFolder: true };
    public bool SelectedIsFile => SelectedItem is { IsFile: true };
    public bool IsArchiveOpen => RootPath is not null;
    public bool HasActiveFilters => ActiveFilters.Count > 0;
    public bool ShowOpenArchivePrompt => RootPath is null && !IsLoading;
    public bool ShowNoResults => IsSearchMode && !IsLoading && Items.Count == 0;
    public bool ShowEmptyFolder => !IsSearchMode && RootPath is not null && !IsLoading && Items.Count == 0;

    /// <summary>Set by the view: reads the grid's current vertical scroll offset.</summary>
    public Func<double>? ScrollOffsetProvider { get; set; }

    /// <summary>Raised after a load completes when a saved scroll offset should be restored.</summary>
    public event Action<double>? RestoreScrollRequested;

    /// <summary>Folder to open on launch (from a context-menu click or the remembered archive).</summary>
    public string? StartupFolder { get; set; }

    /// <summary>Whether the startup folder should open ready for tag editing.</summary>
    public bool StartupEdit { get; set; }

    public async Task InitializeAsync()
    {
        if (StartupFolder is not null && Directory.Exists(StartupFolder))
        {
            if (StartupEdit) await OpenForEditAsync(StartupFolder);
            else await OpenRootAsync(StartupFolder);
            return;
        }
        await NavigateToAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    /// <summary>Open a folder's parent as the archive root and select the folder for tag editing.</summary>
    public async Task OpenForEditAsync(string folderPath)
    {
        var full = Path.GetFullPath(folderPath);
        var parent = Directory.GetParent(full)?.FullName ?? full;
        await OpenRootAsync(parent);
        SelectedItem = Items.FirstOrDefault(i => string.Equals(i.FullPath, full, StringComparison.OrdinalIgnoreCase));
    }

    // ---- navigation ----

    public async Task NavigateToAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        SaveCurrentScroll();
        ResetFilterStates();
        IsSearchMode = false;
        _history.Navigate(Path.GetFullPath(path));
        await LoadCurrentAsync(restoreScroll: false);
    }

    /// <summary>Pick a folder as the archive root, browse it, and scan it for tags.</summary>
    public async Task OpenRootAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        RootPath = Path.GetFullPath(path);
        await NavigateToAsync(path);
        await RescanAndRefreshTagsAsync();
    }

    [RelayCommand]
    private Task Open(string path) => NavigateToAsync(path);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task GoBackAsync()
    {
        SaveCurrentScroll();
        if (_history.GoBack() is not null)
            await LoadCurrentAsync(restoreScroll: true);
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private async Task GoForwardAsync()
    {
        SaveCurrentScroll();
        if (_history.GoForward() is not null)
            await LoadCurrentAsync(restoreScroll: true);
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private async Task GoUpAsync()
    {
        var parent = Directory.GetParent(CurrentPath);
        if (parent is not null)
            await NavigateToAsync(parent.FullName);
    }

    // ---- tag filtering / search ----

    partial void OnTagFilterTextChanged(string value) => RefreshVisibleTags();

    private void RefreshVisibleTags()
    {
        var query = TagNormalizer.Normalize(TagFilterText);
        VisibleTags.Clear();
        foreach (var tag in Tags)
        {
            var norm = TagNormalizer.Normalize(tag.Name);
            if (query.Length > 0 && !norm.Contains(query, StringComparison.Ordinal))
                continue;
            // Hide tags that don't co-occur with the current filter — but always keep active ones
            // visible so they can still be toggled off.
            if (_availableTagNorms is not null && tag.State == TagFilterState.None && !_availableTagNorms.Contains(norm))
                continue;
            VisibleTags.Add(tag);
        }
    }

    public async Task ToggleTagAsync(TagFilterViewModel tag, bool exclude)
    {
        tag.State = exclude
            ? (tag.State == TagFilterState.Exclude ? TagFilterState.None : TagFilterState.Exclude)
            : (tag.State == TagFilterState.Include ? TagFilterState.None : TagFilterState.Include);
        UpdateActiveFilters();
        await ApplyFiltersAsync();
    }

    public async Task RemoveActiveFilterAsync(TagFilterViewModel tag)
    {
        tag.State = TagFilterState.None;
        UpdateActiveFilters();
        await ApplyFiltersAsync();
    }

    [RelayCommand]
    private async Task ClearFilters()
    {
        ResetFilterStates();
        IsSearchMode = false;
        await LoadCurrentAsync(restoreScroll: false);
    }

    private void ResetFilterStates()
    {
        foreach (var tag in Tags)
            tag.State = TagFilterState.None;
        ClearTagNarrowing();
        UpdateActiveFilters();
    }

    private async Task ApplyFiltersAsync()
    {
        var include = Tags.Where(t => t.State == TagFilterState.Include).Select(t => t.Name).ToList();
        var exclude = Tags.Where(t => t.State == TagFilterState.Exclude).Select(t => t.Name).ToList();

        if (include.Count == 0 && exclude.Count == 0)
        {
            ClearTagNarrowing();
            IsSearchMode = false;
            await LoadCurrentAsync(restoreScroll: false);
            return;
        }

        IsSearchMode = true;
        var query = new SearchQuery { Include = include, Exclude = exclude, IncludeMatch = TagMatch.All };
        var gen = ++_viewGeneration;
        var results = await _index.SearchAsync(query, RootPath);
        if (gen != _viewGeneration) return;   // superseded by a newer navigation/search
        IsLoading = false;   // release the spinner if this search superseded an in-flight navigation
        ReplaceItems(results
            .OrderBy(r => r.Name, FolderNameComparer.Default)
            .Select(r => new FolderItemViewModel(r.Name, r.AbsolutePath, r.Tags)));
        NarrowTagsTo(results);
        StatusText = $"{Items.Count} result{(Items.Count == 1 ? "" : "s")} · {include.Count} include / {exclude.Count} exclude";
    }

    /// <summary>
    /// Restrict the tag panel to tags that co-occur with the current results and switch each tag's
    /// shown count to how many of those results carry it (faceted live counts).
    /// </summary>
    private void NarrowTagsTo(IReadOnlyList<TaggedFolder> results)
    {
        var counts = FolderQueryEngine.CoOccurringTagCounts(results);
        _availableTagNorms = [.. counts.Keys];
        foreach (var tag in Tags)
            tag.Count = counts.GetValueOrDefault(TagNormalizer.Normalize(tag.Name));
        RefreshVisibleTags();
    }

    /// <summary>Drop the co-occurrence narrowing and restore archive-wide tag counts.</summary>
    private void ClearTagNarrowing()
    {
        if (_availableTagNorms is null) return;
        _availableTagNorms = null;
        foreach (var tag in Tags)
            tag.Count = tag.GlobalCount;
        RefreshVisibleTags();
    }

    // ---- tag editing on the selected folder ----

    /// <summary>
    /// Add a typed tag only if it's already an existing tag (added in its canonical spelling).
    /// Returns false when it isn't — the view then asks the user to confirm creating a new tag, so
    /// a typo can't quietly mint a near-duplicate.
    /// </summary>
    public async Task<bool> TryAddExistingTagAsync(string text)
    {
        if (SelectedItem is not { IsFolder: true }) return false;
        if (TagSuggester.ExactMatch(Tags.Select(t => t.Name), text) is not { } canonical) return false;
        await AddTagValueAsync(canonical);
        return true;
    }

    /// <summary>Deliberately create and apply a brand-new tag (after the user confirms).</summary>
    public Task CreateTagAsync(string text) => AddTagValueAsync(text);

    private async Task AddTagValueAsync(string tag)
    {
        if (SelectedItem is not { IsFolder: true } folder) return;
        var value = tag.Trim();
        if (value.Length == 0) return;

        var root = ResolveRoot(folder.FullPath);
        await _tagging.AddTagsAsync(root, folder.FullPath, [value]);
        NewTagText = "";
        folder.Tags = _tagging.GetTags(folder.FullPath);
        await AfterTagEditAsync();
    }

    /// <summary>Rebuild the add-tag autocomplete pool for the current selection.</summary>
    private void RefreshAddTagSuggestions()
    {
        AddTagSuggestions.Clear();
        if (SelectedItem is not { IsFolder: true } folder) return;
        foreach (var name in TagSuggester.Available(Tags.Select(t => t.Name), folder.Tags))
            AddTagSuggestions.Add(name);
    }

    public async Task RemoveTagAsync(string tag)
    {
        if (SelectedItem is null) return;
        var root = ResolveRoot(SelectedItem.FullPath);
        await _tagging.RemoveTagsAsync(root, SelectedItem.FullPath, [tag]);
        SelectedItem.Tags = _tagging.GetTags(SelectedItem.FullPath);
        await AfterTagEditAsync();
    }

    private async Task AfterTagEditAsync()
    {
        var selectedPath = SelectedItem?.FullPath;
        await RefreshTagsAsync();
        if (IsSearchMode)
        {
            await ApplyFiltersAsync();
            if (selectedPath is not null)
                SelectedItem = Items.FirstOrDefault(
                    i => string.Equals(i.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    private string ResolveRoot(string folderPath)
        => RootPath is not null && PathUtil.IsUnderRoot(RootPath, folderPath)
            ? RootPath
            : Directory.GetParent(folderPath)?.FullName ?? folderPath;

    // ---- folder covers ----

    /// <summary>Generate and apply a cover for the selected folder from the given image.</summary>
    public async Task SetCoverAsync(string imagePath)
    {
        var item = SelectedItem;
        if (item is null) return;

        IsLoading = true;
        try
        {
            var source = await Task.Run(() => _covers.SetCover(item.FullPath, imagePath));
            _tagging.SetCover(item.FullPath, source);
            _thumbnails.Invalidate(item.FullPath);
            await ReloadThumbnailAsync(item);
            StatusText = $"Cover set for {item.Name}";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to set cover for {Folder}", item.FullPath);
            StatusText = $"Couldn't set cover: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(SelectedIsFolder))]
    private async Task RemoveCover()
    {
        var item = SelectedItem;
        if (item is null) return;

        try
        {
            await Task.Run(() => _covers.RemoveCover(item.FullPath));
            _tagging.RemoveCover(item.FullPath);
            _thumbnails.Invalidate(item.FullPath);
            await ReloadThumbnailAsync(item);
            StatusText = $"Cover removed for {item.Name}";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to remove cover for {Folder}", item.FullPath);
            StatusText = $"Couldn't remove cover: {ex.Message}";
        }
    }

    private async Task ReloadThumbnailAsync(FolderItemViewModel item)
    {
        item.Thumbnail = null;
        var image = await _thumbnails.GetThumbnailAsync(item.FullPath, ThumbnailSize);
        if (image is not null)
            item.Thumbnail = image;
    }

    // ---- tag management (rename / delete) ----

    public async Task RenameTagAsync(TagFilterViewModel tag, string newName)
    {
        await _tagManager.RenameAsync(RootPath ?? CurrentPath, tag.Name, newName);
        await RefreshTagsAsync();
        await RefreshViewAsync();
    }

    public async Task DeleteTagAsync(TagFilterViewModel tag)
    {
        await _tagManager.DeleteAsync(RootPath ?? CurrentPath, tag.Name);
        await RefreshTagsAsync();
        await RefreshViewAsync();
    }

    private Task RefreshViewAsync()
        => IsSearchMode ? ApplyFiltersAsync() : LoadCurrentAsync(restoreScroll: false);

    // ---- scanning / tag list ----

    [RelayCommand(CanExecute = nameof(HasRoot))]
    private Task Rescan() => RescanAndRefreshTagsAsync();

    public async Task RescanAndRefreshTagsAsync()
    {
        if (RootPath is null) return;
        var root = RootPath;
        IsLoading = true;
        StatusText = "Scanning for tags…";
        try
        {
            // Run the directory walk off the UI thread so a large archive (or a drive root)
            // can't freeze the window.
            await Task.Run(() => _scanner.RescanAsync(root));
            await RefreshTagsAsync();
            await RefreshViewAsync();
            StatusText = $"{Tags.Count} tag{(Tags.Count == 1 ? "" : "s")} in archive";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshTagsAsync()
    {
        var counts = await _index.GetTagCountsAsync(RootPath);
        var previous = Tags.ToDictionary(t => TagNormalizer.Normalize(t.Name), t => t.State, StringComparer.Ordinal);
        Tags.Clear();
        foreach (var count in counts)
        {
            var tag = new TagFilterViewModel(count.Name, count.Count);
            if (previous.TryGetValue(TagNormalizer.Normalize(count.Name), out var state))
                tag.State = state;
            Tags.Add(tag);
        }
        RefreshVisibleTags();
        UpdateActiveFilters();
        RefreshAddTagSuggestions();
    }

    // ---- loading / thumbnails ----

    private async Task LoadCurrentAsync(bool restoreScroll)
    {
        var entry = _history.Current;
        if (entry is null) return;

        var gen = ++_viewGeneration;
        IsLoading = true;
        try
        {
            CurrentPath = entry.Path;
            BuildBreadcrumbs(entry.Path);

            var entries = await Task.Run(() => _browser.ListEntries(entry.Path));
            if (gen != _viewGeneration) return;   // superseded by a newer navigation/search
            ReplaceItems(entries.Select(f => new FolderItemViewModel(f)));
            var fileCount = Items.Count(i => i.IsFile);
            StatusText = DescribeContents(Items.Count - fileCount, fileCount);
            NotifyNavigationState();

            if (restoreScroll)
                RestoreScrollRequested?.Invoke(entry.ScrollOffset);
        }
        finally
        {
            if (gen == _viewGeneration) IsLoading = false;
        }
    }

    /// <summary>Explorer-style "N folders · M files" summary for the status bar.</summary>
    private static string DescribeContents(int folders, int files)
    {
        var parts = new List<string>(2);
        if (folders > 0) parts.Add($"{folders} folder{(folders == 1 ? "" : "s")}");
        if (files > 0) parts.Add($"{files} file{(files == 1 ? "" : "s")}");
        return parts.Count > 0 ? string.Join(" · ", parts) : "Empty folder";
    }

    private void ReplaceItems(IEnumerable<FolderItemViewModel> items)
    {
        CancelThumbnailLoads();
        SelectedItem = null;
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        NotifyEmptyStates();
        _ = LoadThumbnailsAsync([.. Items]);
    }

    private async Task LoadThumbnailsAsync(IReadOnlyList<FolderItemViewModel> items)
    {
        _thumbnailCts = new CancellationTokenSource();
        var token = _thumbnailCts.Token;
        using var gate = new SemaphoreSlim(MaxConcurrentThumbnails);

        var tasks = items.Select(async item =>
        {
            try
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var image = await _thumbnails.GetThumbnailAsync(item.FullPath, ThumbnailSize, token).ConfigureAwait(false);
                    if (image is not null && !token.IsCancellationRequested)
                        _uiContext.Post(_ => item.Thumbnail = image, null);
                }
                finally
                {
                    gate.Release();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log.LogDebug(ex, "Thumbnail load failed for {Path}", item.FullPath); }
        });

        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
    }

    private void CancelThumbnailLoads()
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;
    }

    private void SaveCurrentScroll()
    {
        if (ScrollOffsetProvider is not null)
            _history.SaveScrollOffset(ScrollOffsetProvider());
    }

    private void BuildBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        var chain = new Stack<DirectoryInfo>();
        for (var dir = new DirectoryInfo(path); dir is not null; dir = dir.Parent)
            chain.Push(dir);
        foreach (var dir in chain)
            Breadcrumbs.Add(new BreadcrumbSegment(dir.Name.Length > 0 ? dir.Name : dir.FullName, dir.FullName));
    }

    private void NotifyNavigationState()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        GoUpCommand.NotifyCanExecuteChanged();
    }

    partial void OnRootPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasRoot));
        OnPropertyChanged(nameof(IsArchiveOpen));
        RescanCommand.NotifyCanExecuteChanged();
        NotifyEmptyStates();
    }

    partial void OnIsLoadingChanged(bool value) => NotifyEmptyStates();

    partial void OnIsSearchModeChanged(bool value) => NotifyEmptyStates();

    private void NotifyEmptyStates()
    {
        OnPropertyChanged(nameof(ShowOpenArchivePrompt));
        OnPropertyChanged(nameof(ShowNoResults));
        OnPropertyChanged(nameof(ShowEmptyFolder));
    }

    private void UpdateActiveFilters()
    {
        ActiveFilters.Clear();
        foreach (var tag in Tags)
            if (tag.State != TagFilterState.None)
                ActiveFilters.Add(tag);
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    partial void OnSelectedItemChanged(FolderItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(SelectedIsFolder));
        OnPropertyChanged(nameof(SelectedIsFile));
        RemoveCoverCommand.NotifyCanExecuteChanged();
        NewTagText = "";
        RefreshAddTagSuggestions();
    }
}
