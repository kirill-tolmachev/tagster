using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Tagster.Shell;
using Wpf.Ui.Appearance;

namespace Tagster.App;

public partial class App : Application
{
    private IHost? _host;
    private SingleInstanceManager? _singleInstance;
    private MainWindow? _mainWindow;
    private bool _diagnosticMode;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.CreateDirectory(AppPaths.DataDirectory);
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        ConfigureLogging();
        _diagnosticMode = e.Args.Contains("--selftest")
            || e.Args.Contains("--cover-test")
            || e.Args.Contains("--integration-test")
            || e.Args.Contains("--make-icon")
            || e.Args.Contains("--unregister")
            || e.Args.Contains("--log-test")
            || e.Args.Contains("--showtest")
            || e.Args.Contains("--screenshot");
        HookGlobalExceptionHandlers();
        ApplicationThemeManager.ApplySystemTheme();
        Log.Information("Tagster {Version} starting. Args: {Args}",
            typeof(App).Assembly.GetName().Version?.ToString() ?? "?", e.Args);

        // Single instance: hand off to the already-running window if there is one.
        if (!_diagnosticMode)
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
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog();
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
            catch (Exception ex) { Log.Warning(ex, "Failed to unregister the context menu"); }
            _ = Dispatcher.BeginInvoke(new Action(() => Shutdown(0)), DispatcherPriority.ApplicationIdle);
            return;
        }

        if (e.Args.Contains("--log-test"))
        {
            _ = Dispatcher.BeginInvoke(new Action(() => Shutdown(RunLogTest() ? 0 : 1)), DispatcherPriority.ApplicationIdle);
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

        _mainWindow = _host.Services.GetRequiredService<MainWindow>();

        if (e.Args.Contains("--showtest"))
        {
            // Actually show the window so render/layout errors surface (then auto-close).
            SystemThemeWatcher.Watch(_mainWindow);
            _mainWindow.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { timer.Stop(); Console.WriteLine("SHOWTEST: rendered OK"); Shutdown(0); };
            timer.Start();
            return;
        }

        if (e.Args.Contains("--screenshot"))
        {
            ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
            _mainWindow.Background = Brushes.White;
            _mainWindow.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    var element = (FrameworkElement)_mainWindow.Content;
                    var w = Math.Max(1, (int)element.ActualWidth);
                    var h = Math.Max(1, (int)element.ActualHeight);
                    var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(element);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var path = Path.Combine(AppPaths.LogsDirectory, "screenshot.png");
                    using (var stream = File.Create(path)) encoder.Save(stream);
                    Console.WriteLine($"SCREENSHOT {w}x{h}: {path}");
                }
                catch (Exception ex) { Console.WriteLine("SCREENSHOT FAIL: " + ex); }
                Shutdown(0);
            };
            timer.Start();
            return;
        }

        if (e.Args.Contains("--selftest"))
        {
            _ = _host.Services.GetRequiredService<SettingsWindow>(); // validate its XAML too
            _ = Dispatcher.BeginInvoke(new Action(() => Shutdown(0)), DispatcherPriority.ApplicationIdle);
            return;
        }

        _singleInstance?.StartServer(args => Dispatcher.Invoke(() => OnActivated(args)));
        SystemThemeWatcher.Watch(_mainWindow);
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

    private static void ConfigureLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(AppPaths.LogsDirectory, "tagster-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private void HookGlobalExceptionHandlers()
    {
        // Exceptions raised on the UI thread.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Fatal(args.Exception, "Unhandled exception on the UI thread");
            args.Handled = true; // keep the app alive; the error is logged

            if (_diagnosticMode)
            {
                // Headless runs must never block on a dialog.
                Console.WriteLine("UI EXCEPTION: " + args.Exception);
                Shutdown(1);
                return;
            }

            MessageBox.Show(
                "An unexpected error occurred and has been written to the log.\n\n" + args.Exception.Message,
                "Tagster", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        // Exceptions on any thread that nobody caught (usually fatal).
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", args.IsTerminating);
            Log.CloseAndFlush();
        };

        // Exceptions from tasks that were never awaited/observed.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    private bool RunLogTest()
    {
        var logger = _host!.Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("log-test: ILogger<App> resolved via DI");
        try
        {
            throw new InvalidOperationException("log-test deliberate exception");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "log-test: exception logged via DI ILogger");
        }
        Log.Information("log-test: static Serilog Log");
        Log.CloseAndFlush();

        var latest = Directory.GetFiles(AppPaths.LogsDirectory, "tagster-*.log")
            .OrderByDescending(f => f).FirstOrDefault();
        var content = latest is not null ? File.ReadAllText(latest) : "";
        var ok = content.Contains("log-test: exception logged via DI ILogger")
            && content.Contains("log-test deliberate exception");
        Console.WriteLine(ok ? "PASS: logging to file works (DI + static, with stack trace)" : "FAIL: log entries not found");
        return ok;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("Tagster exiting");
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        _singleInstance?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
