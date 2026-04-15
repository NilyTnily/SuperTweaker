using System.IO;
using System.Text;

namespace SuperTweaker.Core;

public class Logger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SuperTweaker", "Logs");

    private readonly string _logFile;
    private readonly object _lock = new();

    public event Action<string>? OnLine;

    public Logger(string sessionId)
    {
        Directory.CreateDirectory(LogDir);
        _logFile = Path.Combine(LogDir, $"session-{sessionId}.log");
    }

    public void Info(string msg)    => Write("INFO ", msg);
    public void Success(string msg) => Write("OK   ", msg);
    public void Warn(string msg)    => Write("WARN ", msg);
    public void Error(string msg)   => Write("ERR  ", msg);

    private void Write(string level, string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {msg}";
        lock (_lock)
        {
            try { File.AppendAllText(_logFile, line + Environment.NewLine, Encoding.UTF8); }
            catch { }
        }
        OnLine?.Invoke(line);
    }

    public string LogFilePath => _logFile;
}
