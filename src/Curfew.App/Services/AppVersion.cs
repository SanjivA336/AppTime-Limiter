using System.Reflection;

namespace Curfew.App.Services;

/// <summary>The version this copy of the app was built as, read from the program itself so it can never disagree with what is installed.</summary>
public static class AppVersion
{
    /// <summary>For example "1.3.1" (empty if the program carries no version).</summary>
    public static string Current { get; } = Read();

    /// <summary>For example "v1.3.1", as shown next to the app's name.</summary>
    public static string Label => Current.Length == 0 ? string.Empty : "v" + Current;

    private static string Read()
    {
        var info = typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info))
        {
            return string.Empty;
        }

        // .NET adds the source revision after a "+" ("1.3.1+3fa9c1..."); only the part before it is the version.
        var plus = info.IndexOf('+');
        return (plus >= 0 ? info[..plus] : info).Trim();
    }
}
