using System.IO;

namespace Curfew.App.Services;

/// <summary>A tiny append-only log for diagnosing problems on a machine we can't see. Never throws.</summary>
public static class AppLog
{
    private const long MaxBytes = 512 * 1024;
    private static string? _path;

    public static void Init(string path) => _path = path;

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
            {
                File.Move(_path, _path + ".old", overwrite: true);
            }

            File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Logging must never take the app down.
        }
    }
}
