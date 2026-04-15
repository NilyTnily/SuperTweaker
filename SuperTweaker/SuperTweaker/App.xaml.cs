using System.Windows;
using System.Text;
using System.IO;
using System.Threading;

namespace SuperTweaker;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, @"Global\SuperTweaker_SingleInstance_2026", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "SuperTweaker is already running. Close the existing window first.",
                "Already Running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += (s, ex) =>
        {
            ShowDetailedError("UI Thread Exception", ex.Exception);
            ex.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            if (ex.ExceptionObject is Exception err)
                ShowDetailedError("Unhandled Exception", err);
        };

        try
        {
            var main = new MainWindow();
            MainWindow = main;
            main.Show();
        }
        catch (Exception ex)
        {
            ShowDetailedError("Startup Exception", ex);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch
        {
            // Ignore release errors during shutdown.
        }
        finally
        {
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    private static void ShowDetailedError(string title, Exception ex)
    {
        var text = BuildExceptionDump(ex);

        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SuperTweaker", "CrashLogs");
            Directory.CreateDirectory(logDir);
            var file = Path.Combine(logDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(file, text, Encoding.UTF8);

            MessageBox.Show(
                $"{title}\n\n{text}\n\nCrash log saved to:\n{file}",
                "SuperTweaker Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            MessageBox.Show(
                $"{title}\n\n{text}",
                "SuperTweaker Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string BuildExceptionDump(Exception ex)
    {
        var sb = new StringBuilder();
        int depth = 0;
        Exception? cur = ex;
        while (cur != null)
        {
            sb.AppendLine($"[{depth}] {cur.GetType().FullName}: {cur.Message}");
            if (!string.IsNullOrWhiteSpace(cur.StackTrace))
            {
                sb.AppendLine("Stack:");
                sb.AppendLine(cur.StackTrace);
            }
            sb.AppendLine();
            cur = cur.InnerException;
            depth++;
        }
        return sb.ToString();
    }
}
