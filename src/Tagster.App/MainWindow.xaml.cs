using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Tagster.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
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

    private void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
            _viewModel.OpenCommand.Execute(box.Text);
    }

    private async void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select a folder to browse" };
        if (dialog.ShowDialog(this) == true)
            await _viewModel.NavigateToAsync(dialog.FolderName);
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
}
