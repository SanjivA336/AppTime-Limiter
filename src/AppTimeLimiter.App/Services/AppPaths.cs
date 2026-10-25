using System.IO;

namespace AppTimeLimiter.App.Services;

/// <summary>Where the app keeps its files. Everything lives in one per-user folder, nothing is stored remotely.</summary>
public sealed class AppPaths
{
    public AppPaths(string? overrideFolder = null)
    {
        Folder = overrideFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppTimeLimiter");
        Directory.CreateDirectory(Folder);
    }

    public string Folder { get; }

    public string Settings => Path.Combine(Folder, "settings.json");

    public string Usage => Path.Combine(Folder, "usage.json");

    public string Log => Path.Combine(Folder, "app.log");
}
