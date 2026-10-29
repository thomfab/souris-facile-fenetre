namespace SourisFacile;

internal static class Program
{
    // The installer uses these names too, to close the app before updating it.
    private const string MutexName = "SourisFacileFenetre";
    private const string ShowEventName = "SourisFacileFenetre.Show";
    private const string QuitEventName = "SourisFacileFenetre.Quit";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            // Already running: bring up its settings instead.
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var show))
            {
                show.Set();
                show.Dispose();
            }
            return;
        }

        ApplicationConfiguration.Initialize();

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SourisFacileFenetre");
        var store = new SettingsStore(Path.Combine(folder, "settings.json"));

        // A control to run code on the UI thread from other threads.
        using var invoker = new Control();
        invoker.CreateControl();

        using var app = new App(store);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        using var quitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName);
        var showWait = ThreadPool.RegisterWaitForSingleObject(showEvent,
            (_, _) => invoker.BeginInvoke(app.ShowSettings), null, Timeout.Infinite, executeOnlyOnce: false);
        var quitWait = ThreadPool.RegisterWaitForSingleObject(quitEvent,
            (_, _) => invoker.BeginInvoke(app.ExitThread), null, Timeout.Infinite, executeOnlyOnce: true);

        Application.Run(app);

        showWait.Unregister(null);
        quitWait.Unregister(null);
    }
}
