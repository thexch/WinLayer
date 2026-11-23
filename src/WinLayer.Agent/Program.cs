using System.Diagnostics;
using Velopack;
using WinLayer.Core;

namespace WinLayer.Agent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            var firstRun = false;
            VelopackApp.Build()
                .OnFirstRun(_ => firstRun = true)
                .OnBeforeUninstallFastCallback(_ => WindowsSettings.SetAutoStart(false, removeOwnEntryOnly: true))
                .Run();

            var timer = Stopwatch.StartNew();
            var page = args.Contains("--battery") ? Page.Battery : Page.Dashboard;
            var quit = args.Contains("--quit");
            var open = !quit && (firstRun || !args.Contains("--background"));
            using var singleInstance = new Mutex(false, "WinLayer.Agent", new NamedWaitHandleOptions
            {
                CurrentUserOnly = true,
                CurrentSessionOnly = true
            });

            if (!Acquire(singleInstance))
            {
                if (open || quit)
                    Forward(new(quit ? Command.Quit : Command.OpenSettings, page));
                return;
            }

            try
            {
                if (quit)
                    return;

                ApplicationConfiguration.Initialize();
                using var synchronization = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(synchronization);
                using var agent = new ResidentApplication(open, page, timer, args.Contains("--measure-startup"));
                Application.Run(agent);
            }
            finally
            {
                singleInstance.ReleaseMutex();
            }
        }
        catch (Exception error)
        {
            WindowsSettings.Log("Échec du lancement de l’agent.", error);
            if (!args.Contains("--background"))
                ShowFailure("WinLayer n’a pas pu démarrer. Consultez le journal local.");
        }
    }

    private static void ShowFailure(string message)
    {
        try { UtilityWindows.ConfirmMessage("WinLayer", message); }
        catch (Exception error)
        {
            WindowsSettings.Log("Affichage de l’erreur.", error);
            MessageBox.Show(message, "WinLayer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool Acquire(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static void Forward(Request request)
    {
        // The second process has no message loop. The resident process handles the request.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            LocalProtocol.SendAsync(request, timeout.Token).GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            WindowsSettings.Log("Impossible de joindre l’instance active.", error);
            ShowFailure("L’agent WinLayer ne répond pas. Réessayez dans quelques instants.");
        }
    }
}
