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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.CreateDirectory(AppPaths.DataDirectory);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTagsterCore();
        builder.Services.AddTagsterSqliteIndex(AppPaths.IndexDatabasePath);
        builder.Services.AddTagsterShell();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        await _host.StartAsync();

        if (e.Args.Contains("--cover-test"))
        {
            var report = CoverSelfTest.Run(_host.Services.GetRequiredService<IFolderCoverService>());
            Console.WriteLine(report.Message);
            _ = Dispatcher.BeginInvoke(new Action(() => Shutdown(report.Ok ? 0 : 1)), DispatcherPriority.ApplicationIdle);
            return;
        }

        // Constructing the window parses all XAML and resolves the DI graph.
        var window = _host.Services.GetRequiredService<MainWindow>();

        if (e.Args.Contains("--selftest"))
        {
            // Headless wiring check: we got here, so DI + XAML are valid. Exit cleanly.
            _ = Dispatcher.BeginInvoke(new Action(() => Shutdown(0)), DispatcherPriority.ApplicationIdle);
            return;
        }

        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
