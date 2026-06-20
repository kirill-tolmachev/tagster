using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tagster.Shell;

namespace Tagster.App;

public partial class App : Application
{
    private IHost? _host;
    private SingleInstanceManager? _singleInstance;
    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Directory.CreateDirectory(AppPaths.DataDirectory);

        var diagnostic = e.Args.Contains("--selftest")
            || e.Args.Contains("--cover-test")
            || e.Args.Contains("--integration-test")
            || e.Args.Contains("--make-icon")
            || e.Args.Contains("--unregister");

        // Single instance: hand off to the already-running window if there is one.
        if (!diagnostic)
        {
            _singleInstance = new SingleInstanceManager();
            if (!_singleInstance.TryAcquire())
            {
                _singleInstance.SignalFirstInstance(e.Args);
                Shutdown(0);
                return;
            }
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTagsterCore();
        builder.Services.AddTagsterSqliteIndex(AppPaths.IndexDatabasePath);
        builder.Services.AddTagsterShell();
        builder.Services.AddSingleton<SettingsStore>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<SettingsWindow>();
        builder.Services.AddSingleton<Func<SettingsWindow>>(sp => () => sp.GetRequiredService<SettingsWindow>());
        _host = builder.Build();
        await _host.StartAsync();

        if (e.Args.Contains("--cover-test"))
        {
            var report = CoverSelfTest.Run(_host.Services.GetRequiredService<IFolderCoverService>());
            Console.WriteLine(report.Message);
            _ = Dispatcher.BeginInvoke(new Action(() => Shutdown(report.Ok ? 0 : 1)), DispatcherPriority.ApplicationIdle);
            return;
        }

        if (e.Args.Contains("--integration-test"))
        {
            var report = IntegrationSelfTest.Run(_host.Services.GetRequiredService<IExplorerIntegration>());
            Console.WriteLine(report.Message);
            _ = Dispatcher.BeginInvoke(new Action(() => Shutdown(report.Ok ? 0 : 1)), DispatcherPriority.ApplicationIdle);
            return;
        }

        if (e.Args.Contains("--make-icon"))
        {
            var index = Array.IndexOf(e.Args, "--make-icon");
            var path = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : "Tagster.ico";
            IconFactory.Write(path);
            Console.WriteLine("Icon written to " + path);
            _ = Dispatcher.BeginInvoke(new Action(() => Shutdown(0)), DispatcherPriority.ApplicationIdle);
            return;
        }

        if (e.Args.Contains("--unregister"))
        {
            // Used by the uninstaller to remove the per-user context-menu entries.
            try { _host.Services.GetRequiredService<IExplorerIntegration>().Unregister(); }
            catch { /* best effort */ }
            _ = Dispatcher.BeginInvoke(new Action(() => Shutdown(0)), DispatcherPriority.ApplicationIdle);
            return;
        }

        var settingsStore = _host.Services.GetRequiredService<SettingsStore>();
        var settings = settingsStore.Load();
        var viewModel = _host.Services.GetRequiredService<MainViewModel>();

        var (folder, edit) = CommandLine.Parse(e.Args);
        if (folder is not null)
        {
            viewModel.StartupFolder = folder;
            viewModel.StartupEdit = edit;
        }
        else if (settings.ReopenLastArchive && settings.LastArchivePath is not null && Directory.Exists(settings.LastArchivePath))
        {
            viewModel.StartupFolder = settings.LastArchivePath;
        }

        // Remember the archive whenever it changes.
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.RootPath) && viewModel.RootPath is not null)
            {
                var current = settingsStore.Load();
                current.LastArchivePath = viewModel.RootPath;
                settingsStore.Save(current);
            }
        };

        // Constructing the window parses all XAML and resolves the DI graph.
        _mainWindow = _host.Services.GetRequiredService<MainWindow>();

        if (e.Args.Contains("--selftest"))
        {
            _ = _host.Services.GetRequiredService<SettingsWindow>(); // validate its XAML too
            _ = Dispatcher.BeginInvoke(new Action(() => Shutdown(0)), DispatcherPriority.ApplicationIdle);
            return;
        }

        _singleInstance?.StartServer(args => Dispatcher.Invoke(() => OnActivated(args)));
        _mainWindow.Show();
    }

    private async void OnActivated(string[] args)
    {
        if (_host is null || _mainWindow is null) return;

        var (folder, edit) = CommandLine.Parse(args);
        if (folder is null) return;

        var viewModel = _host.Services.GetRequiredService<MainViewModel>();
        if (edit) await viewModel.OpenForEditAsync(folder);
        else await viewModel.OpenRootAsync(folder);

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
