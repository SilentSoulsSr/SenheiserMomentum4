namespace SenheiserControl;

internal static class Program
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "crash.log");

    [STAThread]
    private static void Main()
    {
        Application.ThreadException += (_, e) => LogException("WinForms ThreadException", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogException("AppDomain UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogException("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }

    private static void LogException(string source, Exception? ex)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:O}] {source}: {ex}\n\n");
        }
        catch
        {
            // logging must never itself crash the app
        }
    }
}
