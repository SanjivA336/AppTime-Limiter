using System.Text.Json;
using System.Text.Json.Serialization;
using Curfew.Core.Models;

namespace Curfew.Core.Settings;

/// <summary>What <see cref="SettingsStore.Load"/> found: the settings to use, plus anything the user should know.</summary>
public sealed record SettingsLoadResult(AppSettings Settings, string? Error, IReadOnlyList<string> Repairs);

/// <summary>Reads and writes settings.json. Writes are atomic so a crash can't leave a half-written file.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public SettingsLoadResult Load()
    {
        if (!File.Exists(Path))
        {
            return new SettingsLoadResult(new AppSettings(), null, Array.Empty<string>());
        }

        try
        {
            var json = File.ReadAllText(Path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? throw new JsonException("The file is empty.");
            var repairs = SettingsRules.Sanitize(settings);
            return new SettingsLoadResult(settings, null, repairs);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the unreadable file so the user can recover it by hand; the app starts with empty settings.
            var backup = BackupUnreadableFile();
            var where = backup is null ? string.Empty : $" A copy was saved as {System.IO.Path.GetFileName(backup)}.";
            return new SettingsLoadResult(new AppSettings(), $"settings.json couldn't be read ({ex.Message}).{where}", Array.Empty<string>());
        }
    }

    public void Save(AppSettings settings)
    {
        var folder = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(Path))
        {
            File.Replace(temp, Path, null);
        }
        else
        {
            File.Move(temp, Path);
        }
    }

    private string? BackupUnreadableFile()
    {
        try
        {
            var backup = System.IO.Path.ChangeExtension(Path, $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(Path, backup, overwrite: true);
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
