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
    private readonly IFileOperationService _fileOps;
    private readonly SettingsStore _settingsStore;
    private readonly ILogger<MainViewModel> _log;
    private readonly NavigationHistory _history = new();
    private readonly SynchronizationContext _uiContext;

    private CancellationTokenSource? _thumbnailCts;

    /// <summary>
    /// The current multi-selection in the grid. File operations (copy/cut/delete) act on this whole
    /// set, while <see cref="SelectedItem"/> stays the single "primary" that drives the inspector and
    /// tag editing. Maintained by the view through <see cref="UpdateSelection"/> because
    /// <c>ListBox.SelectedItems</c> isn't bindable.
    /// </summary>
    private IReadOnlyList<FolderItemViewModel> _selection = [];

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
        IFileOperationService fileOps,
        SettingsStore settingsStore,
        ILogger<MainViewModel> logger)
    {
        _browser = browser;
        _thumbnails = thumbnails;
        _index = index;
        _tagging = tagging;
        _scanner = scanner;
        _tagManager = tagManager;
        _covers = covers;
        _fileOps = fileOps;
        _settingsStore = settingsStore;
        _log = logger;
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _tagSort = _settingsStore.Load().TagSort;
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
    [ObservableProperty] private TagSortMode _tagSort;
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

    /// <summary>The whole grid selection; file operations act on this (see <see cref="_selection"/>).</summary>
    public IReadOnlyList<FolderItemViewModel> Selection => _selection;
    public int SelectionCount => _selection.Count;

    /// <summary>Rename targets exactly one item (copy / cut / delete act on the whole selection).</summary>
    public bool CanRenameSelection => _selection.Count == 1;
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

    /// <summary>Persist the chosen ordering and re-sort the panel.</summary>
    partial void OnTagSortChanged(TagSortMode value)
    {
        var settings = _settingsStore.Load();
        settings.TagSort = value;
        _settingsStore.Save(settings);
        RefreshVisibleTags();
    }

    private void RefreshVisibleTags()
    {
        var query = TagNormalizer.Normalize(TagFilterText);
        var matches = new List<TagFilterViewModel>();
        foreach (var tag in Tags)
        {
            var norm = TagNormalizer.Normalize(tag.Name);
            // Layout-tolerant: a query typed in the wrong keyboard layout still finds its tag.
            if (!KeyboardLayout.MatchesEitherLayout(norm, query))
                continue;
            // Hide tags that don't co-occur with the current filter — but always keep active ones
            // visible so they can still be toggled off.
            if (_availableTagNorms is not null && tag.State == TagFilterState.None && !_availableTagNorms.Contains(norm))
                continue;
            matches.Add(tag);
        }

        SortForPanel(matches);

        VisibleTags.Clear();
        foreach (var tag in matches)
            VisibleTags.Add(tag);
    }

    /// <summary>
    /// Order the panel by the user's chosen mode: alphabetical (culture-aware, the same order as the
    /// folder list) or most-used-first by archive-wide count, with the name as the tie-break.
    /// </summary>
    private void SortForPanel(List<TagFilterViewModel> tags)
    {
        if (TagSort == TagSortMode.Count)
            tags.Sort(static (a, b) =>
            {
                var byUsage = b.GlobalCount.CompareTo(a.GlobalCount);
                return byUsage != 0 ? byUsage : FolderNameComparer.Default.Compare(a.Name, b.Name);
            });
        else
            tags.Sort(static (a, b) => FolderNameComparer.Default.Compare(a.Name, b.Name));
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

    // ---- file operations (rename / delete / new folder) ----

    /// <summary>Supplied by the view: the owner window handle the shell dialogs are parented to.</summary>
    public Func<nint>? OwnerWindowProvider { get; set; }

    private nint OwnerWindow => OwnerWindowProvider?.Invoke() ?? 0;

    private IReadOnlyList<string> SelectionPaths => [.. _selection.Select(i => i.FullPath)];

    /// <summary>Rename a folder/file in place through the shell (undo-able, with conflict prompts).</summary>
    public async Task RenameAsync(FolderItemViewModel item, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || string.Equals(newName, item.Name, StringComparison.Ordinal)) return;

        string? newPath;
        try
        {
            newPath = _fileOps.Rename(item.FullPath, newName, OwnerWindow);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Rename failed for {Path}", item.FullPath);
            StatusText = $"Couldn't rename: {ex.Message}";
            return;
        }
        if (newPath is null) return; // cancelled

        await AfterStructuralChangeAsync(mayAffectIndex: item.IsFolder);
        SelectByPath(newPath);
        StatusText = $"Renamed to {Path.GetFileName(newPath)}";
    }

    /// <summary>Send the whole current selection to the Recycle Bin.</summary>
    public async Task DeleteSelectionAsync()
    {
        var paths = SelectionPaths;
        if (paths.Count == 0) return;
        var touchedFolder = _selection.Any(i => i.IsFolder);

        FileOpResult result;
        try
        {
            result = _fileOps.Delete(paths, OwnerWindow);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Delete failed");
            StatusText = $"Couldn't delete: {ex.Message}";
            return;
        }
        if (result == FileOpResult.NothingDone) return; // nothing recycled

        // A partly-finished delete may still have recycled some items, so reconcile regardless.
        await AfterStructuralChangeAsync(mayAffectIndex: touchedFolder);
        StatusText = result == FileOpResult.Completed
            ? $"{paths.Count} item{(paths.Count == 1 ? "" : "s")} deleted"
            : "Delete didn't finish — view refreshed";
    }

    /// <summary>Create a folder in the current directory, select it, and return it for inline renaming.</summary>
    public async Task<FolderItemViewModel?> NewFolderAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath) || !Directory.Exists(CurrentPath)) return null;

        string created;
        try
        {
            created = _fileOps.CreateFolder(CurrentPath);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Create folder failed in {Path}", CurrentPath);
            StatusText = $"Couldn't create folder: {ex.Message}";
            return null;
        }

        // A brand-new empty folder has no sidecar, so it can't affect the index — just reload.
        await AfterStructuralChangeAsync(mayAffectIndex: false);
        SelectByPath(created);
        StatusText = "Folder created";
        return SelectedItem;
    }

    /// <summary>Tiles currently shown dimmed because they're on the clipboard as a cut.</summary>
    private List<FolderItemViewModel> _cutItems = [];

    /// <summary>Put the selection on the clipboard as a copy.</summary>
    public void CopySelection()
    {
        var paths = SelectionPaths;
        if (paths.Count == 0) return;
        ClipboardFiles.Set(paths, cut: false);
        MarkCut(null); // copying clears any prior cut dim
        StatusText = $"{paths.Count} item{(paths.Count == 1 ? "" : "s")} copied";
    }

    /// <summary>Put the selection on the clipboard as a cut (the paste will move, not copy).</summary>
    public void CutSelection()
    {
        if (_selection.Count == 0) return;
        ClipboardFiles.Set(SelectionPaths, cut: true);
        MarkCut(_selection);
        StatusText = $"{_selection.Count} item{(_selection.Count == 1 ? "" : "s")} cut";
    }

    /// <summary>Dim the given tiles as cut (and un-dim whatever was previously cut).</summary>
    private void MarkCut(IReadOnlyList<FolderItemViewModel>? items)
    {
        foreach (var item in _cutItems) item.IsCut = false;
        _cutItems = items is null ? [] : [.. items];
        foreach (var item in _cutItems) item.IsCut = true;
    }

    /// <summary>Paste the clipboard's files into the current folder, moving them if they were cut.</summary>
    public async Task PasteAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath) || !Directory.Exists(CurrentPath)) return;
        if (!ClipboardFiles.TryGet(out var paths, out var isMove) || paths.Count == 0) return;

        // Determined before the op: a move erases the source paths, so we can't probe them afterwards.
        var touchedFolder = paths.Any(Directory.Exists);

        FileOpResult result;
        try
        {
            result = isMove
                ? _fileOps.Move(paths, CurrentPath, OwnerWindow)
                : _fileOps.Copy(paths, CurrentPath, OwnerWindow);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Paste into {Path} failed", CurrentPath);
            StatusText = $"Couldn't paste: {ex.Message}";
            return;
        }
        if (result == FileOpResult.NothingDone) return; // nothing pasted

        if (isMove)
        {
            ClipboardFiles.Clear(); // a cut is consumed by its paste, like Explorer
            MarkCut(null);
        }

        // A partial paste still moved/copied some items, so reconcile even when it didn't fully finish.
        await AfterStructuralChangeAsync(mayAffectIndex: touchedFolder);
        StatusText = result == FileOpResult.Completed
            ? $"{paths.Count} item{(paths.Count == 1 ? "" : "s")} pasted"
            : "Paste didn't finish — view refreshed";
    }

    /// <summary>
    /// Handle a drag-drop onto the grid: move (default) or copy (Ctrl) the dropped paths into
    /// <paramref name="destinationFolder"/>. Invalid drops — a folder onto itself or a descendant, or a
    /// no-op move into its own parent — are filtered out first.
    /// </summary>
    public async Task DropAsync(IReadOnlyList<string> paths, string destinationFolder, bool copy)
    {
        if (string.IsNullOrEmpty(destinationFolder) || !Directory.Exists(destinationFolder)) return;

        var valid = paths.Where(p => !string.IsNullOrWhiteSpace(p) && IsValidDrop(p, destinationFolder, copy)).ToList();
        if (valid.Count == 0) return;

        var touchedFolder = valid.Any(Directory.Exists);
        FileOpResult result;
        try
        {
            result = copy
                ? _fileOps.Copy(valid, destinationFolder, OwnerWindow)
                : _fileOps.Move(valid, destinationFolder, OwnerWindow);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Drop into {Path} failed", destinationFolder);
            StatusText = $"Couldn't {(copy ? "copy" : "move")}: {ex.Message}";
            return;
        }
        if (result == FileOpResult.NothingDone) return; // nothing dropped

        // A partial drop still moved/copied some items, so reconcile even when it didn't fully finish.
        await AfterStructuralChangeAsync(mayAffectIndex: touchedFolder);
        StatusText = result == FileOpResult.Completed
            ? $"{valid.Count} item{(valid.Count == 1 ? "" : "s")} {(copy ? "copied" : "moved")}"
            : $"{(copy ? "Copy" : "Move")} didn't finish — view refreshed";
    }

    private static bool IsValidDrop(string source, string destination, bool copy)
    {
        var src = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dest = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (Directory.Exists(src))
        {
            // Never into itself or any descendant of itself (that would be a cycle).
            if (string.Equals(src, dest, StringComparison.OrdinalIgnoreCase)) return false;
            if (dest.StartsWith(src + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        }
        // A move into the folder it already lives in is a no-op (a copy there is a legitimate duplicate).
        if (!copy && string.Equals(Path.GetDirectoryName(src), dest, StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    /// <summary>
    /// Reconcile after a structural change: a folder op under an open archive needs a rescan (it may
    /// have moved tagged subfolders or duplicated a GUID, which only the scanner resolves); anything
    /// else just reloads the current view.
    /// </summary>
    private Task AfterStructuralChangeAsync(bool mayAffectIndex)
        => mayAffectIndex && RootPath is not null
            ? RescanAndRefreshTagsAsync()
            : RefreshViewAsync();

    private void SelectByPath(string fullPath)
        => SelectedItem = Items.FirstOrDefault(
            i => string.Equals(i.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));

    private static string? NearestExistingAncestor(string path)
    {
        for (var dir = Directory.GetParent(path); dir is not null; dir = dir.Parent)
            if (dir.Exists) return dir.FullName;
        return null;
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

        // The current folder may have just been moved/deleted out from under us — re-home to the
        // nearest surviving ancestor instead of loading a path that no longer exists.
        if (!Directory.Exists(entry.Path) && NearestExistingAncestor(entry.Path) is { } fallback)
        {
            await NavigateToAsync(fallback);
            return;
        }

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

    // ---- multi-selection (file operations act on the whole set) ----

    /// <summary>
    /// Called by the view whenever the grid selection changes — the canonical source of the
    /// multi-selection, since <c>ListBox.SelectedItems</c> can't be data-bound. We snapshot it so the
    /// set stays stable even as the live collection mutates.
    /// </summary>
    public void UpdateSelection(IEnumerable<FolderItemViewModel> items)
    {
        _selection = [.. items];
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(CanRenameSelection));
    }
}
