using System.Reflection;

namespace WinLayer.Settings;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(false, "WinLayer.Settings", new NamedWaitHandleOptions
        {
            CurrentUserOnly = true,
            CurrentSessionOnly = true
        });

        try
        {
            if (!instance.WaitOne(0))
                return;
        }
        catch (AbandonedMutexException)
        {
        }

        try
        {
            ApplicationConfiguration.Initialize();
            using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinLayer.ico");
            using var icon = new Icon(iconStream!);
            using var window = new SettingsWindow { Icon = icon };
            Application.Run(window);
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }
}
