using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

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
        viewModel.RestoreScrollRequested += OnRestoreScroll;

        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    private async void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderItemViewModel item })
            await _viewModel.NavigateToAsync(item.FullPath);
    }

    private void OnOpenSelectedClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedItem is not { } item) return;
        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
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
                $"Remove the tag “{tag.Name}” from all {tag.Count} folder(s)?",
                "Delete tag", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.OK)
                await _viewModel.DeleteTagAsync(tag);
        }
    }

    private async void OnRemoveTagChip(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string tag })
            await _viewModel.RemoveTagAsync(tag);
    }

    private void OnNewTagKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.AddTagCommand.CanExecute(null))
            _viewModel.AddTagCommand.Execute(null);
    }

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
