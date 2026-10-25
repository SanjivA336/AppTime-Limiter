using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AppTimeLimiter.Core.Processes;

namespace AppTimeLimiter.App.Services;

/// <summary>
/// Finds the icon for an app so the group cards can show it.
///
/// We only save an app's name, and the icon lives in its .exe file, whose location is only known while the
/// app is running. So the first time we see an app running we copy its icon into a small cache folder,
/// and use that copy from then on, even when the app isn't running. Apps we haven't seen yet have no icon.
/// </summary>
public sealed class AppIconService
{
    /// <summary>Wait at least this long before trying again for an app whose icon we couldn't get.</summary>
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    private readonly string _folder;
    private readonly Dictionary<string, ImageSource> _loaded = new(ProcessNames.Comparer);
    private readonly Dictionary<string, DateTime> _lastFailure = new(ProcessNames.Comparer);

    public AppIconService(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
    }

    /// <summary>The saved icon for this app, or null if we've never managed to get one.</summary>
    public ImageSource? GetIcon(string processName)
    {
        if (_loaded.TryGetValue(processName, out var alreadyLoaded))
        {
            return alreadyLoaded;
        }

        var path = PathFor(processName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = LoadImage(path);
            _loaded[processName] = image;
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException)
        {
            AppLog.Error($"Could not read the saved icon for {processName}", ex);
            return null;
        }
    }

    /// <summary>
    /// For each app that is running right now and has no saved icon yet, saves one.
    /// Returns true if at least one new icon was saved (so the caller knows to refresh what it shows).
    /// </summary>
    public bool CaptureIfRunning(IEnumerable<string> processNames)
    {
        var savedAny = false;
        foreach (var name in processNames)
        {
            var needsIcon = !File.Exists(PathFor(name));
            var triedRecently = _lastFailure.TryGetValue(name, out var failedAt) && DateTime.Now - failedAt < RetryAfterFailure;
            if (!needsIcon || triedRecently)
            {
                continue;
            }

            if (TryCapture(name))
            {
                savedAny = true;
            }
            else
            {
                _lastFailure[name] = DateTime.Now;
            }
        }

        return savedAny;
    }

    private bool TryCapture(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    var exePath = process.MainModule?.FileName;
                    if (exePath is null)
                    {
                        continue;
                    }

                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (icon is null)
                    {
                        continue;
                    }

                    using var bitmap = icon.ToBitmap();
                    bitmap.Save(PathFor(processName), System.Drawing.Imaging.ImageFormat.Png);
                    return true;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or ArgumentException)
                {
                    // Most often "access denied" because the app runs as administrator. Try the next copy, if any.
                }
            }
        }

        return false;
    }

    /// <summary>Loaded into memory straight away so the file isn't left locked.</summary>
    private static BitmapImage LoadImage(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private string PathFor(string processName)
    {
        var safeName = string.Concat(processName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_folder, safeName.ToLowerInvariant() + ".png");
    }
}
