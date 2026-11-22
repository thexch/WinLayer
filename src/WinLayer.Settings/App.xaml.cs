using System.Windows;
using WinLayer.Core;

namespace WinLayer.Settings;

public partial class App : Application
{
    private Mutex? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(false, "WinLayer.Settings", new NamedWaitHandleOptions
        {
            CurrentUserOnly = true,
            CurrentSessionOnly = true
        });
        try
        {
            if (!_instance.WaitOne(0))
            {
                _instance.Dispose();
                _instance = null;
                Shutdown();
                return;
            }
        }
        catch (AbandonedMutexException)
        {
        }

        var page = e.Args.Length > 0 && Enum.TryParse<Page>(e.Args[0], out var requestedPage) &&
            Enum.IsDefined(requestedPage) ? requestedPage : Page.Dashboard;
        MainWindow = new MainWindow(page);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
