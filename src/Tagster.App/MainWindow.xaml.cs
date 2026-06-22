using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Tagster.Core;

namespace Tagster.App;

public partial class MainWindow
{
    private readonly MainViewModel _viewModel;
    private readonly Func<SettingsWindow> _settingsWindowFactory;

    public MainWindow(MainViewModel viewModel, Func<SettingsWindow> settingsWindowFactory)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsWindowFactory = settingsWindowFactory;
        DataContext = viewModel;

        viewModel.ScrollOffsetProvider = () => FindScrollViewer(FolderGrid)?.VerticalOffset ?? 0d;
        viewModel.OwnerWindowProvider = () => new WindowInteropHelper(this).Handle;
        viewModel.RestoreScrollRequested += OnRestoreScroll;

        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    private async void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderItemViewModel item }) return;
        if (item.IsFolder)
            await _viewModel.NavigateToAsync(item.FullPath);
        else
            LaunchPath(item.FullPath); // open a file in its default app, like Explorer
    }

    /// <summary>Push the grid's multi-selection into the VM (ListBox.SelectedItems isn't bindable).</summary>
    private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        => _viewModel.UpdateSelection(FolderGrid.SelectedItems.Cast<FolderItemViewModel>());

    /// <summary>Right-clicking an unselected tile selects just it, so context actions target it;
    /// right-clicking within an existing multi-selection leaves the set intact (like Explorer).</summary>
    private void OnItemRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { IsSelected: false } container)
        {
            FolderGrid.SelectedItems.Clear();
            container.IsSelected = true;
        }
    }

    private async void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (e.Key)
        {
            case Key.F2:
                e.Handled = true;
                await RenameSelectedAsync();
                break;
            case Key.Delete:
                e.Handled = true;
                await DeleteSelectedAsync();
                break;
            case Key.C when ctrl:
                e.Handled = true;
                _viewModel.CopySelection();
                break;
            case Key.X when ctrl:
                e.Handled = true;
                _viewModel.CutSelection();
                break;
            case Key.V when ctrl:
                e.Handled = true;
                await _viewModel.PasteAsync();
                break;
            case Key.N when ctrl && shift:
                e.Handled = true;
                await NewFolderFlowAsync();
                break;
        }
    }

    private void OnCutClick(object sender, RoutedEventArgs e) => _viewModel.CutSelection();

    private void OnCopyClick(object sender, RoutedEventArgs e) => _viewModel.CopySelection();

    private async void OnPasteClick(object sender, RoutedEventArgs e) => await _viewModel.PasteAsync();

    private async void OnOpenItemClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedItem is not { } item) return;
        if (item.IsFolder) await _viewModel.NavigateToAsync(item.FullPath);
        else LaunchPath(item.FullPath);
    }

    private void OnOpenInExplorerClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedItem is { IsFolder: true } folder)
            LaunchPath(folder.FullPath);
    }

    private void OnOpenParentFolderClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedItem is { IsFile: true } file)
            ShowInExplorer(file.FullPath);
    }

    private async void OnRenameSelectedClick(object sender, RoutedEventArgs e) => await RenameSelectedAsync();

    private async void OnDeleteSelectedClick(object sender, RoutedEventArgs e) => await DeleteSelectedAsync();

    private async void OnNewFolderClick(object sender, RoutedEventArgs e) => await NewFolderFlowAsync();

    private async Task RenameSelectedAsync()
    {
        if (!_viewModel.CanRenameSelection || _viewModel.SelectedItem is not { } item) return;
        var newName = PromptForText(this, "Rename", "New name:", item.Name);
        if (!string.IsNullOrWhiteSpace(newName))
            await _viewModel.RenameAsync(item, newName);
    }

    private async Task DeleteSelectedAsync()
    {
        var count = _viewModel.SelectionCount;
        if (count == 0) return;
        var message = count == 1
            ? $"Send “{_viewModel.SelectedItem?.Name}” to the Recycle Bin?"
            : $"Send {count} items to the Recycle Bin?";
        if (MessageBox.Show(this, message, "Delete", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
            await _viewModel.DeleteSelectionAsync();
    }

    private async Task NewFolderFlowAsync()
    {
        var created = await _viewModel.NewFolderAsync();
        if (created is null) return;
        var newName = PromptForText(this, "New folder", "Folder name:", created.Name);
        if (!string.IsNullOrWhiteSpace(newName) && newName.Trim() != created.Name)
            await _viewModel.RenameAsync(created, newName);
    }

    // ---- drag and drop ----

    private Point _dragStart;
    private bool _maybeDragging;
    private FolderItemViewModel[] _dragSnapshot = [];
    private FolderItemViewModel? _pressedItem;

    private void OnGridPreviewLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedItem = ItemDataUnder(e.OriginalSource as DependencyObject);
        _maybeDragging = _pressedItem is not null; // only a press that lands on a tile can begin a drag
        _dragStart = e.GetPosition(null);
        _dragSnapshot = _viewModel.Selection.ToArray(); // snapshot before this click mutates the selection
    }

    private void OnGridPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_maybeDragging || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _maybeDragging = false;

        // Dragging one tile of an existing multi-selection drags the whole set; otherwise just that tile.
        var set = _pressedItem is not null && _dragSnapshot.Length > 1 && _dragSnapshot.Contains(_pressedItem)
            ? _dragSnapshot
            : _viewModel.Selection;
        var paths = set.Select(i => i.FullPath).ToArray();
        if (paths.Length == 0) return;

        try
        {
            DragDrop.DoDragDrop(FolderGrid, new DataObject(DataFormats.FileDrop, paths),
                DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch (Exception)
        {
            // A cancelled/failed drag must not bring the app down.
        }
    }

    private void OnGridDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? EffectFor(e) : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnGridDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        var destination = FolderPathUnder(e.OriginalSource as DependencyObject) ?? _viewModel.CurrentPath;
        await _viewModel.DropAsync(paths, destination, copy: EffectFor(e) == DragDropEffects.Copy);
    }

    /// <summary>Default to move; hold Ctrl to copy — matching Explorer's drag semantics.</summary>
    private static DragDropEffects EffectFor(DragEventArgs e)
        => (e.KeyStates & DragDropKeyStates.ControlKey) != 0 ? DragDropEffects.Copy : DragDropEffects.Move;

    /// <summary>Walk up from a hit-tested element to the FolderItemViewModel of the tile under it, if any.</summary>
    private static FolderItemViewModel? ItemDataUnder(DependencyObject? source)
    {
        for (var node = source; node is Visual; node = VisualTreeHelper.GetParent(node))
            if (node is ListBoxItem { DataContext: FolderItemViewModel item })
                return item;
        return null;
    }

    /// <summary>Drop target: a folder tile receives into itself; a file tile (or empty space) does not.</summary>
    private static string? FolderPathUnder(DependencyObject? source)
        => ItemDataUnder(source) is { IsFolder: true } folder ? folder.FullPath : null;

    private void OnOpenSelectedClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedItem is { } item)
            LaunchPath(item.FullPath);
    }

    private static void LaunchPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // ignore failures to launch Explorer / the default handler
        }
    }

    /// <summary>Open a file's containing folder in Explorer with the file selected ("show in folder").</summary>
    private static void ShowInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch
        {
            // ignore failures to launch Explorer
        }
    }

    private async void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the archive folder" };
        if (dialog.ShowDialog(this) == true)
            await _viewModel.OpenRootAsync(dialog.FolderName);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var window = _settingsWindowFactory();
        window.Owner = this;
        window.ShowDialog();
    }

    /// <summary>The sort glyph is a plain button, so open its dropdown on left-click (not just right).</summary>
    private void OnTagSortClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } target)
        {
            menu.PlacementTarget = target;
            menu.IsOpen = true;
        }
    }

    /// <summary>Tick the row matching the active sort each time the dropdown opens.</summary>
    private void OnTagSortMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        var current = _viewModel.TagSort.ToString();
        foreach (var item in menu.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag as string == current;
    }

    private void OnTagSortItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<TagSortMode>(tag, out var mode))
            _viewModel.TagSort = mode;
    }

    private async void OnTagIncludeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TagFilterViewModel tag })
            await _viewModel.ToggleTagAsync(tag, exclude: false);
    }

    private async void OnTagExcludeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TagFilterViewModel tag })
            await _viewModel.ToggleTagAsync(tag, exclude: true);
    }

    private async void OnRemoveActiveFilter(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TagFilterViewModel tag })
            await _viewModel.RemoveActiveFilterAsync(tag);
    }

    private async void OnRenameTag(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TagFilterViewModel tag })
        {
            var newName = PromptForText(this, "Rename tag",
                "New name (renaming into an existing tag merges them):", tag.Name);
            if (!string.IsNullOrWhiteSpace(newName) && newName.Trim() != tag.Name)
                await _viewModel.RenameTagAsync(tag, newName.Trim());
        }
    }

    private async void OnDeleteTag(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TagFilterViewModel tag })
        {
            var confirm = MessageBox.Show(this,
                $"Remove the tag “{tag.Name}” from all {tag.GlobalCount} folder(s)?",
                "Delete tag", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.OK)
                await _viewModel.DeleteTagAsync(tag);
        }
    }

    private async void OnRemoveTagChip(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: string tag }) return;

        var folder = _viewModel.SelectedItem?.Name;
        var message = folder is null
            ? $"Remove the tag “{tag}”?"
            : $"Remove the tag “{tag}” from “{folder}”?";
        var confirm = MessageBox.Show(this, message, "Remove tag",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.OK)
            await _viewModel.RemoveTagAsync(tag);
    }

    /// <summary>
    /// Layout-tolerant autocomplete: take over the AutoSuggestBox's built-in filtering so a query
    /// typed in the wrong keyboard layout still surfaces the existing tag (typing "ktc" shows «лес»).
    /// Marking the event handled suppresses the control's default <c>Contains</c> filter; we replace
    /// it with <see cref="KeyboardLayout.MatchesEitherLayout"/>, which also matches the as-typed text.
    /// Only user typing is intercepted — programmatic resets and suggestion picks keep their behavior.
    /// </summary>
    private void OnAddTagTextChanged(
        Wpf.Ui.Controls.AutoSuggestBox sender, Wpf.Ui.Controls.AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != Wpf.Ui.Controls.AutoSuggestionBoxTextChangeReason.UserInput) return;
        args.Handled = true;
        var query = TagNormalizer.Normalize(args.Text);
        sender.ItemsSource = _viewModel.AddTagSuggestions
            .Where(name => KeyboardLayout.MatchesEitherLayout(TagNormalizer.Normalize(name), query))
            .ToList();
    }

    private async void OnAddTagQuerySubmitted(
        Wpf.Ui.Controls.AutoSuggestBox sender, Wpf.Ui.Controls.AutoSuggestBoxQuerySubmittedEventArgs args)
        => await SubmitAddTagAsync(args.QueryText);

    private async void OnAddTagClick(object sender, RoutedEventArgs e)
        => await SubmitAddTagAsync(AddTagBox.Text);

    /// <summary>
    /// Picking a suggestion — clicking it, or arrowing to it and pressing Enter — adds that tag at
    /// once. Every suggestion is by construction an existing tag, so this always takes the silent
    /// reuse path (never the new-tag confirm). The catch: the control raises SuggestionChosen on
    /// every arrow-key highlight too, so we commit only when no list-navigation key is held — that
    /// is what separates a real pick (mouse-up / Enter) from a mere preview as you arrow past items.
    /// </summary>
    private async void OnAddTagSuggestionChosen(
        Wpf.Ui.Controls.AutoSuggestBox sender, Wpf.Ui.Controls.AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (IsListNavigationKeyDown()) return;
        if (args.SelectedItem is not string tag || tag.Trim().Length == 0) return;

        args.Handled = true; // skip the control's text-copy into the box; the add clears it anyway
        await SubmitAddTagAsync(tag);
        FocusAddTagInput(); // keep focus on the input so the next tag can be typed/picked right away
    }

    /// <summary>
    /// Commit the add-tag box: reuse an existing tag silently, but require an explicit confirm to
    /// create a brand-new one — so a typo can't quietly become a near-duplicate tag.
    /// </summary>
    private async Task SubmitAddTagAsync(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) return;

        if (await _viewModel.TryAddExistingTagAsync(value)) return;

        var choice = MessageBox.Show(this,
            $"“{value}” isn’t an existing tag yet. Create it as a new tag?",
            "New tag", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.OK)
            await _viewModel.CreateTagAsync(value);
    }

    /// <summary>
    /// True while a key that merely moves the suggestion highlight is held — used to tell an
    /// arrow-driven preview of a suggestion apart from an explicit pick (a click or Enter). The
    /// suggestion list lives in a popup, so its key events don't route through this window; the
    /// live keyboard state at the moment SuggestionChosen fires is the reliable discriminator.
    /// </summary>
    private static bool IsListNavigationKeyDown() =>
        Keyboard.IsKeyDown(Key.Up) || Keyboard.IsKeyDown(Key.Down)
        || Keyboard.IsKeyDown(Key.PageUp) || Keyboard.IsKeyDown(Key.PageDown)
        || Keyboard.IsKeyDown(Key.Home) || Keyboard.IsKeyDown(Key.End);

    /// <summary>
    /// Return focus to the add-tag text box (the control's inner <c>PART_TextBox</c>) after a pick,
    /// out-prioritizing the focus the suggestion popup grabs as it closes.
    /// </summary>
    private void FocusAddTagInput() => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (AddTagBox.Template?.FindName("PART_TextBox", AddTagBox) is TextBox textBox)
            textBox.Focus();
        else
            AddTagBox.Focus();
    }), DispatcherPriority.Background);

    private async void OnSetCoverClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedItem is null) return;

        var dialog = new OpenFileDialog
        {
            Title = "Choose a cover image",
            InitialDirectory = _viewModel.SelectedItem.FullPath,
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
        };
        if (dialog.ShowDialog(this) == true)
            await _viewModel.SetCoverAsync(dialog.FileName);
    }

    private void OnRestoreScroll(double offset)
        => Dispatcher.BeginInvoke(
            new Action(() => FindScrollViewer(FolderGrid)?.ScrollToVerticalOffset(offset)),
            DispatcherPriority.Background);

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>A tiny modal text-input dialog, built in code to avoid an extra XAML window.</summary>
    private static string? PromptForText(Window owner, string title, string prompt, string initial)
    {
        var label = new TextBlock { Text = prompt, Margin = new Thickness(12, 12, 12, 4), TextWrapping = TextWrapping.Wrap };
        var box = new TextBox { Text = initial, Margin = new Thickness(12, 0, 12, 12), MinWidth = 280 };

        var ok = new Button { Content = "OK", IsDefault = true, Width = 76 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 76, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 12, 12),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var layout = new DockPanel();
        DockPanel.SetDock(label, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(label);
        layout.Children.Add(buttons);
        layout.Children.Add(box);

        var dialog = new Window
        {
            Title = title,
            Content = layout,
            Width = 340,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
        };

        string? result = null;
        ok.Click += (_, _) => { result = box.Text; dialog.DialogResult = true; };
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };

        return dialog.ShowDialog() == true ? result : null;
    }
}
