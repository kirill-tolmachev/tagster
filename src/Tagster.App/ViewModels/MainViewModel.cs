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
            if (query.Length == 0 || TagNormalizer.Normalize(tag.Name).Contains(query, StringComparison.Ordinal))
                VisibleTags.Add(tag);
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
        UpdateActiveFilters();
    }

    private async Task ApplyFiltersAsync()
    {
        var include = Tags.Where(t => t.State == TagFilterState.Include).Select(t => t.Name).ToList();
        var exclude = Tags.Where(t => t.State == TagFilterState.Exclude).Select(t => t.Name).ToList();

        if (include.Count == 0 && exclude.Count == 0)
        {
            IsSearchMode = false;
            await LoadCurrentAsync(restoreScroll: false);
            return;
        }

        IsSearchMode = true;
        var query = new SearchQuery { Include = include, Exclude = exclude, IncludeMatch = TagMatch.All };
        var results = await _index.SearchAsync(query, RootPath);
        ReplaceItems(results
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new FolderItemViewModel(r.Name, r.AbsolutePath, r.Tags)));
        StatusText = $"{Items.Count} result{(Items.Count == 1 ? "" : "s")} · {include.Count} include / {exclude.Count} exclude";
    }

    // ---- tag editing on the selected folder ----

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task AddTag()
    {
        if (SelectedItem is null) return;
        var tag = NewTagText.Trim();
        if (tag.Length == 0) return;

        var root = ResolveRoot(SelectedItem.FullPath);
        await _tagging.AddTagsAsync(root, SelectedItem.FullPath, [tag]);
        NewTagText = "";
        SelectedItem.Tags = _tagging.GetTags(SelectedItem.FullPath);
        await AfterTagEditAsync();
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
        await RefreshTagsAsync();
        if (IsSearchMode) await ApplyFiltersAsync();
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

    [RelayCommand(CanExecute = nameof(HasSelection))]
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
        var previous = Tags.ToDictionary(t => t.Name, t => t.State, StringComparer.Ordinal);
        Tags.Clear();
        foreach (var count in counts)
        {
            var tag = new TagFilterViewModel(count.Name, count.Count);
            if (previous.TryGetValue(count.Name, out var state))
                tag.State = state;
            Tags.Add(tag);
        }
        RefreshVisibleTags();
        UpdateActiveFilters();
    }

    // ---- loading / thumbnails ----

    private async Task LoadCurrentAsync(bool restoreScroll)
    {
        var entry = _history.Current;
        if (entry is null) return;

        IsLoading = true;
        try
        {
            CurrentPath = entry.Path;
            BuildBreadcrumbs(entry.Path);

            var folders = await Task.Run(() => _browser.ListFolders(entry.Path));
            ReplaceItems(folders.Select(f => new FolderItemViewModel(f)));
            StatusText = Items.Count == 1 ? "1 folder" : $"{Items.Count} folders";
            NotifyNavigationState();

            if (restoreScroll)
                RestoreScrollRequested?.Invoke(entry.ScrollOffset);
        }
        finally
        {
            IsLoading = false;
        }
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
        AddTagCommand.NotifyCanExecuteChanged();
        RemoveCoverCommand.NotifyCanExecuteChanged();
    }
}
