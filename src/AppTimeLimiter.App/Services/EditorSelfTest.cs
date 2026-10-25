using System.IO;
using System.Windows.Threading;
using AppTimeLimiter.App.ViewModels;
using AppTimeLimiter.Core.Models;

namespace AppTimeLimiter.App.Services;

/// <summary>
/// Development aid (--self-test): runs the group editor's save, revert and cancel rules on the app's own view
/// models and writes a pass/fail report. It needs no mouse, no focus and no screen, so it is safe to run while
/// someone is using the computer. It edits the data folder it is given, so use a throwaway copy.
/// </summary>
internal static class EditorSelfTest
{
    /// <returns>True if every check passed.</returns>
    public static async Task<bool> RunAsync(MainViewModel main, string settingsPath, string reportPath)
    {
        var report = new List<string>();
        var failures = 0;

        void Check(string what, bool passed)
        {
            report.Add($"{(passed ? "PASS" : "FAIL")}  {what}");
            if (!passed)
            {
                failures++;
            }
        }

        string OnDisk() => File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : string.Empty;

        var card = main.Groups[0];
        var real = card.Model;
        var originalName = real.Name;
        var originalColor = real.Color;
        var otherGroupName = main.Groups[1].Name;
        var saturdayBudget = real.BudgetMinutesFor(DayOfWeek.Saturday);
        var otherGroupSaturdayBudget = main.Groups[1].Model.BudgetMinutesFor(DayOfWeek.Saturday);

        main.SaveNow();
        var diskAtStart = OnDisk();

        // 1. Edits go to a draft. Nothing real changes, and nothing is written, until Save.
        var editor = new GroupEditorViewModel(main, card);
        editor.Name = "Renamed";
        editor.ColorHex = "#BB6BD9";
        editor.Draft.Schedule.SetState(DayOfWeek.Monday, 3, CellState.Blocked);
        editor.NotifyScheduleEdited();
        editor.SetBudgetMinutes(DayOfWeek.Saturday, 77);

        Check("editing marks the editor as having changes", editor.HasChanges);
        Check("editing does not rename the real group", real.Name == originalName);
        Check("editing does not recolor the real group", real.Color == originalColor);
        Check("editing does not change the real schedule", real.Schedule.GetState(DayOfWeek.Monday, 3) != CellState.Blocked);
        Check("editing does not change the real budget", real.BudgetMinutesFor(DayOfWeek.Saturday) == saturdayBudget);
        Check("editing does not change the card", card.Name == originalName);
        Check("editing writes nothing to disk", OnDisk() == diskAtStart);

        // 2. Revert puts the draft back, and the window stays usable.
        editor.Revert();
        Check("revert clears the changes flag", !editor.HasChanges);
        Check("revert restores the name", editor.Name == originalName);
        Check("revert restores the color", editor.ColorHex == originalColor);
        Check("revert restores the schedule", editor.Schedule.GetState(DayOfWeek.Monday, 3) == real.Schedule.GetState(DayOfWeek.Monday, 3));
        Check("revert restores the budget", editor.GetBudgetMinutes(DayOfWeek.Saturday) == saturdayBudget);

        // 3. Save makes the draft real and writes the file.
        editor.Name = "Renamed";
        editor.ColorHex = "#BB6BD9";
        editor.Draft.Schedule.SetState(DayOfWeek.Monday, 3, CellState.Blocked);
        editor.NotifyScheduleEdited();
        editor.SetBudgetMinutes(DayOfWeek.Saturday, 77);
        var saved = editor.Save();

        Check("save reports that it saved", saved);
        Check("save renames the real group", real.Name == "Renamed");
        Check("save recolors the real group", real.Color == "#BB6BD9");
        Check("save changes the real schedule", real.Schedule.GetState(DayOfWeek.Monday, 3) == CellState.Blocked);
        Check("save changes this group's budget", real.BudgetMinutesFor(DayOfWeek.Saturday) == 77);
        Check("save leaves every other group's budget alone", main.Groups[1].Model.BudgetMinutesFor(DayOfWeek.Saturday) == otherGroupSaturdayBudget);
        Check("a saved budget is written to disk inside the group", OnDisk().Contains("77"));
        Check("save updates the card", card.Name == "Renamed");
        Check("save writes the new name to disk", OnDisk().Contains("\"Renamed\""));
        Check("save writes the new budget to disk", OnDisk().Contains("77"));

        // 4. Closing without saving throws the draft away.
        var abandoned = new GroupEditorViewModel(main, card);
        abandoned.Name = "Discarded";
        abandoned.Draft.Processes.Add("some-app");
        abandoned.Draft.Schedule.SetState(DayOfWeek.Tuesday, 5, CellState.Limited);
        abandoned.NotifyScheduleEdited();
        // (the window is closed here, so nothing else happens)

        Check("an abandoned edit leaves the real name alone", real.Name == "Renamed");
        Check("an abandoned edit leaves the real apps alone", !real.Processes.Contains("some-app"));
        Check("an abandoned edit writes nothing", !OnDisk().Contains("Discarded"));

        // 5. Saving with nothing changed does nothing.
        var untouched = new GroupEditorViewModel(main, card);
        Check("save with no changes reports that nothing was saved", !untouched.Save());

        // 6. Two warnings with the same number can't both be kept, so the list must not pretend otherwise.
        var warnings = new GroupEditorViewModel(main, card);
        warnings.AddWarningCommand.Execute(null);
        warnings.AddWarningCommand.Execute(null);
        Check("two warnings can be added", warnings.Warnings.Count == 2);
        warnings.Warnings[1].Minutes = warnings.Warnings[0].Minutes; // make them the same
        await FlushDispatcherAsync();
        Check("a repeated warning is removed from the list", warnings.Warnings.Count == 1);
        Check("the draft and the list agree", warnings.Draft.WarningMinutes.Count == warnings.Warnings.Count);
        Check("the user is told why", !string.IsNullOrEmpty(warnings.Message));
        warnings.Message = null;

        // 7. Names must be unique, and a refused name leaves the old one in place.
        var naming = new GroupEditorViewModel(main, card);
        naming.Name = otherGroupName;
        Check("a name used by another group is refused", naming.Name == "Renamed");
        Check("the user is told the name is taken", !string.IsNullOrEmpty(naming.Message));

        // 8. A new group only exists once saved.
        var groupsBefore = main.Settings.Groups.Count;
        var cardsBefore = main.Groups.Count;

        var discardedNew = new GroupEditorViewModel(main, main.CreateGroup());
        discardedNew.Name = "Never saved";
        Check("a new group's editor has nothing to delete", !discardedNew.CanDelete);
        Check("an unsaved new group is not in the settings", main.Settings.Groups.Count == groupsBefore);
        Check("an unsaved new group has no card", main.Groups.Count == cardsBefore);

        var newCard = main.CreateGroup();
        var created = new GroupEditorViewModel(main, newCard);
        created.Name = "Brand new";
        created.Save();
        Check("a saved new group joins the settings", main.Settings.Groups.Count == groupsBefore + 1);
        Check("a saved new group gets a card", main.Groups.Count == cardsBefore + 1);
        Check("a saved new group is written to disk", OnDisk().Contains("Brand new"));
        Check("an abandoned new group is not on disk", !OnDisk().Contains("Never saved"));

        // 9. The lock boxes: seconds carry into minutes and borrow from them, and the total stays between 0:00 and 5:00.
        var lockBoxes = new GroupEditorViewModel(main, card);
        Check("a group without a lock opens straight into the editor", !lockBoxes.IsLocked && lockBoxes.IsUnlocked);

        lockBoxes.LockSecondsPart = 60;
        Check("60 seconds becomes 1 minute 0 seconds", lockBoxes.LockMinutes == 1 && lockBoxes.LockSecondsPart == 0);
        lockBoxes.LockSecondsPart = -5;
        Check("stepping the seconds below 0 borrows a minute (0:55)", lockBoxes.LockMinutes == 0 && lockBoxes.LockSecondsPart == 55);
        lockBoxes.LockSecondsPart = 60;
        Check("stepping 0:55 up past 59 carries to 1:00", lockBoxes.LockMinutes == 1 && lockBoxes.LockSecondsPart == 0);

        lockBoxes.LockMinutes = 5;
        lockBoxes.LockSecondsPart = 5;
        Check("the lock can't go past 5:00", lockBoxes.LockMinutes == 5 && lockBoxes.LockSecondsPart == 0);
        lockBoxes.LockMinutes = 0;
        lockBoxes.LockSecondsPart = -5;
        Check("the lock can't go below 0:00", lockBoxes.LockMinutes == 0 && lockBoxes.LockSecondsPart == 0);
        Check("0:00 reads as no lock", lockBoxes.LockSummaryText == "No lock");

        lockBoxes.LockMinutes = 0;
        lockBoxes.LockSecondsPart = 7;
        Check("a typed 7 seconds is tidied to 5", lockBoxes.LockSecondsPart == 5);

        lockBoxes.LockMinutes = 2;
        lockBoxes.Revert();
        Check("revert also undoes the lock", lockBoxes.LockMinutes == 0 && lockBoxes.LockSecondsPart == 0);

        // 10. Setting a lock takes effect the NEXT time the group is opened, never in the window it was set in.
        var setter = new GroupEditorViewModel(main, card);
        setter.LockMinutes = 1;
        Check("setting a lock does not lock the window you are in", setter.IsUnlocked);
        setter.Save();
        Check("the lock is saved to the group", real.LockSeconds == 60);
        Check("the lock is written to disk", OnDisk().Contains("\"lockSeconds\": 60"));

        var locked = new GroupEditorViewModel(main, card);
        Check("opening a locked group shows the lock screen", locked.IsLocked);
        Check("the lock screen hides the editor", !locked.IsUnlocked);
        Check("the countdown starts at the full length", locked.LockRemainingText == "1:00");
        locked.StopCountdown();

        // 11. The countdown really counts down, then reveals the editor, and every open starts the wait again.
        real.LockSeconds = 2;
        var waiting = new GroupEditorViewModel(main, card);
        Check("a short lock starts locked", waiting.IsLocked);
        await Task.Delay(1000);
        Check("it is still locked partway through", waiting.IsLocked);
        Check("the bar has gone down by then", waiting.LockProgress < 1 && waiting.LockProgress > 0);
        await Task.Delay(1600);
        Check("the editor appears when the countdown ends", waiting.IsUnlocked && waiting.LockRemainingText == "0:00");

        var again = new GroupEditorViewModel(main, card);
        Check("opening it again starts the whole wait over", again.IsLocked);
        again.StopCountdown();

        // 12. Taking the lock off (from inside the editor) and a lock never hiding a new group.
        real.LockSeconds = 0;
        Check("a group whose lock was removed opens straight into the editor", new GroupEditorViewModel(main, card).IsUnlocked);
        Check("a new group is never locked", new GroupEditorViewModel(main, main.CreateGroup()).IsUnlocked);

        report.Add(string.Empty);
        report.Add(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        File.WriteAllLines(reportPath, report);
        return failures == 0;
    }

    /// <summary>Lets anything queued to run "right after" the current work actually run.</summary>
    private static Task FlushDispatcherAsync() =>
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
}
