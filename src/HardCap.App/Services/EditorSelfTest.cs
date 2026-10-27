using System.IO;
using System.Windows.Threading;
using HardCap.App.ViewModels;
using HardCap.Core.Enforcement;
using HardCap.Core.Models;
using HardCap.Core.Usage;

namespace HardCap.App.Services;

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

        // 6. Two warnings with the same number can't both be saved, but they may briefly match while being edited.
        var warnings = new GroupEditorViewModel(main, card);
        warnings.AddWarningCommand.Execute(null);
        warnings.AddWarningCommand.Execute(null);
        Check("two warnings can be added", warnings.Warnings.Count == 2);
        warnings.Warnings[1].Minutes = warnings.Warnings[0].Minutes; // make them the same
        await FlushDispatcherAsync();
        Check("a repeated warning is NOT removed while editing", warnings.Warnings.Count == 2);
        Check("both repeated rows are marked", warnings.Warnings.All(w => w.IsDuplicate) && warnings.HasDuplicateWarnings);
        Check("saving is refused while there is a repeat", !warnings.ValidateForSave());
        Check("the user is told why", !string.IsNullOrEmpty(warnings.Message));
        Check("the real group is untouched by the refused save", card.Model.WarningMinutes.Count != 2 || card.Model.WarningMinutes.Distinct().Count() == 2);
        warnings.Warnings[1].Minutes = warnings.Warnings[0].Minutes + 1; // step it past the other one
        await FlushDispatcherAsync();
        Check("moving past the other number clears the marks", !warnings.HasDuplicateWarnings && warnings.Warnings.All(w => !w.IsDuplicate));
        Check("and the reminder goes away", string.IsNullOrEmpty(warnings.Message));
        Check("saving is allowed once they differ", warnings.ValidateForSave());
        Check("the draft keeps both distinct warnings", warnings.Draft.WarningMinutes.Count == 2);

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

        lockBoxes.Lock.Seconds = 60;
        Check("60 seconds becomes 1 minute 0 seconds", lockBoxes.Lock.Minutes == 1 && lockBoxes.Lock.Seconds == 0);
        lockBoxes.Lock.Seconds = -5;
        Check("stepping the seconds below 0 borrows a minute (0:55)", lockBoxes.Lock.Minutes == 0 && lockBoxes.Lock.Seconds == 55);
        lockBoxes.Lock.Seconds = 60;
        Check("stepping 0:55 up past 59 carries to 1:00", lockBoxes.Lock.Minutes == 1 && lockBoxes.Lock.Seconds == 0);

        lockBoxes.Lock.Minutes = 5;
        lockBoxes.Lock.Seconds = 5;
        Check("the lock can't go past 5:00", lockBoxes.Lock.Minutes == 5 && lockBoxes.Lock.Seconds == 0);
        lockBoxes.Lock.Minutes = 0;
        lockBoxes.Lock.Seconds = -5;
        Check("the lock can't go below 0:00", lockBoxes.Lock.Minutes == 0 && lockBoxes.Lock.Seconds == 0);
        Check("0:00 reads as no wait", lockBoxes.Lock.Summary == "No wait");

        lockBoxes.Lock.Minutes = 0;
        lockBoxes.Lock.Seconds = 7;
        Check("a typed 7 seconds is tidied to 5", lockBoxes.Lock.Seconds == 5);

        lockBoxes.Lock.Minutes = 2;
        lockBoxes.Revert();
        Check("revert also undoes the lock", lockBoxes.Lock.Minutes == 0 && lockBoxes.Lock.Seconds == 0);

        // 10. Setting a lock takes effect the NEXT time the group is opened, never in the window it was set in.
        var setter = new GroupEditorViewModel(main, card);
        setter.Lock.Minutes = 1;
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

        // 11b. A paused group ignores its lock, without changing it.
        real.LockSeconds = 60;
        real.IsPaused = true;
        real.PausedUntil = null;
        var whilePaused = new GroupEditorViewModel(main, card);
        Check("a paused group opens straight into the editor", whilePaused.IsUnlocked && !whilePaused.IsLocked);
        Check("pausing does not change the saved lock", real.LockSeconds == 60 && whilePaused.Draft.LockSeconds == 60);
        real.IsPaused = false;
        var afterPause = new GroupEditorViewModel(main, card);
        Check("the lock is back once the group is no longer paused", afterPause.IsLocked);
        afterPause.StopCountdown();

        // 12. Taking the lock off (from inside the editor) and a lock never hiding a new group.
        real.LockSeconds = 0;
        Check("a group whose lock was removed opens straight into the editor", new GroupEditorViewModel(main, card).IsUnlocked);
        Check("a new group is never locked", new GroupEditorViewModel(main, main.CreateGroup()).IsUnlocked);

        // 13. The real number boxes in a real window (everything above skipped the controls and used the view models).
        var window = new Views.GroupEditorWindow(main, card)
        {
            ShowActivated = true,
            WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
            Left = 100,
            Top = 0,
        };
        window.Show();
        window.Activate();
        var windowEditor = (GroupEditorViewModel)window.DataContext;
        windowEditor.AddWarningCommand.Execute(null);
        window.SelectSection("Warnings");
        await FlushDispatcherAsync();
        window.UpdateLayout();

        var warningBox = FindAll<Wpf.Ui.Controls.NumberBox>(window).First(box => box.Maximum == 1440);
        warningBox.Focus();
        warningBox.Text = "45";
        Check("typing in a warning box has not saved it yet (it saves on leaving the box)", windowEditor.Warnings[0].Minutes != 45);

        window.SaveButton.Focus();
        await FlushDispatcherAsync();
        Check("moving focus to Save commits the typed warning", windowEditor.Warnings[0].Minutes == 45);

        window.SelectSection("Edit friction");
        await FlushDispatcherAsync();
        window.UpdateLayout();
        var lockBox = FindAll<Wpf.Ui.Controls.NumberBox>(window).Last(box => box.Minimum == -300); // the lock card comes after the grace card
        lockBox.Focus();
        lockBox.Text = "45";
        window.SaveButton.Focus();
        await FlushDispatcherAsync();
        Check("moving focus to Save commits the typed lock seconds (0:45)", windowEditor.Lock.Minutes == 0 && windowEditor.Lock.Seconds == 45);
        // The NumberBox is itself a text box; the room its text gets is the "content host" scroll viewer in its template.
        var textRoom = FindAll<System.Windows.Controls.ScrollViewer>(lockBox).First().ActualWidth;
        Check($"a two-digit number fits in the box (text area is {textRoom:0} px wide, needs 40)", textRoom >= 40);

        // The arrow buttons of a number box change its value without any typing.
        var minutesBox = FindAll<Wpf.Ui.Controls.NumberBox>(window).Last(box => box.Minimum == -1);
        minutesBox.Value = 2;
        await FlushDispatcherAsync();
        Check($"changing a box's value in code (like its arrows do) reaches the editor (box {minutesBox.Value}, text '{minutesBox.Text}', editor {windowEditor.Lock.Minutes}:{windowEditor.Lock.Seconds}, draft {windowEditor.Draft.LockSeconds}s)", windowEditor.Lock.Minutes == 2);

        // The budget boxes on the Schedule tab.
        windowEditor.Draft.Schedule.SetState(DayOfWeek.Monday, 20, CellState.Limited);
        windowEditor.NotifyScheduleEdited();
        window.SelectTab(1);
        await FlushDispatcherAsync();
        window.UpdateLayout();
        var budgetBox = FindAll<Wpf.Ui.Controls.NumberBox>(window).First(box => box.Maximum == 1440 && box.Minimum == 0 && box.IsEnabled);
        budgetBox.Focus();
        budgetBox.Text = "45";
        window.SaveButton.Focus();
        await FlushDispatcherAsync();
        Check("moving focus to Save commits a typed daily budget", windowEditor.GetBudgetMinutes(DayOfWeek.Monday) == 45);

        // The grace period's boxes, in the real window.
        window.SelectSection("Grace period");
        await FlushDispatcherAsync();
        window.UpdateLayout();
        var graceSecondsBox = FindAll<Wpf.Ui.Controls.NumberBox>(window).First(box => box.Minimum == -300);
        graceSecondsBox.Focus();
        graceSecondsBox.Text = "30";
        window.SaveButton.Focus();
        await FlushDispatcherAsync();
        Check("a typed grace period reaches the draft", windowEditor.Draft.GraceSeconds == 30);
        Check("typing a grace period does not change the lock", windowEditor.Draft.LockSeconds == 165); // 2:45, from the checks above

        // The wide layout (a list of sections) and the narrow one (the tabs and stacked cards the editor always had).
        Check("a wide editor window shows the section list", window.IsSidebarShowing);
        window.SelectSection("Pause");
        await FlushDispatcherAsync();
        window.UpdateLayout();
        Check("the list shows one section at a time", window.PauseCard.Visibility == System.Windows.Visibility.Visible && window.GraceCard.Visibility == System.Windows.Visibility.Collapsed);
        Check("a card that shared a row takes the whole row when shown alone", System.Windows.Controls.Grid.GetColumnSpan(window.PauseCard) == 2 && System.Windows.Controls.Grid.GetColumn(window.PauseCard) == 0);
        window.Width = 760;
        await FlushDispatcherAsync();
        window.UpdateLayout();
        Check("a narrow editor window goes back to tabs", !window.IsSidebarShowing);
        Check("every card on the General tab is visible again when narrow", window.GraceCard.Visibility == System.Windows.Visibility.Visible && window.AppsCard.Visibility == System.Windows.Visibility.Visible && window.FrictionCard.Visibility == System.Windows.Visibility.Visible);
        Check("paired cards are back in their own columns when narrow", System.Windows.Controls.Grid.GetColumn(window.PauseCard) == 1 && System.Windows.Controls.Grid.GetColumnSpan(window.PauseCard) == 1);
        window.Close();

        // 14. Pause, in the editor's draft.
        real.IsPaused = false;
        real.PausedUntil = null;
        var pausing = new GroupEditorViewModel(main, card);
        Check("a group that isn't paused opens as \"Not paused\"", pausing.SelectedPauseOption?.Kind == PauseKind.NotPaused);
        pausing.SelectedPauseOption = pausing.PauseOptions.First(o => o.Kind == PauseKind.UntilResumed);
        Check("pausing until resumed marks the draft paused with no end", pausing.Draft.IsPaused && pausing.Draft.PausedUntil is null);
        Check("pausing is not real until it is saved", !real.IsPaused);
        pausing.SelectedPauseOption = pausing.PauseOptions.First(o => o.Duration == TimeSpan.FromHours(1));
        Check("\"for 1 hour\" sets an end about an hour away", pausing.Draft.PausedUntil is { } end && Math.Abs((end - DateTime.Now).TotalMinutes - 60) < 1);
        pausing.Save();
        Check("saving a pause makes it real", real.IsPausedAt(DateTime.Now) && real.PausedUntil is not null);
        Check("the card says it is paused", card.StatusText.StartsWith("Paused until"));

        var resuming = new GroupEditorViewModel(main, card);
        Check("a group paused until a time offers to keep that pause", resuming.SelectedPauseOption?.Kind == PauseKind.KeepCurrent);
        resuming.SelectedPauseOption = resuming.PauseOptions.First(o => o.Kind == PauseKind.NotPaused);
        resuming.Save();
        Check("choosing \"not paused\" and saving resumes the group", !real.IsPausedAt(DateTime.Now));

        // 14b. Count time: what uses up the budget.
        real.CountMode = CountMode.WhileRunning;
        var counting = new GroupEditorViewModel(main, card);
        Check("a group counts whenever it is running by default", counting.CountsWhileRunning && !counting.CountsWhileInFront);
        counting.CountsWhileInFront = true;
        Check("choosing in front changes the draft, and flips the other choice", counting.Draft.CountMode == CountMode.WhileInFront && !counting.CountsWhileRunning);
        Check("the count mode is not real until it is saved", real.CountMode == CountMode.WhileRunning && counting.HasChanges);
        counting.Revert();
        Check("revert puts the count mode back", counting.CountsWhileRunning && counting.Draft.CountMode == CountMode.WhileRunning);
        counting.CountsWhileRunning = false;
        counting.Save();
        Check("saving makes the count mode real", real.CountMode == CountMode.WhileInFront);
        real.CountMode = CountMode.WhileRunning;

        // 15. The countdown only runs while its window is the active one.
        var waiting2 = new WaitCountdown(30);
        waiting2.SetWindowActive(false);
        var before = waiting2.Progress;
        await Task.Delay(700);
        Check("a countdown standing in the background does not move", waiting2.Progress == before && waiting2.IsPaused);
        waiting2.SetWindowActive(true);
        await Task.Delay(700);
        Check("it carries on when its window is active again", waiting2.Progress < before && !waiting2.IsPaused);
        waiting2.Stop();

        // 16. A group waits only for its own lock. The general wait (settings) is for the settings page and quitting, so it
        // does not lock a group, and open hours don't change a group's lock.
        real.LockSeconds = 0;
        main.Settings.WaitSeconds = 20;
        main.Settings.OpenHoursEnabled = false;
        var general = new GroupEditorViewModel(main, card);
        Check("the general wait does not make a group without a lock wait", general.IsUnlocked);

        real.LockSeconds = 30;
        var ownLock = new GroupEditorViewModel(main, card);
        Check("a group's own lock is the only wait for opening it", ownLock.IsLocked && ownLock.Countdown?.TotalSeconds == 30);
        ownLock.StopCountdown();

        main.Settings.OpenHoursEnabled = true;
        main.Settings.OpenHoursStartMinutes = 0;
        main.Settings.OpenHoursEndMinutes = 24 * 60 - 1;
        var lockInOpenHours = new GroupEditorViewModel(main, card);
        Check("a group's own lock still applies during open hours", lockInOpenHours.IsLocked && lockInOpenHours.Countdown?.TotalSeconds == 30);
        lockInOpenHours.StopCountdown();
        real.LockSeconds = 0;
        main.Settings.WaitSeconds = 0;
        main.Settings.OpenHoursEnabled = false;

        // 17. The Settings page: its wait, the reset time, and export / import.
        var settingsPage = new SettingsViewModel(main);
        settingsPage.BeginWait();
        Check("with no wait the settings open straight away", settingsPage.IsUnlocked && settingsPage.Countdown is null);

        main.Settings.WaitSeconds = 2;
        settingsPage.BeginWait();
        Check("a general wait puts the settings behind a countdown", settingsPage.IsLocked && settingsPage.Countdown?.TotalSeconds == 2);
        await Task.Delay(2600);
        Check("the settings open when the countdown ends", settingsPage.IsUnlocked);
        settingsPage.BeginWait();
        Check("opening the settings again starts the wait over", settingsPage.IsLocked);
        settingsPage.EndWait();
        main.Settings.WaitSeconds = 0;

        // The Settings page edits a draft: nothing is real until "Save changes", like the group editor.
        Check("a fresh Settings page has nothing to save", !settingsPage.HasChanges && settingsPage.NoChanges);
        settingsPage.Save();
        Check("saving with no changes does nothing", settingsPage.Message is null);
        settingsPage.SelectedReset = TimeChoice.For(4 * 60);
        Check("choosing a reset time is only a draft", main.Settings.ResetMinutes == 0 && settingsPage.HasChanges && !settingsPage.NoChanges);
        Check("the reset time is explained", settingsPage.ResetSummary.Contains("4:00 AM"));
        settingsPage.Revert();
        Check("revert puts the reset time back", settingsPage.SelectedReset.Minutes == 0 && !settingsPage.HasChanges);
        settingsPage.SelectedReset = TimeChoice.For(4 * 60);
        settingsPage.Save();
        Check("saving makes the reset time real", main.Settings.ResetMinutes == 240 && !settingsPage.HasChanges);
        Check("saving says so", settingsPage.Message == "Saved your settings.");
        Check("the group editor's note shows the reset time", new GroupEditorViewModel(main, card).BudgetNoteText.Contains("4:00 AM"));

        // 17b. Away from the computer.
        Check("the idle setting starts off, so the computer isn't asked", !settingsPage.IdleDetectionEnabled && !main.Settings.IdleDetectionEnabled);
        Check("the off setting is explained", settingsPage.IdleSummary.StartsWith("Off"));
        settingsPage.IdleDetectionEnabled = true;
        Check("turning it on is a draft until saved", settingsPage.IdleDetectionEnabled && !main.Settings.IdleDetectionEnabled && settingsPage.HasChanges);
        settingsPage.IdleMinutes = 12;
        Check("a typed number of minutes reaches the draft", settingsPage.IdleMinutes == 12 && settingsPage.IdleSummary.Contains("12 minutes"));
        settingsPage.IdleMinutes = 0;
        Check("too few minutes is tidied to 1", settingsPage.IdleMinutes == 1 && settingsPage.IdleSummary.Contains("1 minute "));
        settingsPage.IdleMinutes = 500;
        Check("too many minutes is tidied to the longest", settingsPage.IdleMinutes == AppSettings.MaxIdleMinutes);
        settingsPage.IdleMinutes = double.NaN;
        Check("clearing the box leaves the setting alone", settingsPage.IdleMinutes == AppSettings.MaxIdleMinutes);
        Check("nothing away is real before saving", !main.Settings.IdleDetectionEnabled && main.Settings.IdleMinutes == AppSettings.DefaultIdleMinutes);
        settingsPage.Save();
        Check("saving makes the away setting real", main.Settings.IdleDetectionEnabled && main.Settings.IdleMinutes == AppSettings.MaxIdleMinutes);
        settingsPage.IdleDetectionEnabled = false;
        settingsPage.IdleMinutes = AppSettings.DefaultIdleMinutes;
        settingsPage.Save();
        Check("and it can be switched off again", !main.Settings.IdleDetectionEnabled && main.Settings.IdleMinutes == AppSettings.DefaultIdleMinutes);

        settingsPage.IdleDetectionEnabled = true;
        Check("unsaved changes turn export and import off", settingsPage.HasChanges && !settingsPage.NoChanges);
        settingsPage.Revert();
        Check("revert turns them back on and clears the switch", settingsPage.NoChanges && !settingsPage.IdleDetectionEnabled && settingsPage.Message is null);

        var backup = Path.Combine(Path.GetTempPath(), $"hardcap-selftest-{Guid.NewGuid():N}.json");
        try
        {
            var groupCount = main.Settings.Groups.Count;
            Check("exporting writes a file", settingsPage.Export(backup) && File.Exists(backup));

            main.Settings.ResetMinutes = 0;
            main.RemoveGroup(main.Groups[^1]);
            Check("a group was removed after the export", main.Settings.Groups.Count == groupCount - 1);

            Check("importing the file works", settingsPage.Import(backup));
            Check("importing brings the groups back", main.Settings.Groups.Count == groupCount && main.Groups.Count == groupCount);
            Check("importing brings the reset time back", main.Settings.ResetMinutes == 240);

            File.WriteAllText(backup, "this is not settings");
            Check("a broken file is refused", !settingsPage.Import(backup) && main.Settings.Groups.Count == groupCount);
        }
        finally
        {
            File.Delete(backup);
            main.Settings.ResetMinutes = 0;
        }

        // 19. The Analytics page: choosing groups, the three views, and moving around.
        var analytics = new AnalyticsViewModel(main);
        var groupTotal = main.Settings.Groups.Count;
        Check("the selector lists every group", analytics.Choices.Count == groupTotal);
        Check("every group is picked to begin with", analytics.AllSelected && analytics.Legend.Count == groupTotal);
        Check("the button then offers to deselect all", analytics.SelectAllLabel == "Deselect all");
        Check("it opens on this week", analytics.IsWeekView && analytics.PeriodTitle == "This week" && analytics.CurrentButtonText == "This week");
        Check("this week shows its dates", analytics.PeriodRange.Contains('–'));
        Check("the week chart has seven columns, with today the only highlighted one", analytics.Days.Count == 7 && analytics.ChartColumns.Count == 7 && analytics.ChartColumns.Count(c => c.IsHighlighted) == 1);
        Check("only days that have happened can be opened", analytics.ChartColumns.All(c => c.ClickDate is null || c.ClickDate <= main.CurrentBudgetDay));

        analytics.ToggleAllCommand.Execute(null);
        Check("deselecting all leaves nothing picked", analytics.NoneSelected && !analytics.AllSelected && analytics.Legend.Count == 0);
        Check("the button then offers to select all", analytics.SelectAllLabel == "Select all");
        Check("nothing picked says so", analytics.EmptyTitle == "No groups selected" && !analytics.ShowContent);
        Check("nothing picked charts nothing", analytics.Days.All(day => day.Groups.Count == 0));
        analytics.ToggleAllCommand.Execute(null);
        Check("selecting all picks every group again", analytics.AllSelected && analytics.Legend.Count == groupTotal && analytics.EmptyTitle != "No groups selected");

        analytics.Choices[0].IsSelected = false;
        Check("a group can be unpicked on its own", !analytics.AllSelected && !analytics.NoneSelected && analytics.Legend.Count == groupTotal - 1);
        analytics.ToggleAllCommand.Execute(null);
        Check("with some picked, the button picks the rest", analytics.AllSelected);
        analytics.ToggleAllCommand.Execute(null);
        analytics.Choices[1].IsSelected = true;
        Check("from nothing, one group can be picked", analytics.Legend.Count == 1 && analytics.Days[0].Groups.Count == 1);
        analytics.Choices[2].IsSelected = true;
        Check("and another added", analytics.Legend.Count == 2 && analytics.Days[0].Groups.Count == 2);
        analytics.ToggleAllCommand.Execute(null); // two are picked, so this picks the rest

        Check("the horizontal chart has a bar for each group", analytics.Rows.Count == groupTotal);
        analytics.ShowApps = true;
        var appTotal = main.Settings.Groups.Sum(g => g.Processes.Count);
        Check("switching to apps shows a bar for each app", analytics.Rows.Count == appTotal && analytics.Rows.All(r => r.HasSubtitle));
        analytics.ShowApps = false;

        // The three views.
        analytics.SelectedViewChoice = analytics.ViewChoices.First(c => c.View == AnalyticsView.Day);
        Check("the day view is named for today", analytics.IsDayView && analytics.PeriodTitle == "Today" && analytics.CurrentButtonText == "Today" && analytics.Days.Count == 1);
        Check("the day view has a column for each hour of the day", analytics.ChartColumns.Count == 24 && analytics.ChartColumns[0].Label == "12a" && analytics.ChartColumns[15].Label == "3p");
        Check("the day view highlights the current hour only", analytics.ChartColumns.Count(c => c.IsHighlighted) == 1);
        Check("hours are not clickable", analytics.ChartColumns.All(c => c.ClickDate is null));
        Check("hours have readable names", analytics.ChartColumns[15].Title == "3 PM to 4 PM" && analytics.ChartColumns[23].Title == "11 PM to 12 AM");
        analytics.SelectedViewChoice = analytics.ViewChoices.First(c => c.View == AnalyticsView.Month);
        Check("the month view is named for this month", analytics.IsMonthView && analytics.PeriodTitle == "This month" && analytics.CurrentButtonText == "This month");
        Check("the month shows its name and every day", analytics.PeriodRange.Contains(DateTime.Now.Year.ToString()) && analytics.Days.Count == DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month));
        analytics.SelectedViewChoice = analytics.ViewChoices.First(c => c.View == AnalyticsView.Week);

        // Moving around: forward stops at the current period, but going back never does.
        Check("you can't go forward from the current week", !analytics.NextCommand.CanExecute(null) && !analytics.CurrentCommand.CanExecute(null));
        analytics.PreviousCommand.Execute(null);
        Check("going back shows last week", analytics.PeriodTitle == "Last week" && !analytics.IsCurrentPeriod && analytics.ChartColumns.All(c => !c.IsHighlighted));
        Check("going forward and the jump button are then possible", analytics.NextCommand.CanExecute(null) && analytics.CurrentCommand.CanExecute(null));
        analytics.NextCommand.Execute(null);
        Check("going forward again lands on this week", analytics.PeriodTitle == "This week");

        for (var step = 0; step < 80; step++)
        {
            analytics.PreviousCommand.Execute(null);
        }

        Check("going back has no limit, and an empty week says so", analytics.PeriodTitle == "80 weeks ago" && analytics.EmptyTitle.StartsWith("No data for") && !analytics.ShowContent);
        analytics.CurrentCommand.Execute(null);
        Check("the jump button goes straight back to this week", analytics.PeriodTitle == "This week" && analytics.IsCurrentPeriod);

        // Clicking a day opens it; clicking a week number opens that week.
        var today = main.CurrentBudgetDay;
        analytics.OpenDay(today.AddDays(-2));
        Check("clicking a day opens the day view for it", analytics.IsDayView && analytics.Anchor == today.AddDays(-2) && analytics.PeriodTitle == "2 days ago");
        analytics.OpenDay(today.AddDays(-4));
        Check("clicking another day while on the day view moves to it", analytics.Anchor == today.AddDays(-4) && analytics.PeriodTitle == "4 days ago");
        var lastMonday = UsageAnalytics.WeekStartOf(today).AddDays(-7);
        analytics.OpenWeek(lastMonday);
        Check("clicking a week number opens that week", analytics.IsWeekView && analytics.PeriodTitle == "Last week" && analytics.Days[0].Date == lastMonday);
        analytics.CurrentCommand.Execute(null);

        // 18. The quit window opens, counts down, and offers the choice at the end.
        var exitWindow = new Views.ExitWaitWindow(1) { WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = 100, Top = 0 };
        exitWindow.Show();
        await Task.Delay(1600);
        Check("the quit window has not quit by itself", !exitWindow.QuitConfirmed);
        exitWindow.Close();


        // 19. Launch friction: the editor card, then the real thing end to end with a real (throwaway) process.
        real.LaunchFrictionSeconds = 0;
        var friction = new GroupEditorViewModel(main, card);
        Check("launch friction starts off", friction.LaunchFrictionSeconds == 0 && friction.LaunchFrictionSummary.StartsWith("Off"));
        friction.LaunchFrictionSeconds = 20;
        Check("a typed wait reaches the draft, and is not real until saved", friction.Draft.LaunchFrictionSeconds == 20 && real.LaunchFrictionSeconds == 0);
        friction.LaunchFrictionSeconds = 22;
        Check("launch friction snaps to steps of 5", friction.Draft.LaunchFrictionSeconds == 20 && friction.LaunchFrictionSeconds == 20);
        friction.LaunchFrictionSeconds = 500;
        Check("too long is tidied to a minute", friction.Draft.LaunchFrictionSeconds == 60);
        friction.LaunchFrictionSeconds = 10;
        friction.Save();
        Check("saving makes launch friction real", real.LaunchFrictionSeconds == 10);
        real.LaunchFrictionSeconds = 0;

        await RunLaunchFrictionEndToEnd(Check);

        report.Add(string.Empty);
        report.Add(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        File.WriteAllLines(reportPath, report);
        return failures == 0;
    }

    /// <summary>A "notifier" that just remembers what it was asked to show.</summary>
    private sealed class RememberingNotifier : HardCap.Core.Enforcement.INotifier
    {
        public List<string> Messages { get; } = new();

        public void Notify(string title, string message) => Messages.Add(message);
    }

    /// <summary>
    /// Runs launch friction against a real throwaway process (a renamed copy of ping.exe, so nothing the person runs can be
    /// touched): it must be frozen when it starts, carry on when the countdown runs out, and be closed, not relaunched, when the
    /// countdown window is closed.
    /// </summary>
    private static async Task RunLaunchFrictionEndToEnd(Action<string, bool> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "hardcap-lf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var name = "atl-gate-" + Guid.NewGuid().ToString("N")[..8];
        var exe = Path.Combine(folder, name + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), exe);

        var group = new AppGroup { Name = "Gate test", Processes = { name }, LaunchFrictionSeconds = 5 };
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            for (var cell = 0; cell < 48; cell++)
            {
                group.Schedule.SetState(day, cell, CellState.Free);
            }
        }

        var settings = new AppSettings { Groups = { group } };
        var processes = new HardCap.Core.Processes.WindowsProcessController();
        var notifier = new RememberingNotifier();
        var runner = new HardCap.Core.Enforcement.EnforcementRunner(() => settings, processes, notifier, new UsageLedger());

        Views.LaunchGateWindow? gate = null;
        var holdsStarted = 0;
        runner.HoldStarted += request =>
        {
            holdsStarted++;
            gate = new Views.LaunchGateWindow(request);
            gate.Completed += id => runner.CompleteHold(id);
            gate.Cancelled += id => _ = runner.CancelHoldAsync(id);
            gate.Show();
        };

        static bool IsFrozen(string processName)
        {
            var found = System.Diagnostics.Process.GetProcessesByName(processName);
            try
            {
                return found.Length > 0 && found.All(p => p.Threads.Cast<System.Diagnostics.ProcessThread>().All(t =>
                    t.ThreadState == System.Diagnostics.ThreadState.Wait && t.WaitReason == System.Diagnostics.ThreadWaitReason.Suspended));
            }
            finally
            {
                foreach (var p in found)
                {
                    p.Dispose();
                }
            }
        }

        async Task<bool> WaitFor(Func<bool> condition, int milliseconds)
        {
            for (var waited = 0; waited < milliseconds; waited += 100)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(100);
            }

            return condition();
        }

        System.Diagnostics.Process Start() => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(exe, "-n 120 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;

        System.Diagnostics.Process? running = null;
        try
        {
            var now = DateTime.Now;
            await runner.RunOnceAsync(now);                            // HardCap looks first: nothing is running
            running = Start();
            check("the throwaway app starts", await WaitFor(() => processes.GetRunningProcessNames().Contains(name), 5000));

            await runner.RunOnceAsync(now.AddSeconds(1));             // and sees it start
            check("a newly started app is held: the countdown opens", holdsStarted == 1 && gate is not null);
            check("a newly started app is really frozen", await WaitFor(() => IsFrozen(name), 3000));
            check("a held app is not closed", !running.HasExited);

            // The countdown only runs while its window is in front. (The window is told it isn't, so the check doesn't depend on
            // which window the computer happens to have focused.)
            await FlushDispatcherAsync();
            gate!.Countdown.SetWindowActive(false);
            var progressBefore = gate.Countdown.Progress;
            await Task.Delay(2500);
            check("the countdown stands still while its window is not in front", Math.Abs(gate.Countdown.Progress - progressBefore) < 0.001 && !gate.IsReady);
            check("the app stays frozen meanwhile", IsFrozen(name));

            // Back in front: it runs, and when it reaches zero the choice appears. The app is still frozen until the person chooses.
            var ready = false;
            for (var waited = 0; waited < 9000 && !ready; waited += 100)
            {
                gate.Countdown.SetWindowActive(true);
                ready = gate.IsReady;
                if (!ready)
                {
                    await Task.Delay(100);
                }
            }

            check("the countdown runs when its window is in front and then offers the choice", ready);
            check("the app stays frozen until the choice is made", IsFrozen(name));

            gate.ChooseOpen();                                          // "Open it"
            check("choosing to open unfreezes the app", await WaitFor(() => !IsFrozen(name), 3000));
            check("the countdown window goes away when the choice is made", await WaitFor(() => gate is { IsVisible: false }, 2000));
            check("the app is still running after it was opened", !running.HasExited);

            // Close the countdown window instead: "don't open it".
            running.Kill();
            await running.WaitForExitAsync();
            await runner.RunOnceAsync(now.AddSeconds(20));            // sees it gone
            running = Start();
            await WaitFor(() => processes.GetRunningProcessNames().Contains(name), 5000);
            await runner.RunOnceAsync(now.AddSeconds(21));            // sees it start again
            check("opening it again is held again", holdsStarted == 2 && await WaitFor(() => IsFrozen(name), 3000));

            gate!.Close();                                            // the person chooses not to open it
            check("closing the countdown closes the app, not relaunches it", await WaitFor(() => running.HasExited, 6000));
            await Task.Delay(300);
            await runner.RunOnceAsync(now.AddSeconds(25));
            await runner.RunOnceAsync(now.AddSeconds(26));
            check("nothing is held or started again after that", holdsStarted == 2
                && System.Diagnostics.Process.GetProcessesByName(name).Length == 0);
            check("nothing is left frozen", !IsFrozen(name));
        }
        finally
        {
            runner.ReleaseAllHolds();
            gate?.CloseQuietly();
            foreach (var leftover in System.Diagnostics.Process.GetProcessesByName(name))
            {
                try
                {
                    leftover.Kill();
                }
                finally
                {
                    leftover.Dispose();
                }
            }

            try
            {
                await Task.Delay(300);
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // the exe can stay locked for a moment after it exits; temp files are harmless
            }
        }
    }

    /// <summary>Every element of the given type anywhere inside <paramref name="root"/>, in the order they appear.</summary>
    private static IEnumerable<T> FindAll<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var inner in FindAll<T>(child))
            {
                yield return inner;
            }
        }
    }

    /// <summary>Lets anything queued to run "right after" the current work actually run.</summary>
    private static Task FlushDispatcherAsync() =>
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
}
