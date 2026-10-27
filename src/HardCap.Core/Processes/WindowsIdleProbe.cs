using System.Runtime.InteropServices;
using HardCap.Core.Enforcement;

namespace HardCap.Core.Processes;

/// <summary>
/// Asks Windows how long it has been since the last keyboard or mouse input. This is the same measure the screen saver and the
/// power settings use, and it needs no special rights. Game controllers are not counted: Windows does not treat them as input here.
/// </summary>
public sealed class WindowsIdleProbe : IIdleProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    public TimeSpan? GetIdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return null;
        }

        // Both numbers are milliseconds since Windows started, as 32-bit values that wrap around after about 49 days. Subtracting
        // as unsigned numbers gives the right gap even across a wrap.
        var idleMilliseconds = unchecked((uint)Environment.TickCount - info.Time);
        return TimeSpan.FromMilliseconds(idleMilliseconds);
    }
}
