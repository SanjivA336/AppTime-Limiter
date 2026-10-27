using Curfew.Core.Processes;

namespace Curfew.Tests;

public class BackgroundWarningTests
{
    [Theory]
    [InlineData("vgc")]
    [InlineData("VGTRAY")]
    [InlineData("EasyAntiCheat.exe")]
    [InlineData("BEService")]
    public void Known_anti_cheat_programs_get_a_warning(string name) =>
        Assert.Contains("Anti-cheat", ProcessNames.BackgroundWarning(name));

    [Theory]
    [InlineData("GoogleUpdater")]
    [InlineData("steamwebhelper")]
    [InlineData("RiotClientServices")]
    [InlineData("crashpad_handler")]
    public void Names_that_look_like_helpers_get_a_warning(string name) =>
        Assert.NotNull(ProcessNames.BackgroundWarning(name));

    [Theory]
    [InlineData("VALORANT-Win64-Shipping")]
    [InlineData("Warframe.x64")]
    [InlineData("notepad")]
    [InlineData("helldivers2")]
    public void Ordinary_apps_get_no_warning(string name) =>
        Assert.Null(ProcessNames.BackgroundWarning(name));
}
