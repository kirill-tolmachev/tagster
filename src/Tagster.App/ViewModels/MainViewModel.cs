using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tagster.Core;
using Tagster.Shell;

namespace Tagster.App;

/// <summary>Drives the browser window: folder listing, navigation, and async thumbnail loading.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const int ThumbnailSize = 128;
    private const int MaxConcurrentThumbnails = 6;

    private readonly IFolderBrowser _browser;
    private readonly IThumbnailService _thumbnails;
    private readonly NavigationHistory _history = new();
    private readonly SynchronizationContext _uiContext;

    private CancellationTokenSource? _thumbnailCts;

    public MainViewModel(IFolderBrowser browser, IThumbnailService thumbnails)
    {
        _browser = browser;
        _thumbnails = thumbnails;
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public ObservableCollection<FolderItemViewModel> Items { get; } = [];
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _statusText;

    public bool CanGoBack => _history.CanGoBack;
    public bool CanGoForward => _history.CanGoForward;
    public bool CanGoUp => !string.IsNullOrEmpty(CurrentPath) && Directory.GetParent(CurrentPath) is not null;

    /// <summary>Set by the view: reads the grid's current vertical scroll offset.</summary>
    public Func<double>? ScrollOffsetProvider { get; set; }

    /// <summary>Raised after a load completes when a saved scroll offset should be restored.</summary>
    public event Action<double>? RestoreScrollRequested;

    /// <summary>Open the initial location (the user's profile folder).</summary>
    public Task InitializeAsync()
        => NavigateToAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public async Task NavigateToAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        SaveCurrentScroll();
        _history.Navigate(Path.GetFullPath(path));
        await LoadCurrentAsync(restoreScroll: false);
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

    private void SaveCurrentScroll()
    {
        if (ScrollOffsetProvider is not null)
            _history.SaveScrollOffset(ScrollOffsetProvider());
    }

    private async Task LoadCurrentAsync(bool restoreScroll)
    {
        var entry = _history.Current;
        if (entry is null) return;

        IsLoading = true;
        CancelThumbnailLoads();
        try
        {
            CurrentPath = entry.Path;
            BuildBreadcrumbs(entry.Path);

            var folders = await Task.Run(() => _browser.ListFolders(entry.Path));

            Items.Clear();
            foreach (var folder in folders)
                Items.Add(new FolderItemViewModel(folder));

            StatusText = Items.Count == 1 ? "1 folder" : $"{Items.Count} folders";
            NotifyNavigationState();

            if (restoreScroll)
                RestoreScrollRequested?.Invoke(entry.ScrollOffset);

            _ = LoadThumbnailsAsync([.. Items]);
        }
        finally
        {
            IsLoading = false;
        }
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
                    var image = await _thumbnails.GetThumbnailAsync(item.FullPath, ThumbnailSize, token)
                        .ConfigureAwait(false);
                    if (image is not null && !token.IsCancellationRequested)
                        _uiContext.Post(_ => item.Thumbnail = image, null);
                }
                finally
                {
                    gate.Release();
                }
            }
            catch (OperationCanceledException) { /* navigation moved on */ }
            catch { /* ignore per-item thumbnail failures */ }
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
}
